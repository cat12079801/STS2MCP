using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    // 許可リスト。v0.113.1（sts2-a671e9ef）の宣言型・型を検査した private フィールドと、
    // 副作用の無い getter（下記）だけを読む。move の実行・初期化・乱数・履歴からの再構成はしない。
    // 対象外の敵は null（combat_state を省略）。
    private static Dictionary<string, object?>? BuildEnemyCombatState(MonsterModel? monster, string entityId)
    {
        if (monster is not WaterfallGiant giant)
            return null;
        var state = new Dictionary<string, object?>();
        string context = $"battle.enemies.{entityId}.combat_state";
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key, read, context);
        // TriggerAboutToBlowState（SteamEruptionPower.AfterDeath から）で true。false も実値。
        Read("is_about_to_blow", () => ReadDeclaredStateField<bool>(giant, typeof(WaterfallGiant), "_isAboutToBlow"));
        // AboutToBlowMove が SteamEruptionPower の量を写し、ExplodeMove の攻撃に使う。写す前は 0 の実値。
        Read("steam_eruption_damage", () => ReadDeclaredStateField<int>(giant, typeof(WaterfallGiant), "_steamEruptionDamage"));
        // AfterAddedToRoom が BasePressureGunDamage を入れ、PressureGunMove が攻撃の後に PressureGunIncrease（5）を足す。
        Read("current_pressure_gun_damage", () => RequireNonNegative(
            ReadDeclaredStateField<int>(giant, typeof(WaterfallGiant), "_currentPressureGunDamage"), "_currentPressureGunDamage"));
        // 以下は getter の現在値（AscensionHelper.GetValueIfAscension / IsDread → RunManager の読み取りだけ）。
        // RunManager.IsInProgress が false だと getter は Ascension・Dread を「無し」として返すので、そのときは読まない。
        Read("steam_boost_amount", () => ReadGiantGetter(giant, "SteamBoostAmount"));
        Read("pressurize_amount", () => ReadGiantGetter(giant, "PressurizeAmount"));
        Read("stomp_damage", () => ReadGiantGetter(giant, "StompDamage"));
        Read("ram_damage", () => ReadGiantGetter(giant, "RamDamage"));
        Read("pressure_up_damage", () => ReadGiantGetter(giant, "PressureUpDamage"));
        // SiphonMove はこれにプレイヤー人数を掛けて回復する。ここは掛ける前の 1 人分。
        Read("siphon_heal", () => ReadGiantGetter(giant, "SiphonHeal"));
        return state;
    }

    private static int ReadGiantGetter(WaterfallGiant giant, string name)
    {
        if (!RunManager.Instance.IsInProgress)
            throw new InvalidOperationException($"RunManager is not in progress; WaterfallGiant.{name} would ignore ascension and game mode");
        return RequireNonNegative(ReadDeclaredStateProperty<int>(giant, typeof(WaterfallGiant), name), name);
    }

    private static int RequireNonNegative(int value, string name)
        => value >= 0 ? value : throw new InvalidOperationException($"Expected non-negative WaterfallGiant.{name}, got {value}");

    private static string FormatEnemyCombatState(Dictionary<string, object?> enemy)
    {
        if (enemy.GetValueOrDefault("combat_state") is not Dictionary<string, object?> state || state.Count == 0)
            return "";
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        return "**Combat state:** " + string.Join(" | ", state.Where(entry => entry.Key != "unavailable_reasons").Select(entry =>
        {
            string value = entry.Value is bool flag ? (flag ? "yes" : "no")
                : entry.Value == null ? $"null ({reasons?.GetValueOrDefault(entry.Key) ?? "unavailable"})"
                : Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "?";
            return $"{entry.Key.Replace('_', ' ')}: {value}";
        }));
    }
}
