using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;

namespace STS2_MCP;

public static partial class McpMod
{
    // 実際にロードした本体の版。固定の出典ラベルで別版を信頼させない。
    private static readonly Lazy<string> GenerationAssemblySha256 = new(() =>
    {
        using var stream = File.OpenRead(typeof(CardModel).Assembly.Location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    });

    // pool 契約の版。v0.113.1（sts2-a671e9ef）の CardFactory.GetDistinctForCombat は
    // private な FilterForRun（= CardModel.IsAllowedInRun）を通す。版1は人数条件だけを手で写していた。
    private const int GenerationPoolFilterVersion = 2;
    private const string GenerationPoolFilterSource =
        "CardPoolModel.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;OrobicAcid.OnUse:Attack,Skill,Power";
    private const string ColorlessGenerationPoolFilterSource =
        "ColorlessCardPool.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;JackOfAllTrades.OnPlay:ExcludeSelf";
    private const string ColorlessPotionGenerationPoolFilterSource =
        "ColorlessCardPool.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;ColorlessPotion.OnUse";

    private const string OtherCharacterAttackGenerationPoolFilterSource =
        "UnlockState.CharacterCardPools(Character.CardPool removed when Count>1).GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;Splash.OnPlay:Attack";

    // OrobicAcid.OnUse: キャラの解放済み pool → GetDistinctForCombat（FilterForRun → FilterForCombat）を
    // Attack/Skill/Power の型ごとに抽選する。その抽選前の候補一覧。
    private static Dictionary<string, object?> BuildGenerationPool(Player player) =>
        BuildCombatGenerationPool(player, "player.generation_pool", GenerationPoolFilterSource,
            p => p.Character.CardPool.GetUnlockedCards(p.UnlockState, p.RunState.CardMultiplayerConstraint),
            c => c.Type is CardType.Attack or CardType.Skill or CardType.Power);

    // JackOfAllTrades.OnPlay: 無色の解放済み pool から自身（JackOfAllTrades）を除き、
    // GetDistinctForCombat（FilterForRun → FilterForCombat）で抽選する。その抽選前の候補一覧。
    private static Dictionary<string, object?> BuildColorlessGenerationPool(Player player) =>
        BuildCombatGenerationPool(player, "player.colorless_generation_pool", ColorlessGenerationPoolFilterSource,
            p => GetUnlockedColorlessCards(p).Where(c => c is not JackOfAllTrades),
            _ => true);

    // ColorlessPotion.OnUse passes the entire unlocked colorless pool to GetDistinctForCombat.
    private static Dictionary<string, object?> BuildColorlessPotionGenerationPool(Player player) =>
        BuildCombatGenerationPool(player, "player.colorless_potion_generation_pool", ColorlessPotionGenerationPoolFilterSource,
            GetUnlockedColorlessCards, _ => true);

    private static IEnumerable<CardModel> GetUnlockedColorlessCards(Player player) =>
        ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint);

    // Splash.OnPlay: UnlockState.CharacterCardPools を列挙し、2 つ以上あれば自キャラの Character.CardPool を除く
    // （1 つだけなら除かない）。各 pool の GetUnlockedCards から Attack だけを GetDistinctForCombat
    // （FilterForRun → FilterForCombat）で 3 枚抽選する。その抽選前の候補一覧。抽選の結果は出さない。
    private static Dictionary<string, object?> BuildOtherCharacterAttackGenerationPool(Player player) =>
        BuildCombatGenerationPool(player, "player.other_character_attack_generation_pool", OtherCharacterAttackGenerationPoolFilterSource,
            p =>
            {
                var pools = p.UnlockState.CharacterCardPools.ToList();
                if (pools.Count > 1) pools.Remove(p.Character.CardPool);
                return pools.SelectMany(pool => pool.GetUnlockedCards(p.UnlockState, p.RunState.CardMultiplayerConstraint));
            },
            c => c.Type == CardType.Attack);

    private static Dictionary<string, object?> BuildCombatGenerationPool(Player player, string warningPath,
        string filterSource, Func<Player, IEnumerable<CardModel>> unlockedCards, Func<CardModel, bool> keep)
    {
        var result = new Dictionary<string, object?>
        {
            ["known"] = false,
            ["cards"] = new List<Dictionary<string, object?>>(),
            ["filter_version"] = GenerationPoolFilterVersion,
            ["filter_source"] = filterSource
        };
        try
        {
            var run = player.RunState;
            result["character_id"] = player.Character.Id.Entry;
            result["player_id"] = player.NetId.ToString();
            result["players"] = run.Players.Count;
            result["multiplayer_constraint"] = run.CardMultiplayerConstraint.ToString();
            result["game_assembly_sha256"] = GenerationAssemblySha256.Value;
            RequireGenerationPoolOwner(player, run.Players);

            // FilterForRun は private なので、同じ式の public な CardModel.IsAllowedInRun を呼ぶ
            // （人数の制約と GameMode.Blitz の IsAllowedInBlitz）。条件を MOD に写さない。
            var cards = CardFactory.FilterForCombat(unlockedCards(player).Where(c => c.IsAllowedInRun(run)))
                .Where(keep)
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
            Warn(warningPath, ex);
        }
        return result;
    }

    private static void RequireGenerationPoolOwner(Player player, IEnumerable<Player> players)
    {
        if (!players.Any(p => ReferenceEquals(p, player)) || players.Count(p => p.NetId == player.NetId) != 1
            || string.IsNullOrWhiteSpace(player.Character.Id.Entry))
            throw new InvalidOperationException("the owner or character cannot be identified in run.Players");
    }
}
