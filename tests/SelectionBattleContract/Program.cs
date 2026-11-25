STS2_MCP.McpMod.RunSelectionBattleContract();

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

        // 引数の並び: stateType, hasBattle, inCombatRoom, combatInProgress
        private static bool Attach(string? stateType, bool hasBattle = false, bool inCombatRoom = true, bool inProgress = true)
            => ShouldAttachSelectionBattle(stateType, hasBattle, inCombatRoom, inProgress);

        private static int CountOf(string text, string needle)
        {
            int count = 0;
            for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        public static void RunSelectionBattleContract()
        {
            // --- 判定（ゲームの型を使わない） ---

            // 戦闘中の選択画面はすべて battle を付ける。グリッドの card_select（計略・未来視の雫の NCombatPileCardSelectScreen）が D-15。
            foreach (var type in new[] { "hand_select", "card_select", "bundle_select", "relic_select" })
                Check(Attach(type), $"戦闘中の {type} に battle を付ける");
            // 画面のクラスでなく state_type で決める。今後の *_select も同じく付く。
            Check(Attach("future_select"), "今後の *_select にも付く");
            Check(IsSelectionStateType("card_select") && !IsSelectionStateType("select") && !IsSelectionStateType("menu_select_x"),
                "選択画面は _select で終わる state_type");

            // 選択画面でない state には付けない（戦闘中でも）。
            foreach (var type in new[] { "monster", "elite", "boss", "map", "menu", "game_over", "card_reward", "rewards",
                                         "overlay", "event", "shop", "fake_merchant", "rest_site", "treasure", "crystal_sphere", "unknown" })
                Check(!Attach(type), $"選択画面でない {type} には付けない");
            Check(!Attach(null), "state_type が無ければ付けない");

            // 戦闘の外では付けない（battle を作らない）。
            Check(!Attach("card_select", inProgress: false), "戦闘の前後（IsInProgress が false）の選択画面には付けない");
            Check(!Attach("card_select", inCombatRoom: false), "戦闘の部屋でない（イベント・ショップなど）選択画面には付けない");
            Check(!Attach("relic_select", inCombatRoom: false, inProgress: false), "戦闘外のレリック選択には付けない");
            // 二重に作らない。
            Check(!Attach("hand_select", hasBattle: true), "既に battle があれば作り直さない");

            // --- 配線（本番ソースの文字列検査） ---
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string multi = File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs"));
            string mod = File.ReadAllText(Path.Combine(sources, "McpMod.cs"));

            CheckWiring(builder, "単独", "BuildBattleState", "NCardGridSelectionScreen cardSelectScreen");
            CheckWiring(multi, "マルチ", "BuildMultiplayerBattleState", "NCardGridSelectionScreen cardSelectScreen");
            CheckActivityWiring(builder, "BuildBattleState");
            CheckActivityWiring(multi, "BuildMultiplayerBattleState");

            // battle の中身は戦闘の画面と同じ作り方で、山の中身（順）を足さない。
            string battleBody = builder[builder.IndexOf("private static Dictionary<string, object?> BuildBattleState", StringComparison.Ordinal)..];
            battleBody = battleBody[..battleBody.IndexOf("return battle;\n    }", StringComparison.Ordinal)];
            Check(!battleBody.Contains("Pile", StringComparison.Ordinal), "battle は山の中身を持たない（隠す情報は player 側の規則のまま）");

            Check(System.Text.RegularExpressions.Regex.IsMatch(mod, @"public const string StateSchema = ""2\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";")
                && mod.Contains("/// 1.4.0: every selection state (*_select) shown while a fight is in progress carries battle", StringComparison.Ordinal),
                "追加は MINOR（1.4.0。版の値そのものは V0113CompatContract）");

            Console.WriteLine($"SelectionBattleContract: {Checks} checks passed");
        }

        private static void CheckActivityWiring(string source, string battleBuilder)
        {
            string body = source[source.IndexOf($"private static Dictionary<string, object?> {battleBuilder}(", StringComparison.Ordinal)..];
            body = body[..body.IndexOf("return battle;\n    }", StringComparison.Ordinal)];
            const string attach = "AttachCreatureActivity(battle, combatState);";
            Check(CountOf(body, attach) == 2 && CountOf(source, attach) == 2,
                $"{battleBuilder}: 通常・選択共通の battle にだけ活動列を付ける。戦闘 state の欠落時も理由を出す");
            Check(body.IndexOf("BuildEnemyList(combatState);", StringComparison.Ordinal)
                < body.LastIndexOf(attach, StringComparison.Ordinal),
                $"{battleBuilder}: 敵の ID を準備してから活動列を組み立てる");
        }

        private static void CheckWiring(string source, string label, string battleBuilder, string gridBranch)
        {
            string attach = $"result[\"battle\"] = {battleBuilder}(runState, selectionCombatRoom);";
            int central = source.IndexOf(attach, StringComparison.Ordinal);
            int common = source.IndexOf("result[\"run\"] = BuildRunState(runState);", StringComparison.Ordinal);
            int grid = source.IndexOf(gridBranch, StringComparison.Ordinal);
            Check(central > 0 && common > central && grid > 0 && grid < central,
                $"{label}: 画面の振り分けの後、run・player の前で一度だけ付ける");
            int blockStart = source.LastIndexOf("bool inCombatRoom = currentRoom is CombatRoom;", central, StringComparison.Ordinal);
            Check(blockStart > grid, $"{label}: 戦闘の部屋かどうかを計算して判定に渡す（真理表の inCombatRoom が実際に効く）");
            string block = source[blockStart..common];
            Check(block.Contains("ShouldAttachSelectionBattle(", StringComparison.Ordinal)
                && block.Contains("result.GetValueOrDefault(\"state_type\") as string", StringComparison.Ordinal)
                && block.Contains("result.ContainsKey(\"battle\")", StringComparison.Ordinal)
                && block.Contains("inCombatRoom, CombatManager.Instance.IsInProgress)", StringComparison.Ordinal)
                && !block.Contains("inCombatRoom: true", StringComparison.Ordinal),
                $"{label}: state_type・既存の battle・戦闘の部屋か・IsInProgress で判定する");
            Check(block.IndexOf("ShouldAttachSelectionBattle(", StringComparison.Ordinal) < block.IndexOf("currentRoom is CombatRoom selectionCombatRoom", StringComparison.Ordinal),
                $"{label}: 部屋の取り出しは判定の後で別に行う");
            Check(block.Contains("try { " + attach + " }", StringComparison.Ordinal)
                && block.Contains("catch (Exception ex) { Warn(\"battle.selection\", ex); }", StringComparison.Ordinal),
                $"{label}: battle を作れなければ warning にして battle を省き、残りの state は返す");

            // 振り分けの中で battle を付けるのは戦闘の画面（monster / elite / boss）だけ。選択画面は共通の配線に任せる。
            string dispatch = source[grid..central];
            Check(CountOf(dispatch, $"result[\"battle\"] = {battleBuilder}(") == 1,
                $"{label}: 振り分けの中の battle は戦闘の画面の 1 か所だけ（hand_select・choose は共通の配線）");
            string normal = $"result[\"battle\"] = {battleBuilder}(runState, combatRoom);";
            Check(dispatch.IndexOf("CombatManager.Instance.IsInProgress", StringComparison.Ordinal) is var inProgress
                && inProgress >= 0 && inProgress < dispatch.IndexOf(normal, StringComparison.Ordinal),
                $"{label}: 通常戦闘の battle は戦闘中に同じビルダーで作る");
            string handBranch = dispatch[dispatch.IndexOf("result[\"state_type\"] = \"hand_select\";", StringComparison.Ordinal)..];
            handBranch = handBranch[..handBranch.IndexOf("else", StringComparison.Ordinal)];
            Check(!handBranch.Contains("result[\"battle\"]", StringComparison.Ordinal), $"{label}: hand_select も共通の配線で付ける");
        }
    }
}
