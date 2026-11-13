using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace STS2_MCP;

public static partial class McpMod
{
    private static string FormatPowerInternalState(Dictionary<string, object?> state)
    {
        var parts = new List<string>();
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        foreach (string key in new[] { "skip_next_duration_tick", "block_base_value", "facing",
            "stolen_strength", "stolen_dexterity", "has_gained_block_this_turn", "attacks_played_this_turn",
            "card_to_clone_uid", "card_to_clone_readable" })
        {
            if (!state.TryGetValue(key, out var value)) continue;
            if (key == "skip_next_duration_tick" && value is false) continue;
            string text;
            if (value is List<Dictionary<string, object?>> amounts)
            {
                if (amounts.Count == 0) continue;
                text = string.Join(", ", amounts.Select(row =>
                    $"{row["player_id"]}: {Convert.ToString(row["amount"], CultureInfo.InvariantCulture)}"));
            }
            else text = value is bool flag ? (flag ? "yes" : "no")
                : key == "card_to_clone_uid" && value == null && state.GetValueOrDefault("card_to_clone_readable") is true ? "none"
                : value == null ? $"null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})"
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
            parts.Add($"{key.Replace('_', ' ')}: {text}");
        }
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }
}
