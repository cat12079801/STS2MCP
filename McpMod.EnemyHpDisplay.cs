using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    // battle.enemies[].hp_display_is_infinite: the game's own HpDisplayExtensions.IsInfinite over
    // Creature.HpDisplay (sts2-ae309f85). IsInfinite is true for InfiniteWithNumbers and
    // InfiniteWithoutNumbers, false for Normal; it is a pure static switch on the enum and the getter is an
    // auto-property, so the read changes nothing and draws no RNG.
    // Writers in v0.114.0: HardenedShellPower.AfterDamageReceived (InfiniteWithNumbers once the turn's
    // received damage reaches its Amount) / BeforeSideTurnStart (back to Normal), and
    // WaterfallGiant.TriggerAboutToBlowState (InfiniteWithoutNumbers for the death continuation).
    // Gameplay reader: HellraiserPower.AfterCardDrawnEarly (CombatState.HittableEnemies.All(IsInfinite)
    // caps the auto-played Strikes per turn). NHealthBar reads it only for the bar's look.
    // Read on every listed enemy, alive or not. Never derived from hp, max_hp or power names; a failed
    // read is null with a reason, distinct from false.
    private static void AttachEnemyHpDisplay(Dictionary<string, object?> state, Creature creature, string entityId)
    {
        try { state["hp_display_is_infinite"] = creature.HpDisplay.IsInfinite(); }
        catch (Exception ex)
        {
            state["hp_display_is_infinite"] = null;
            if (state.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
                state["unavailable_reasons"] = reasons = new Dictionary<string, string>();
            reasons["hp_display_is_infinite"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"battle.enemies.{entityId}.hp_display_is_infinite", ex);
        }
    }

    // Appended right after "HP: hp/max_hp": " ∞" when the display is infinite, nothing when it is not
    // (or for an old state without the key), " ∞: null (reason)" when unreadable.
    private static string FormatEnemyHpDisplay(Dictionary<string, object?> enemy)
    {
        if (!enemy.TryGetValue("hp_display_is_infinite", out var value)) return "";
        return value switch
        {
            true => " ∞",
            false => "",
            _ => $" ∞: null ({(enemy.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>)?.GetValueOrDefault("hp_display_is_infinite") ?? "unavailable"})"
        };
    }
}
