using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace STS2_MCP;

// 生成した許可リスト（McpMod.StateAllowlist.Generated.cs）の型と、internal_state の markdown。
// ゲームの型に依存しない（契約試験がこのファイルと生成物だけをリンクできるように）。読む処理は McpMod.StateAllowlist.cs。

/// <summary>読む対象のメンバーの種類（棚卸しの kind）。</summary>
internal enum StateMemberKind { Field, AutoProperty, DataField }

/// <summary>ゲームの値を state の値に変える方法。参照型は既存の id（カードの uid・combat_id・モデルの id）にする。</summary>
internal enum StateValueConverter
{
    Bool, Int, NullableInt, Decimal, EnumName,
    CardUid, CardModelId, CardPlayCardUid,
    CreatureCombatId, CreatureCombatIds,
    KeywordNames, PowerRefs, SerializableCards
}

/// <summary>markdown に出す条件。NonDefault は false・0・空・null（理由なし）・Normal を省く。</summary>
internal enum StateMarkdown { NonDefault, Never, DiffersFromAmount }

/// <summary>
/// 生成した許可リスト（McpMod.StateAllowlist.Generated.cs）の 1 項目。
/// Section は state の行の種類（card / relic / power / player / battle）、ModelType は行のモデルが
/// その型（の派生）のときだけ読む型、DeclaringType はメンバーを宣言した型（パワーの Data は Outer+Data）。
/// Note は分類表の備考（人間に見える根拠）で、レビューのために生成物に残す。
/// </summary>
internal sealed record StateAllowlistEntry(
    string Section, string ModelType, string DeclaringType, string Member, StateMemberKind Kind,
    string Key, StateValueConverter Converter, StateMarkdown Markdown, string Note);

public static partial class McpMod
{
    /// <summary>
    /// internal_state を markdown の 1 行ぶんの文字列にする（" (internal: key value, ...)"）。
    /// 許可リストの Markdown が NonDefault の項目は既定値（false・0・空・null・Normal）を省き、
    /// Never は出さず、DiffersFromAmount は行の amount と違うときだけ出す。読めなかった値は理由を出す。
    /// </summary>
    private static string FormatAllowlistedState(Dictionary<string, object?> row, string section)
    {
        string parts = FormatAllowlistedParts(row, section);
        return parts.Length == 0 ? "" : " (internal: " + parts + ")";
    }

    /// <summary>internal_state の出す項目を ", " でつないだもの（無ければ空）。カードの詳細の行が使う。</summary>
    private static string FormatAllowlistedParts(Dictionary<string, object?> row, string section)
    {
        if (row.GetValueOrDefault("internal_state") is not Dictionary<string, object?> values) return "";
        var reasons = values.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        var parts = new List<string>();
        foreach (var (key, value) in values)
        {
            if (key == "unavailable_reasons") continue;
            var mode = StateAllowlist.FirstOrDefault(e => e.Section == section && e.Key == key)?.Markdown
                ?? StateMarkdown.NonDefault;
            if (value == null && reasons?.GetValueOrDefault(key) is { } reason)
            {
                parts.Add($"{key.Replace('_', ' ')} null ({reason})");
                continue;
            }
            if (mode == StateMarkdown.Never) continue;
            if (mode == StateMarkdown.DiffersFromAmount && Equals(value, row.GetValueOrDefault("amount"))) continue;
            if (mode == StateMarkdown.NonDefault && IsDefaultAllowlistValue(value)) continue;
            parts.Add($"{key.Replace('_', ' ')} {FormatAllowlistValue(value)}");
        }
        return string.Join(", ", parts);
    }

    private static bool IsDefaultAllowlistValue(object? value) => value switch
    {
        null => true,
        bool flag => !flag,
        int number => number == 0,
        decimal number => number == 0m,
        string text => text == "Normal",
        ICollection collection => collection.Count == 0,
        _ => false
    };

    private static string FormatAllowlistValue(object? value) => value switch
    {
        null => "none",
        bool flag => flag ? "yes" : "no",
        IEnumerable<Dictionary<string, object?>> rows => "[" + string.Join(", ", rows.Select(r =>
            string.Join(" ", r.Where(p => p.Value != null).Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture))))) + "]",
        string text => text,
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(item =>
            Convert.ToString(item, CultureInfo.InvariantCulture))) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?"
    };
}
