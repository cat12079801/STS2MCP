using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AppendCombatHistoryMarkdown(StringBuilder sb, Dictionary<string, object?> player)
    {
        if (player.GetValueOrDefault("combat_history") is not Dictionary<string, object?> history) return;
        var parts = new List<string>();
        var reasons = history.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        foreach (var (key, value) in history)
        {
            if (key is "turn_number" or "unavailable_reasons") continue;
            if (value == null)
                parts.Add($"{key}=null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})");
            else if (value is int count && count > 0)
            {
                string label = key switch
                {
                    "cards_exhausted_this_turn" => "exhausted",
                    "unblocked_damage_events_this_turn" => "unblocked hits (turn)",
                    "unblocked_damage_events_this_combat" => "unblocked hits (combat)",
                    "block_gains_this_turn" => "card block gains",
                    _ => key
                };
                parts.Add($"{label}={count}");
            }
            else if (value is List<string> uids && uids.Count > 0)
                parts.Add($"last turn=[{string.Join(", ", uids)}]");
            else if (value is Dictionary<string, object?> plays && plays.GetValueOrDefault("total") is int total && total > 0)
            {
                var byType = plays["by_type"] as Dictionary<string, int>;
                string types = byType == null ? "" : string.Join(", ", byType.Where(p => p.Value > 0).Select(p => $"{p.Key}:{p.Value}"));
                parts.Add($"{(key == "card_plays_started_this_turn" ? "started" : "finished")}={total} ({types})");
            }
        }
        if (history.GetValueOrDefault("turn_number") == null)
            parts.Insert(0, $"turn=null ({reasons?.GetValueOrDefault("turn_number") ?? "unavailable"})");
        if (parts.Count == 0) return;
        string turn = history.GetValueOrDefault("turn_number") is int number ? $" (player turn {number})" : "";
        sb.AppendLine($"History{turn}: {string.Join("; ", parts)}");
        sb.AppendLine();
    }
}
