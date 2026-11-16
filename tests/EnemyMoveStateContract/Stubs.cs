// 公開 getter の形だけを写す。ゲームの抽選・遷移・実行メソッドは用意しない。
namespace MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine
{
    public class MonsterState
    {
        public string? IdValue { get; set; }
        public HashSet<string> Fail { get; } = new();
        protected T Read<T>(string key, T value)
            => Fail.Contains(key) ? throw new InvalidOperationException($"{key} 読取失敗") : value;
        public string Id => Read(nameof(Id), IdValue!);
    }

    public class MoveState : MonsterState
    {
        public MonsterState? FollowUpValue { get; set; }
        public string? FollowUpIdValue { get; set; }
        public bool TransitionValue { get; set; } = true;
        public MonsterState? FollowUpState => Read(nameof(FollowUpState), FollowUpValue);
        public string? FollowUpStateId => Read(nameof(FollowUpStateId), FollowUpIdValue);
        public bool CanTransitionAway => Read(nameof(CanTransitionAway), TransitionValue);
    }

    public class MonsterMoveStateMachine
    {
        public List<MonsterState>? Log { get; set; } = new();
        public bool ThrowLog { get; set; }
        public List<MonsterState> StateLog => ThrowLog
            ? throw new InvalidOperationException("StateLog 読取失敗") : Log!;
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    using MonsterMoves.MonsterMoveStateMachine;

    public class MonsterModel
    {
        public MoveState? Move { get; set; }
        public MonsterMoveStateMachine? Machine { get; set; }
        public bool ThrowMove { get; set; }
        public bool ThrowMachine { get; set; }
        public MoveState NextMove => ThrowMove
            ? throw new InvalidOperationException("NextMove 読取失敗") : Move!;
        public MonsterMoveStateMachine? MoveStateMachine => ThrowMachine
            ? throw new InvalidOperationException("MoveStateMachine 読取失敗") : Machine;
    }
}
