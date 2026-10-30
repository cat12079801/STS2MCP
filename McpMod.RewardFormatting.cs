using System.Collections.Generic;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void FormatRewardsMarkdown(StringBuilder sb, Dictionary<string, object?> rewards)
    {
        if (rewards.TryGetValue("items", out var itemsObj) && itemsObj is List<Dictionary<string, object?>> items && items.Count > 0)
        {
            sb.AppendLine("## Rewards");
            foreach (var item in items)
            {
                string extra = "";
                if (item.TryGetValue("gold_amount", out var gold) && gold != null)
                    extra = $" ({gold} gold)";
                else if (item.TryGetValue("potion_description", out var pDesc) && pDesc != null)
                    extra = $" - {pDesc}";
                else if (item.TryGetValue("potion_name", out var pName) && pName != null)
                    extra = $" ({pName})";
                sb.AppendLine($"- [{item["index"]}] **{item["type"]}**: {item["description"]}{extra}"
                    + (item.TryGetValue("reward_id", out var rewardId) ? $" · `{rewardId}` · {item.GetValueOrDefault("selection_state") ?? "unknown"}" : ""));
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Rewards");
            sb.AppendLine("No rewards available.");
            sb.AppendLine();
        }

        if (rewards.TryGetValue("screen_revision", out var revision))
            sb.AppendLine($"**Screen revision:** `{revision}`. Prefer `claim_reward reward_id <id>`; index is the current list position.");
        if (rewards.GetValueOrDefault("selection_history") is List<Dictionary<string, object?>> history)
        {
            foreach (var entry in history)
                sb.AppendLine($"- `{entry.GetValueOrDefault("reward_id")}`: {entry.GetValueOrDefault("selection_state")}, claimable={entry.GetValueOrDefault("claimable")}, skipped={entry.GetValueOrDefault("skipped")}, consumed={entry.GetValueOrDefault("reward_consumed") ?? "unknown"}, last={entry.GetValueOrDefault("last_selection") ?? "unknown"} {entry.GetValueOrDefault("last_option_id")}");
        }
        bool canProceed = rewards.TryGetValue("can_proceed", out var cp) && cp is true;
        sb.AppendLine($"**Can proceed:** {(canProceed ? "Yes" : "No")}");
        sb.AppendLine();
    }

    /// <summary>
    /// Prints the upgraded form under a card line, on the screens where the state
    /// carries it. Silent when the card has no "upgrade_preview".
    /// </summary>
    private static void AppendUpgradePreviewLine(StringBuilder sb, Dictionary<string, object?> card)
    {
        if (!card.TryGetValue("upgrade_preview", out var previewObj)
            || previewObj is not Dictionary<string, object?> preview) return;

        string starCost = preview.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
        sb.AppendLine($"  → **Upgraded**: ({preview.GetValueOrDefault("cost")} energy{starCost}) - {preview.GetValueOrDefault("description")}");
    }

    private static void FormatCardRewardMarkdown(StringBuilder sb, Dictionary<string, object?> cardReward)
    {
        sb.AppendLine("## Card Reward Selection");
        if (cardReward.TryGetValue("parent_reward_id", out var parentId))
            sb.AppendLine($"**Parent reward:** `{parentId ?? "unknown"}` · selection_state: {cardReward.GetValueOrDefault("selection_state") ?? "unknown"}");
        sb.AppendLine("Choose a card to add to your deck:");
        sb.AppendLine();

        if (cardReward.TryGetValue("cards", out var cardsObj) && cardsObj is List<Dictionary<string, object?>> cards)
        {
            foreach (var card in cards)
            {
                string starCost = card.TryGetValue("star_cost", out var sc) && sc != null ? $" + {sc} star" : "";
                string keywords = card.TryGetValue("keywords", out var kw) && kw is List<string> kwList && kwList.Count > 0
                    ? $" [{string.Join(", ", kwList)}]" : "";
                sb.AppendLine($"- [{card["index"]}] **{card["name"]}** ({card["cost"]} energy{starCost}) [{card["type"]}] {card["rarity"]}{keywords} - {card["description"]}");
                AppendUpgradePreviewLine(sb, card);
            }
            sb.AppendLine();
        }

        // Relics add their own options here (Pael's Wing's SACRIFICE). Taking one is
        // not the same as skipping, so list them all rather than a skip yes/no.
        if (cardReward.TryGetValue("alternatives", out var altObj)
            && altObj is List<Dictionary<string, object?>> alternatives && alternatives.Count > 0)
        {
            sb.AppendLine("**Alternatives** (`select_card_reward_alternative option_id <id>`):");
            foreach (var alt in alternatives)
            {
                string title = alt.GetValueOrDefault("title")?.ToString() ?? "";
                string disabled = alt.TryGetValue("enabled", out var en) && en is false ? " *(disabled)*" : "";
                sb.AppendLine($"- [{alt["index"]}] `{alt["option_id"]}`{(title.Length > 0 ? $" — {title}" : "")}{disabled}");
            }
            sb.AppendLine();
        }

        bool canSkip = cardReward.TryGetValue("can_skip", out var cs) && cs is true;
        sb.AppendLine($"**Can skip:** {(canSkip ? "Yes" : "No")}");
        sb.AppendLine();
    }

}
