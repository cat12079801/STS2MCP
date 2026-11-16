using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_MCP;

public static partial class McpMod
{
    // v0.113.1: MonsterModel.NextMove / MoveStateMachine と MoveState の getter だけを読む。
    // FollowUpState は分岐状態の場合もある。次の行動を抽選・実行して解決しない。
    private static Dictionary<string, object?> BuildEnemyMoveState(MonsterModel? monster, string entityId)
    {
        var state = new Dictionary<string, object?>();
        string context = $"battle.enemies.{entityId}.move_state";
        MonsterModel Monster() => monster ?? throw new InvalidOperationException("Monster がありません");
        MoveState NextMove() => Monster().NextMove
            ?? throw new InvalidOperationException("NextMove がありません");
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
            var log = Monster().MoveStateMachine?.StateLog
                ?? throw new InvalidOperationException("MoveStateMachine.StateLog がありません");
            if (log.Count == 0)
                throw new InvalidOperationException("MoveStateMachine.StateLog が空です");
            return log[log.Count - 1]?.Id;
        });
        Read("can_transition_away", () => NextMove().CanTransitionAway);
        state["readable"] = IsEnemyMoveStateReadable(state);
        return state;
    }

    // false や空文字列（初期状態へ戻る FollowUpStateId）も実値として保持する。
    // null や欠落を通常行動・ロック解除済みへ補完しない。
    internal static bool IsEnemyMoveStateReadable(IReadOnlyDictionary<string, object?> state)
        => state.TryGetValue("id", out var id) && id is string
        && state.TryGetValue("follow_up_id", out var followUp) && followUp is string
        && state.TryGetValue("last_logged_id", out var last) && last is string
        && state.TryGetValue("can_transition_away", out var transition) && transition is bool;
}
