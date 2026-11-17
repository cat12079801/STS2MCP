using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace STS2_MCP;

public static partial class McpMod
{
    // 許可リスト。v0.113.1（sts2-a671e9ef）の宣言型・型を検査した private フィールドだけを読む。
    // move の実行・初期化・乱数・履歴からの再構成はしない。対象外の敵は null（combat_state を省略）。
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
        return state;
    }

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
