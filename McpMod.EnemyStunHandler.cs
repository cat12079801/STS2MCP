using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string StunNoOpHandler = "CreatureCmd.Stun.no_op";

    // v0.114.0（sts2-ae309f85）で CreatureCmd.Stun(creature, stunMove, nextMoveId) に stunMove を渡す呼び出しの全件。
    // 本体のソースを grep した結果（Whistle・PlowPower の他の敵・ImbalancedPower・ShriekPower は
    // 引数なしの CreatureCmd.Stun(creature, nextMoveId) で、no-op の lambda になる）。
    // OnMonster=true は、その敵自身のメソッド（delegate の Target がこの個体の MonsterModel）。
    private static readonly (Type Type, string Method, bool OnMonster)[] KnownStunHandlers =
    [
        (typeof(BowlbugRock), "DizzyMove", true),            // BowlbugRock.Stun（HeadbuttMove の後）
        (typeof(SlumberingBeetle), "WakeUpMove", true),      // SlumberPower.AfterDamageReceived
        (typeof(Tunneler), "StillDizzyMove", true),          // BurrowedPower.AfterBlockBroken
        (typeof(CeremonialBeast), "StunnedMove", true),      // PlowPower.AfterDamageReceived
        (typeof(LagavulinMatriarch), "WakeUpMove", true),    // AsleepPower.AfterDamageReceived
        (typeof(RavenousPower), "StunnedMove", false),       // RavenousPower.AfterDeath
        (typeof(FlutterPower), "StunnedMove", false)         // FlutterPower.AfterDamageReceived
    ];

    // CreatureCmd.Stun は stunMove をローカル関数 Wrapper（CreatureCmd の入れ子型の
    // <Stun>g__Wrapper。捕捉フィールドは creature と stunMove）で包み、Creature.StunInternal が
    // new MoveState("STUNNED", Wrapper, StunIntent) を NextMove にする。_onPerform.Method.Name だけでは
    // 通常のスタンと起床（SlumberingBeetle.WakeUpMove など）を区別できないので、Wrapper の捕捉した stunMove を
    // 宣言型・delegate 型を検査して読み、許可リストの処理に対応付ける。delegate は呼ばない。
    // is_stun=false は「NextMove が CreatureCmd.Stun の Wrapper ではなく、ID も STUNNED ではない」実値で、stun_handler は null。
    // Wrapper と ID（MonsterModel.stunnedMoveId）が食い違うときは is_stun も null と理由（未観測）。
    // is_stun=true で対応付けられない delegate は stun_handler=null と理由（未観測）。復帰 ID から推測しない。
    private static void AttachEnemyStunHandler(Dictionary<string, object?> state, MonsterModel? monster, string context)
    {
        Delegate? inner = null;
        Creature? captured = null;
        try
        {
            var model = monster ?? throw new InvalidOperationException("Monster がありません");
            var move = model.NextMove ?? throw new InvalidOperationException("NextMove がありません");
            var onPerform = ReadDeclaredStateField<Func<IReadOnlyList<Creature>, Task>>(move, typeof(MoveState), "_onPerform");
            bool wrapper = TryReadStunWrapper(onPerform, out inner, out captured);
            // v0.114.0 では MonsterModel.stunnedMoveId（"STUNNED"）を使うのは Creature.StunInternal だけ。
            // Wrapper の名前・形が変わった版で、スタン中に is_stun=false を実値として返さない（未観測に倒す）。
            string id = move.Id ?? throw new InvalidOperationException("NextMove.Id が null です");
            bool stunnedId = id == MonsterModel.stunnedMoveId;
            if (!wrapper && stunnedId)
                throw new InvalidOperationException(
                    $"{MonsterModel.stunnedMoveId} だが CreatureCmd.Stun の Wrapper の形が既知と違います（{onPerform.Method.DeclaringType?.FullName ?? "?"}.{onPerform.Method.Name}）");
            if (wrapper && !stunnedId)
                throw new InvalidOperationException(
                    $"CreatureCmd.Stun の Wrapper だが行動 ID が {MonsterModel.stunnedMoveId} ではありません（{id}）");
            state["is_stun"] = wrapper;
        }
        catch (Exception ex)
        {
            state["is_stun"] = null;
            state["stun_handler"] = null;
            RecordInternalStateFailure(state, "is_stun", ex, context);
            RecordInternalStateFailure(state, "stun_handler",
                new InvalidOperationException("is_stun が読めないので stun_handler も未観測です", ex), context);
            return;
        }
        if (state["is_stun"] is not true)
        {
            state["stun_handler"] = null;
            return;
        }
        ReadInternalStateValue(state, "stun_handler", () => ResolveStunHandler(monster!, inner!, captured!), context);
    }

    // Wrapper なら true と捕捉値。Wrapper でなければ false。Wrapper の形が崩れていれば例外（未観測）。
    private static bool TryReadStunWrapper(Delegate onPerform, out Delegate? inner, out Creature? captured)
    {
        inner = null;
        captured = null;
        var method = onPerform.Method;
        var closureType = method.DeclaringType;
        if (closureType?.DeclaringType != typeof(CreatureCmd) || !method.Name.StartsWith("<Stun>g__Wrapper|", StringComparison.Ordinal))
            return false;
        if (onPerform.GetInvocationList().Length != 1 || method.IsStatic
            || onPerform.Target is not { } target || target.GetType() != closureType)
            throw new InvalidOperationException($"CreatureCmd.Stun の Wrapper の捕捉先が {closureType.Name} ではありません");
        inner = ReadDeclaredStateField<Func<IReadOnlyList<Creature>, Task>>(target, closureType, "stunMove");
        captured = ReadDeclaredStateField<Creature>(target, closureType, "creature");
        return true;
    }

    private static string ResolveStunHandler(MonsterModel monster, Delegate inner, Creature captured)
    {
        // Wrapper はこの個体に対して作られたものか（CreatureCmd.Stun の creature 引数）。
        if (!ReferenceEquals(captured, monster.Creature))
            throw new InvalidOperationException("Wrapper の creature がこの個体ではありません");
        if (inner.GetInvocationList().Length != 1)
            throw new InvalidOperationException("stunMove が multicast delegate です");
        if (IsCreatureCmdStunNoOp(inner))
            return StunNoOpHandler;
        var method = inner.Method;
        foreach (var (type, name, onMonster) in KnownStunHandlers)
        {
            if (method.DeclaringType != type || method.Name != name || method.IsStatic)
                continue;
            if (inner.Target is not { } target || target.GetType() != type)
                throw new InvalidOperationException($"{type.Name}.{name} の Target が {type.Name} ではありません");
            if (onMonster && !ReferenceEquals(target, monster))
                throw new InvalidOperationException($"{type.Name}.{name} の Target がこの個体ではありません");
            // パワーの処理は、そのパワーの所有者がこの個体か（PowerModel.Owner は AssertMutable の後 _owner を返す getter）。
            if (!onMonster && (target is not PowerModel power || !ReferenceEquals(power.Owner, monster.Creature)))
                throw new InvalidOperationException($"{type.Name}.{name} の所有者がこの個体ではありません");
            return $"{type.Name}.{name}";
        }
        throw new InvalidOperationException(
            $"未対応の stunMove: {method.DeclaringType?.FullName ?? "?"}.{method.Name}");
    }

    // CreatureCmd.Stun(creature, nextMoveId) の (IReadOnlyList<Creature> _) => Task.CompletedTask
    // （CreatureCmd の入れ子型の <Stun>b__…）。名前の序数に頼らず、
    // 宣言型が CreatureCmd の入れ子で、本体の IL が「call Task.get_CompletedTask; ret」だけであることを確かめる。
    private static bool IsCreatureCmdStunNoOp(Delegate inner)
    {
        var method = inner.Method;
        if (method.DeclaringType?.DeclaringType != typeof(CreatureCmd) || !method.Name.StartsWith("<Stun>b__", StringComparison.Ordinal))
            return false;
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is not { Length: 6 } || il[0] != 0x28 || il[5] != 0x2A)
            return false;
        MethodBase? called = method.Module.ResolveMethod(BitConverter.ToInt32(il, 1));
        return called == typeof(Task).GetProperty(nameof(Task.CompletedTask))?.GetMethod;
    }
}
