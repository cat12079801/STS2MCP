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
                sb.AppendLine($"  - {field} {Value(attachment.GetValueOrDefault("id"), $"{field}.id")} (amount {Value(attachment.GetValueOrDefault("amount"), $"{field}.amount")}){disabled}");
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

        // Shape is already in the main card line. Keep its failure reasons visible.
        if (reasons != null)
        {
            foreach (string field in new[] { "type", "target_type", "can_play", "unplayable_reason" })
                if (reasons.ContainsKey(field)) sb.AppendLine($"  - {field} {Value(null, field)}");
        }
    }
}
