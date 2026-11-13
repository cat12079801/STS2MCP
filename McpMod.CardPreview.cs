using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AddCardPreviewValues(Dictionary<string, object?> preview,
        Dictionary<string, object?> values, Creature target, string? cardId = null,
        bool targetHasVulnerable = false, bool ownerLostHpThisTurn = false)
    {
        int? damage = PickDynamicVar(values, "CalculatedDamage", "Damage");
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
            var cap = BuildDamageCap(target, damage.Value, hits ?? 1);
            if (cap != null)
                preview["damage_cap"] = cap;
        }
    }

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
    /// Powers that cap how much HP a creature can lose, per hit. Damage modifiers do not
    /// see these — they are applied when the damage lands — so a 18-damage card against
    /// Slippery takes the enemy down by 1, and reporting 18 sends the caller into a
    /// fight it cannot win. Keyed by power type because that survives an id rename.
    /// </summary>
    private static readonly Dictionary<string, string> _perHitDamageCaps = new()
    {
        ["SlipperyPower"] = "next hit reduced to 1",
        ["IntangiblePower"] = "all damage reduced to 1",
        ["HardToKillPower"] = "all damage reduced to the power's amount"
    };

    private const string HardenedShellPowerName = "HardenedShellPower";

    /// <summary>
    /// Describes any cap on what this target can actually lose from one hit, and the
    /// resulting effective damage. Returns null when nothing caps it.
    /// </summary>
    private static Dictionary<string, object?>? BuildDamageCap(Creature target, int damage, int hits)
    {
        try
        {
            foreach (var power in target.Powers)
            {
                string typeName = power.GetType().Name;

                if (_perHitDamageCaps.TryGetValue(typeName, out var note))
                {
                    int perHit = typeName == "HardToKillPower" ? System.Math.Max(power.Amount, 0) : 1;
                    int capped = System.Math.Min(damage, perHit);
                    int remaining = typeName == "SlipperyPower" ? System.Math.Max(power.Amount, 0) : int.MaxValue;
                    int cappedHits = System.Math.Min(hits, remaining);

                    return new Dictionary<string, object?>
                    {
                        ["capped_by"] = SafeGetText(() => power.Title) ?? typeName,
                        ["power_id"] = SafeGetText(() => power.Id.Entry),
                        ["per_hit_max"] = perHit,
                        ["note"] = note,
                        ["effective_damage"] = capped * cappedHits + System.Math.Max(hits - cappedHits, 0) * damage
                    };
                }

                if (typeName == HardenedShellPowerName)
                {
                    return new Dictionary<string, object?>
                    {
                        ["capped_by"] = SafeGetText(() => power.Title) ?? typeName,
                        ["power_id"] = SafeGetText(() => power.Id.Entry),
                        ["per_turn_max"] = power.Amount,
                        ["note"] = "total HP lost this turn is capped, not per hit"
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
