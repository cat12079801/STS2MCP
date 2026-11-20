using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string PotionGenerationPoolSource =
        "PotionFactory.GetPotionOptions(Player)=Character.PotionPool.GetUnlockedPotions(UnlockState)+SharedPotionPool.GetUnlockedPotions(UnlockState)";

    /// <summary>
    /// <c>player.potion_generation_pool</c>: the potions a random potion is drawn from (v0.113.1 PotionFactory.GetPotionOptions,
    /// the list CreateRandomPotionsOutOfCombat / CreateRandomPotionInCombat filter and draw from). The game's own public static
    /// method builds the list; the MOD does not copy its conditions. <c>can_be_generated_in_combat</c> is the filter
    /// CreateRandomPotionInCombat adds (PotionModel.CanBeGeneratedInCombat); the rarity is what the draw weights by
    /// (the weights and the draw itself are not published).
    /// </summary>
    private static Dictionary<string, object?> BuildPotionGenerationPool(Player player)
    {
        var result = new Dictionary<string, object?>
        {
            ["known"] = false,
            ["potions"] = new List<Dictionary<string, object?>>(),
            ["source"] = PotionGenerationPoolSource
        };
        try
        {
            result["character_id"] = player.Character.Id.Entry;
            result["player_id"] = player.NetId.ToString();
            result["potions"] = PotionFactory.GetPotionOptions(player)
                .OrderBy(p => p.Id.Entry, StringComparer.Ordinal)
                .Select(p => new Dictionary<string, object?>
                {
                    ["id"] = p.Id.Entry,
                    ["rarity"] = p.Rarity.ToString(),
                    ["can_be_generated_in_combat"] = p.CanBeGeneratedInCombat
                })
                .ToList();
            result["known"] = true;
        }
        catch (Exception ex)
        {
            result["reason"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("player.potion_generation_pool", ex);
        }
        return result;
    }
}
