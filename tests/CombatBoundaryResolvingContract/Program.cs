STS2_MCP.McpMod.RunCombatBoundaryResolvingContract();

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

        public static void RunCombatBoundaryResolvingContract()
        {
            // 戦闘中（IsInProgress）は境界ではない。戦闘中の理由（player_actions_disabled・combat_ending など）が受け持つ。
            foreach (var hasTurnState in new[] { false, true })
            foreach (var starting in new[] { false, true })
            foreach (var preFinished in new[] { false, true })
                Check(CombatBoundaryReason(true, hasTurnState, starting, preFinished) == null,
                    $"in progress is not a boundary ({hasTurnState}/{starting}/{preFinished})");

            // 開始: RunManager.EnterRoomInternal が部屋を積んでから CombatRoom.StartCombat が SetUpCombat を呼ぶまで
            // （ターンの状態がまだ無い）と、SetUpCombat から StartCombatInternal が IsInProgress を立てるまで（IsStarting）。
            Check(CombatBoundaryReason(false, false, false, false) == CombatStartingReason, "room pushed, combat not set up yet");
            Check(CombatBoundaryReason(false, true, true, false) == CombatStartingReason, "set up, turn loop not started");

            // 終了（勝ち）: EndCombatInternal が IsInProgress を下ろし、後で CombatRoom.MarkPreFinished を呼ぶ。どちらの側でも終了。
            Check(CombatBoundaryReason(false, true, false, false) == CombatEndingReason, "won, before MarkPreFinished");
            Check(CombatBoundaryReason(false, true, false, true) == CombatEndingReason, "won, after MarkPreFinished");
            // 終了（負け）: ProcessPendingLoss は IsInProgress を下ろすだけで、部屋は終わった扱いにしない。
            // ゲームオーバーの画面は CreatureCmd.Kill が先に積むので、この state の前に game_over が返る。
            // 入力は勝ちの MarkPreFinished 前と同じ。負けを守るのは、game_over を戦闘の部屋より前で拾う配線の検査（下）。
            // 保存から再開した終わった戦闘（CombatRoom.StartPreFinishedCombat）はターンの状態を作らず、報酬を出し直す。
            Check(CombatBoundaryReason(false, false, false, true) == CombatEndingReason, "loaded pre-finished room");

            // message は実際の局面に合わせる。開始時に「Combat ended」と言わない。
            string startMessage = CombatBoundaryMessage(CombatStartingReason, true);
            Check(!startMessage.Contains("ended", StringComparison.OrdinalIgnoreCase)
                  && startMessage.Contains("starting", StringComparison.OrdinalIgnoreCase), "start message says starting");
            Check(CombatBoundaryMessage(CombatStartingReason, false) == startMessage, "start message does not depend on rewards");
            Check(CombatBoundaryMessage(CombatEndingReason, true) == "Combat ended. Waiting for rewards...", "end message keeps its text");
            string noRewards = CombatBoundaryMessage(CombatEndingReason, false);
            Check(noRewards.StartsWith("Combat ended.", StringComparison.Ordinal) && noRewards.Contains("no rewards"),
                "end message for an encounter without rewards");

            var sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            var stateSource = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            var mpSource = File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs"));
            const string oldMessage = "\"Combat ended. Waiting for rewards...\"";
            Check(!stateSource.Contains(oldMessage) && !mpSource.Contains(oldMessage), "the fixed message is gone from both builders");
            Check(mpSource.Contains("CombatBoundaryMessage(") && mpSource.Contains("ReadCombatBoundaryReason(combatRoom)"),
                "multiplayer state uses the same message");

            // 配線 1: 入力待ちの画面（報酬・カード報酬・ゲームオーバー・未対応の overlay・マップ）は、戦闘の部屋の分岐より前で拾う。
            // このため理由が付くのは、戦闘の部屋で何も前に出ていない state だけ。
            int build = stateSource.IndexOf("private static Dictionary<string, object?> BuildGameStateCore(", StringComparison.Ordinal);
            int combatBranch = stateSource.IndexOf("else if (currentRoom is CombatRoom combatRoom)", build, StringComparison.Ordinal);
            Check(build >= 0 && combatBranch > build, "combat room branch exists");
            foreach (var earlier in new[]
                     {
                         "topOverlay is NRewardsScreen rewardsScreen", "topOverlay is NCardRewardSelectionScreen",
                         "topOverlay is NGameOverScreen", "topOverlay is IOverlayScreen", "else if (mapIsOpen)",
                         "topOverlay is NCardGridSelectionScreen", "pauseState != null"
                     })
            {
                int at = stateSource.IndexOf(earlier, build, StringComparison.Ordinal);
                Check(at > build && at < combatBranch, $"'{earlier}' is decided before the combat room");
            }
            int notInProgress = stateSource.IndexOf("combatBoundaryReason = ReadCombatBoundaryReason(combatRoom);", combatBranch, StringComparison.Ordinal);
            int inProgress = stateSource.IndexOf("if (CombatManager.Instance.IsInProgress)", combatBranch, StringComparison.Ordinal);
            int mapInBranch = stateSource.IndexOf("if (IsMapScreenOpenOrVisible())", inProgress, StringComparison.Ordinal);
            int eventBranch = stateSource.IndexOf("else if (currentRoom is EventRoom eventRoom)", combatBranch, StringComparison.Ordinal);
            Check(inProgress > combatBranch && mapInBranch > inProgress && notInProgress > mapInBranch && notInProgress < eventBranch,
                "the reason is set only in the battle-less branch, after the map check");
            Check(stateSource.Contains("AddResolvingState(result, result.GetValueOrDefault(\"state_type\")?.ToString(), combatBoundaryReason);"),
                "the reason is handed to AddResolvingState");

            // 配線 2: AddResolvingState は理由を入力待ちの画面の消去より前に足す。戦闘中の分岐の IsStarting（立たない）は使わない。
            int start = stateSource.IndexOf("private static void AddResolvingState(", StringComparison.Ordinal);
            Check(start >= 0, "AddResolvingState exists");
            int end = stateSource.IndexOf("private static string ReadCombatBoundaryReason(", start, StringComparison.Ordinal);
            Check(end > start, "ReadCombatBoundaryReason follows AddResolvingState");
            string body = stateSource[start..end];
            int add = body.IndexOf("reasons.Add(combatBoundaryReason);", StringComparison.Ordinal);
            int clear = body.IndexOf("_playerInputStates.Contains(stateType)", StringComparison.Ordinal);
            Check(add > 0 && clear > add, "boundary reason is added before the input-screen clear");
            Check(!body.Contains("if (combat.IsStarting) reasons.Add"), "the unreachable IsStarting check is gone");
            Check(body.Contains("if (combat.IsOverOrEnding) reasons.Add(CombatEndingReason);"), "in-combat ending is kept");

            // 入力待ちの画面の一覧は変えない（理由が残って待つ側が行き詰まらない）。
            int set = stateSource.IndexOf("_playerInputStates = new()", StringComparison.Ordinal);
            int setEnd = stateSource.IndexOf("};", set, StringComparison.Ordinal);
            string inputStates = stateSource[set..setEnd];
            foreach (var screen in new[] { "\"rewards\"", "\"card_reward\"", "\"map\"", "\"event\"", "\"game_over\"", "\"menu\"", "\"hand_select\"" })
                Check(inputStates.Contains(screen), $"{screen} stays an input screen");

            // 読む値は公開のプロパティだけ。
            string reader = stateSource[end..];
            reader = reader[..reader.IndexOf("private static bool ReadEncounterGivesRewards(", StringComparison.Ordinal)];
            Check(reader.Contains("combat.IsInProgress") && reader.Contains("combat.CurrentCombatId != null")
                  && reader.Contains("combat.IsStarting") && reader.Contains("combatRoom.IsPreFinished"), "reads the public values");
            Check(stateSource.Contains("combatRoom.Encounter.ShouldGiveRewards"), "reads whether the encounter gives rewards");

            var modSource = File.ReadAllText(Path.Combine(sources, "McpMod.cs"));
            Check(modSource.Contains("public const int StateSchemaVersion = 11;"), "published schema is 11 (sts2#1012)");
            int settled = modSource.IndexOf("internal static bool IsSettledState(", StringComparison.Ordinal);
            Check(settled >= 0, "IsSettledState exists");
            string settledBody = modSource[settled..modSource.IndexOf("internal static string StateFingerprint(", settled, StringComparison.Ordinal)];
            Check(settledBody.Contains("state.TryGetValue(\"is_resolving\"") && settledBody.Contains("is not Dictionary<string, object?> battle"),
                "POST wait still settles on is_resolving and needs a battle in combat");

            Console.WriteLine($"combat boundary resolving contract: {Checks} checks passed");
        }
    }
}
