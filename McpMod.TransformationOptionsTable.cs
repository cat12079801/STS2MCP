using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

// The game-free half of player.transformation_options: grouping the candidate lists and the Markdown line.
// McpMod.TransformationOptions.cs feeds it the game's cards and CardFactory.GetDefaultTransformationOptions.
public static partial class McpMod
{
    private const string TransformationOptionsSource =
        "EntropyPower.AfterPlayerTurnStart;CardCmd.TransformToRandom;CardTransformation(original).IsInCombat=original.CombatState!=null;CardFactory.GetDefaultTransformationOptions(original,isInCombat)";

    // Game types stay at the edges (the four delegates) so the contract test can drive the grouping.
    // Cards with the same id and the same candidate list share one row; uids are sorted, never in
    // pile order (a draw-pile uid list in pile order would be the hidden draw order).
    internal static Dictionary<string, object?> BuildTransformationOptions<TCard>(
        Func<IEnumerable<TCard>> cards, Func<TCard, string> id, Func<TCard, string> uid,
        Func<TCard, IEnumerable<string>> options)
    {
        var result = new Dictionary<string, object?>
        {
            ["known"] = false,
            ["source"] = TransformationOptionsSource,
            ["cards"] = new List<Dictionary<string, object?>>()
        };
        try
        {
            var groups = new Dictionary<string, (string Id, List<string> Uids, List<string>? Options, string? Reason)>(StringComparer.Ordinal);
            foreach (var card in cards().ToList())
            {
                string cardId = id(card);
                List<string>? list = null;
                string? reason = null;
                try
                {
                    // Materialize inside the catch: GetDefaultTransformationOptions throws when nothing is left
                    // (CardFactory.GetFilteredTransformationOptions), and the game's transform would throw too.
                    list = options(card).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                }
                catch (Exception ex)
                {
                    reason = $"{ex.GetType().Name}: {ex.Message}";
                    Warn($"player.transformation_options.{cardId}", ex);
                }
                string key = cardId + "\n" + (list == null ? "!" + reason : string.Join(",", list));
                if (!groups.TryGetValue(key, out var group))
                {
                    group = (cardId, new List<string>(), list, reason);
                    groups[key] = group;
                }
                group.Uids.Add(uid(card));
            }
            var rows = new List<Dictionary<string, object?>>();
            foreach (var group in groups.Values.OrderBy(g => g.Id, StringComparer.Ordinal)
                         .ThenBy(g => g.Options == null ? "" : string.Join(",", g.Options), StringComparer.Ordinal))
            {
                var row = new Dictionary<string, object?>
                {
                    ["id"] = group.Id,
                    ["uids"] = group.Uids.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    ["options"] = group.Options
                };
                if (group.Reason != null) row["reason"] = group.Reason;
                rows.Add(row);
            }
            result["cards"] = rows;
            result["known"] = true;
        }
        catch (Exception ex)
        {
            result["reason"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("player.transformation_options", ex);
        }
        return result;
    }

    private static void AppendTransformationOptionsMarkdown(StringBuilder sb, Dictionary<string, object?> transform)
    {
        string summary = transform.GetValueOrDefault("known") is true
            && transform.GetValueOrDefault("cards") is List<Dictionary<string, object?>> rows
            ? string.Join(", ", rows.Select(r => r.GetValueOrDefault("options") is List<string> o
                ? $"{r.GetValueOrDefault("id")} {o.Count}"
                : $"{r.GetValueOrDefault("id")} unknown"))
            : $"unknown: {transform.GetValueOrDefault("reason")}";
        sb.AppendLine($"Transformation options (Entropy): {summary}");
        sb.AppendLine();
    }
}
