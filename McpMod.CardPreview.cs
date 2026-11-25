using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    private static string? AddCardPreviewValues(Dictionary<string, object?> preview,
        Dictionary<string, object?> values, Creature target, string? cardId = null,
        bool targetHasVulnerable = false, bool ownerLostHpThisTurn = false)
    {
        string? damageVar = PickDamageVarName(name => values.GetValueOrDefault(name) is int);
        int? damage = damageVar != null ? (int)values[damageVar]! : null;
        if (damage != null)
            preview["damage"] = damage;

        int? hits = ResolveCardPreviewHitCount(values, cardId, targetHasVulnerable, ownerLostHpThisTurn);
        if (hits != null)
        {
            preview["hits"] = hits;
            if (damage != null)
                preview["total_damage"] = damage * hits;
        }

        int? block = PickDynamicVar(values, "CalculatedBlock", "Block");
        if (block != null)
            preview["block"] = block;

        if (damage != null)
        {
            var cap = BuildDamageCap(target);
            if (cap != null)
                preview["damage_cap"] = cap;
        }
        return damageVar;
    }

    private static string? PickDamageVarName(Func<string, bool> hasValue) =>
        hasValue("CalculatedDamage") ? "CalculatedDamage" : hasValue("Damage") ? "Damage" : null;

    // Pure correction of already-previewed values. Repeat describes non-attack effects in
    // v0.113. Dismantle/Spite choose a single hit in OnPlay when their condition is false;
    // even Thumbscrew leaves that single hit alone. On the multi-hit branch, keep the
    // preview value (including Thumbscrew and upgrades) without applying hooks twice.
    private static int? ResolveCardPreviewHitCount(Dictionary<string, object?> values,
        string? cardId, bool targetHasVulnerable, bool ownerLostHpThisTurn)
    {
        int? hits = PickDynamicVar(values, "HitCount", "CalculatedHits");
        if (hits == null) return null;
        return cardId switch
        {
            "DISMANTLE" when !targetHasVulnerable => 1,
            "SPITE" when !ownerLostHpThisTurn => 1,
            _ => hits
        };
    }

    /// <summary>
    /// Display metadata for damage caps. DamageVar and CalculatedDamageVar already
    /// include these effects in the targeted game preview.
    /// </summary>
    private static readonly Dictionary<string, string> _perHitDamageCaps = new()
    {
        ["SlipperyPower"] = "HP loss after Block and Osty capped at 1 until Slippery is spent",
        ["IntangiblePower"] = "damage before Block and HP loss after Osty each capped at 1",
        ["HardToKillPower"] = "HP loss after Block, before Osty redirects it, capped at the power's amount"
    };

    private const string HardenedShellPowerName = "HardenedShellPower";

    /// <summary>
    /// Describes the first recognized cap on this target for display.
    /// </summary>
    private static Dictionary<string, object?>? BuildDamageCap(Creature target)
    {
        try
        {
            foreach (var power in target.Powers)
            {
                string typeName = power.GetType().Name;

                if (_perHitDamageCaps.TryGetValue(typeName, out var note))
                {
                    return new Dictionary<string, object?>
                    {
                        ["capped_by"] = SafeGetText(() => power.Title) ?? typeName,
                        ["power_id"] = SafeGetText(() => power.Id.Entry),
                        ["per_hit_max"] = typeName == "HardToKillPower" ? power.Amount : 1,
                        ["note"] = note
                    };
                }

                if (typeName == HardenedShellPowerName)
                {
                    return new Dictionary<string, object?>
                    {
                        ["capped_by"] = SafeGetText(() => power.Title) ?? typeName,
                        ["power_id"] = SafeGetText(() => power.Id.Entry),
                        ["per_turn_max"] = power.Amount,
                        ["note"] = "HP loss after Block, before Osty, capped by the remaining allowance this turn"
                    };
                }
            }
        }
        catch (Exception ex) { Warn("card.damage_cap", ex); }

        return null;
    }

    private static int? PickDynamicVar(Dictionary<string, object?> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && value is int i)
                return i;
        }
        return null;
    }
}
