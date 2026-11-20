using System.Text;
using System.Text.RegularExpressions;

STS2_MCP.McpMod.RunCombatLogContract();

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

        private static Dictionary<string, object?> Row(string kind) => new() { ["seq"] = null, ["kind"] = kind };

        public static void RunCombatLogContract()
        {
            CheckStore();
            CheckTracker();
            CheckMarkdown();
            CheckSources();
            Console.WriteLine($"CombatLogContract: {Checks} checks passed");
        }

        // CombatLogStore: seq, window, capacity, one fight at a time, incomplete logs.
        private static void CheckStore()
        {
            CombatLogStore.Clear();
            object fight = new(), next = new();
            for (int i = 0; i < 5; i++) CombatLogStore.Append(fight, "7", Row("draw"));
            var read = CombatLogStore.Read(fight, null, 3);
            Check(read.Rows.Count == 3 && (long)read.Rows[0]["seq"]! == 2 && (long)read.Rows[2]["seq"]! == 4, "既定の窓は最後の N 行で、seq は 0 から順に振る");
            Check(read.FirstSeq == 0 && read.NextSeq == 5 && read.WindowFirstSeq == 2 && read.CombatId == "7", "first_seq・next_seq・窓の始まり・戦闘の ID");
            Check(read.Reasons.Count == 0 && !read.SinceTruncated, "欠けの無い列は理由なし");
            var since = CombatLogStore.Read(fight, 1, 3);
            Check(since.Rows.Count == 4 && (long)since.Rows[0]["seq"]! == 1, "since を渡すとその seq から残っている全行");
            Check(CombatLogStore.Read(fight, 99, 3).Rows.Count == 0, "next_seq より先の since は空");
            Check(CombatLogStore.NextSeqOf(fight) == 5 && CombatLogStore.NextSeqOf(next) == 0, "NextSeqOf は同じ戦闘の次の seq、別の戦闘は 0");
            Check(CombatLogStore.Read(next, null, 3).Rows.Count == 0, "別の戦闘の行は返さない");

            var first = read.Rows[0];
            CombatLogStore.Append(fight, "7", Row("play"));
            Check(ReferenceEquals(CombatLogStore.Read(fight, 2, 100).Rows[0], first), "記録した行は後から変わらない（前の読みが後の読みの先頭になる）");

            for (int i = 0; i < CombatLogStore.Capacity + 10; i++) CombatLogStore.Append(fight, "7", Row("hook"));
            var full = CombatLogStore.Read(fight, 0, CombatLogStore.DefaultWindow);
            Check(full.FirstSeq == 16 && full.NextSeq == CombatLogStore.Capacity + 16, "上限を超えた古い行を捨て、first_seq が進む");
            Check(full.SinceTruncated && full.WindowFirstSeq == 16 && full.Rows.Count == CombatLogStore.Capacity, "捨てた行より前の since は since_truncated で、残っている先頭から返す");
            Check(CombatLogStore.Read(fight, null, CombatLogStore.DefaultWindow).Rows.Count == CombatLogStore.DefaultWindow, "既定の窓の行数");

            CombatLogStore.MarkBroken(fight, "7", "hook: boom");
            Check(CombatLogStore.Read(fight, null, 3).Reasons.Contains("hook: boom"), "行を作れなかった戦闘は理由を出す（known=false）");
            CombatLogStore.Append(next, "8", Row("draw"));
            var fresh = CombatLogStore.Read(next, null, 3);
            Check(fresh.Rows.Count == 1 && (long)fresh.Rows[0]["seq"]! == 0 && fresh.Reasons.Count == 0 && fresh.CombatId == "8",
                "新しい戦闘で列・seq・欠けの印を初めからにする");
            Check(CombatLogStore.Read(fight, null, 3).Rows.Count == 0, "前の戦闘の行は新しい戦闘の後に読めない");

            CombatLogStore.AddInstallError("flash: missing");
            Check(CombatLogStore.Read(next, null, 3).Reasons.Any(r => r.StartsWith("install: flash")), "当てられなかった patch の群は全戦闘の理由に出る");
            Check(CombatLogStore.Read(new object(), null, 3).Reasons.Count == 1, "見たことの無い戦闘でも導入の失敗は出す");
            CombatLogStore.Clear();
        }

        // OpenCardSelections: the inner entry point writes the selection row; the new screen kinds.
        private static void CheckTracker()
        {
            OpenCardSelections.Clear();
            object player = new(), context = new(), combat = new();
            var outer = OpenCardSelections.Add("FromHandForDiscard", context, player, combat);
            var inner = OpenCardSelections.Add("FromHand", context, player, combat);
            var other = OpenCardSelections.Add("FromChooseACardScreen", new object(), player, combat);
            Check(outer.HasInner && !inner.HasInner && !other.HasInner, "同じ context の内側の入口が開いたら外側に印を付ける（選択の行は内側だけ）");
            Check(OpenCardSelections.OuterSelectors(inner).SequenceEqual(new[] { "FromHandForDiscard" }), "外側の入口");
            OpenCardSelections.Clear();

            var deck = OpenCardSelections.Add("FromDeckGeneric", player, player, null);
            Check(OpenCardSelections.SelectorShowsScreen("deck", "FromDeckForUpgrade") && OpenCardSelections.SelectorShowsScreen("deck", "FromDeckGeneric")
                && OpenCardSelections.SelectorShowsScreen("deck", "FromDeckForEnchantment") && !OpenCardSelections.SelectorShowsScreen("deck", "FromHand"),
                "デッキの画面は FromDeck* の入口");
            Check(OpenCardSelections.SelectorShowsScreen("bundle", "FromChooseABundleScreen") && OpenCardSelections.SelectorShowsScreen("relic", "FromChooseARelicScreen")
                && !OpenCardSelections.SelectorShowsScreen("relic", "FromChooseACardScreen"), "束とレリックの画面");
            Check(ReferenceEquals(OpenCardSelections.Latest(s => s.CombatState == null && OpenCardSelections.SelectorShowsScreen("deck", s.Selector)), deck),
                "戦闘外のデッキの選択も記録から引ける");
            deck.Opener = new Dictionary<string, object?> { ["kind"] = "rest_site_option" };
            deck.OpenedAtLogSeq = 12;
            Check(deck.Opener != null && deck.OpenedAtLogSeq == 12, "開いた元と開いた時点の seq を記録に持つ");
            OpenCardSelections.Clear();
        }

        private static void CheckMarkdown()
        {
            var rows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < 20; i++)
                rows.Add(new Dictionary<string, object?> { ["seq"] = (long)i, ["kind"] = "draw", ["card"] = new Dictionary<string, object?> { ["uid"] = $"STRIKE#{i}" } });
            rows.Add(new Dictionary<string, object?>
            {
                ["seq"] = 20L, ["kind"] = "hook", ["hook"] = "AfterCardPlayed",
                ["passes"] = new List<Dictionary<string, object?>>
                {
                    new() { ["method"] = "AfterCardPlayed", ["listeners"] = new List<Dictionary<string, object?>> { new() { ["model_id"] = "RAGE_POWER" } } },
                    new() { ["method"] = "AfterCardPlayedLate", ["listeners"] = new List<Dictionary<string, object?>>() }
                }
            });
            rows.Add(new Dictionary<string, object?> { ["seq"] = 21L, ["kind"] = "pile_move", ["card"] = null, ["from"] = "Draw", ["to"] = "Draw", ["position"] = "top" });
            rows.Add(new Dictionary<string, object?> { ["seq"] = 22L, ["kind"] = "hidden_rows", ["count"] = 9, ["reason"] = "shuffle_order" });
            var battle = new Dictionary<string, object?>
            {
                ["combat_log"] = new Dictionary<string, object?> { ["known"] = true, ["first_seq"] = 0L, ["next_seq"] = 23L, ["rows"] = rows }
            };
            var sb = new StringBuilder();
            AppendCombatLogMarkdown(sb, battle);
            string text = sb.ToString();
            Check(text.Contains("### Combat Log (last 15 of seq 0-22)"), "見出しは表示した行数と seq の範囲");
            Check(!text.Contains("STRIKE#7\n") && text.Contains("STRIKE#8") && text.Contains("- 22 9 rows hidden (shuffle_order)"), "最後の 15 行だけ");
            Check(text.Contains("hook AfterCardPlayed: AfterCardPlayed [RAGE_POWER]") && !text.Contains("AfterCardPlayedLate ["), "hook の行は listener のいる巡だけ");
            Check(text.Contains("move hidden Draw → Draw (top)"), "山札の中の移動は札を出さない");

            var unknown = new StringBuilder();
            AppendCombatLogMarkdown(unknown, new Dictionary<string, object?>
            {
                ["combat_log"] = new Dictionary<string, object?> { ["known"] = false, ["first_seq"] = 0L, ["next_seq"] = 0L, ["rows"] = new List<Dictionary<string, object?>>(), ["unavailable_reasons"] = new List<string> { "install: hooks: x" } }
            });
            Check(unknown.ToString().Contains("— unknown (install: hooks: x)"), "unknown の理由");

            var decision = new StringBuilder();
            AppendDecisionContextMarkdown(decision, new Dictionary<string, object?>
            {
                ["decision_context"] = new Dictionary<string, object?>
                {
                    ["known"] = true, ["selector"] = "FromDeckForUpgrade",
                    ["opener"] = new Dictionary<string, object?> { ["kind"] = "rest_site_option", ["model_type"] = "SmithRestSiteOption", ["method"] = "OnSelect" }
                }
            });
            Check(decision.ToString().StartsWith("Decision context: FromDeckForUpgrade; opener rest_site_option SmithRestSiteOption.OnSelect"), "Decision context の 1 行");
        }

        private static void CheckSources()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Read(string name) => File.ReadAllText(Path.Combine(dir, name));
            string patches = Read("McpMod.CombatLogPatches.cs");
            string log = Read("McpMod.CombatLog.cs");
            string decision = Read("McpMod.DecisionContext.cs");
            string all = patches + log + decision + Read("McpMod.PotionGenerationPool.cs") + Read("McpMod.CardXValue.cs");

            int hooks = Regex.Matches(patches, "^        \\(\"(After|Before)[A-Za-z]+\", \"(combat|combat_all|run)\", new\\[\\]", RegexOptions.Multiline).Count;
            Check(hooks == 53, $"通知の表は 53 件（{hooks}）");
            Check(Regex.Matches(patches, "\"(StartCombatInternal|StartTurn|RunAutoPrePlayPhase|SetupPlayerTurn|EndPlayerTurnPhaseOneInternal|DoTurnEnd|ResolveTurnEndCardEffects|FlushPlayerHand|EndPlayerTurnPhaseTwoInternal|SwitchFromPlayerToEnemySide|EndEnemyTurnInternal|EndCombatInternal)\"").Count == 12,
                "手番の段階は 12 件");
            Check(!Regex.IsMatch(patches + decision, "private static bool (CombatLog|Decision)"), "prefix は bool を返さない（ゲームの呼出しを飛ばさない）");
            Check(!Regex.IsMatch(all, "\\.Next(Int|Float|Item|Bool)\\(|StableShuffle|\\.Rng\\."), "乱数に触らない");
            Check(!Regex.IsMatch(patches, "ref (CardPile|CardModel|Creature|PowerModel|PotionModel|decimal|int) [a-z]"), "ゲームの引数を書き換えない（ref は out 引数の読み取りと struct の __instance だけ）");
            Check(log.Contains("if (_combatLogSuppressed.Value is { } suppressed) { suppressed.Count++; return; }"), "シャッフルの Add の中の行は数えるだけ");
            Check(patches.Contains("_shuffleScope.Value != null && _combatLogSuppressed.Value == null ? new SuppressedRows()") && patches.Contains("row[\"reason\"] = \"shuffle_order\";"),
                "シャッフルの Add の間を伏せ、件数を hidden_rows で出す");
            Check(patches.Contains("bool hidden = from == PileType.Draw && to == PileType.Draw;"), "山札の中の移動は札を出さない");
            Check(!patches.Contains("row[\"index\"]") && patches.Contains("The index is never published"), "置いた位置の添字を出さない");
            Check(decision.Contains("row[\"offered_hidden_reason\"] = \"draw_pile\";"), "山札からの選択の候補は順を出さない");
            Check(patches.Contains("if (!power.IsMutable || mods == null || mods.Count == 0) return;"), "PowerVar のプレビュー（正準のパワー）を出来事にしない");
            Check(patches.Contains("BuildHookListenerRows(listeners,"), "listener は既存の BuildHookListenerRows（山札の位置を隠す）で出す");
            Check(patches.Contains("HookOverrideDeclarer(model.GetType(), pass.Method, pass.Parameters) != null)))") && patches.Contains("                return false;"), "上書きする listener の無い通知は行にしない");
            Check(log.Contains("CombatLogStore.MarkBroken") && log.Contains("[\"known\"] = snapshot.Reasons.Count == 0"), "行を作れなければ known=false");
            Check(decision.Contains("if (record.HasInner || record.CombatState == null || !task.IsCompletedSuccessfully) return;"), "選択の行は内側の入口だけ・戦闘の中だけ");

            string mod = Read("McpMod.cs");
            Check(mod.Contains("InstallCombatLogTracking();") && mod.Contains("InstallDecisionContextTracking();"), "初期化で patch を当てる");
            Check(mod.Contains("request.QueryString[\"combat_log_since\"]") && mod.Contains("_combatLogSince = null;"), "?combat_log_since= は読むたびに戻す");
            Check(mod.Contains("public const string StateSchema = \"1.10.0\";"), "schema 1.10.0");
            string builder = Read("McpMod.StateBuilder.cs");
            Check(builder.Contains("battle[\"combat_log\"] = BuildCombatLogState(combatState);"), "battle に combat_log を付ける");
            Check(Regex.Matches(builder, "state\\[\"decision_context\"\\] = BuildDecisionContext\\(").Count == 5, "5 つの選択画面に decision_context を付ける");
            Check(builder.Contains("state[\"potion_generation_pool\"] = BuildPotionGenerationPool(player);"), "player.potion_generation_pool");
            Check(builder.Contains("AttachXValueIfPlayed((Dictionary<string, object?>)state[\"energy_cost\"]!, card);"), "手札の energy_cost に x_value_if_played");
            Check(Read("McpMod.AutoPlayReservationPatches.cs").Contains("capture[\"combat_log\"] = BuildCombatLogState(combat, 0);"), "ended_combat に戦闘の残りの全行");
            string formatting = Read("McpMod.Formatting.cs");
            Check(formatting.Contains("AppendCombatLogMarkdown(sb, battle);") && Regex.Matches(formatting, "AppendDecisionContextMarkdown\\(sb, ").Count == 4, "Markdown の配線");
        }
    }
}
