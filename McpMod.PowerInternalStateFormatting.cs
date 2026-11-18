using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace STS2_MCP;

public static partial class McpMod
{
    // Nullable reference fields (card uids, the Hex applier) and their read-success
    // flags. A readable null is shown as "none"; readable=true is JSON-only, false is shown.
    private static readonly Dictionary<string, string> ReferenceReadableKeys = new()
    {
        ["card_to_clone_uid"] = "card_to_clone_readable",
        ["played_card_uid"] = "played_card_readable",
        ["applier_combat_id"] = "applier_readable"
    };

    private static string FormatPowerInternalState(Dictionary<string, object?> state)
    {
        var parts = new List<string>();
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        foreach (string key in new[] { "skip_next_duration_tick", "block_base_value", "facing",
            "stolen_strength", "stolen_dexterity", "has_gained_block_this_turn", "bound_card_played", "attacks_played_this_turn",
            "card_to_clone_uid", "card_to_clone_readable", "played_card_uid", "played_card_readable",
            "stolen_gold", "target_player_id", "applier_combat_id", "applier_readable", "was_just_applied_by_enemy",
            "damage", "self_damage", "cards_left", "already_applied" })
        {
            if (!state.TryGetValue(key, out var value)) continue;
            if (key == "skip_next_duration_tick" && value is false) continue;
            if (ReferenceReadableKeys.ContainsValue(key) && value is true) continue;
            string text;
            if (value is List<Dictionary<string, object?>> amounts)
            {
                if (amounts.Count == 0) continue;
                text = string.Join(", ", amounts.Select(row =>
                    $"{row["player_id"]}: {Convert.ToString(row["amount"], CultureInfo.InvariantCulture)}"));
            }
            else text = value is bool flag ? (flag ? "yes" : "no")
                : value == null && ReferenceReadableKeys.TryGetValue(key, out var readableKey)
                    && state.GetValueOrDefault(readableKey) is true ? "none"
                : value == null ? $"null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})"
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
            parts.Add($"{key.Replace('_', ' ')}: {text}");
        }
        if (state.GetValueOrDefault("vigor_attack") is Dictionary<string, object?> vigor)
            parts.Add("vigor attack: " + FormatVigorAttack(vigor, reasons));
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }

    // Unbound reads as "has command: no, attacker is owner: none"; a failed read shows
    // "readable: no" and each null with its reason. readable=true is JSON-only.
    private static string FormatVigorAttack(Dictionary<string, object?> vigor, Dictionary<string, string>? reasons)
    {
        bool readable = vigor.GetValueOrDefault("readable") is true;
        var items = new List<string>();
        if (!readable) items.Add("readable: no");
        foreach (string key in new[] { "has_command", "attacker_is_owner", "amount_when_started" })
        {
            object? value = vigor.GetValueOrDefault(key);
            string text = value is bool flag ? (flag ? "yes" : "no")
                : value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?"
                : reasons?.GetValueOrDefault("vigor_attack." + key) is string reason ? $"null ({reason})"
                : key == "attacker_is_owner" && vigor.GetValueOrDefault("has_command") is false ? "none"
                : "null (unavailable)";
            items.Add($"{key.Replace('_', ' ')}: {text}");
        }
        return string.Join(", ", items);
    }
}
