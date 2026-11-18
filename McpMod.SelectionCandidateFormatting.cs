using System.Collections.Generic;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    // combat_instance.source values (McpMod.SelectionCandidate).
    internal const string CandidateSourcePile = "pile";
    internal const string CandidateSourceDetached = "detached";

    /// <summary>
    /// One line for an in-combat candidate's combat_instance (sts2#1019): the pile row it is,
    /// or "detached" (its effect inputs are in the JSON values). Silent out of combat.
    /// </summary>
    private static void AppendCombatInstanceMarkdown(StringBuilder sb, Dictionary<string, object?> card)
    {
        if (card.GetValueOrDefault("combat_instance") is not Dictionary<string, object?> instance) return;
        var reasons = instance.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        switch (instance.GetValueOrDefault("source"))
        {
            case CandidateSourcePile:
                sb.AppendLine($"  - instance: {instance.GetValueOrDefault("pile")} `{instance.GetValueOrDefault("uid") ?? "uid unavailable"}`");
                break;
            case CandidateSourceDetached:
                string where = instance.GetValueOrDefault("pile") is string pile ? $" (in {pile})" : "";
                int failures = (instance.GetValueOrDefault("values") as Dictionary<string, object?>)
                    ?.GetValueOrDefault("unavailable_reasons") is Dictionary<string, string> valueReasons ? valueReasons.Count : 0;
                sb.AppendLine($"  - instance: detached{where}, values in JSON{(failures > 0 ? $" ({failures} unreadable)" : "")}");
                break;
            default:
                sb.AppendLine($"  - instance: unknown ({reasons?.GetValueOrDefault("source") ?? "no reason"})");
                break;
        }
    }
}
