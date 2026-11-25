using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Dread;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    // Recheck the guard and the iterator bodies together when adding a game assembly.
    private const string AuditedJackObservationAssembly = "ae309f852fb69204a59c04b7b5af63acc839829c65c1349777621ce616e65310";

    private static void AttachJackObservationContext(Dictionary<string, object?> state, Player player, string? screen)
    {
        if (screen is not ("monster" or "elite" or "boss") || player.PlayerCombatState?.Hand is not { } hand)
            return;
        var jacks = hand.Cards.OfType<JackOfAllTrades>()
            .Where(card => card.IsUpgraded && card.UpgradePreviewType == CardUpgradePreviewType.None).ToArray();
        if (jacks.Length == 0) return;

        var context = new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["known"] = false,
            ["unavailable_reasons"] = Array.Empty<string>(),
            ["owner_player_id"] = player.NetId.ToString(CultureInfo.InvariantCulture),
            ["attempt_id"] = null,
            ["game_assembly_sha256"] = null,
            ["should_skip_combat_effect"] = null,
            ["combat"] = null,
            ["run"] = null
        };
        string readFailure = "settled_input_read_failed";
        try
        {
            if (player.Creature.CombatState is not CombatState combat
                || !IsSettledPotionInput(player, combat, screen, checkCombatEnding: false))
                return;

            readFailure = "identity_read_failed";
            var attempt = CaptureCombatAttempt(combat, liveOnly: true);
            context["attempt_id"] = attempt?.Id;
            string hash = GenerationAssemblySha256.Value;
            context["game_assembly_sha256"] = hash;
            if (hash != AuditedJackObservationAssembly)
                throw new JackObservationUnavailable("unaudited_game_assembly");

            // Same order as AttachChoicePotionContext. Without a living primary enemy,
            // CombatManager.IsCombatEnding can invoke Hook.ShouldStopCombatFromEnding.
            readFailure = "combat_effect_guard_read_failed";
            // Creature.IsSecondaryEnemy invokes the virtual PowerModel.OwnerIsSecondaryEnemy getter.
            if (combat.Enemies.Where(enemy => enemy is { IsAlive: true }).SelectMany(enemy => enemy.Powers)
                .Any(power => power.GetType().Assembly != typeof(AbstractModel).Assembly))
                throw new JackObservationUnavailable("external_listener_model");
            if (!combat.Enemies.Any(enemy => enemy is { IsAlive: true, IsPrimaryEnemy: true }))
                throw new JackObservationUnavailable("no_living_primary_enemy");
            bool shouldSkip = CombatManager.Instance.ShouldSkipCombatEffect;
            if (shouldSkip) return;
            context["should_skip_combat_effect"] = shouldSkip;
            if (attempt == null) throw new JackObservationUnavailable("combat_attempt_unavailable");

            readFailure = "owner_read_failed";
            if (player.RunState is not RunState run || !ReferenceEquals(combat.RunState, run)
                || !ReferenceEquals(player.Creature.Player, player) || !player.IsActiveForHooks
                || jacks.Any(card => !ReferenceEquals(card.Owner, player) || card.HasBeenRemovedFromState
                    || !ReferenceEquals(card.Pile, hand)))
                throw new JackObservationUnavailable("owner_or_hand_mismatch");

            var runPlayers = run.Players.ToDictionary(p => p.NetId);
            var combatPlayers = combat.Players;
            if (runPlayers.Count != combatPlayers.Count
                || combatPlayers.Select(p => p.NetId).Distinct().Count() != runPlayers.Count
                || !runPlayers.TryGetValue(player.NetId, out var owner) || !ReferenceEquals(owner, player)
                || combatPlayers.Any(p => !runPlayers.TryGetValue(p.NetId, out var member)
                    || !ReferenceEquals(member, p) || !ReferenceEquals(p.Creature.CombatState, combat)))
                throw new JackObservationUnavailable("run_or_combat_players_mismatch");

            readFailure = "mod_subscriber_read_failed";
            int runSubscribers = CountModHookSubscribers("_runHookSubscribers");
            int combatSubscribers = CountModHookSubscribers("_combatHookSubscribers");
            if (runSubscribers > 0 || combatSubscribers > 0)
                throw new JackObservationUnavailable("external_hook_subscribers");

            readFailure = "combat_listener_read_failed";
            var combatModels = combat.IterateHookListeners().ToList();
            var combatScope = BuildJackListenerScope(combatModels);
            readFailure = "run_listener_read_failed";
            var runModels = run.IterateHookListeners(combat).ToList();
            // RunState.IterateHookListeners(combat) appends the complete combat list.
            if (runModels.Count < combatModels.Count
                || !runModels.Skip(runModels.Count - combatModels.Count)
                    .SequenceEqual(combatModels, ReferenceEqualityComparer.Instance))
                throw new JackObservationUnavailable("run_combat_listeners_mismatch");
            var runScope = BuildJackListenerScope(runModels);

            context["combat"] = combatScope;
            context["run"] = runScope;
            context["known"] = true;
        }
        catch (JackObservationUnavailable ex)
        {
            context["unavailable_reasons"] = new[] { ex.Message };
        }
        catch (Exception ex)
        {
            context["unavailable_reasons"] = new[] { readFailure };
            // Engine exception messages can contain individual models or their positions.
            Warn("player." + JackObservationContextKey, readFailure + ": " + ex.GetType().Name);
        }
        state[JackObservationContextKey] = context;
    }

    private sealed class JackObservationUnavailable(string reason) : Exception(reason);

    private static Dictionary<string, object?> BuildJackListenerScope(IReadOnlyList<AbstractModel> listeners)
    {
        var rows = listeners.GroupBy(model => (Type: model.GetType(), model.Id)).Select(group =>
        {
            var (type, id) = group.Key;
            if (type.Assembly != typeof(AbstractModel).Assembly)
                throw new JackObservationUnavailable("external_listener_model");
            string category = group.First() switch
            {
                PowerModel => "power",
                MonsterModel => "monster",
                RelicModel => "relic",
                PotionModel => "potion",
                OrbModel => "orb",
                CardModel => "card",
                AfflictionModel => "affliction",
                EnchantmentModel => "enchantment",
                ModifierModel => "modifier",
                BadgeModel => "badge",
                RunModel => "run_model",
                DominionModel => "dominion",
                _ => throw new JackObservationUnavailable("unclassified_listener_model")
            };
            if (type.FullName is not { } concreteType || id is null || id == ModelId.none
                || string.IsNullOrWhiteSpace(id.Entry) || id != ModelDb.GetId(type))
                throw new JackObservationUnavailable("listener_identity_mismatch");
            return (Category: category, Id: id.Entry, ConcreteType: concreteType, Count: group.Count());
        }).OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ThenBy(row => row.ConcreteType, StringComparer.Ordinal)
            .Select(row => new Dictionary<string, object?>
            {
                ["category"] = row.Category,
                ["id"] = row.Id,
                ["concrete_type"] = row.ConcreteType,
                ["count"] = row.Count
            }).ToList();
        return new Dictionary<string, object?> { ["listener_count"] = listeners.Count, ["models"] = rows };
    }
}
