using System.Collections.Generic;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void FormatDeckPilesMarkdown(StringBuilder sb, Dictionary<string, object?> player)
    {
        sb.AppendLine("### Deck Information");
        sb.AppendLine();

        FormatPileMarkdown(sb, player, "draw_pile", "draw_pile_count", "Draw Pile", " sorted by rarity");
        FormatPileMarkdown(sb, player, "discard_pile", "discard_pile_count", "Discard Pile");
        FormatPileMarkdown(sb, player, "exhaust_pile", "exhaust_pile_count", "Exhaust Pile");
        if (player.GetValueOrDefault("play_pile_count") is int count && count >= 1)
            FormatPileMarkdown(sb, player, "play_pile", "play_pile_count", "Play Pile");
    }

    private static void FormatPileMarkdown(StringBuilder sb, Dictionary<string, object?> player,
        string pileKey, string countKey, string label, string suffix = "")
    {
        sb.AppendLine($"#### {label} ({player[countKey]} cards{(suffix.Length > 0 ? "," : "")}{suffix})");
        if (player.TryGetValue(pileKey, out var pileObj) && pileObj is List<Dictionary<string, object?>> pile && pile.Count > 0)
        {
            foreach (var card in pile)
            {
                string starCost = card.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
                string exhaustNext = card.GetValueOrDefault("exhaust_on_next_play") is true ? " [exhaust-next]" : "";
                sb.AppendLine($"- {card["name"]} ({card["cost"]}{starCost}){exhaustNext}: {card["description"]}");
                AppendCardEnergyCostMarkdown(sb, card);
            }
        }
        else
            sb.AppendLine("- *(empty)*");
        sb.AppendLine();
    }
}
