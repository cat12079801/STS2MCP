using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int CombatLogVersion = 1;

    // ?combat_log_since=<seq> on GET /api/v1/singleplayer: the rows from that seq on instead of the last DefaultWindow.
    private static long? _combatLogSince;

    /// <summary>
    /// <c>battle.combat_log</c>: what happened in this fight, in order (docs/raw-full.md「戦闘の出来事の通し列」). The rows
    /// come from read-only Harmony patches (McpMod.CombatLogPatches.cs); each is fixed once recorded. <c>known</c> is false
    /// when a patch group is missing or a row could not be made, since a log with a hole looks complete.
    /// </summary>
    private static Dictionary<string, object?> BuildCombatLogState(ICombatState combat, long? since = null)
    {
        var snapshot = CombatLogStore.Read(combat, since ?? _combatLogSince, CombatLogStore.DefaultWindow);
        var result = new Dictionary<string, object?>
        {
            ["version"] = CombatLogVersion,
            ["known"] = snapshot.Reasons.Count == 0,
            ["combat_id"] = snapshot.CombatId,
            ["capacity"] = CombatLogStore.Capacity,
            ["default_window"] = CombatLogStore.DefaultWindow,
            ["first_seq"] = snapshot.FirstSeq,
            ["next_seq"] = snapshot.NextSeq,
            ["window_first_seq"] = snapshot.WindowFirstSeq,
            ["since_truncated"] = snapshot.SinceTruncated,
            ["rows"] = snapshot.Rows
        };
        if (snapshot.Reasons.Count > 0) result["unavailable_reasons"] = snapshot.Reasons;
        return result;
    }

    private static string? CombatLogCombatId()
    {
        try { return CombatManager.Instance.CurrentCombatId?.Value.ToString(); }
        catch { return null; }
    }

    /// <summary>
    /// Appends one row to the current fight's log (nothing outside a fight). <paramref name="fill"/> runs after the
    /// uid and entity id registries are refreshed for the fight, so card and enemy references match the state's.
    /// A failure marks the fight's log incomplete; it never reaches the game.
    /// </summary>
    private static void LogCombatRow(string kind, Action<Dictionary<string, object?>, ICombatState> fill)
        => LogCombatRowIf(kind, (row, combat) => { fill(row, combat); return true; });

    /// <summary>As <see cref="LogCombatRow"/>, but <paramref name="fill"/> returning false drops the row (nothing happened).</summary>
    private static void LogCombatRowIf(string kind, Func<Dictionary<string, object?>, ICombatState, bool> fill)
    {
        // Inside the shuffle's CardPileCmd.Add every row would follow the new draw order: counted, not written.
        if (_combatLogSuppressed.Value is { } suppressed) { suppressed.Count++; return; }
        CombatState? combat;
        try { combat = CombatManager.Instance?.DebugOnlyGetState(); }
        catch { return; }
        if (combat == null) return;
        try
        {
            RefreshCardUidRegistry(combat);
            RefreshEntityIdRegistry(combat);
            var row = new Dictionary<string, object?>
            {
                ["seq"] = null,
                ["kind"] = kind,
                ["round"] = combat.RoundNumber,
                ["side"] = combat.CurrentSide.ToString().ToLowerInvariant()
            };
            if (!fill(row, combat)) return;
            CombatLogStore.Append(combat, CombatLogCombatId(), row);
        }
        catch (Exception ex)
        {
            CombatLogStore.MarkBroken(combat, CombatLogCombatId(), $"{kind}: {ex.GetType().Name}: {ex.Message}");
            GD.PrintErr($"[STS2 MCP] combat_log {kind} row failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A card as the log names it: the combat uid while it is in a combat pile or the fight knows it, else its id only
    // (a deck card outside the fight's piles). Cards in the draw pile are named only where a caller has decided the
    // reference is public (a card arriving there from a public pile, a hook argument the game shows).
    private static Dictionary<string, object?>? LogCardRef(CardModel? card)
    {
        if (card == null) return null;
        var row = new Dictionary<string, object?> { ["card_id"] = card.Id.Entry };
        var pile = card.Pile;
        if (pile?.IsCombatPile == true || card.CombatState != null) row["uid"] = GetStableCardUid(card);
        row["pile"] = pile?.Type.ToString();
        return row;
    }

    private static Dictionary<string, object?>? LogCreatureRef(Creature? creature)
    {
        if (creature == null) return null;
        var row = new Dictionary<string, object?> { ["combat_id"] = creature.CombatId };
        if (creature.Player is { } player) row["player_id"] = player.NetId.ToString();
        else if (creature.IsEnemy) row["entity_id"] = GetStableEntityId(creature);
        else if (creature.Monster is { } monster) row["monster_id"] = monster.Id.Entry;
        return row;
    }

    private static Dictionary<string, object?>? LogPowerRef(PowerModel? power)
    {
        if (power == null) return null;
        var row = new Dictionary<string, object?> { ["power_id"] = power.Id.Entry, ["amount"] = power.Amount };
        Creature? owner = null;
        try { owner = power.Owner; } catch { /* a power not applied yet has no owner */ }
        row["owner"] = LogCreatureRef(owner);
        return row;
    }

    private static Dictionary<string, object?>? LogPotionRef(PotionModel? potion)
    {
        if (potion == null) return null;
        var row = new Dictionary<string, object?> { ["potion_id"] = potion.Id.Entry };
        try
        {
            var owner = potion.Owner;
            row["owner_player_id"] = owner.NetId.ToString();
            int slot = owner.PotionSlots.ToList().FindIndex(p => ReferenceEquals(p, potion));
            row["slot"] = slot >= 0 ? slot : null;
        }
        catch { row["slot"] = null; }
        return row;
    }

    // A model that took part (a modifier, cloned_by, a flashing relic): the same row as the hook listener rows.
    private static Dictionary<string, object?>? LogModelRef(AbstractModel? model, List<string> reasons)
        => model == null ? null : BuildHookListenerRow(model, reasons);

    // The game lists modifiers in hook listener order, where draw-pile cards stand in the hidden draw order: their slots
    // keep their places but the draw-pile models in them are put in the draw pile's display order.
    private static List<Dictionary<string, object?>> LogModelRefs(IEnumerable<AbstractModel>? models, List<string> reasons)
    {
        if (models == null) return new List<Dictionary<string, object?>>();
        var list = models.Where(m => m != null).ToList();
        var slots = Enumerable.Range(0, list.Count).Where(i => DrawPileOf(list[i]) != null).ToList();
        if (slots.Count > 1)
        {
            var byCard = slots.Select(i => list[i]).ToList();
            var rank = new Dictionary<CardModel, int>(ReferenceEqualityComparer.Instance);
            foreach (var card in OrderPileCardsForDisplay(byCard.Select(m => ListenerCard(m)!).Distinct(ReferenceEqualityComparer.Instance)
                         .Cast<CardModel>().ToList(), PileType.Draw))
                rank.TryAdd(card, rank.Count);
            var sorted = byCard.OrderBy(m => rank[ListenerCard(m)!]).ThenBy(m => m is CardModel ? 0 : m is AfflictionModel ? 1 : 2).ToList();
            for (int k = 0; k < slots.Count; k++) list[slots[k]] = sorted[k];
        }
        return list.Select(m => BuildHookListenerRow(m, reasons)).ToList();
    }

    private static string LogSnake(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
