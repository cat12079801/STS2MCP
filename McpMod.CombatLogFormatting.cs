using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int CombatLogMarkdownRows = 15;

    // "### Combat Log (last 15 of seq 0-212)" and one short line per row; the details are in JSON.
    internal static void AppendCombatLogMarkdown(StringBuilder sb, Dictionary<string, object?> battle)
    {
        if (battle.GetValueOrDefault("combat_log") is not Dictionary<string, object?> log) return;
        var rows = log.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new List<Dictionary<string, object?>>();
        string known = log.GetValueOrDefault("known") is true ? ""
            : $" — unknown ({string.Join("; ", log.GetValueOrDefault("unavailable_reasons") as List<string> ?? new List<string>())})";
        sb.AppendLine($"### Combat Log (last {Math.Min(rows.Count, CombatLogMarkdownRows)} of seq {log.GetValueOrDefault("first_seq")}-{Convert.ToInt64(log.GetValueOrDefault("next_seq") ?? 0L, CultureInfo.InvariantCulture) - 1}){known}");
        foreach (var row in rows.Skip(Math.Max(0, rows.Count - CombatLogMarkdownRows)))
            sb.AppendLine($"- {row.GetValueOrDefault("seq")} {FormatCombatLogRow(row)}");
        sb.AppendLine();
    }

    internal static string FormatCombatLogRow(Dictionary<string, object?> row)
    {
        string kind = row.GetValueOrDefault("kind")?.ToString() ?? "?";
        string Card(object? c) => c is Dictionary<string, object?> d ? (d.GetValueOrDefault("uid") ?? d.GetValueOrDefault("card_id"))?.ToString() ?? "?" : "hidden";
        string Who(object? c) => c is Dictionary<string, object?> d
            ? (d.GetValueOrDefault("entity_id") ?? (d.GetValueOrDefault("player_id") is { } p ? $"player {p}" : null) ?? d.GetValueOrDefault("monster_id") ?? d.GetValueOrDefault("combat_id"))?.ToString() ?? "?"
            : "-";
        string Model(object? m) => m is Dictionary<string, object?> d
            ? (d.GetValueOrDefault("uid") ?? d.GetValueOrDefault("model_id"))?.ToString() ?? "?" : "?";
        return kind switch
        {
            "hook" => $"hook {row.GetValueOrDefault("hook")}: " + string.Join(" / ",
                (row.GetValueOrDefault("passes") as List<Dictionary<string, object?>> ?? new())
                    .Where(p => p.GetValueOrDefault("listeners") is List<Dictionary<string, object?>> { Count: > 0 })
                    .Select(p => $"{p.GetValueOrDefault("method")} [{string.Join(", ", ((List<Dictionary<string, object?>>)p["listeners"]!).Select(l => Model(l)))}]")),
            "turn_phase" => $"phase {row.GetValueOrDefault("phase")}",
            "draw" => $"draw {Card(row.GetValueOrDefault("card"))}",
            "generated" => $"generated {Card(row.GetValueOrDefault("card"))}",
            "play" => $"play {row.GetValueOrDefault("phase")} {row.GetValueOrDefault("uid")}",
            "pile_move" => $"move {Card(row.GetValueOrDefault("card"))} {row.GetValueOrDefault("from") ?? "new"} → {row.GetValueOrDefault("to")} ({row.GetValueOrDefault("position")})",
            "damage_received" => row.GetValueOrDefault("result") is Dictionary<string, object?> r
                ? $"damage {Who(r.GetValueOrDefault("receiver"))} -{r.GetValueOrDefault("unblocked")} hp, {r.GetValueOrDefault("blocked")} blocked" : "damage",
            "attacked" => $"attack by {Who(row.GetValueOrDefault("actor"))}",
            "block_gained" => $"block {Who(row.GetValueOrDefault("actor"))} +{row.GetValueOrDefault("amount")}",
            "power_received" => row.GetValueOrDefault("power") is Dictionary<string, object?> pw
                ? $"power {pw.GetValueOrDefault("power_id")} {row.GetValueOrDefault("amount")} on {Who(pw.GetValueOrDefault("owner"))}" : "power",
            "power" => $"power {row.GetValueOrDefault("action")} {(row.GetValueOrDefault("power") as Dictionary<string, object?>)?.GetValueOrDefault("power_id") ?? row.GetValueOrDefault("power_id")}",
            "energy" => $"energy {row.GetValueOrDefault("before")} → {row.GetValueOrDefault("after")}",
            "energy_spent" => $"energy spent {row.GetValueOrDefault("amount")}",
            "monster_move" => $"{Who(row.GetValueOrDefault("actor"))} move {row.GetValueOrDefault("move_id")}",
            "flash" => $"flash {Model(row.GetValueOrDefault("model"))}",
            "selection" => $"selection {row.GetValueOrDefault("selector")} chose [{string.Join(", ", (row.GetValueOrDefault("chosen") as List<object?> ?? new()).Select(Card))}]",
            "modify" => $"modify {row.GetValueOrDefault("hook")} {row.GetValueOrDefault("original") switch { Dictionary<string, object?> o => o.GetValueOrDefault("pile"), var o => o }} → {row.GetValueOrDefault("result") switch { Dictionary<string, object?> o => o.GetValueOrDefault("pile"), var o => o }}",
            "shuffle" => $"shuffle ({row.GetValueOrDefault("reason")}, discard {row.GetValueOrDefault("discard_count")})",
            "hidden_rows" => $"{row.GetValueOrDefault("count")} rows hidden ({row.GetValueOrDefault("reason")})",
            "card_exhausted" or "card_discarded" or "card_afflicted" or "exhaust" => $"{kind} {Card(row.GetValueOrDefault("card"))}",
            "card_change" => $"transform {Card(row.GetValueOrDefault("from"))} → {Card(row.GetValueOrDefault("to"))}",
            "hp" or "block" => $"{row.GetValueOrDefault("action")} {Who(row.GetValueOrDefault("target"))} {row.GetValueOrDefault("amount")}",
            "creature" => $"creature {row.GetValueOrDefault("action")}",
            "potion" => $"potion {row.GetValueOrDefault("action")}",
            "potion_used" => $"potion used {row.GetValueOrDefault("potion_id")}",
            _ => kind
        };
    }

    // "Decision context: FromDeckForUpgrade; opener rest_site_option SmithRestSiteOption.OnSelect". Details in JSON.
    internal static void AppendDecisionContextMarkdown(StringBuilder sb, Dictionary<string, object?> selection)
    {
        if (selection.GetValueOrDefault("decision_context") is not Dictionary<string, object?> context) return;
        var parts = new List<string>();
        if (context.GetValueOrDefault("selector") is string selector) parts.Add(selector);
        if (context.GetValueOrDefault("opener") is Dictionary<string, object?> opener && opener.GetValueOrDefault("kind") is string kind)
            parts.Add($"opener {kind} {opener.GetValueOrDefault("model_type")}.{opener.GetValueOrDefault("method")}");
        if (context.GetValueOrDefault("known") is not true)
            parts.Add($"unknown ({string.Join("; ", context.GetValueOrDefault("unavailable_reasons") as List<string> ?? new List<string>())})");
        sb.AppendLine("Decision context: " + string.Join("; ", parts));
        sb.AppendLine();
    }
}
