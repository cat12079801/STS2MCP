using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

STS2_MCP.McpMod.RunTurnStartResolvingContract();

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

        public static void RunTurnStartResolvingContract()
        {
            const ActionSynchronizerCombatState notPlay = ActionSynchronizerCombatState.NotPlayPhase;
            const ActionSynchronizerCombatState play = ActionSynchronizerCombatState.PlayPhase;

            // CombatManager.StartTurn: None → Start（エナジー・ドロー・AfterPlayerTurnStart）→ AutoPrePlay → Play。
            // 同期器は手番の初めの間 NotPlayPhase のまま。
            foreach (var phase in new[] { PlayerTurnPhase.None, PlayerTurnPhase.Start, PlayerTurnPhase.AutoPrePlay })
                Check(IsLocalPlayerTurnStarting(CombatSide.Player, true, phase, notPlay), $"{phase} during the turn-start draw");

            // Play になってから StartTurn の最後（Unpause・SetCombatState(PlayPhase)）までの間。
            Check(IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.Play, notPlay),
                "phase Play before the queues reopen");

            // 手番の初めの選択で SetupPlayerTurn が止まると、StartTurn は先へ進んで PlayPhase にする。
            // 選択に答えた後の残り（ドローや AfterPlayerTurnStart）は Phase で捕まえる。
            Check(IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.Start, play),
                "setup resumed after a turn-start choice");
            Check(IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.AutoPrePlay, play),
                "auto pre-play hooks after the queues reopened");

            // 通常のプレイの局面は落ち着いている。
            Check(!IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.Play, play), "play phase is settled");

            // 手番の終わり（AutoPostPlay・End）は手番の初めではない（player_actions_disabled が受け持つ）。
            foreach (var queue in new[] { play, ActionSynchronizerCombatState.EndTurnPhaseOne, notPlay })
            {
                Check(!IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.AutoPostPlay, queue), $"AutoPostPlay / {queue}");
                Check(!IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.End, queue), $"End / {queue}");
            }
            Check(!IsLocalPlayerTurnStarting(CombatSide.Player, true, PlayerTurnPhase.Play, ActionSynchronizerCombatState.EndTurnPhaseOne),
                "end-turn phase one is not a turn start");

            // 敵の手番は対象外（PlayerActionsDisabled と is_play_phase が受け持つ）。
            foreach (var phase in Enum.GetValues<PlayerTurnPhase>())
            {
                Check(!IsLocalPlayerTurnStarting(CombatSide.Enemy, true, phase, notPlay), $"enemy side / {phase}");
                Check(!IsLocalPlayerTurnStarting(CombatSide.None, true, phase, notPlay), $"no side / {phase}");
            }

            // 死んだローカルの Player（協力プレイ）は Start のまま手番を過ごすので、Phase では判定しない。
            // 同期器が PlayPhase に戻れば落ち着いたとみなす。
            Check(!IsLocalPlayerTurnStarting(CombatSide.Player, false, PlayerTurnPhase.Start, play), "dead local player after the turn start");
            Check(IsLocalPlayerTurnStarting(CombatSide.Player, false, PlayerTurnPhase.Start, notPlay), "dead local player during the turn start");

            // Phase を読めないときは同期器だけで判定する。
            Check(IsLocalPlayerTurnStarting(CombatSide.Player, true, null, notPlay), "unreadable phase, queues closed");
            Check(!IsLocalPlayerTurnStarting(CombatSide.Player, true, null, play), "unreadable phase, queues open");

            // 配線: 戦闘の画面でだけ理由を足し、入力待ちの画面の消去より前に足す。
            var sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            var stateSource = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            int start = stateSource.IndexOf("private static void AddResolvingState(", StringComparison.Ordinal);
            Check(start >= 0, "AddResolvingState exists");
            string body = stateSource[start..];
            int reason = body.IndexOf("reasons.Add(\"player_turn_starting\")", StringComparison.Ordinal);
            int guard = body.IndexOf("if (stateType is \"monster\" or \"elite\" or \"boss\")", StringComparison.Ordinal);
            int clear = body.IndexOf("_playerInputStates.Contains(stateType)", StringComparison.Ordinal);
            Check(reason > 0 && guard > 0 && guard < reason, "reason is limited to the fight's own screens");
            Check(clear > reason, "input-boundary screens still clear the reason");
            Check(body.Contains("IsLocalPlayerTurnStarting(") && body.Contains("me?.PlayerCombatState?.Phase")
                && body.Contains("RunManager.Instance.ActionQueueSynchronizer.CombatState"), "reads the phase and the synchronizer");
            Check(body.Contains("!combat.IsStarting && !combat.IsOverOrEnding"), "combat start/end keep their own reasons");

            var modSource = File.ReadAllText(Path.Combine(sources, "McpMod.cs"));
            Check(modSource.Contains("public const int StateSchemaVersion = 4;"), "published schema remains 4");
            int settled = modSource.IndexOf("internal static bool IsSettledState(", StringComparison.Ordinal);
            Check(settled >= 0 && modSource.IndexOf("state.TryGetValue(\"is_resolving\"", settled, StringComparison.Ordinal) > settled,
                "POST wait still settles on is_resolving");

            Console.WriteLine($"turn-start resolving contract: {Checks} checks passed");
        }
    }
}
