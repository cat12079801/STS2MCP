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
            var ally = new Player { NetId = 8 };
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
            const string potionSource = "ColorlessCardPool.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;ColorlessPotion.OnUse";
            var potionPool = BuildColorlessPotionGenerationPool(owner);
            Check(potionPool["known"] is true && Equals(potionPool["filter_version"], 2) && Equals(potionPool["filter_source"], potionSource),
                "無色ポーションは版2と固有の出典");
            Check(Ids(potionPool).SequenceEqual(new[] { "CURSE_LIKE", "FLASH_OF_STEEL", "JACK_OF_ALL_TRADES", "MY_SHINIES" })
                && Equals(potionPool["player_id"], "7") && Equals(potionPool["players"], 1)
                && Equals(potionPool["multiplayer_constraint"], "SingleplayerOnly")
                && Equals(potionPool["game_assembly_sha256"], colorlessPool["game_assembly_sha256"]),
                "解放済みの何でも屋を含め、型と共通の所有者・人数・制約・DLL を維持する");
            Check(((List<Dictionary<string, object?>>)potionPool["cards"]!).Single(row =>
                Equals(row["id"], "JACK_OF_ALL_TRADES"))["type"] is "Skill", "候補に本体の型を付ける");
            colorless.RemoveAll(card => card is JackOfAllTrades);
            Check(Ids(BuildColorlessPotionGenerationPool(owner)).SequenceEqual(Ids(colorlessPool)), "何でも屋が未解放なら出ない");
            owner.RunState.Blitz = true;
            Check(!Ids(BuildColorlessGenerationPool(owner)).Contains("MY_SHINIES"), "Blitz では MY_SHINIES を除く");
            Check(!Ids(BuildColorlessPotionGenerationPool(owner)).Contains("MY_SHINIES"), "無色ポーションにもランのフィルターを適用");
            owner.RunState.Blitz = false;
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            var differentOwner = new Player { NetId = 8 };
            owner.RunState.Players.Add(differentOwner);
            differentOwner.RunState.Players.Add(differentOwner);
            ModelDb.Colorless.Read = (unlocks, _) => ReferenceEquals(unlocks, differentOwner.UnlockState)
                ? new[] { new CardModel("ALLY_OPTION", CardType.Power) } : [];
            var differentPool = BuildColorlessPotionGenerationPool(differentOwner);
            Check(Equals(differentPool["player_id"], "8") && Ids(differentPool).SequenceEqual(new[] { "ALLY_OPTION" })
                && Ids(BuildColorlessPotionGenerationPool(owner)).Count == 0,
                "別 owner の解放情報で候補を組み、既知の空集合を保持する");
            differentOwner.RunState.Players.Clear();
            Check(BuildColorlessPotionGenerationPool(differentOwner)["known"] is false, "run と結べない owner の候補は unknown");
            owner.RunState.Players.Remove(differentOwner);

            ModelDb.Colorless.Read = (_, _) => throw new InvalidOperationException("pool renamed");
            var failed = BuildColorlessGenerationPool(owner);
            Check(failed["known"] is false && ((List<Dictionary<string, object?>>)failed["cards"]!).Count == 0 && ((string)failed["reason"]!).Contains("pool renamed"), "失敗は known=false・cards=[]・reason");
            Check(_stateWarnings.Any(w => w.StartsWith("player.colorless_generation_pool:")), "無色の warning パス");
            ModelDb.Colorless.Read = (_, _) => Throwing();
            var failedPotion = BuildColorlessPotionGenerationPool(owner);
            Check(failedPotion["known"] is false && Ids(failedPotion).Count == 0 && ((string)failedPotion["reason"]!).Contains("enumeration failed")
                && _stateWarnings.Any(w => w.StartsWith("player.colorless_potion_generation_pool:")),
                "無色瓶の読み取り途中の失敗は部分一覧を出さず、専用の reason と warning を返す");
            Check(BuildGenerationPool(owner)["known"] is true, "無色の失敗はキャラ pool に波及しない");
            var lateFailure = new List<CardModel> { attack };
            owner.Character.CardPool.Read = (_, _) => lateFailure.Concat(Throwing());
            var partial = BuildGenerationPool(owner);
            Check(partial["known"] is false && ((List<Dictionary<string, object?>>)partial["cards"]!).Count == 0, "遅延列挙の途中失敗で部分一覧を known にしない");
            owner.Character.CardPool.Read = (_, _) => character;
            owner.RunState.Players.Clear();
            var stranger = BuildGenerationPool(owner);
            Check(stranger["known"] is false && ((string)stranger["reason"]!).Contains("run.Players"), "run にいない player は known=false");

            CheckOtherCharacterAttackPool();

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string playerBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal)];
            int guard = playerBuilder.IndexOf("if (creature != null && combatState != null && CombatManager.Instance.IsInProgress)", StringComparison.Ordinal);
            int wiring = playerBuilder.IndexOf("state[\"colorless_generation_pool\"] = BuildColorlessGenerationPool(player)", StringComparison.Ordinal);
            Check(guard >= 0 && wiring > guard, "無色の最終候補は戦闘中の共通 Player builder だけで出す");
            int potionGuard = playerBuilder.IndexOf("if (player.PotionSlots.Any(potion => potion is ColorlessPotion))", StringComparison.Ordinal);
            int potionWiring = playerBuilder.IndexOf("state[\"colorless_potion_generation_pool\"] = BuildColorlessPotionGenerationPool(player)", StringComparison.Ordinal);
            Check(potionGuard > guard && potionWiring > potionGuard, "無色ポーションを持つ戦闘にだけ専用 pool を付ける");
            Check(playerBuilder.IndexOf("state[\"generation_pool\"] = BuildGenerationPool(player)", StringComparison.Ordinal) is >= 0 and var g && g < guard, "酸液の pool は従来どおり戦闘外も出す");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("Colorless generation pool:"), "Markdown の1行");
            int splashWiring = playerBuilder.IndexOf("state[\"other_character_attack_generation_pool\"] = BuildOtherCharacterAttackGenerationPool(player)", StringComparison.Ordinal);
            Check(splashWiring > guard, "スプラッシュの候補は戦闘中の共通 Player builder だけで出す");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("Other-character attack pool:"), "スプラッシュの候補の Markdown の1行");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 12;"), "pool 契約は filter_version で版を示す（state 全体の schema は 12）");
            Check(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(sources, "McpMod.cs")), @"public const string StateSchema = ""1\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";"), "published schema MAJOR is 1 (the exact version is checked in V0113CompatContract)");
            Console.WriteLine($"GenerationPoolContract: {Checks} checks passed");
        }

        // Splash.OnPlay: UnlockState.CharacterCardPools, minus the owner's Character.CardPool when there is more
        // than one, each pool's GetUnlockedCards, Attack only, GetDistinctForCombat (FilterForRun, FilterForCombat).
        private static void CheckOtherCharacterAttackPool()
        {
            BeginStateWarnings();
            CardModel.AllowedCalls.Clear();
            var owner = new Player();
            owner.RunState.Players.Add(owner);
            var silent = new CardPoolModel();
            var defect = new CardPoolModel();
            var calls = new List<CardPoolModel>();
            Func<MegaCrit.Sts2.Core.Unlocks.UnlockState, CardMultiplayerConstraint, IEnumerable<CardModel>> Reader(CardPoolModel pool, params CardModel[] cards)
                => (unlocks, constraint) =>
                {
                    Check(ReferenceEquals(unlocks, owner.UnlockState) && constraint == owner.RunState.CardMultiplayerConstraint, "Splash.OnPlay と同じ引数（owner の UnlockState・run の制約）");
                    calls.Add(pool);
                    return cards;
                };
            var shared = new CardModel("SHARED_ATTACK", CardType.Attack);
            owner.Character.CardPool.Read = Reader(owner.Character.CardPool, new CardModel("OWN_ATTACK", CardType.Attack), new CardModel("OWN_SKILL", CardType.Skill));
            silent.Read = Reader(silent, new CardModel("S_ATTACK", CardType.Attack), new CardModel("S_SKILL", CardType.Skill),
                new CardModel("S_NO_GEN", CardType.Attack) { GeneratedInCombat = false }, shared,
                new CardModel("S_MP", CardType.Attack) { MultiplayerConstraint = CardMultiplayerConstraint.MultiplayerOnly });
            defect.Read = Reader(defect, new CardModel("D_ATTACK", CardType.Attack), shared, new CardModel("D_POWER", CardType.Power));

            owner.UnlockState.Pools.AddRange(new[] { silent, owner.Character.CardPool, defect });
            const string source = "UnlockState.CharacterCardPools(Character.CardPool removed when Count>1).GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;Splash.OnPlay:Attack";
            var pool = BuildOtherCharacterAttackGenerationPool(owner);
            Check(pool["known"] is true && Equals(pool["filter_version"], 2) && Equals(pool["filter_source"], source), "スプラッシュの候補は版2・filter_source の完全一致");
            Check(Ids(pool).SequenceEqual(new[] { "D_ATTACK", "SHARED_ATTACK", "S_ATTACK" }), "自キャラの pool を除き、Attack だけ、FilterForRun・FilterForCombat 後、ID 昇順・重複なし");
            Check(calls.SequenceEqual(new[] { silent, defect }), "自キャラの pool の GetUnlockedCards は呼ばない（Splash は除いてから列挙する）");
            Check(CardModel.AllowedCalls.All(c => ReferenceEquals(c.Run, owner.RunState)), "IsAllowedInRun は owner の run");

            owner.UnlockState.Pools.Clear();
            owner.UnlockState.Pools.Add(owner.Character.CardPool);
            Check(Ids(BuildOtherCharacterAttackGenerationPool(owner)).SequenceEqual(new[] { "OWN_ATTACK" }), "pool が 1 つだけなら自キャラの pool も除かない（Splash.OnPlay の list.Count > 1）");
            owner.UnlockState.Pools.Clear();
            owner.UnlockState.Pools.Add(silent);
            Check(Ids(BuildOtherCharacterAttackGenerationPool(owner)).SequenceEqual(new[] { "SHARED_ATTACK", "S_ATTACK" }), "自キャラが一覧に無く 1 つだけでもそのまま列挙する");
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            owner.UnlockState.Pools.Add(defect);
            defect.Read = (_, _) => throw new InvalidOperationException("defect pool renamed");
            var failed = BuildOtherCharacterAttackGenerationPool(owner);
            Check(failed["known"] is false && ((List<Dictionary<string, object?>>)failed["cards"]!).Count == 0 && ((string)failed["reason"]!).Contains("defect pool renamed"), "1 つの pool の失敗で部分一覧を known にしない");
            Check(_stateWarnings.Any(w => w.StartsWith("player.other_character_attack_generation_pool:")), "スプラッシュの候補の warning パス");
            BeginStateWarnings();
        }

        private static IEnumerable<CardModel> Throwing()
        {
            yield return new CardModel("B_SKILL", CardType.Skill);
            throw new InvalidOperationException("enumeration failed");
        }
    }
}
