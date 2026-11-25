using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    // battle.enemies[].can_receive_powers: Creature.CanReceivePowers as the game answers it (sts2-ae309f85):
    // CombatState == null -> false, else Hook.ShouldAllowHitting(CombatState, creature) over the combat hook
    // listeners (ReattachPower / IllusionPower / AdaptablePower: not while the owner is reviving;
    // DieForYouPower / WuppysLoyaltyPower: creature.IsAlive). Unlike IsHittable there is no IsDead check, so a
    // corpse kept in the fight can receive powers unless a listener refuses.
    // PowerCmd.Apply<T> and PowerCmd.Apply(power, ...) (on entry and again after power.BeforeApplied) skip a
    // target whose getter is false (Queen.BurnBrightForMeMove's Strength on every teammate, any power aimed at
    // a dead-but-listed enemy).
    // The read changes nothing: IterateHookListeners builds a fresh list, each override compares references
    // and reads a private bool or IsAlive; no RNG, no hook is invoked for its effect.
    // Read on every listed enemy, alive or not. Never derived from hittable, hp, alive or power names;
    // a failed read is null with a reason, distinct from false.
    private static void AttachEnemyCanReceivePowers(Dictionary<string, object?> state, Creature creature, string entityId)
    {
        try { state["can_receive_powers"] = creature.CanReceivePowers; }
        catch (Exception ex)
        {
            state["can_receive_powers"] = null;
            if (state.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
                state["unavailable_reasons"] = reasons = new Dictionary<string, string>();
            reasons["can_receive_powers"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"battle.enemies.{entityId}.can_receive_powers", ex);
        }
    }

    // " | Receives powers: yes|no|null (reason)"; nothing for an old state without the key.
    private static string FormatEnemyCanReceivePowers(Dictionary<string, object?> enemy)
    {
        if (!enemy.TryGetValue("can_receive_powers", out var value)) return "";
        return value switch
        {
            true => " | Receives powers: yes",
            false => " | Receives powers: no",
            _ => $" | Receives powers: null ({(enemy.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>)?.GetValueOrDefault("can_receive_powers") ?? "unavailable"})"
        };
    }
}
