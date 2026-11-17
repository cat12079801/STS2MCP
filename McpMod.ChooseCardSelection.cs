using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // Choose-a-card rows. `holderCards` is FindAllSortedByPosition<NGridCardHolder>(screen)
    // mapped to holder.CardModel, the same list ExecuteSelectCard indexes, so `index` is the
    // holder position. A holder without a CardModel gets no row (a warning names its index)
    // and select_card refuses it instead of pressing a different card.
    //
    // During a live combat the rows are the generated card instances themselves
    // (CardFactory.GetDistinctForCombat -> CombatState.CreateCard), not yet in any pile.
    // Read them like hand cards: uid, details, playability, exhaust flag, energy/star cost.
    // Nothing here selects, frees (SetToFreeThisTurn), adds or rolls a card; the shown
    // pre-selection cost is not rewritten to 0.
    private static List<Dictionary<string, object?>> BuildChooseCardRows(
        IReadOnlyList<CardModel?> holderCards, ICombatState? combatState)
    {
        if (combatState != null) RefreshCardUidRegistry(combatState);
        var rows = new List<Dictionary<string, object?>>(holderCards.Count);
        for (int index = 0; index < holderCards.Count; index++)
        {
            if (holderCards[index] is not { } card)
            {
                Warn($"card_select.cards[{index}]", "choose holder has no CardModel; select_card refuses this index");
                continue;
            }
            Dictionary<string, object?> row;
            if (combatState == null)
            {
                row = BuildCardInfo(card);
            }
            else
            {
                // Same order as BuildCardState: details first, playability reuses its reasons.
                row = BuildCardInfo(card, includeType: false);
                AttachCardDetails(row, card);
                row["uid"] = GetStableCardUid(card);
                row["exhaust_on_next_play"] = SafeGetBool(() => card.ExhaustOnNextPlay);
                row["energy_cost"] = BuildCardEnergyCostState(card);
                row["star_cost_state"] = BuildCardStarCostState(card);
                AttachCardPlayability(row, card);
            }
            row["index"] = index;
            rows.Add(row);
        }
        return rows;
    }

    // Live combat only: CombatManager.IsInProgress with an available combat state.
    private static ICombatState? GetLiveCombatStateForSelection()
    {
        var manager = CombatManager.Instance;
        return manager is { IsInProgress: true } ? manager.DebugOnlyGetState() : null;
    }
}
