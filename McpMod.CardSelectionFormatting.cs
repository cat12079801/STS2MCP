using System.Collections.Generic;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void FormatCardSelectMarkdown(StringBuilder sb, Dictionary<string, object?> cardSelect)
    {
        string screenType = cardSelect.TryGetValue("screen_type", out var st) ? st?.ToString() ?? "select" : "select";
        string screenLabel = screenType switch
        {
            "transform" => "Transform",
            "upgrade" => "Upgrade",
            "enchant" => "Enchant",
            "select" => "Select",
            "simple_select" => "Select",
            _ => screenType
        };
        sb.AppendLine($"## Card Selection: {screenLabel}");

        if (cardSelect.TryGetValue("prompt", out var promptObj) && promptObj != null)
        {
            sb.AppendLine($"*{promptObj}*");
        }

        // How many are picked out of how many the screen wants. Without this line a caller
        // on a multi-card screen cannot tell whether confirming will do anything.
        if (cardSelect.TryGetValue("selected_count", out var selCountObj) && selCountObj is int selCount)
        {
            string wanted = "";
            if (cardSelect.TryGetValue("required_count", out var reqObj) && reqObj is int req)
                wanted = $" of {req} required";
            else if (cardSelect.TryGetValue("min_select", out var minObj) && minObj is int min
                     && cardSelect.TryGetValue("max_select", out var maxObj) && maxObj is int max)
                wanted = $" ({min}-{max} accepted)";
            sb.AppendLine($"**Selected: {selCount}{wanted}**");
        }
        sb.AppendLine();

        if (cardSelect.TryGetValue("cards", out var cardsObj) && cardsObj is List<Dictionary<string, object?>> cards)
        {
            sb.AppendLine(screenType == "choose" ? "### Cards" : $"### Cards ({cards.Count} candidates)");
            if (screenType != "choose")
                sb.AppendLine("All candidate indices are available without scrolling; re-read state when candidates change.");
            foreach (var card in cards)
            {
                string starCost = card.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
                string selected = card.TryGetValue("selected", out var selObj) && selObj is true ? "[x] " : "";
                string uid = card.TryGetValue("uid", out var u) && u != null ? $" `{u}`" : "";
                string exhaustNext = card.GetValueOrDefault("exhaust_on_next_play") is true ? " [exhaust-next]" : "";
                sb.AppendLine($"- {selected}[{card["index"]}]{uid} **{card["name"]}** ({card["cost"]} energy{starCost}) [{card["type"]}] {card["rarity"]}{exhaustNext} - {card["description"]}");
                AppendUpgradePreviewLine(sb, card);
                AppendCardEnergyCostMarkdown(sb, card);
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("Card candidates unavailable (cards: null). Re-read state before selecting.");
            sb.AppendLine();
        }

        bool preview = cardSelect.TryGetValue("preview_showing", out var pv) && pv is true;
        bool canConfirm = cardSelect.TryGetValue("can_confirm", out var cc) && cc is true;
        bool canCancel = cardSelect.TryGetValue("can_cancel", out var cn) && cn is true;

        if (preview)
        {
            sb.AppendLine("**Preview is showing** - use `confirm_selection` to confirm or `cancel_selection` to go back.");
            if (cardSelect.TryGetValue("preview_cards", out var previewCardsObj) && previewCardsObj is List<Dictionary<string, object?>> previewCards && previewCards.Count > 0)
            {
                sb.AppendLine("### Preview Cards");
                foreach (var card in previewCards)
                {
                    string starCost = card.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
                    sb.AppendLine($"- **{card["name"]}** ({card["cost"]} energy{starCost}) [{card["type"]}] {card["rarity"]} - {card["description"]}");
                }
                sb.AppendLine();
            }
        }
        else
        {
            if (screenType == "choose")
                sb.AppendLine("Choose immediately using `select_card(index)`.");
            else
                sb.AppendLine($"**Select cards** using `select_card(index)`. Can confirm: {(canConfirm ? "Yes" : "No")} | Can cancel: {(canCancel ? "Yes" : "No")}");
        }
        sb.AppendLine();
    }
}
