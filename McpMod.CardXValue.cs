using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// <c>energy_cost.x_value_if_played</c> on an X-cost card in the hand: the X the card's effects would read if it were
    /// played from the hand now. A manual play spends <c>CardEnergyCost.GetAmountToSpend()</c> (all current energy for an
    /// X cost), <c>CardModel.SpendEnergy</c> stores it as <c>CapturedXValue</c>, and the effects read
    /// <c>CardModel.ResolveEnergyXValue()</c> = <c>Hook.ModifyXValue(CombatState, card, CapturedXValue)</c> (ChemicalX adds
    /// its Increase). The same pure calculation over the current energy; nothing is captured or spent. An auto-play
    /// (CardCmd.AutoPlay) captures the energy the same way.
    /// </summary>
    private static void AttachXValueIfPlayed(Dictionary<string, object?> energyCost, CardModel card)
    {
        if (energyCost.GetValueOrDefault("costs_x") is not true) return;
        var reasons = energyCost.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        try
        {
            if (card.Pile?.Type != PileType.Hand) throw new InvalidOperationException("not in the hand");
            var combat = card.CombatState ?? throw new InvalidOperationException("no combat state");
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("no fight in progress");
            energyCost["x_value_if_played"] = Hook.ModifyXValue(combat, card, card.EnergyCost.GetAmountToSpend());
        }
        catch (Exception ex)
        {
            energyCost["x_value_if_played"] = null;
            if (reasons != null) reasons["x_value_if_played"] = $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
