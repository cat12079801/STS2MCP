using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// Prints the upgraded form under a card line, on the screens where the state
    /// carries it. Silent when the card has no "upgrade_preview". Combat previews carry a
    /// status: not_upgradable prints nothing, unavailable prints its reasons (unless
    /// <paramref name="showFailures"/> is false), and ready prints the upgraded form unless
    /// <paramref name="showReady"/> is false (pile rows: only failures are printed).
    /// </summary>
    private static void AppendUpgradePreviewLine(StringBuilder sb, Dictionary<string, object?> card,
        bool showReady = true, bool showFailures = true)
    {
        if (!card.TryGetValue("upgrade_preview", out var previewObj)
            || previewObj is not Dictionary<string, object?> preview) return;

        // Out-of-combat previews have no status and are always the upgraded form.
        object? status = preview.GetValueOrDefault("status");
        if (status is "unavailable")
        {
            if (!showFailures) return;
            string reasons = preview.GetValueOrDefault("unavailable_reasons") is Dictionary<string, string> r && r.Count > 0
                ? string.Join(", ", r.Select(entry => $"{entry.Key}: {entry.Value}")) : "no reason";
            sb.AppendLine($"  → upgrade preview unavailable ({reasons})");
            return;
        }
        if (status != null && status is not "ready") return;
        if (!showReady) return;

        string starCost = preview.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
        sb.AppendLine($"  → **Upgraded**: ({preview.GetValueOrDefault("cost")} energy{starCost}) - {preview.GetValueOrDefault("description")}");
    }
}
