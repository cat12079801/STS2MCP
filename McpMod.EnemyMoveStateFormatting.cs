using System.Collections.Generic;
using System.Linq;

namespace STS2_MCP;

public static partial class McpMod
{
    // 行動ログの Markdown に出す末尾の要素数（全体の件数は括弧で出す）。
    private const int MarkdownStateLogTail = 4;

    private static string FormatEnemyMoveState(Dictionary<string, object?> enemy)
    {
        if (enemy.GetValueOrDefault("move_state") is not Dictionary<string, object?> move)
            return "";
        string Id(string key) => move.GetValueOrDefault(key) is string id ? $"`{id}`" : "unknown";
        string line = $"**Move:** {Id("id")} | follow-up: {Id("follow_up_id")}";
        if (move.GetValueOrDefault("has_follow_up") is false)
            line += " (none: terminal move)";
        if (move.GetValueOrDefault("is_stun") is true)
            line += $" | stun: {Id("stun_handler")}";
        if (move.GetValueOrDefault("state_log") is List<Dictionary<string, object?>> log)
        {
            var tail = log.Skip(System.Math.Max(0, log.Count - MarkdownStateLogTail))
                .Select(entry => entry.GetValueOrDefault("id") is string id ? $"`{id}`" : "?");
            line += log.Count == 0 ? " | log: empty"
                : $" | log({log.Count}): {(log.Count > MarkdownStateLogTail ? "… " : "")}{string.Join(" > ", tail)}";
        }
        var failures = move.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Reasons() => failures is { Count: > 0 }
            ? string.Join("; ", failures.Select(pair => $"{pair.Key}: {Flatten(pair.Value)}"))
            : "unknown";
        if (move.GetValueOrDefault("readable") is not true)
            line += $" | unreadable ({Reasons()})";
        else if (failures is { Count: > 0 })
            line += $" | inputs unreadable ({Reasons()})";
        return line;
    }
}
