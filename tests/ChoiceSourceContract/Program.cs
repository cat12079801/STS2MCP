using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

STS2_MCP.McpMod.RunContractTests();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }

        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

        private sealed record FakeCard(string Id, string Uid, string[]? Options, string? Error = null);

        public static void RunContractTests()
        {
            CheckTracker();
            CheckTransformationOptions();
            CheckMarkdown();
            CheckSources();
            Console.WriteLine($"ChoiceSourceContract: {Checks} checks passed");
        }

        // OpenCardSelections: what the CardSelectCmd prefixes record and BuildChoiceSource looks up.
        private static void CheckTracker()
        {
            OpenCardSelections.Clear();
            object player = new(), other = new(), combat = new(), nextCombat = new();
            object ctxA = new(), ctxB = new(), power = new();
            object cardA = new(), cardB = new(), cardC = new();

            var hand = OpenCardSelections.Add("FromHand", ctxA, player, combat, handSource: power);
            var choose = OpenCardSelections.Add("FromChooseACardScreen", ctxB, player, combat, candidates: new[] { cardA, cardB, cardC });
            Check(OpenCardSelections.Count == 2 && choose.Sequence > hand.Sequence, "開いた順に番号を振る");
            Check(ReferenceEquals(OpenCardSelections.Latest(_ => true), choose), "最も新しい開いている選択を返す");
            Check(ReferenceEquals(OpenCardSelections.Latest(s => OpenCardSelections.SelectorShowsScreen("hand", s.Selector)), hand)
                && ReferenceEquals(hand.HandSource, power) && ReferenceEquals(hand.Context, ctxA),
                "画面の種類で絞ると古い方でも返し、手札選択の source と context を保つ");
            Check(OpenCardSelections.Latest(s => ReferenceEquals(s.Player, other)) == null, "別の player の選択は返さない");
            Check(OpenCardSelections.Latest(s => ReferenceEquals(s.CombatState, nextCombat)) == null, "別の戦闘の選択は返さない（古い記録を次の戦闘に使わない）");

            Check(OpenCardSelections.ShowsCandidates(choose, new object?[] { cardA, null, cardB, cardC }), "null の holder を飛ばして同じ札・同じ順なら一致");
            Check(!OpenCardSelections.ShowsCandidates(choose, new object?[] { cardB, cardA, cardC }), "順が違えば不一致");
            Check(!OpenCardSelections.ShowsCandidates(choose, new object?[] { cardA, cardB }), "枚数が違えば不一致");
            Check(!OpenCardSelections.ShowsCandidates(choose, new object?[] { cardA, cardB, new object() }), "別の実体なら不一致（参照で比べる）");
            Check(!OpenCardSelections.ShowsCandidates(hand, new object?[] { cardA }), "候補を記録していない選択は一致しない");

            foreach (var (screen, selectors) in new (string, string[])[]
            {
                ("hand", new[] { "FromHand", "FromHandForDiscard", "FromHandForUpgrade" }),
                ("choose", new[] { "FromChooseACardScreen" }),
                ("combat_pile", new[] { "FromCombatPile" }),
                ("simple_grid", new[] { "FromSimpleGrid", "FromSimpleGridForRewards" }),
            })
            {
                foreach (var selector in new[] { "FromHand", "FromHandForDiscard", "FromHandForUpgrade", "FromChooseACardScreen",
                             "FromCombatPile", "FromSimpleGrid", "FromSimpleGridForRewards", "FromDeckGeneric" })
                    Check(OpenCardSelections.SelectorShowsScreen(screen, selector) == selectors.Contains(selector),
                        $"{screen} の画面は {string.Join("/", selectors)} だけが出す（{selector}）");
            }
            Check(!OpenCardSelections.SelectorShowsScreen("bundle", "FromHand"), "知らない画面はどれとも一致しない");

            OpenCardSelections.Remove(choose);
            Check(ReferenceEquals(OpenCardSelections.Latest(_ => true), hand), "閉じた選択は返さない");
            OpenCardSelections.Remove(choose);
            Check(OpenCardSelections.Count == 1, "二度閉じても他を消さない");

            // A selection whose task never completes must not stay forever.
            for (int i = 0; i < OpenCardSelections.Capacity + 5; i++)
                OpenCardSelections.Add("FromCombatPile", new object(), player, combat, pile: new object());
            Check(OpenCardSelections.Count == OpenCardSelections.Capacity, "上限を超えた古い記録は捨てる");
            Check(OpenCardSelections.Latest(s => ReferenceEquals(s, hand)) == null, "最古の記録から捨てる");
            OpenCardSelections.Clear();
            Check(OpenCardSelections.Count == 0 && OpenCardSelections.Latest(_ => true) == null, "Clear で空になる");

            // FromHandForDiscard forwards to FromHand with the same context: the outer name stays visible.
            object ctx = new(), otherCtx = new();
            var outer = OpenCardSelections.Add("FromHandForDiscard", ctx, player, combat, handSource: power);
            OpenCardSelections.Add("FromHand", otherCtx, player, combat);
            var inner = OpenCardSelections.Add("FromHand", ctx, player, combat, handSource: power);
            OpenCardSelections.Add("FromHand", ctx, other, combat);
            Check(OpenCardSelections.OuterSelectors(inner).SequenceEqual(new[] { "FromHandForDiscard" }), "同じ context・同じ player の外側の入口だけを古い順に出す");
            Check(OpenCardSelections.OuterSelectors(outer).Count == 0, "外側の記録自身には外側が無い");
            OpenCardSelections.Remove(outer);
            Check(OpenCardSelections.OuterSelectors(inner).Count == 0, "閉じた外側は出さない");

            // Records of a fight that is gone are dropped (save and quit mid-selection never completes the task).
            OpenCardSelections.Clear();
            var outside = OpenCardSelections.Add("FromSimpleGrid", new object(), player, null);
            var old = OpenCardSelections.Add("FromCombatPile", new object(), player, combat);
            OpenCardSelections.DropOtherCombats(combat);
            Check(OpenCardSelections.Count == 2, "いまの戦闘の記録と戦闘外の記録は残す");
            OpenCardSelections.DropOtherCombats(null);
            Check(OpenCardSelections.Latest(r => ReferenceEquals(r, old)) == null && OpenCardSelections.Latest(r => ReferenceEquals(r, outside)) != null,
                "戦闘が終わったら戦闘の記録を捨て、戦闘外の記録は残す");
            OpenCardSelections.Add("FromCombatPile", new object(), player, combat);
            OpenCardSelections.Add("FromHand", new object(), player, nextCombat);
            Check(OpenCardSelections.Latest(r => ReferenceEquals(r.CombatState, combat)) == null && OpenCardSelections.Count == 2,
                "別の戦闘で選択が開いたら、前の戦闘の記録を捨てる");
            OpenCardSelections.Clear();
        }

        // player.transformation_options: EntropyPower → CardCmd.TransformToRandom → GetDefaultTransformationOptions.
        private static void CheckTransformationOptions()
        {
            BeginStateWarnings();
            var cards = new List<FakeCard>
            {
                new("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#9", new[] { "UPPERCUT", "ANGER", "ANGER" }),
                new("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#10", new[] { "ANGER", "UPPERCUT" }),
                new("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#2", new[] { "UPPERCUT", "ANGER" }),
                new("ANGER", "ANGER#4", new[] { "UPPERCUT" }),
                new("BURN", "BURN#7", null, "no options"),
            };
            var built = BuildTransformationOptions(() => cards, c => c.Id, c => c.Uid,
                c => c.Error != null ? throw new InvalidOperationException(c.Error) : c.Options!);
            var rows = (List<Dictionary<string, object?>>)built["cards"]!;
            Check(built["known"] is true && ((string)built["source"]!).Contains("CardFactory.GetDefaultTransformationOptions(original,isInCombat)"), "known と出典");
            Check(rows.Select(r => (string)r["id"]!).SequenceEqual(new[] { "ANGER", "BURN", "STRIKE_IRONCLAD" }), "ID 昇順に並べる");
            var strike = rows.Single(r => Equals(r["id"], "STRIKE_IRONCLAD"));
            Check(((List<string>)strike["uids"]!).SequenceEqual(new[] { "STRIKE_IRONCLAD#10", "STRIKE_IRONCLAD#2", "STRIKE_IRONCLAD#9" }),
                "同じ ID・同じ候補の札は 1 行にまとめ、uid は序数順（山の順を出さない）");
            Check(((List<string>)strike["options"]!).SequenceEqual(new[] { "ANGER", "UPPERCUT" }), "候補は重複を除いて ID 昇順");
            var burn = rows.Single(r => Equals(r["id"], "BURN"));
            Check(burn["options"] == null && ((string)burn["reason"]!).Contains("no options"), "候補が無い（本体が例外）札は options=null と理由。空の一覧にしない");
            Check(_stateWarnings.Count == 0, "候補なしは正常な状態で、行の reason だけ（warning にしない）");

            cards.Add(new("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#11", new[] { "ANGER" }));
            var split = (List<Dictionary<string, object?>>)BuildTransformationOptions(() => cards, c => c.Id, c => c.Uid,
                c => c.Error != null ? throw new InvalidOperationException(c.Error) : c.Options!)["cards"]!;
            Check(split.Count(r => Equals(r["id"], "STRIKE_IRONCLAD")) == 2, "同じ ID でも候補が違う個体は別の行（ID で決め打ちしない）");

            BeginStateWarnings();
            var failed = BuildTransformationOptions<FakeCard>(() => throw new InvalidOperationException("AllPiles renamed"), c => c.Id, c => c.Uid, c => c.Options!);
            Check(failed["known"] is false && ((List<Dictionary<string, object?>>)failed["cards"]!).Count == 0
                && ((string)failed["reason"]!).Contains("AllPiles renamed"), "札の列挙の失敗は known=false・cards=[]・reason");
            Check(_stateWarnings.Any(w => w.StartsWith("player.transformation_options:")), "全体の失敗の warning");

            var sb = new StringBuilder();
            AppendTransformationOptionsMarkdown(sb, built);
            Check(sb.ToString().Contains("Transformation options (Entropy): ANGER 1, BURN unknown, STRIKE_IRONCLAD 2"), "Markdown の 1 行");
            sb.Clear();
            AppendTransformationOptionsMarkdown(sb, failed);
            Check(sb.ToString().Contains("unknown: InvalidOperationException: AllPiles renamed"), "失敗の Markdown");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(built, WireOptions)))
                Check(json.RootElement.GetProperty("cards")[1].GetProperty("options").ValueKind == JsonValueKind.Null,
                    "options=null は本番の wire 設定でも null のまま残る");
            BeginStateWarnings();
        }

        private static void CheckMarkdown()
        {
            string Render(Dictionary<string, object?> selection)
            {
                var sb = new StringBuilder();
                AppendChoiceSourceMarkdown(sb, selection);
                return sb.ToString();
            }
            Check(Render(new()) == "", "choice_source が無い画面は何も書かない");
            var entropy = new Dictionary<string, object?>
            {
                ["choice_source"] = new Dictionary<string, object?>
                {
                    ["known"] = true,
                    ["selector"] = "FromHand",
                    ["models"] = new List<Dictionary<string, object?>> { new() { ["model_id"] = "ENTROPY_POWER" } },
                    ["card_play"] = null,
                    ["hook"] = new Dictionary<string, object?> { ["name"] = "AfterPlayerTurnStart", ["remaining_listeners"] = new List<Dictionary<string, object?>> { new(), new() } }
                }
            };
            Check(Render(entropy).Contains("Choice source: FromHand; models ENTROPY_POWER; hook AfterPlayerTurnStart (2 listeners left)"), "エントロピーの手札選択の 1 行");
            var splash = new Dictionary<string, object?>
            {
                ["choice_source"] = new Dictionary<string, object?>
                {
                    ["known"] = true,
                    ["selector"] = "FromChooseACardScreen",
                    ["models"] = new List<Dictionary<string, object?>> { new() { ["model_id"] = "SPLASH", ["uid"] = "SPLASH#15" }, new() { ["model_id"] = "SPLASH", ["uid"] = "SPLASH#15" } },
                    ["card_play"] = new Dictionary<string, object?> { ["uid"] = "SPLASH#15", ["play_index"] = 1, ["play_count"] = 2 },
                    ["hook"] = new Dictionary<string, object?> { ["name"] = null, ["remaining_listeners"] = null }
                }
            };
            Check(Render(splash).Contains("Choice source: FromChooseACardScreen; models SPLASH#15 > SPLASH#15; card play SPLASH#15 (2/2)") && !Render(splash).Contains("hook"),
                "スプラッシュの選択は uid と解決の番号（1 始まりの表示）");
            var unknown = new Dictionary<string, object?>
            {
                ["choice_source"] = new Dictionary<string, object?> { ["known"] = false, ["unavailable_reasons"] = new List<string> { "no open CardSelectCmd call matches this hand screen" } }
            };
            Check(Render(unknown).Contains("Choice source: unknown (no open CardSelectCmd call matches this hand screen)"), "分からないときは理由");
            string formatting = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", "McpMod.Formatting.cs"));
            Check(formatting.Contains("AppendChoiceSourceMarkdown(sb, handSelect);") && formatting.Contains("AppendChoiceSourceMarkdown(sb, cardSelect);"), "hand_select・card_select の Markdown に配線する");
        }

        // Wiring and patch targets, read from the production sources.
        private static void CheckSources()
        {
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Read(string name) => File.ReadAllText(Path.Combine(sources, name));
            string choice = Read("McpMod.ChoiceSource.cs");
            string builder = Read("McpMod.StateBuilder.cs");
            string mod = Read("McpMod.cs");

            foreach (var target in new[]
            {
                "(\"FromChooseACardScreen\", new[] { typeof(PlayerChoiceContext), typeof(IReadOnlyList<CardModel>), typeof(Player), typeof(bool) }, true)",
                "(\"FromSimpleGridForRewards\", new[] { typeof(PlayerChoiceContext), typeof(List<CardCreationResult>)",
                "(\"FromSimpleGrid\", new[] { typeof(PlayerChoiceContext), typeof(IReadOnlyList<CardModel>)",
                "(\"FromCombatPile\", new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs) }, false)",
                "(\"FromCombatPile\", new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs), typeof(Func<CardModel, bool>) }, false)",
                "(\"FromHand\", new[] { typeof(PlayerChoiceContext), typeof(Player)",
                "(\"FromHandForDiscard\", new[] { typeof(PlayerChoiceContext), typeof(Player)",
                "(\"FromHandForUpgrade\", new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(AbstractModel) }, true)",
            })
                Check(choice.Contains(target, StringComparison.Ordinal), $"CardSelectCmd の patch 対象: {target}");
            Check(Regex.Matches(choice, @"\(""From[A-Za-z]+"", new\[\]").Count == 8, "patch 対象は 8 つ（PlayerChoiceContext を取る CardSelectCmd の入口すべて）");
            Check(choice.Contains("private static void ChoiceSelectionOpened(MethodBase __originalMethod, object[] __args, out OpenCardSelection? __state)", StringComparison.Ordinal)
                && !Regex.IsMatch(choice, @"static bool ChoiceSelection"), "prefix は void（本体の呼び出しを飛ばさない）");
            Check(choice.Contains("Task<CardModel?> __result") && choice.Contains("Task<IEnumerable<CardModel>> __result")
                && !Regex.IsMatch(choice, @"ref Task[^ ]* __result"), "postfix は戻り値の Task を差し替えない（ref で受けない）");
            Check(choice.Contains("TaskContinuationOptions.ExecuteSynchronously"), "選択の完了で記録を閉じる");
            Check(Regex.IsMatch(mod, @"InstallRewardTracking\(\);\s*\n\s*//[^\n]*\n\s*InstallChoiceSourceTracking\(\);"), "初期化で patch を当てる");

            // The hook table: every Hook method that pushes the listener onto the choice context (v0.113.1 Hook.cs).
            foreach (var hook in new[]
            {
                "(\"AfterShuffle\", \"combat\")", "(\"AfterPlayerTurnStart\", \"combat\")", "(\"AfterPlayerTurnStartEarly\", \"combat\")",
                "(\"AfterPlayerTurnStartLate\", \"combat\")", "(\"AfterCardPlayed\", \"combat_all\")", "(\"AfterDamageReceived\", \"run\")",
            })
                Check(choice.Contains(hook, StringComparison.Ordinal), $"フックの表: {hook}");
            Check(Regex.Matches(choice, @"\(""(After|Before)[A-Za-z]+"", ""(combat|combat_all|run)""\)").Count == 24, "フックの表は 24 件（Hook.cs の PushModel(model) の箇所）");
            Check(choice.Contains("OrderPileCardsForDisplay") == false && choice.Contains("BuildHookListenerRows(listeners.Skip(at + 1).ToList()"),
                "残りの listener は既存の BuildHookListenerRows（山札の位置を隠す）で出す");
            Check(Regex.IsMatch(choice, @"if \(DrawPileOf\(top\) != null\)\s*\{\s*reasons\.Add\([^\n]*\);\s*return row;"),
                "山札の listener が選択を開いたら残りの listener を出さない（山札の順の漏れ）");
            Check(choice.Contains(": top is PotionModel ? \"potion_pushes_itself\"") && choice.Contains(": top is OrbModel ? \"orb_pushes_itself\""),
                "使用中のポーション・オーブは自分で積むので通知を決めない");
            Check(choice.Contains("result[\"outer_selectors\"] = OpenCardSelections.OuterSelectors(record);"), "外側の入口を出す");
            foreach (var file in new[] { "McpMod.StateBuilder.cs", "McpMod.MultiplayerState.cs" })
                Check(Read(file).Contains("OpenCardSelections.DropOtherCombats(GetLiveCombatStateForSelection());"), $"state を作るたびに終わった戦闘の記録を捨てる（{file}）");
            Check(choice.Contains("CountModHookSubscribers(\"_runHookSubscribers\") + CountModHookSubscribers(\"_combatHookSubscribers\")"),
                "ModHelper の購読者がいれば残りの listener を出さない");
            Check(!Regex.IsMatch(choice, @"\.Rng|Shuffle\(|NextItem|TakeRandom"), "乱数・抽選に触らない");

            string chooseBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildChooseCardState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildBundleSelectState", StringComparison.Ordinal)];
            int combatGuard = chooseBuilder.IndexOf("if (combatState != null)", StringComparison.Ordinal);
            int chooseWiring = chooseBuilder.IndexOf("state[\"choice_source\"] = BuildChoiceSource(\"choose\", holderCards, runState);", StringComparison.Ordinal);
            Check(combatGuard >= 0 && chooseWiring > combatGuard, "choose-a-card は戦闘中だけ choice_source を付け、表示中の札で照合する");
            string handBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildHandSelectState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildRelicSelectState", StringComparison.Ordinal)];
            Check(handBuilder.Contains("if (combatState != null) state[\"choice_source\"] = BuildChoiceSource(\"hand\", null, runState);"), "hand_select は戦闘中だけ choice_source を付ける");
            string gridBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildCardSelectState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildChooseCardState", StringComparison.Ordinal)];
            Check(gridBuilder.Contains("NCombatPileCardSelectScreen => \"combat_pile\"") && gridBuilder.Contains("NSimpleCardSelectScreen => \"simple_grid\"")
                && gridBuilder.Contains("if (choiceScreen != null && GetLiveCombatStateForSelection() != null)"), "grid は戦闘中の CardSelectCmd の画面だけ");

            string playerBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal)];
            int guard = playerBuilder.IndexOf("if (combatState != null && CombatManager.Instance.IsInProgress)", StringComparison.Ordinal);
            int transform = playerBuilder.IndexOf("BuildTransformationOptionsIfRelevant(player, combatState)", StringComparison.Ordinal);
            Check(guard >= 0 && transform > guard, "変化の候補は戦闘中の共通 Player builder だけ");
            string transformEdge = Read("McpMod.TransformationOptions.cs");
            Check(transformEdge.Contains("CardFactory.GetDefaultTransformationOptions(card, card.CombatState != null)"),
                "本体の GetDefaultTransformationOptions に CardTransformation と同じ isInCombat を渡す（条件を写さない）");
            Check(transformEdge.Contains("p is EntropyPower") && transformEdge.Contains("c is Entropy"), "エントロピーの戦闘だけ出す");
            Check(!Regex.IsMatch(transformEdge, @"\.Rng|NextItem|CreateRandomCardForTransform\(|TransformToRandom\("), "抽選・変化そのものは呼ばない");
            Check(Read("McpMod.CombatHistory.cs").Contains("Read(\"card_plays_finished_this_combat\", () => CombatManager.Instance.History.CardPlaysFinished.Count());"),
                "金の斧の件数は GoldAxe.CanonicalVars と同じ式");
            Check(Read("McpMod.Formatting.cs").Contains("AppendTransformationOptionsMarkdown(sb, transform)"), "変化の候補の Markdown を配線する");
            Check(mod.Contains("/// 1.5.0: the inputs of four colorless cards"), "追加は MINOR（1.5.0。版の値そのものは V0113CompatContract）");
        }
    }
}
