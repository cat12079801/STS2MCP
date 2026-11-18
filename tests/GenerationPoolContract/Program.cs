using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

STS2_MCP.McpMod.RunContractTests();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }
        private static List<string> Ids(Dictionary<string, object?> pool)
            => ((List<Dictionary<string, object?>>)pool["cards"]!).Select(c => (string)c["id"]!).ToList();

        public static void RunContractTests()
        {
            BeginStateWarnings();
            var owner = new Player();
            owner.RunState.Players.Add(owner);
            var blitzOnlyOut = new CardModel("BLITZ_FORBIDDEN", CardType.Skill) { BlitzForbidden = true };
            var attack = new CardModel("Z_ATTACK", CardType.Attack);
            var character = new List<CardModel>
            {
                attack, attack, // Distinct comes from FilterForCombat, not re-implemented here
                new("A_POWER", CardType.Power),
                blitzOnlyOut,
                new("MP_ONLY", CardType.Attack) { MultiplayerConstraint = CardMultiplayerConstraint.MultiplayerOnly },
                new("SP_ONLY", CardType.Skill) { MultiplayerConstraint = CardMultiplayerConstraint.SingleplayerOnly },
                new("NOT_GENERATED", CardType.Attack) { GeneratedInCombat = false },
                new("A_STATUS", CardType.Status),
            };
            int unlockCalls = 0;
            owner.Character.CardPool.Read = (unlocks, constraint) =>
            {
                unlockCalls++;
                Check(ReferenceEquals(unlocks, owner.UnlockState), "owner の UnlockState を渡す");
                Check(constraint == owner.RunState.CardMultiplayerConstraint, "run の CardMultiplayerConstraint を渡す（OrobicAcid.OnUse と同じ引数）");
                return character;
            };

            var pool = BuildGenerationPool(owner);
            Check(pool["known"] is true && Equals(pool["filter_version"], 2), "版2で known");
            Check(Equals(pool["filter_source"], "CardPoolModel.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;OrobicAcid.OnUse:Attack,Skill,Power"), "filter_source は FilterForRun を宣言する");
            Check(Ids(pool).SequenceEqual(new[] { "A_POWER", "BLITZ_FORBIDDEN", "SP_ONLY", "Z_ATTACK" }), "IsAllowedInRun → FilterForCombat → A/S/P、ID 昇順、重複なし");
            Check(CardModel.AllowedCalls.Count == character.Count && CardModel.AllowedCalls.All(c => ReferenceEquals(c.Run, owner.RunState)), "全候補に本体の IsAllowedInRun を owner の run で呼ぶ");
            Check(unlockCalls == 1 && CardFactory.FilterCalls == 1, "本体の GetUnlockedCards と FilterForCombat を1回ずつ");
            Check(Equals(pool["players"], 1) && Equals(pool["player_id"], "7") && Equals(pool["character_id"], "IRONCLAD") && pool["game_assembly_sha256"] is string { Length: 64 }, "既存のメタデータを保つ");

            owner.RunState.Blitz = true;
            Check(!Ids(BuildGenerationPool(owner)).Contains("BLITZ_FORBIDDEN"), "Blitz では IsAllowedInBlitz=false の札を本体の判定どおり除く");
            owner.RunState.Blitz = false;
            var ally = new Player();
            owner.RunState.Players.Add(ally);
            owner.RunState.CardMultiplayerConstraint = CardMultiplayerConstraint.MultiplayerOnly;
            var multi = BuildGenerationPool(owner);
            Check(Ids(multi).SequenceEqual(new[] { "A_POWER", "BLITZ_FORBIDDEN", "MP_ONLY", "Z_ATTACK" }) && Equals(multi["players"], 2), "複数人では SingleplayerOnly を除く");
            owner.RunState.Players.Remove(ally);
            owner.RunState.CardMultiplayerConstraint = CardMultiplayerConstraint.SingleplayerOnly;

            // JackOfAllTrades.OnPlay: colorless unlocked pool, minus itself, FilterForRun, FilterForCombat.
            var colorless = new List<CardModel>
            {
                new JackOfAllTrades(),
                new("MY_SHINIES", CardType.Skill) { BlitzForbidden = true },
                new("FLASH_OF_STEEL", CardType.Attack),
                new("CURSE_LIKE", CardType.Curse),
                new("NO_GEN", CardType.Skill) { GeneratedInCombat = false },
            };
            ModelDb.Colorless.Read = (unlocks, constraint) =>
            {
                Check(ReferenceEquals(unlocks, owner.UnlockState) && constraint == owner.RunState.CardMultiplayerConstraint, "無色も owner の UnlockState と run の制約");
                return colorless;
            };
            var colorlessPool = BuildColorlessGenerationPool(owner);
            Check(colorlessPool["known"] is true && Equals(colorlessPool["filter_version"], 2), "無色の最終候補も版2");
            Check(Equals(colorlessPool["filter_source"], "ColorlessCardPool.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;JackOfAllTrades.OnPlay:ExcludeSelf"), "無色の filter_source");
            Check(Ids(colorlessPool).SequenceEqual(new[] { "CURSE_LIKE", "FLASH_OF_STEEL", "MY_SHINIES" }), "自己除外し、型では絞らない（何でも屋は型を絞らない）");
            owner.RunState.Blitz = true;
            Check(!Ids(BuildColorlessGenerationPool(owner)).Contains("MY_SHINIES"), "Blitz では MY_SHINIES を除く");
            owner.RunState.Blitz = false;
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            ModelDb.Colorless.Read = (_, _) => throw new InvalidOperationException("pool renamed");
            var failed = BuildColorlessGenerationPool(owner);
            Check(failed["known"] is false && ((List<Dictionary<string, object?>>)failed["cards"]!).Count == 0 && ((string)failed["reason"]!).Contains("pool renamed"), "失敗は known=false・cards=[]・reason");
            Check(_stateWarnings.Any(w => w.StartsWith("player.colorless_generation_pool:")), "無色の warning パス");
            Check(BuildGenerationPool(owner)["known"] is true, "無色の失敗はキャラ pool に波及しない");
            var lateFailure = new List<CardModel> { attack };
            owner.Character.CardPool.Read = (_, _) => lateFailure.Concat(Throwing());
            var partial = BuildGenerationPool(owner);
            Check(partial["known"] is false && ((List<Dictionary<string, object?>>)partial["cards"]!).Count == 0, "遅延列挙の途中失敗で部分一覧を known にしない");
            owner.Character.CardPool.Read = (_, _) => character;
            owner.RunState.Players.Clear();
            var stranger = BuildGenerationPool(owner);
            Check(stranger["known"] is false && ((string)stranger["reason"]!).Contains("run.Players"), "run にいない player は known=false");

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string playerBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal)];
            int guard = playerBuilder.IndexOf("if (combatState != null && CombatManager.Instance.IsInProgress)", StringComparison.Ordinal);
            int wiring = playerBuilder.IndexOf("state[\"colorless_generation_pool\"] = BuildColorlessGenerationPool(player)", StringComparison.Ordinal);
            Check(guard >= 0 && wiring > guard, "無色の最終候補は戦闘中の共通 Player builder だけで出す");
            Check(playerBuilder.IndexOf("state[\"generation_pool\"] = BuildGenerationPool(player)", StringComparison.Ordinal) is >= 0 and var g && g < guard, "酸液の pool は従来どおり戦闘外も出す");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("Colorless generation pool:"), "Markdown の1行");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 6;"), "pool 契約は filter_version で版を示す（state 全体の schema は 6）");
            Console.WriteLine($"GenerationPoolContract: {Checks} checks passed");
        }

        private static IEnumerable<CardModel> Throwing()
        {
            yield return new CardModel("B_SKILL", CardType.Skill);
            throw new InvalidOperationException("enumeration failed");
        }
    }
}
