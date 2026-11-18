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
        ["applier_combat_id"] = "applier_readable",
        ["follow_up_state_id"] = "follow_up_state_readable",
        ["stolen_card"] = "stolen_card_readable"
    };

    private static string FormatPowerInternalState(Dictionary<string, object?> state)
    {
        var parts = new List<string>();
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        // sts2#1034: shown only when it differs from the row's PowerModel.Type (a negative
        // Strength reads Debuff) or could not be read; the usual same-as-type value is JSON-only.
        if (state.TryGetValue("type_for_current_amount", out var currentType)
            && (currentType == null || !Equals(currentType, state.GetValueOrDefault("type"))))
            parts.Add("type for current amount: " + (currentType as string
                ?? $"null ({reasons?.GetValueOrDefault("type_for_current_amount") ?? "unavailable"})"));
        foreach (string key in new[] { "is_temporary_power", "skip_next_duration_tick", "block_base_value", "facing",
            "stolen_strength", "stolen_dexterity", "has_gained_block_this_turn", "bound_card_played", "attacks_played_this_turn",
            "card_to_clone_uid", "card_to_clone_readable", "played_card_uid", "played_card_readable",
            "stolen_gold", "target_player_id", "applier_combat_id", "applier_readable", "was_just_applied_by_enemy",
            "damage", "self_damage", "cards_left", "already_applied",
            "infinite_auto_plays_this_turn", "ethereal_count", "has_command_to_modify", "damage_received_this_turn",
            "is_reviving", "follow_up_state_id", "follow_up_state_readable", "should_apply_intangible",
            "cards_played_this_turn", "has_already_been_given_block", "stolen_card_readable" })
        {
            if (!state.TryGetValue(key, out var value)) continue;
            if ((key == "skip_next_duration_tick" || key == "is_temporary_power") && value is false) continue;
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
        // Dampen: an empty set/map is shown as "none", a failed read as "null (reason)".
        if (state.TryGetValue("caster_combat_ids", out var casters))
            parts.Add("caster combat ids: " + (casters is List<uint> ids
                ? (ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))))
                : $"null ({reasons?.GetValueOrDefault("caster_combat_ids") ?? "unavailable"})"));
        if (state.TryGetValue("downgraded_cards", out var downgraded))
            parts.Add("downgraded cards: " + (downgraded is List<Dictionary<string, object?>> rows
                ? (rows.Count == 0 ? "none" : string.Join(", ", rows.Select(FormatDampenDowngradedCard)))
                : $"null ({reasons?.GetValueOrDefault("downgraded_cards") ?? "unavailable"})"));
        // sts2#1020: card-keyed Data as "STRIKE_R#0=3" ("none" when empty), the stolen card as its uid.
        foreach (var (key, valueKey) in new[] { ("played_cards", "pending_strength"),
            ("amounts_for_played_cards", "amount"), ("auto_playing_cards", (string?)null) })
        {
            if (!state.TryGetValue(key, out var cardRows)) continue;
            parts.Add(key.Replace('_', ' ') + ": " + (cardRows is List<Dictionary<string, object?>> list
                ? (list.Count == 0 ? "none" : string.Join(", ", list.Select(row => FormatRegisteredCardRow(row)
                    + (valueKey == null ? "" : "=" + Convert.ToString(row.GetValueOrDefault(valueKey), CultureInfo.InvariantCulture)))))
                : $"null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})"));
        }
        if (state.TryGetValue("stolen_card", out var stolen))
            parts.Add("stolen card: " + (stolen is Dictionary<string, object?> stolenRow ? FormatRegisteredCardRow(stolenRow)
                : state.GetValueOrDefault("stolen_card_readable") is true ? "none"
                : $"null ({reasons?.GetValueOrDefault("stolen_card") ?? "unavailable"})"));
        // sts2#1020: "vars: SelfDamage=2, Decrement=1". An empty set (most powers) is JSON-only;
        // a failed set or var shows null and its reason.
        if (state.TryGetValue("dynamic_vars", out var dynamicVars))
        {
            if (dynamicVars is Dictionary<string, object?> vars)
            {
                if (vars.Count > 0)
                    parts.Add("vars: " + string.Join(", ", vars.Select(pair => pair.Key + "=" + (pair.Value == null
                        ? $"null ({reasons?.GetValueOrDefault("dynamic_vars." + pair.Key) ?? "unavailable"})"
                        : Convert.ToString(pair.Value, CultureInfo.InvariantCulture)))));
            }
            else parts.Add($"vars: null ({reasons?.GetValueOrDefault("dynamic_vars") ?? "unavailable"})");
        }
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }

    // "STRIKE_R#0", or "CARD_ID (uid unknown)" for a card the uid registry has not seen.
    private static string FormatRegisteredCardRow(Dictionary<string, object?> row)
        => row.GetValueOrDefault("uid") is string uid ? uid : $"{row.GetValueOrDefault("card_id") ?? "unknown"} (uid unknown)";

    // "STRIKE_R#0 from +1"; a card the uid registry has not seen shows its id and "uid unknown".
    private static string FormatDampenDowngradedCard(Dictionary<string, object?> row)
    {
        string level = Convert.ToString(row.GetValueOrDefault("original_upgrade_level"), CultureInfo.InvariantCulture) ?? "?";
        return row.GetValueOrDefault("uid") is string uid
            ? $"{uid} from +{level}"
            : $"{row.GetValueOrDefault("card_id") ?? "unknown"} (uid unknown) from +{level}";
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
