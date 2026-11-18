using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace STS2_MCP;

// Live reads of NPlayerHand's in-combat selection (hand_select), shared by the state and the
// combat_select_card / combat_confirm_selection actions so both see the same thing (sts2#1037).
// Reads only: nothing here presses, selects or invokes the selection filter.
public static partial class McpMod
{
    /// <summary>NPlayerHand.PeekButton.IsPeeking; null when it cannot be read.</summary>
    private static bool? ReadHandPeeking(NPlayerHand hand)
    {
        try { return hand.PeekButton?.IsPeeking; }
        catch (Exception ex)
        {
            Warn("hand_select.peeking", ex);
            return null;
        }
    }

    /// <summary>
    /// NPlayerHand._selectedCards, copied: the models the game hands back on confirm, in selection
    /// order (the upgrade-mode preview card included). Null with a reason when the field is unreadable.
    /// </summary>
    private static List<CardModel>? ReadHandSelectedCards(NPlayerHand hand, out string? reason)
    {
        reason = null;
        try
        {
            if (GetInstanceFieldValue(hand, "_selectedCards") is List<CardModel> cards)
            {
                var copy = new List<CardModel>(cards.Count);
                foreach (var card in cards)
                {
                    if (card == null)
                    {
                        reason = "NPlayerHand._selectedCards contains null";
                        return null;
                    }
                    copy.Add(card);
                }
                return copy;
            }
            reason = "NPlayerHand._selectedCards is not a readable List<CardModel>";
        }
        catch (Exception ex) { reason = $"{ex.GetType().Name}: {ex.Message}"; }
        return null;
    }

    /// <summary>NPlayerHand._prefs: MinSelect / MaxSelect are what RefreshSelectModeConfirmButton checks.</summary>
    private static CardSelectorPrefs? ReadHandSelectPrefs(NPlayerHand hand, out string? reason)
    {
        reason = null;
        try
        {
            if (GetInstanceFieldValue(hand, "_prefs") is CardSelectorPrefs prefs) return prefs;
            reason = "NPlayerHand._prefs is not a readable CardSelectorPrefs";
        }
        catch (Exception ex) { reason = $"{ex.GetType().Name}: {ex.Message}"; }
        return null;
    }

    private static NConfirmButton? GetHandSelectConfirmButton(NPlayerHand hand)
        => hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");

    /// <summary>The selection's confirm button is shown and enabled: what a player could press.</summary>
    private static bool IsHandSelectConfirmPressable(NPlayerHand hand)
    {
        try
        {
            var button = GetHandSelectConfirmButton(hand);
            return button != null && button.IsEnabled && IsNodeVisible(button);
        }
        catch (Exception ex)
        {
            Warn("hand_select.can_confirm", ex);
            return false;
        }
    }
}
