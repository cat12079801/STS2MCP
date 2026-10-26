using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // 実際にロードした本体の版。固定の出典ラベルで別版を信頼させない。
    private static readonly Lazy<string> GenerationAssemblySha256 = new(() =>
    {
        using var stream = File.OpenRead(typeof(CardModel).Assembly.Location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    });

    private static Dictionary<string, object?> BuildGenerationPool(Player player)
    {
        var result = new Dictionary<string, object?>
        {
            ["known"] = false,
            ["cards"] = new List<Dictionary<string, object?>>(),
            ["filter_version"] = 1,
            ["filter_source"] = "CardPoolModel.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForPlayerCount;CardFactory.FilterForCombat;OrobicAcid.OnUse:Attack,Skill,Power"
        };
        try
        {
            var run = player.RunState;
            result["character_id"] = player.Character.Id.Entry;
            result["player_id"] = player.NetId.ToString();
            result["players"] = run.Players.Count;
            result["multiplayer_constraint"] = run.CardMultiplayerConstraint.ToString();
            result["game_assembly_sha256"] = GenerationAssemblySha256.Value;
            if (run.Players.Count < 1 || !run.Players.Contains(player))
                throw new InvalidOperationException("対象playerがrun.Playersに存在しない");

            var unlocked = player.Character.CardPool.GetUnlockedCards(player.UnlockState, run.CardMultiplayerConstraint);
            // privateなFilterForPlayerCountと同じ条件（sts2-9cb4f1ad）。
            var excluded = run.Players.Count > 1
                ? CardMultiplayerConstraint.SingleplayerOnly : CardMultiplayerConstraint.MultiplayerOnly;
            var cards = CardFactory.FilterForCombat(unlocked.Where(c => c.MultiplayerConstraint != excluded))
                .Where(c => c.Type is CardType.Attack or CardType.Skill or CardType.Power)
                .OrderBy(c => c.Id.Entry, StringComparer.Ordinal)
                .Select(c => new Dictionary<string, object?> { ["id"] = c.Id.Entry, ["type"] = c.Type.ToString() })
                .ToList();
            // 抽選・CreateCard・RNGには触らない。列挙完了後だけknownを立てる。
            result["cards"] = cards;
            result["known"] = true;
        }
        catch (Exception ex)
        {
            result["reason"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("player.generation_pool", ex);
        }
        return result;
    }
}
