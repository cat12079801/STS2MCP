using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// The transformation candidates of every card in the player's combat piles, for EntropyPower:
    /// <c>EntropyPower.AfterPlayerTurnStart</c> picks hand cards and calls <c>CardCmd.TransformToRandom</c>,
    /// whose <c>CardTransformation(original)</c> (no options, <c>IsInCombat = original.CombatState != null</c>)
    /// draws one card from <c>CardFactory.GetDefaultTransformationOptions(original, isInCombat)</c>
    /// (<c>CardFactory.CreateRandomCardForTransform</c>). The candidate set comes from the card's pool, the
    /// player's unlocks and the run (public); the draw, its RNG and the draw-pile order are never read.
    /// Built only while the player can still transform this way (an EntropyPower on the creature, or an
    /// Entropy card in a combat pile), so other fights do not carry a large table. Null otherwise.
    /// </summary>
    private static Dictionary<string, object?>? BuildTransformationOptionsIfRelevant(Player player, PlayerCombatState combatState)
    {
        try
        {
            bool relevant = player.Creature.Powers.Any(p => p is EntropyPower)
                || combatState.AllPiles.Any(pile => pile.Cards.Any(c => c is Entropy));
            if (!relevant) return null;
        }
        catch (Exception ex)
        {
            Warn("player.transformation_options", ex);
            return new Dictionary<string, object?>
            {
                ["known"] = false,
                ["source"] = TransformationOptionsSource,
                ["cards"] = new List<Dictionary<string, object?>>(),
                ["reason"] = $"{ex.GetType().Name}: {ex.Message}"
            };
        }
        return BuildTransformationOptions(
            () => combatState.AllPiles.SelectMany(pile => pile.Cards),
            card => card.Id.Entry,
            GetStableCardUid,
            card => CardFactory.GetDefaultTransformationOptions(card, card.CombatState != null).Select(c => c.Id.Entry));
    }
}
