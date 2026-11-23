using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // 戦闘の手札と 4 パイルの実個体に付ける。候補・切り離した強化プレビューは
    // 既存の BuildCardStarCostState を使う。
    private static void AttachCombatStarXValues(Dictionary<string, object?> state, CardModel card, PileType pile)
    {
        if (state.GetValueOrDefault("costs_x") is not true) return;
        const string context = "card.star_cost_state";
        const string starsKey = "stars_if_played";
        const string xKey = "x_value_if_played";

        ReadInternalStateValue(state, StarXLastStarsSpentKey, () => card.LastStarsSpent, context);
        ReadInternalStateValue(state, "resolved_x_value", () => card.ResolveStarXValue(), context);
        if (pile != PileType.Hand)
        {
            var reasons = (Dictionary<string, string>)state["unavailable_reasons"]!;
            state[starsKey] = state[xKey] = null;
            reasons[starsKey] = reasons[xKey] = "not_in_hand";
            return;
        }

        ReadInternalStateValue(state, starsKey, () => CalculateManualStarsToSpend(card), context);
        ReadInternalStateValue(state, xKey, () =>
        {
            if (state[starsKey] is not int stars)
                throw new InvalidOperationException("stars_if_played is unavailable");
            var combat = card.CombatState ?? throw new InvalidOperationException("Card combat state is unavailable");
            return Hook.ModifyXValue(combat, card, stars);
        }, context);
    }

    // CardModel.SpendResources の支払い開始前の計算。
    private static int CalculateManualStarsToSpend(CardModel card)
    {
        var owner = card.Owner;
        var combat = card.CombatState ?? throw new InvalidOperationException("Card combat state is unavailable");
        var playerCombat = owner.PlayerCombatState ?? throw new InvalidOperationException("PlayerCombatState is unavailable");
        int energy = playerCombat.Energy;
        int energyToSpend = card.EnergyCost.GetAmountToSpend();
        int starsToSpend = Math.Max(0, card.GetStarCostWithModifiers());
        if (energyToSpend > energy && Hook.ShouldPayExcessEnergyCostWithStars(combat, owner))
            starsToSpend += (energyToSpend - energy) * 2;
        return starsToSpend;
    }
}
