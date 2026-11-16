using System.Collections.Generic;
using System.Linq;

namespace STS2_MCP;

public static partial class McpMod
{
    private static string FormatEnemyMoveState(Dictionary<string, object?> enemy)
    {
        if (enemy.GetValueOrDefault("move_state") is not Dictionary<string, object?> move)
            return "";
        string Id(string key) => move.GetValueOrDefault(key) is string id ? $"`{id}`" : "unknown";
        string line = $"**Move:** {Id("id")} | follow-up: {Id("follow_up_id")}";
        if (move.GetValueOrDefault("readable") is not true)
        {
            string reasons = move.GetValueOrDefault("unavailable_reasons") is Dictionary<string, string> failures
                ? string.Join("; ", failures.Select(pair => $"{pair.Key}: {Flatten(pair.Value)}"))
                : "unknown";
            line += $" | unreadable ({reasons})";
        }
        return line;
    }
}
