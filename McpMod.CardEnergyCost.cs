using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // sts2-d009478a: CardEnergyCost.GetWithModifiers and LocalCostModifier.
    // Read the live ordered list, never reconstruct modifiers from card/play history.
    private static Dictionary<string, object?> BuildCardEnergyCostState(CardModel card)
    {
        var reasons = new Dictionary<string, string>();
        var state = new Dictionary<string, object?>
        {
            ["costs_x"] = null,
            ["base"] = null,
            ["base_raw"] = null,
            ["canonical"] = null,
            ["captured_x_value"] = null,
            ["with_local_modifiers"] = null,
            ["with_all_modifiers"] = null,
            ["local_modifiers"] = null,
            ["unavailable_reasons"] = reasons
        };

        CardEnergyCost cost;
        try { cost = card.EnergyCost ?? throw new InvalidOperationException("EnergyCost is null"); }
        catch (Exception ex)
        {
            foreach (string key in state.Keys.Where(key => key != "unavailable_reasons").ToArray())
                reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("card.energy_cost", ex);
            return state;
        }

        ReadEnergyCostValue(state, reasons, "costs_x", () => cost.CostsX);
        ReadEnergyCostValue(state, reasons, "base_raw", () => cost.GetWithModifiers(CostModifiers.None));
        ReadEnergyCostValue(state, reasons, "canonical", () => cost.Canonical);
        if (state["costs_x"] is true)
            ReadEnergyCostValue(state, reasons, "captured_x_value", () => cost.CapturedXValue);
        else
            reasons["captured_x_value"] = state["costs_x"] is false ? "not_x_cost" : "costs_x_unavailable";
        ReadEnergyCostValue(state, reasons, "local_modifiers", () => ReadLocalEnergyCostModifiers(cost));

        string? unavailable = state["costs_x"] switch
        {
            true => "x_cost",
            false => null,
            _ => "costs_x_unavailable"
        };
        if (unavailable == null)
        {
            // None reads the current base, including upgrades/custom base changes.
            // Canonical is the printed starting cost and is not a substitute for it.
            state["base"] = state["base_raw"];
            if (reasons.TryGetValue("base_raw", out string? baseReason)) reasons["base"] = baseReason;
            if (state["base"] is not int baseCost) unavailable = "base_unavailable";
            else if (baseCost < 0) unavailable = "no_energy_cost";
        }

        if (unavailable != null)
        {
            foreach (string key in new[] { "base", "with_local_modifiers", "with_all_modifiers" })
            {
                state[key] = null;
                reasons.TryAdd(key, unavailable);
            }
        }
        else
        {
            // Use the game's own calculation, including its ordering/reduceOnly/clamp.
            // A failed reflection read must not hide still-readable public cost values.
            ReadEnergyCostValue(state, reasons, "with_local_modifiers", () => cost.GetWithModifiers(CostModifiers.Local));
            ReadEnergyCostValue(state, reasons, "with_all_modifiers", () => cost.GetWithModifiers(CostModifiers.All));
        }
        return state;
    }

    private static void ReadEnergyCostValue(Dictionary<string, object?> state,
        Dictionary<string, string> reasons, string key, Func<object?> read)
    {
        try { state[key] = read(); }
        catch (Exception ex)
        {
            state[key] = null;
            reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"card.energy_cost.{key}", ex);
        }
    }

    private static List<Dictionary<string, object?>> ReadLocalEnergyCostModifiers(object cost)
    {
        var field = cost.GetType().GetField("_localModifiers",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException(cost.GetType().Name, "_localModifiers");
        if (field.GetValue(cost) is not List<LocalCostModifier> modifiers)
            throw new InvalidOperationException("Expected _localModifiers to contain List<LocalCostModifier>");

        // Materialize values so later cleanup or upgrades cannot change this snapshot.
        return modifiers.Select(modifier => new Dictionary<string, object?>
        {
            ["amount"] = modifier.Amount,
            ["type"] = modifier.Type.ToString(),
            ["expiration"] = modifier.Expiration.ToString(),
            ["reduce_only"] = modifier.IsReduceOnly
        }).ToList();
    }
}
