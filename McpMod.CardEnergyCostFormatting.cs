using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AppendCardEnergyCostMarkdown(StringBuilder sb, Dictionary<string, object?> card)
    {
        if (card.GetValueOrDefault("energy_cost") is not Dictionary<string, object?> cost) return;
        var reasons = cost.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Value(string key) => cost.GetValueOrDefault(key)?.ToString()
            ?? $"null ({reasons?.GetValueOrDefault(key) ?? "unavailable"})";
        sb.AppendLine($"  - energy cost: base {Value("base")}; local {Value("with_local_modifiers")}; all {Value("with_all_modifiers")}");
        if (cost.GetValueOrDefault("local_modifiers") is List<Dictionary<string, object?>> modifiers)
        {
            if (modifiers.Count > 0)
                sb.AppendLine("  - local cost modifiers (in order): " + string.Join("; ", modifiers.Select(m =>
                    $"{m["type"]} {m["amount"]}, {m["expiration"]}, reduce_only={m["reduce_only"]?.ToString()?.ToLowerInvariant()}")));
        }
        else sb.AppendLine($"  - local cost modifiers: {Value("local_modifiers")}");
    }
}
