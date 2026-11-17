// v0.113.1（sts2-a671e9ef）の公開メンバーの形だけを写す。行動の実行・乱数・パワーの付与は用意しない。
// GetSingleDamage は AttackIntent.GetSingleDamage の本体（DamageCalc → LocalContext.GetMe → Hook.ModifyDamage → Math.Max(0, (int)num)）
// と同じ並びにし、Hook.ModifyDamage だけを試験が与える補正関数に置き換える。
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    using MegaCrit.Sts2.Core.Combat;

    public class Creature
    {
        public ICombatState? CombatState { get; set; }
    }
}

namespace MegaCrit.Sts2.Core.Combat
{
    using MegaCrit.Sts2.Core.Entities.Creatures;

    public interface ICombatState
    {
        IReadOnlyList<Creature> PlayerCreatures { get; }
    }

    public sealed class CombatState : ICombatState
    {
        public List<Creature> Players { get; } = [new Creature()];
        public IReadOnlyList<Creature> PlayerCreatures => Players;
    }
}

namespace MegaCrit.Sts2.Core.Context
{
    using MegaCrit.Sts2.Core.Combat;

    public static class LocalContext
    {
        // 本物は Player? を返す。MOD は null かどうかだけを見る。
        public static object? Me { get; set; } = new();
        public static bool Throw { get; set; }
        public static int Calls { get; set; }
        public static object? GetMe(ICombatState? combatState)
        {
            Calls++;
            if (combatState == null) return null;
            if (Throw) throw new InvalidOperationException("Local player not found in combat.");
            return Me;
        }
    }
}

namespace MegaCrit.Sts2.Core.MonsterMoves.Intents
{
    using MegaCrit.Sts2.Core.Context;
    using MegaCrit.Sts2.Core.Entities.Creatures;

    public enum IntentType { Attack, Buff, DeathBlow }

    public abstract class AbstractIntent
    {
        public abstract IntentType IntentType { get; }
    }

    public sealed class BuffIntent : AbstractIntent
    {
        public override IntentType IntentType => IntentType.Buff;
    }

    public abstract class AttackIntent : AbstractIntent
    {
        // 試験だけの差し替え口。本物は Hook.ModifyDamage（加算 → 倍率 → 後段倍率 → 上限 → Math.Max(0m, num)）。
        public static Func<decimal, decimal> Modify { get; set; } = damage => damage;
        public override IntentType IntentType => IntentType.Attack;
        public Func<decimal>? DamageCalc { get; set; }
        public virtual int Repeats => 0;

        public int GetSingleDamage(IEnumerable<Creature> targets, Creature owner)
        {
            decimal num = DamageCalc!();
            object? me = LocalContext.GetMe(owner.CombatState);
            if (me != null)
                num = Math.Max(0m, Modify(DamageCalc!()));
            return Math.Max(0, (int)num);
        }
    }

    public class SingleAttackIntent : AttackIntent
    {
        public override int Repeats => 1;
        public SingleAttackIntent(int damage) { DamageCalc = () => damage; }
        public SingleAttackIntent(Func<decimal> damageCalc) { DamageCalc = damageCalc; }
    }

    public sealed class DeathBlowIntent : SingleAttackIntent
    {
        public override IntentType IntentType => IntentType.DeathBlow;
        public DeathBlowIntent(Func<decimal> damageCalc) : base(damageCalc) { }
    }

    public sealed class MultiAttackIntent : AttackIntent
    {
        private readonly int _repeat;
        private readonly Func<int>? _repeatCalc;
        public override int Repeats => _repeatCalc?.Invoke() ?? _repeat;
        public MultiAttackIntent(int damage, int repeat) { DamageCalc = () => damage; _repeat = repeat; }
        public MultiAttackIntent(int damage, Func<int> repeatCalc) { DamageCalc = () => damage; _repeatCalc = repeatCalc; }
    }

    // Repeats が負になる壊れた意図（本体には無い。契約の検査用）。
    public sealed class BrokenAttackIntent : AttackIntent
    {
        public int RepeatsValue { get; set; } = -1;
        public bool ThrowRepeats { get; set; }
        public override int Repeats => ThrowRepeats ? throw new InvalidOperationException("Repeats 読取失敗") : RepeatsValue;
    }
}
