using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// "combat_instance" of an in-combat selection candidate (card_select, choose, hand_select,
    /// and a card_reward opened during a live combat; sts2#1019). The row keeps its screen
    /// index; this object says which card instance the engine is comparing:
    ///
    /// - source "pile": the candidate is in exactly one of its owner's combat piles (Hand, Draw,
    ///   Discard, Exhaust, Play), so its effect inputs are already public on that pile's row.
    ///   pile names the pile and uid the row's uid (the same registry the piles use).
    /// - source "detached": the candidate is in no combat pile (a generated three-choice
    ///   candidate, or a card in a non-combat pile, named in pile). No uid is issued: values
    ///   holds the per-instance inputs read off the candidate itself (type, target type,
    ///   upgrade level and cap, numbers, enchantment and affliction, local and effective
    ///   keyword ids, single-turn Retain / Sly, replay counts, the Mad Science rider, energy and
    ///   star cost). Legal targets and playability are not evaluated for a detached card.
    ///
    /// A failed read keeps the key with a null value and a reason in unavailable_reasons (here,
    /// or in values.unavailable_reasons for the values themselves). Read only: Owner.Piles and
    /// CardPile.Cards are walked by reference, nothing is moved, played, cloned or rolled.
    /// </summary>
    private static Dictionary<string, object?> BuildSelectionCandidateInstance(CardModel card)
    {
        var reasons = new Dictionary<string, string>();
        var state = new Dictionary<string, object?>
        {
            ["source"] = null,
            ["pile"] = null,
            ["unavailable_reasons"] = reasons
        };
        List<(string Type, bool Combat)> piles;
        try
        {
            // CardModel.Pile is Owner.Piles.FirstOrDefault(contains): count every pile instead,
            // so a card listed twice is reported rather than mapped to the first one.
            piles = card.Owner.Piles
                .Where(pile => pile.Cards.Any(other => ReferenceEquals(other, card)))
                .Select(pile => (pile.Type.ToString(), pile.IsCombatPile))
                .ToList();
        }
        catch (Exception ex)
        {
            reasons["source"] = reasons["pile"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("card.combat_instance.source", ex);
            return state;
        }
        if (piles.Count > 1)
        {
            reasons["source"] = reasons["pile"] =
                $"ambiguous: in {piles.Count} piles ({string.Join(", ", piles.Select(pile => pile.Type))})";
            Warn("card.combat_instance.source", reasons["source"]);
            return state;
        }
        if (piles.Count == 1 && piles[0].Combat)
        {
            state["source"] = CandidateSourcePile;
            state["pile"] = piles[0].Type;
            ReadCardDetailValue(state, reasons, "uid", () => GetStableCardUid(card));
            return state;
        }
        state["source"] = CandidateSourceDetached;
        state["pile"] = piles.Count == 1 ? piles[0].Type : null;
        state["values"] = BuildDetachedCandidateValues(card);
        return state;
    }

    /// <summary>
    /// The effect inputs of a card that is in no combat pile: the live rows' readers
    /// (AttachCardDetails, the keyword sets, the energy / star cost states) without the
    /// playability check or target previews. with_all_modifiers is null with
    /// detached_preview_context: global cost hooks depend on the pile the card would be in.
    /// </summary>
    private static Dictionary<string, object?> BuildDetachedCandidateValues(CardModel card)
    {
        var values = new Dictionary<string, object?> { ["id"] = null };
        AttachCardDetails(values, card);
        var reasons = (Dictionary<string, string>)values["unavailable_reasons"]!;
        ReadCardDetailValue(values, reasons, "id", () => card.Id.Entry);
        ReadCardDetailValue(values, reasons, "upgrade_level", () => card.CurrentUpgradeLevel);
        ReadCardDetailValue(values, reasons, "max_upgrade_level", () => card.MaxUpgradeLevel);
        ReadCardDetailValue(values, reasons, "local_keyword_ids",
            () => KeywordIds(card.GetKeywordsWithSources(KeywordSources.Local)));
        ReadCardDetailValue(values, reasons, "keyword_ids", () => KeywordIds(card.Keywords));
        ReadCardDetailValue(values, reasons, "exhaust_on_next_play", () => card.ExhaustOnNextPlay);
        values["energy_cost"] = BuildCardEnergyCostState(card, detachedPreview: true);
        values["star_cost_state"] = BuildCardStarCostState(card);
        return values;
    }
}
