using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // sts2-a671e9ef: CardModel.BaseStarCost / CanonicalStarCost / HasStarCostX / CurrentStarCost
    // and the declared private _temporaryStarCosts (TemporaryCardCost list, latest wins).
    // Read the live ordered list; never reconstruct temporary costs from card/play history.
    // BaseStarCost's getter lazily copies CanonicalStarCost on first read; the existing
    // star_cost display (CurrentStarCost) already triggers that same idempotent read.
    private static Dictionary<string, object?> BuildCardStarCostState(CardModel card)
    {
        var reasons = new Dictionary<string, string>();
        var state = new Dictionary<string, object?> { ["unavailable_reasons"] = reasons };
        ReadStarCostValue(state, reasons, "costs_x", () => card.HasStarCostX);
        ReadStarCostValue(state, reasons, "canonical", () => card.CanonicalStarCost);
        ReadStarCostValue(state, reasons, "base", () => card.BaseStarCost);
        ReadStarCostValue(state, reasons, "current", () => card.CurrentStarCost);
        ReadStarCostValue(state, reasons, "temporary_costs", () =>
            ReadDeclaredStateField<List<TemporaryCardCost>>(card, typeof(CardModel), "_temporaryStarCosts")
                .Select(cost => new Dictionary<string, object?>
                {
                    ["cost"] = cost.Cost,
                    ["clears_when_turn_ends"] = cost.ClearsWhenTurnEnds,
                    ["clears_when_card_is_played"] = cost.ClearsWhenCardIsPlayed
                }).ToList());
        return state;
    }

    private static void ReadStarCostValue(Dictionary<string, object?> state,
        Dictionary<string, string> reasons, string key, Func<object?> read)
    {
        try { state[key] = read(); }
        catch (Exception ex)
        {
            state[key] = null;
            reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"card.star_cost_state.{key}", ex);
        }
    }
}
