using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace STS2_MCP;

public static partial class McpMod
{
    // PotionModel.OnUseWrapper -> CardPileCmd.AddGeneratedCardsToCombat / Add -> CheckForEmptyHand.
    private static readonly (string Notification, string Method, Type[] Parameters, bool RunScope, bool PickOnly)[] ChoicePotionHooks =
    {
        Stage(BeforePotionUsedHook),
        Stage(CardEnteredCombatHook, pickOnly: true),
        ("after_card_changed_piles", nameof(AbstractModel.AfterCardChangedPiles),
            new[] { typeof(CardModel), typeof(PileType), typeof(AbstractModel) }, true, true),
        ("after_card_changed_piles_late", nameof(AbstractModel.AfterCardChangedPilesLate),
            new[] { typeof(CardModel), typeof(PileType), typeof(AbstractModel) }, true, true),
        Stage(CardGeneratedForCombatHook, pickOnly: true),
        Stage(AfterPotionUsedHook),
        Stage(HandEmptiedHook)
    };

    private static (string, string, Type[], bool, bool) Stage(
        (string Notification, string Method, Type[] Parameters, bool RunScope) hook, bool pickOnly = false)
        => (hook.Notification, hook.Method, hook.Parameters, hook.RunScope, pickOnly);

    private static bool IsChoicePotion(PotionModel? potion)
        => potion is AttackPotion or SkillPotion or PowerPotion or ColorlessPotion;

    private static void AttachChoicePotionContext(Dictionary<string, object?> state, Player player, string? screen)
    {
        if (screen is not ("monster" or "elite" or "boss") || !player.PotionSlots.Any(IsChoicePotion)) return;
        try
        {
            if (player.Creature.CombatState is not { } combat
                || !IsSettledPotionInput(player, combat, screen, checkCombatEnding: false))
                return;
            // CombatManager.IsCombatEnding can call Hook.ShouldStopCombatFromEnding when no primary
            // enemy is alive. On that boundary, leave the guard unobserved instead of querying the hook.
            if (!combat.Enemies.Any(enemy => enemy is { IsAlive: true, IsPrimaryEnemy: true }))
                throw new InvalidOperationException("combat-effect guard is unobserved without a living primary enemy");
            bool shouldSkip = CombatManager.Instance.ShouldSkipCombatEffect;
            if (!shouldSkip && BuildChoicePotionContext(player, combat, shouldSkip) is { } context)
                state[ChoicePotionContextKey] = context;
        }
        catch (Exception ex)
        {
            var context = NewChoicePotionContext(player);
            RecordChoicePotionReadFailure(context, ex);
            state[ChoicePotionContextKey] = context;
        }
    }

    private static Dictionary<string, object?> NewChoicePotionContext(Player player) => new()
    {
        ["version"] = 1,
        ["known"] = false,
        ["owner_player_id"] = player.NetId.ToString(),
        ["slots"] = null
    };

    private static void RecordChoicePotionReadFailure(Dictionary<string, object?> context, Exception ex)
    {
        context["unavailable_reasons"] = new[] { $"{ex.GetType().Name}: {ex.Message}" };
        Warn("player." + ChoicePotionContextKey, ex);
    }

    private static Dictionary<string, object?>? BuildChoicePotionContext(Player player, ICombatState combat,
        bool shouldSkipCombatEffect)
    {
        var slots = player.PotionSlots.Select((potion, slot) => (Potion: potion, Slot: slot))
            .Where(entry => IsChoicePotion(entry.Potion)).ToList();
        if (slots.Count == 0) return null;

        var context = NewChoicePotionContext(player);
        try
        {
            context["game_assembly_sha256"] = GenerationAssemblySha256.Value;
            context["should_skip_combat_effect"] = shouldSkipCombatEffect;
            if (!Equals(context["game_assembly_sha256"], AuditedPotionAssembly))
                throw new InvalidOperationException("unaudited game assembly for choice potion listeners");
            var run = player.RunState;
            RequireGenerationPoolOwner(player, run.Players);
            if (!combat.Players.Any(p => ReferenceEquals(p, player)) || !ReferenceEquals(player.Creature.CombatState, combat))
                throw new InvalidOperationException("choice potion owner is not in this combat");
            foreach (var (potion, slot) in slots)
                if (!ReferenceEquals(potion!.Owner, player) || IndexOfListener<PotionModel>(player.PotionSlots, potion) != slot)
                    throw new InvalidOperationException($"choice potion owner or instance mismatch at slot {slot}");
            if (CountModHookSubscribers("_runHookSubscribers") + CountModHookSubscribers("_combatHookSubscribers") > 0)
                throw new InvalidOperationException("ModHelper hook subscribers cannot be tied to public state");

            RefreshEntityIdRegistry(combat);
            var runModels = run.IterateHookListeners(combat).ToList();
            var combatModels = shouldSkipCombatEffect ? new List<AbstractModel>() : combat.IterateHookListeners().ToList();
            if (runModels.Concat(combatModels).Any(model => model.GetType().Assembly != typeof(AbstractModel).Assembly))
                throw new InvalidOperationException("external hook listener models cannot be tied to public state");

            var slotRows = new List<Dictionary<string, object?>>(slots.Count);
            bool hasListeners = false;
            foreach (var (potion, slot) in slots)
            {
                var (row, hasOverrides) = BuildChoicePotionSlot(potion!, slot, runModels, combatModels);
                slotRows.Add(row);
                hasListeners |= hasOverrides;
            }
            if (!hasListeners) return null;
            context["slots"] = slotRows;
            context["known"] = slotRows.All(row => row["known"] is true);
        }
        catch (Exception ex)
        {
            RecordChoicePotionReadFailure(context, ex);
        }
        return context;
    }

    private static (Dictionary<string, object?> Row, bool HasOverrides) BuildChoicePotionSlot(PotionModel potion,
        int slot, List<AbstractModel> runModels, List<AbstractModel> combatModels)
    {
        var reasons = new List<string>();
        var notifications = new List<Dictionary<string, object?>>();
        // RemoveBeforeUse clears this exact slot before both scopes see the belt.
        // The other bottles (including same-ID or other-owner bottles) still occupy their positions.
        var runListeners = runModels.Where(model => !ReferenceEquals(model, potion)).ToList();
        var combatListeners = combatModels.Where(model => !ReferenceEquals(model, potion)).ToList();
        bool pickKnown = true;
        bool skipKnown = true;
        bool hasOverrides = false;

        foreach (var (notification, method, parameters, runScope, pickOnly) in ChoicePotionHooks)
        {
            var listeners = runScope ? runListeners : combatListeners;
            int previousReasons = reasons.Count;
            var rows = BuildHookListenerRows(listeners, (notification, method, parameters, runScope), reasons,
                (model, declarer) => MayInterveneInChoicePotion(method, model, declarer, potion, pickKnown),
                useDrawDisplayOrder: false);
            hasOverrides |= rows.Count > 0;
            foreach (var row in rows)
            {
                if (row["order"] is int order && PotionUseListenerOwnerIndex(method, listeners[order]) is >= 0 and var index)
                {
                    row["owner_index"] = index;
                    row["supported"] = true;
                }
                else
                {
                    row["supported"] = false;
                    string detail = row["order"] == null ? "draw pile order is hidden" : "unsupported or unlocated listener";
                    reasons.Add($"{method}: {row["model_id"]}: {detail}");
                }
            }
            bool known = reasons.Count == previousReasons;
            pickKnown &= known;
            if (!pickOnly) skipKnown &= known;
            notifications.Add(new Dictionary<string, object?>
            {
                ["notification"] = notification,
                ["scope"] = runScope ? "run" : "combat",
                ["pick_only"] = pickOnly,
                ["listener_count"] = listeners.Count,
                ["listeners"] = rows
            });
        }
        return (new Dictionary<string, object?>
        {
            ["slot"] = slot,
            ["potion_id"] = potion.Id.Entry,
            ["known"] = pickKnown && skipKnown,
            ["pick_known"] = pickKnown,
            ["skip_known"] = skipKnown,
            ["notifications"] = notifications,
            ["unavailable_reasons"] = reasons
        }, hasOverrides);
    }

    // Fixed-DLL audit. Individual candidate overrides are handled by the consumer for
    // the newly added card. Existing overrides with unresolved conditions stay visible.
    private static bool MayInterveneInChoicePotion(string method, AbstractModel model, Type declarer,
        PotionModel potion, bool earlierStagesKnown) => method switch
    {
        // These bodies only change the entering card when it is this exact card (or the affliction's parent).
        nameof(AbstractModel.AfterCardEnteredCombat) => declarer != typeof(Stomp) && declarer != typeof(Pinpoint)
            && declarer != typeof(BansheesCry) && declarer != typeof(Midnight) && declarer != typeof(Flatten)
            && declarer != typeof(Hexed) && declarer != typeof(SwordSagePower)
            && ((model is not (SmoggyPower or VitalSparkPower)) || potion is SkillPotion or ColorlessPotion)
            && (model is not TangledPower || potion is AttackPotion or ColorlessPotion)
            && (model is not GalvanicPower || potion is PowerPotion or ColorlessPotion),
        // Self-only and deck-only bodies. A newly generated pick goes to Hand or Discard.
        nameof(AbstractModel.AfterCardChangedPiles) => declarer != typeof(SovereignBlade) && declarer != typeof(SpoilsMap)
            && declarer != typeof(BookOfFiveRings) && declarer != typeof(BingBong) && declarer != typeof(DarkstonePeriapt)
            && declarer != typeof(LuckyFysh) && declarer != typeof(Hoarder),
        // SoulFysh only updates the soundtrack.
        nameof(AbstractModel.AfterCardChangedPilesLate) => declarer != typeof(SoulFysh),
        // The typed potions select Attack/Skill/Power. The fixed ColorlessCardPool also
        // has only these types. None includes the Token or Status pools (Soul, Wither, Sadness).
        nameof(AbstractModel.AfterCardGeneratedForCombat) => model switch
        {
            RocketPunch or SmokestackPower or TrashToTreasurePower or Aeonglass or AllIsWellPower or SoulboundPower or Fertilizer => false,
            ArsenalPower or PillarOfCreationPower when model is PowerModel power => ReferenceEquals(power.Owner, potion.Owner.Creature),
            Regalite relic => ReferenceEquals(relic.Owner, potion.Owner),
            _ => true
        },
        nameof(AbstractModel.BeforePotionUsed) when model is SurroundedPower power =>
            ReferenceEquals(power.Owner.Player, potion.Owner),
        nameof(AbstractModel.AfterPotionUsed) when model is ReptileTrinket relic =>
            ReferenceEquals(relic.Owner, potion.Owner),
        nameof(AbstractModel.AfterPotionUsed) when earlierStagesKnown && model is BeltBuckle relic =>
            !relic.Owner.PotionSlots.Any(other => other != null && !ReferenceEquals(other, potion)),
        nameof(AbstractModel.AfterHandEmptied) when model is UnceasingTop relic =>
            ReferenceEquals(relic.Owner, potion.Owner)
            && (!earlierStagesKnown || relic.Owner.PlayerCombatState?.Hand.Cards.Count is null or 0),
        _ => true
    };
}
