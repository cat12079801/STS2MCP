using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace STS2_MCP;

public static partial class McpMod
{
    // Card-reference uid fields and their read-success flags. A readable null is
    // shown as "none"; readable=true is JSON-only, false is shown.
    private static readonly Dictionary<string, string> CardUidReadableKeys = new()
    {
        ["card_to_clone_uid"] = "card_to_clone_readable",
        ["played_card_uid"] = "played_card_readable"
    };

    private static string FormatPowerInternalState(Dictionary<string, object?> state)
    {
        var parts = new List<string>();
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        foreach (string key in new[] { "skip_next_duration_tick", "block_base_value", "facing",
            "stolen_strength", "stolen_dexterity", "has_gained_block_this_turn", "bound_card_played", "attacks_played_this_turn",
            "card_to_clone_uid", "card_to_clone_readable", "played_card_uid", "played_card_readable",
            "stolen_gold", "target_player_id" })
        {
            if (!state.TryGetValue(key, out var value)) continue;
            if (key == "skip_next_duration_tick" && value is false) continue;
            if (CardUidReadableKeys.ContainsValue(key) && value is true) continue;
            string text;
            if (value is List<Dictionary<string, object?>> amounts)
            {
                if (amounts.Count == 0) continue;
                text = string.Join(", ", amounts.Select(row =>
                    $"{row["player_id"]}: {Convert.ToString(row["amount"], CultureInfo.InvariantCulture)}"));
            }
            else text = value is bool flag ? (flag ? "yes" : "no")
                : value == null && CardUidReadableKeys.TryGetValue(key, out var readableKey)
                    && state.GetValueOrDefault(readableKey) is true ? "none"
                : value == null ? $"null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})"
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
            parts.Add($"{key.Replace('_', ' ')}: {text}");
        }
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }
}
