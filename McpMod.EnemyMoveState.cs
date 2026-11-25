using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_MCP;

public static partial class McpMod
{
    // schema 6（sts2#1018）で足した項目。既存の四値（id・follow_up_id・last_logged_id・can_transition_away）の
    // readable には含めず、項目ごとに成否（null と unavailable_reasons）を持つ。
    // inputs_readable はこれらがすべて読めたときだけ true。
    internal static readonly string[] EnemyMoveStateInputKeys =
    [
        "has_follow_up", "must_perform_once", "current_state_id", "current_state_registered", "initial_state_id",
        "performed_first_move", "spawned_this_turn", "state_log", "is_stun", "stun_handler"
    ];

    // v0.114.0: MonsterModel.NextMove / MoveStateMachine / SpawnedThisTurn と MoveState の getter、
    // MonsterMoveStateMachine の private フィールド（_currentState / _initialState / _performedFirstMove）、
    // MoveState._onPerform（CreatureCmd.Stun の Wrapper の判定）だけを読む。
    // FollowUpState は分岐状態の場合もある。次の行動を抽選・実行して解決しない
    // （RollMove・GetNextState・GenerateMoveStateMachine・乱数・delegate の呼び出しはしない）。
    private static Dictionary<string, object?> BuildEnemyMoveState(MonsterModel? monster, string entityId)
    {
        var state = new Dictionary<string, object?>();
        string context = $"battle.enemies.{entityId}.move_state";
        MonsterModel Monster() => monster ?? throw new InvalidOperationException("Monster がありません");
        MoveState NextMove() => Monster().NextMove
            ?? throw new InvalidOperationException("NextMove がありません");
        MonsterMoveStateMachine Machine() => Monster().MoveStateMachine
            ?? throw new InvalidOperationException("MoveStateMachine がありません");
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key,
            () => read() ?? throw new InvalidOperationException($"{key} が null です"), context);

        Read("id", () => NextMove().Id);
        Read("follow_up_id", () =>
        {
            var move = NextMove();
            return move.FollowUpState?.Id ?? move.FollowUpStateId;
        });
        Read("last_logged_id", () =>
        {
            var log = Machine().StateLog
                ?? throw new InvalidOperationException("MoveStateMachine.StateLog がありません");
            if (log.Count == 0)
                throw new InvalidOperationException("MoveStateMachine.StateLog が空です");
            return log[log.Count - 1]?.Id;
        });
        Read("can_transition_away", () => NextMove().CanTransitionAway);
        state["readable"] = IsEnemyMoveStateReadable(state);

        // follow_up_id と同じ式（MoveState.GetNextState の式）が null でないか。false は「次の行動が無い」実値
        // （GasBomb.EXPLODE_MOVE のように、ゲームが FollowUpState も FollowUpStateId も設定しない終端の行動）。
        // 読取の失敗は null と理由で、false と区別する。
        Read("has_follow_up", () =>
        {
            var move = NextMove();
            return (move.FollowUpState?.Id ?? move.FollowUpStateId) != null;
        });
        Read("must_perform_once", () => NextMove().MustPerformOnceBeforeTransitioning);
        // 状態機械の現在地点（分岐状態のこともある）と、空文字列の follow-up で戻る初期地点。
        // 実行予定の NextMove とは別に保持する（SetMoveImmediate → ForceCurrentState で揃うが、別の参照）。
        Read("current_state_id", () => ReadMachineStateId(Machine(), "_currentState"));
        // 現在地点が States に登録された状態そのものか。false は SetMoveImmediate → ForceCurrentState で入った動的な状態
        // （STUNNED・IllusionPower の REVIVE_MOVE など）。同じ ID 文字列でも登録済みの状態と同一視しない。
        Read("current_state_registered", () =>
        {
            var machine = Machine();
            var current = ReadDeclaredStateField<MonsterState>(machine, typeof(MonsterMoveStateMachine), "_currentState");
            var states = machine.States ?? throw new InvalidOperationException("MoveStateMachine.States がありません");
            string id = current.Id ?? throw new InvalidOperationException("MonsterMoveStateMachine._currentState.Id が null です");
            return states.TryGetValue(id, out var registered) && ReferenceEquals(registered, current);
        });
        Read("initial_state_id", () => ReadMachineStateId(Machine(), "_initialState"));
        // FindNextMoveState は false の間、現在地点が MoveState なら遷移しない（最初の行動の保持）。
        Read("performed_first_move", () => ReadDeclaredStateField<bool>(Machine(), typeof(MonsterMoveStateMachine), "_performedFirstMove"));
        // Creature.TakeTurn は true の敵の行動を飛ばす（SetUpForCombat で true、OnSideSwitch で false）。
        Read("spawned_this_turn", () => Monster().SpawnedThisTurn);
        // 順序と重複を保った全要素。途中までの配列は返さない（一つでも読めなければ全体を null）。
        // 空配列はゲームのログが本当に空（分岐状態から始まり、まだ一度も抽選していない）という実値。
        Read("state_log", () => ReadStateLog(Machine()));
        if (state["state_log"] is List<Dictionary<string, object?>> { Count: > 0 } log
            && state["last_logged_id"] is string last && !Equals(log[^1]["id"], last))
        {
            state["state_log"] = null;
            RecordInternalStateFailure(state, "state_log",
                new InvalidOperationException($"StateLog の末尾 {log[^1]["id"]} が last_logged_id {last} と一致しません"), context);
        }
        AttachEnemyStunHandler(state, monster, context);
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        state["inputs_readable"] = EnemyMoveStateInputKeys.All(key => state.ContainsKey(key) && reasons?.ContainsKey(key) != true);
        return state;
    }

    private static string ReadMachineStateId(MonsterMoveStateMachine machine, string field)
        => ReadDeclaredStateField<MonsterState>(machine, typeof(MonsterMoveStateMachine), field).Id
            ?? throw new InvalidOperationException($"MonsterMoveStateMachine.{field}.Id が null です");

    private static List<Dictionary<string, object?>> ReadStateLog(MonsterMoveStateMachine machine)
    {
        var log = machine.StateLog ?? throw new InvalidOperationException("MoveStateMachine.StateLog がありません");
        var entries = new List<Dictionary<string, object?>>(log.Count);
        for (int i = 0; i < log.Count; i++)
        {
            var entry = log[i] ?? throw new InvalidOperationException($"StateLog[{i}] が null です");
            entries.Add(new Dictionary<string, object?>
            {
                ["id"] = entry.Id ?? throw new InvalidOperationException($"StateLog[{i}].Id が null です"),
                // RandomBranchState の cooldown は IsMove の要素だけを数える。
                ["is_move"] = entry.IsMove
            });
        }
        return entries;
    }

    // false や空文字列（初期状態へ戻る FollowUpStateId）も実値として保持する。
    // null や欠落を通常行動・ロック解除済みへ補完しない。
    internal static bool IsEnemyMoveStateReadable(IReadOnlyDictionary<string, object?> state)
        => state.TryGetValue("id", out var id) && id is string
        && state.TryGetValue("follow_up_id", out var followUp) && followUp is string
        && state.TryGetValue("last_logged_id", out var last) && last is string
        && state.TryGetValue("can_transition_away", out var transition) && transition is bool;
}
