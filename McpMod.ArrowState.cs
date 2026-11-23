using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> BuildArrowState(Player owner, ICombatState? combat)
    {
        const string context = "player.arrow_state";
        const string countKey = "available_count";
        var state = new Dictionary<string, object?> { ["unavailable_reasons"] = new Dictionary<string, string>() };
        PlayerCombatState CurrentPlayerCombat() => owner.PlayerCombatState
            ?? throw new InvalidOperationException("PlayerCombatState is unavailable");

        Dictionary<ArrowSlot, int>? indices = null;
        ReadInternalStateValue(state, ArrowSlotsKey, () =>
        {
            var slots = CurrentPlayerCombat().Arrows;
            indices = IndexArrowSlots(owner, slots);
            return slots.Select((slot, index) =>
            {
                var status = slot.Status;
                if (status is not (ArrowStatus.Retrieved or ArrowStatus.Away or ArrowStatus.Destroyed))
                    throw new InvalidOperationException($"Unknown ArrowStatus: {status}");
                return new Dictionary<string, object?>
                {
                    ["slot"] = index,
                    ["status"] = status.ToString(),
                    ["is_available"] = slot.IsAvailable,
                    ["destroy_after_impaled"] = slot.DestroyAfterImpaled
                };
            }).ToList();
        }, context);
        ReadInternalStateValue(state, countKey, () => CurrentPlayerCombat().AvailableArrowCount, context);
        if (state[ArrowSlotsKey] is List<Dictionary<string, object?>> rows && state[countKey] is int count
            && rows.Count(row => row["is_available"] is true) != count)
        {
            var ex = new InvalidOperationException("AvailableArrowCount disagrees with slot availability");
            state[ArrowSlotsKey] = state[countKey] = null;
            RecordInternalStateFailure(state, ArrowSlotsKey, ex, context);
            RecordInternalStateFailure(state, countKey, ex, context);
        }
        ReadInternalStateValue(state, ArrowImpalementsKey, () => BuildArrowImpalementRows(owner,
            combat ?? throw new InvalidOperationException("Owner combat state is unavailable"), indices), context);
        return state;
    }

    private static Dictionary<ArrowSlot, int> IndexArrowSlots(Player owner, IReadOnlyList<ArrowSlot> slots)
    {
        var indices = new Dictionary<ArrowSlot, int>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (!ReferenceEquals(slot.owner, owner))
                throw new InvalidOperationException($"Arrow slot {index} belongs to another player");
            indices.Add(slot, index);
        }
        return indices;
    }

    private static List<Dictionary<string, object?>> BuildArrowImpalementRows(Player owner, ICombatState combat,
        Dictionary<ArrowSlot, int>? indices)
    {
        RefreshEntityIdRegistry(combat);
        var rows = new List<Dictionary<string, object?>>();
        foreach (var target in combat.Creatures)
        {
            foreach (var power in target.Powers.OfType<ImpaledPower>())
            {
                if (!ReferenceEquals(power.Applier, owner.Creature)) continue;
                var row = new Dictionary<string, object?>
                {
                    ["target_combat_id"] = target.CombatId
                        ?? throw new InvalidOperationException("ImpaledPower target has no CombatId"),
                    ["hit_counter"] = power.HitCounter,
                    ["slot_indices"] = power.Arrows.Select(slot =>
                        indices != null && indices.TryGetValue(slot, out int index)
                            ? index : throw new InvalidOperationException("Impaled arrow is not in the owner's slot list"))
                        .ToList()
                };
                if (target.IsEnemy)
                {
                    if (target.Monster?.Id.Entry is not { Length: > 0 })
                        throw new InvalidOperationException("ImpaledPower enemy has no monster ID");
                    row["target_entity_id"] = GetStableEntityId(target);
                }
                rows.Add(row);
            }
        }
        return rows;
    }
}
