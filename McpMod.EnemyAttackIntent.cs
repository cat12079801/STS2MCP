using System;
using System.Collections.Generic;
using System.Globalization;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace STS2_MCP;

public static partial class McpMod
{
    // v0.114.0（sts2-ae309f85）の AttackIntent（SingleAttackIntent・MultiAttackIntent・DeathBlowIntent の基底）だけに付ける。
    // 読むのは現在の行動（MonsterModel.NextMove）の意図そのもので、ラベル・本文・履歴から逆算しない。
    // DamageCalc はモンスターが行動の定義で渡した getter の delegate（定数か、フィールド・パワー量・アセンションを読むだけ）。
    // GetSingleDamage はゲームの意図の表示（GetIntentLabel / GetHoverTip）が毎回呼ぶのと同じ読取で、乱数・状態を動かさない。
    private static Dictionary<string, object?>? BuildEnemyAttackIntent(AbstractIntent intent, Creature creature, string entityId, int intentIndex)
    {
        if (intent is not AttackIntent attack)
            return null;
        var state = new Dictionary<string, object?>();
        string context = $"battle.enemies.{entityId}.intents.{intentIndex}.attack";
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key,
            () => read() ?? throw new InvalidOperationException($"{key} が null です"), context);
        Func<decimal> RequireDamageCalc() => attack.DamageCalc
            ?? throw new InvalidOperationException("AttackIntent.DamageCalc が null です");

        // 補正（筋力・脆弱・脱力など Hook.ModifyDamage）の前の値。decimal のまま invariant culture で文字列にする。
        Read("base_damage", () => RequireDamageCalc()().ToString(CultureInfo.InvariantCulture));
        // SingleAttackIntent（DeathBlowIntent を含む）は 1、MultiAttackIntent は _repeatCalc?.Invoke() ?? _repeat。
        Read("hits", () =>
        {
            int hits = attack.Repeats;
            return hits >= 0 ? hits : throw new InvalidOperationException($"AttackIntent.Repeats が負です: {hits}");
        });
        // GetSingleDamage はローカルのプレイヤー（LocalContext.GetMe(owner.CombatState)）が居るときだけ
        // Hook.ModifyDamage を通し、居なければ補正前の値を返す。補正前を解決値として出さないため、同じ判定を先に行う。
        Read("resolved_damage", () =>
        {
            RequireDamageCalc();
            var combat = creature.CombatState
                ?? throw new InvalidOperationException("ローカルのプレイヤーがいません（Creature.CombatState が null）");
            if (LocalContext.GetMe(combat) == null)
                throw new InvalidOperationException("ローカルのプレイヤーがいません（LocalContext.GetMe が null）");
            return attack.GetSingleDamage(combat.PlayerCreatures, creature);
        });
        state["readable"] = IsEnemyAttackIntentReadable(state);
        return state;
    }

    internal static bool IsEnemyAttackIntentReadable(IReadOnlyDictionary<string, object?> state)
        => state.TryGetValue("base_damage", out var baseDamage) && baseDamage is string
        && state.TryGetValue("hits", out var hits) && hits is int
        && state.TryGetValue("resolved_damage", out var resolved) && resolved is int;

    // 意図の Markdown の末尾に足す。attack が無い意図（攻撃以外・旧 MOD）は空文字列。
    private static string FormatEnemyAttackIntent(Dictionary<string, object?> intent)
    {
        if (intent.GetValueOrDefault("attack") is not Dictionary<string, object?> attack)
            return "";
        var reasons = attack.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Value(string key) => attack.GetValueOrDefault(key) is { } value
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?"
            : $"null ({Flatten(reasons?.GetValueOrDefault(key) ?? "unavailable")})";
        return $" [attack: base {Value("base_damage")} | hits {Value("hits")} | resolved {Value("resolved_damage")}]";
    }
}
