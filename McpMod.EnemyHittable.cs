using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    // battle.enemies[].hittable: Creature.IsHittable as the game answers it (sts2-ae309f85):
    // IsDead -> false, else Hook.ShouldAllowHitting(CombatState, creature) over the combat
    // hook listeners (ReattachPower / IllusionPower / AdaptablePower: not while reviving;
    // DieForYouPower / WuppysLoyaltyPower: creature.IsAlive).
    // CombatState.HittableEnemies and CardCmd.AutoPlay's random target use the same getter.
    // Read on every listed enemy, alive or not. Never derived from hp, alive or power names;
    // a failed read is null with a reason, distinct from false.
    private static void AttachEnemyHittable(Dictionary<string, object?> state, Creature creature, string entityId)
    {
        try { state["hittable"] = creature.IsHittable; }
        catch (Exception ex)
        {
            state["hittable"] = null;
            if (state.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
                state["unavailable_reasons"] = reasons = new Dictionary<string, string>();
            reasons["hittable"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"battle.enemies.{entityId}.hittable", ex);
        }
    }

    // " | Hittable: yes|no|null (reason)"; nothing for an old state without the key.
    private static string FormatEnemyHittable(Dictionary<string, object?> enemy)
    {
        if (!enemy.TryGetValue("hittable", out var value)) return "";
        return value switch
        {
            true => " | Hittable: yes",
            false => " | Hittable: no",
            _ => $" | Hittable: null ({(enemy.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>)?.GetValueOrDefault("hittable") ?? "unavailable"})"
        };
    }
}
