using System.Collections.Generic;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AppendCardDetailsMarkdown(StringBuilder sb, Dictionary<string, object?> card,
        bool includeShape = false)
    {
        var reasons = card.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Value(object? value, string path) => value?.ToString()
            ?? $"null ({reasons?.GetValueOrDefault(path) ?? "unavailable"})";

        if (includeShape && card.ContainsKey("type"))
            sb.AppendLine($"  - type {Value(card.GetValueOrDefault("type"), "type")}, target {Value(card.GetValueOrDefault("target_type"), "target_type")}");
        foreach (string field in new[] { "affliction", "enchantment" })
        {
            if (card.GetValueOrDefault(field) is Dictionary<string, object?> attachment)
            {
                sb.AppendLine($"  - {field} {Value(attachment.GetValueOrDefault("id"), $"{field}.id")} (amount {Value(attachment.GetValueOrDefault("amount"), $"{field}.amount")})");
            }
            else if (reasons?.ContainsKey(field) == true)
                sb.AppendLine($"  - {field} {Value(null, field)}");
        }
        if (card.GetValueOrDefault("dynamic_vars") is Dictionary<string, object?> values)
        {
            foreach (var (name, value) in values)
                sb.AppendLine($"  - dynamic_vars.{name} {Value(value, $"dynamic_vars.{name}")}");
        }
        else if (reasons?.ContainsKey("dynamic_vars") == true)
            sb.AppendLine($"  - dynamic_vars {Value(null, "dynamic_vars")}");

        // The hand already prints these fields in its main line. Show read failures here.
        if (!includeShape && reasons != null)
        {
            foreach (string field in new[] { "type", "target_type", "can_play", "unplayable_reason" })
                if (reasons.ContainsKey(field)) sb.AppendLine($"  - {field} {Value(null, field)}");
        }
    }
}
