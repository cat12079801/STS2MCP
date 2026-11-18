// 公開 getter と、MOD が宣言型・型を検査して読む private フィールドの形だけを写す。
// ゲームの抽選・遷移・実行メソッドは用意しない。CreatureCmd.Stun は本体と同じく stunMove を
// ローカル関数 Wrapper（creature と stunMove を捕捉する）で包み、引数なしの版は no-op の lambda を渡す
// （コンパイラが作る名前・捕捉フィールド・IL の形を本体の v0.113.1 と揃えるため）。
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine
{
    public class MonsterState
    {
        public string? IdValue { get; set; }
        public HashSet<string> Fail { get; } = new();
        protected T Read<T>(string key, T value)
            => Fail.Contains(key) ? throw new InvalidOperationException($"{key} 読取失敗") : value;
        public string Id => Read(nameof(Id), IdValue!);
        public virtual bool IsMove => Read(nameof(IsMove), this is MoveState);
    }

    public class MoveState : MonsterState
    {
        private readonly Func<IReadOnlyList<Creature>, Task> _onPerform;

        public MoveState() : this(UnsetMove) { }

        public MoveState(Func<IReadOnlyList<Creature>, Task> onPerform) => _onPerform = onPerform;

        public MonsterState? FollowUpValue { get; set; }
        public string? FollowUpIdValue { get; set; }
        public bool TransitionValue { get; set; } = true;
        public bool MustPerformValue { get; set; }
        public MonsterState? FollowUpState => Read(nameof(FollowUpState), FollowUpValue);
        public string? FollowUpStateId => Read(nameof(FollowUpStateId), FollowUpIdValue);
        public bool CanTransitionAway => Read(nameof(CanTransitionAway), TransitionValue);
        public bool MustPerformOnceBeforeTransitioning => Read(nameof(MustPerformOnceBeforeTransitioning), MustPerformValue);

        // 本体の UnsetMove と同じく呼ばれたら投げる。MOD は delegate を呼ばない。
        private static Task UnsetMove(IReadOnlyList<Creature> _)
            => throw new InvalidOperationException("delegate を呼んではいけない");
    }

    public class MonsterMoveStateMachine
    {
        private MonsterState? _currentState;
        private MonsterState? _initialState;
        private bool _performedFirstMove;

        public Dictionary<string, MonsterState> States { get; } = new();
        public List<MonsterState>? Log { get; set; } = new();
        public bool ThrowLog { get; set; }
        public Action? OnLogRead { get; set; }

        public List<MonsterState> StateLog
        {
            get
            {
                if (ThrowLog) throw new InvalidOperationException("StateLog 読取失敗");
                var log = Log!;
                OnLogRead?.Invoke();
                return log;
            }
        }

        public void SetInternals(MonsterState? current, MonsterState? initial, bool performedFirstMove)
        {
            _currentState = current;
            _initialState = initial;
            _performedFirstMove = performedFirstMove;
        }

        // 本体の ForceCurrentState（SetMoveImmediate から）: 現在地点だけを差し替え、ログには足さない。
        public void ForceCurrentState(MonsterState state) => _currentState = state;

        public (MonsterState?, MonsterState?, bool) PeekInternals() => (_currentState, _initialState, _performedFirstMove);
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public MonsterModel? Monster { get; set; }

        // 本体の Creature.StunInternal: new MoveState("STUNNED", stunMove, StunIntent) を NextMove にする。
        // 明示の復帰先が無ければ StateLog.Last().Id。
        public void StunInternal(Func<IReadOnlyList<Creature>, Task> stunMove, string? nextMoveId)
        {
            if (string.IsNullOrEmpty(nextMoveId))
                nextMoveId = Monster!.Machine!.Log!.Last().Id;
            var state = new MoveState(stunMove)
            {
                IdValue = "STUNNED",
                FollowUpIdValue = nextMoveId,
                TransitionValue = false,
                MustPerformValue = true
            };
            // 本体の MonsterModel.SetMoveImmediate → MonsterMoveStateMachine.ForceCurrentState。
            Monster!.Move = state;
            Monster.Machine?.ForceCurrentState(state);
        }
    }
}

namespace MegaCrit.Sts2.Core.Commands
{
    public static class CreatureCmd
    {
        public static Task Stun(Creature creature, string? nextMoveId = null)
            => Stun(creature, (IReadOnlyList<Creature> _) => Task.CompletedTask, nextMoveId);

        public static Task Stun(Creature creature, Func<IReadOnlyList<Creature>, Task> stunMove, string? nextMoveId = null)
        {
            creature.StunInternal(Wrapper, nextMoveId);
            return Task.CompletedTask;
            async Task Wrapper(IReadOnlyList<Creature> c)
            {
                // 本体は creature の VFX を足してから stunMove を呼ぶ（creature を捕捉する）。
                if (creature.Monster == null) throw new InvalidOperationException("no monster");
                await stunMove(c);
            }
        }
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    using MonsterMoves.MonsterMoveStateMachine;

    public class MonsterModel
    {
        public const string stunnedMoveId = "STUNNED";
        public MonsterModel() => Creature = new Creature { Monster = this };
        public Creature Creature { get; set; }
        public MoveState? Move { get; set; }
        public MonsterMoveStateMachine? Machine { get; set; }
        public bool ThrowMove { get; set; }
        public bool ThrowMachine { get; set; }
        public bool SpawnedValue { get; set; }
        public bool ThrowSpawned { get; set; }
        public MoveState NextMove => ThrowMove
            ? throw new InvalidOperationException("NextMove 読取失敗") : Move!;
        public MonsterMoveStateMachine? MoveStateMachine => ThrowMachine
            ? throw new InvalidOperationException("MoveStateMachine 読取失敗") : Machine;
        public bool SpawnedThisTurn => ThrowSpawned
            ? throw new InvalidOperationException("SpawnedThisTurn 読取失敗") : SpawnedValue;
    }

    // 本体の PowerModel.Owner は AssertMutable の後 _owner を返す getter。
    public class PowerModel
    {
        public Creature? Owner { get; set; }
    }
}

// 本体で CreatureCmd.Stun に stunMove を渡す敵・パワー。メソッドの宣言型と名前だけを本体と揃える。
namespace MegaCrit.Sts2.Core.Models.Monsters
{
    using Commands;

    public sealed class BowlbugRock : MonsterModel
    {
        public Task StunSelf() => CreatureCmd.Stun(Creature, DizzyMove);
        private Task DizzyMove(IReadOnlyList<Creature> targets) => Task.CompletedTask;
    }

    public sealed class SlumberingBeetle : MonsterModel
    {
        public Task WakeUpMove(IReadOnlyList<Creature> _) => Task.CompletedTask;
        public Task StunSelf() => CreatureCmd.Stun(Creature, WakeUpMove, "ROLL_OUT_MOVE");
    }

    public sealed class Tunneler : MonsterModel
    {
        public Task StillDizzyMove(IReadOnlyList<Creature> _) => Task.CompletedTask;
        public Task StunSelf() => CreatureCmd.Stun(Creature, StillDizzyMove, "BITE_MOVE");
    }

    public sealed class CeremonialBeast : MonsterModel
    {
        public Task StunnedMove(IReadOnlyList<Creature> _) => Task.CompletedTask;
        public Task StunSelf() => CreatureCmd.Stun(Creature, StunnedMove, "BEAST_CRY_MOVE");
    }

    public sealed class LagavulinMatriarch : MonsterModel
    {
        public Task WakeUpMove(IReadOnlyList<Creature> _) => Task.CompletedTask;
        public Task StunSelf() => CreatureCmd.Stun(Creature, WakeUpMove, "SLASH_MOVE");
    }
}

namespace MegaCrit.Sts2.Core.Models.Powers
{
    using Commands;

    public sealed class RavenousPower : PowerModel
    {
        public Task StunOwner(Creature owner) => CreatureCmd.Stun(owner, StunnedMove);
        private Task StunnedMove(IReadOnlyList<Creature> targets) => Task.CompletedTask;
    }

    public sealed class FlutterPower : PowerModel
    {
        public Task StunOwner(Creature owner, string next) => CreatureCmd.Stun(owner, StunnedMove, next);
        private Task StunnedMove(IReadOnlyList<Creature> targets) => Task.CompletedTask;
    }
}
