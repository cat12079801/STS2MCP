using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AppendCardDetailsMarkdown(StringBuilder sb, Dictionary<string, object?> card)
    {
        var reasons = card.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Value(object? value, string path) => value?.ToString()
            ?? $"null ({reasons?.GetValueOrDefault(path) ?? "unavailable"})";

        foreach (string field in new[] { "affliction", "enchantment" })
        {
            if (card.GetValueOrDefault(field) is Dictionary<string, object?> attachment)
            {
                string disabled = attachment.GetValueOrDefault("status") is "Disabled" ? " (disabled)" : "";
                string extra = attachment.TryGetValue("extra_damage", out var extraDamage)
                    ? $", extra damage {Value(extraDamage, $"{field}.extra_damage")}" : "";
                // Hexed cards only: the card's own Ethereal, kept when the Hex is removed.
                if (field == "affliction" && card.TryGetValue("local_ethereal", out var localEthereal))
                    extra += $", local ethereal {(localEthereal is bool flag ? (flag ? "yes" : "no") : Value(null, "local_ethereal"))}";
                sb.AppendLine($"  - {field} {Value(attachment.GetValueOrDefault("id"), $"{field}.id")} (amount {Value(attachment.GetValueOrDefault("amount"), $"{field}.amount")}{extra}){disabled}");
                if (attachment.TryGetValue("status", out var status) && status == null)
                    sb.AppendLine($"  - {field} status {Value(null, $"{field}.status")}");
            }
            else if (reasons?.ContainsKey(field) == true)
                sb.AppendLine($"  - {field} {Value(null, field)}");
        }
        if (card.GetValueOrDefault("dynamic_vars") is Dictionary<string, object?> values)
        {
            if (values.Count > 0)
                sb.AppendLine("  - base " + string.Join(", ", values.Select(entry =>
                    $"{entry.Key} {Value(entry.Value, $"dynamic_vars.{entry.Key}")}")));
        }
        else if (reasons?.ContainsKey("dynamic_vars") == true)
            sb.AppendLine($"  - base {Value(null, "dynamic_vars")}");

        if (card.GetValueOrDefault("tags") is List<string> tags && tags.Count > 0)
            sb.AppendLine($"  - tags {string.Join(", ", tags)}");

        // Shape, upgrade (title "+") and playability show elsewhere. Keep their failure reasons visible.
        if (reasons != null)
        {
            foreach (string field in new[] { "type", "target_type", "is_upgraded", "is_upgradable", "tags", "can_play", "unplayable_reason",
                "upgrade_level", "max_upgrade_level", "local_keyword_ids", "keyword_ids", "retain_this_turn", "single_turn_retain" })
                if (reasons.ContainsKey(field)) sb.AppendLine($"  - {field} {Value(null, field)}");
        }
    }
}
