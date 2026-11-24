using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using Swift = MegaCrit.Sts2.Core.Models.Enchantments.Swift;
using Imbued = MegaCrit.Sts2.Core.Models.Enchantments.Imbued;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AttachAncientDeckTargets(Dictionary<string, object?> option, bool isPresentedAncient,
        RelicModel relic, Player? localPlayer, Dictionary<string, object?>? playerState)
    {
        if (!isPresentedAncient || relic is not (BeautifulBracelet or ElectricShrymp or NeowsTalisman
            or LeafyPoultice or Astrolabe or PandorasBox))
            return;

        var rows = new List<Dictionary<string, object?>>();
        var reasons = new Dictionary<string, string>();
        string path = "player.deck";
        try
        {
            // Both builders run synchronously on the main thread. Bind every row to the
            // deck already published with this response (AddDeckState uses the same pile).
            var cards = localPlayer?.Deck?.Cards ?? throw new InvalidOperationException("Local deck is unavailable.");
            if (playerState?.GetValueOrDefault("deck") is not List<Dictionary<string, object?>> deck
                || deck.Count != cards.Count)
                throw new InvalidOperationException("Published deck is unavailable or differs in size.");

            EnchantmentModel? enchantment = null;
            for (int index = 0; index < cards.Count; index++)
            {
                path = $"rows[{index}].card_id";
                var card = cards[index];
                string id = RequireAncientDeckModelId(card.Id.Entry);
                if (!Equals(deck[index].GetValueOrDefault("index"), index) ||
                    !Equals(deck[index].GetValueOrDefault("id"), id))
                    throw new InvalidOperationException("Card does not match the published deck at this position.");

                var row = new Dictionary<string, object?> { ["deck_index"] = index, ["card_id"] = id };
                void Read(string key, Func<object?> value)
                {
                    path = $"rows[{index}].{key}";
                    row[key] = value();
                }

                Read(AncientDeckEnchantmentIdKey, () => card.Enchantment is { } attached
                    ? RequireAncientDeckModelId(attached.Id.Entry) : null);

                // AfterObtained on these six relics reads these inputs. The reader never
                // calls those effects or their selectors, factories, or RNG.
                switch (relic)
                {
                    case BeautifulBracelet:
                    case ElectricShrymp:
                        Read("can_enchant", () => (enchantment ??= relic is BeautifulBracelet
                            ? ModelDb.Enchantment<Swift>()
                            : ModelDb.Enchantment<Imbued>()).CanEnchant(card));
                        break;
                    case NeowsTalisman:
                    case LeafyPoultice:
                        Read("tags", () => card.Tags.OrderBy(tag => (int)tag).Select(tag => tag.ToString()).ToArray());
                        if (relic is LeafyPoultice)
                            Read("is_transformable", () => card.IsTransformable);
                        break;
                    case Astrolabe:
                        Read("is_transformable", () => card.IsTransformable);
                        break;
                    case PandorasBox:
                        Read("is_basic_strike_or_defend", () => card.IsBasicStrikeOrDefend);
                        Read("is_removable", () => card.IsRemovable);
                        break;
                }
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            rows.Clear();
            reasons[path] = Flatten($"{ex.GetType().Name}: {ex.Message}");
            Warn($"event.options[{option["index"]}].deck_targets.{path}", ex);
        }

        option["deck_targets"] = new Dictionary<string, object?>
        {
            ["known"] = reasons.Count == 0,
            ["rows"] = rows,
            ["unavailable_reasons"] = reasons
        };
    }

    private static string RequireAncientDeckModelId(string? id)
        => !string.IsNullOrWhiteSpace(id) ? id : throw new InvalidOperationException("Model ID is empty.");

    private static void FormatAncientDeckTargetsMarkdown(StringBuilder sb, Dictionary<string, object?> option)
    {
        if (option.GetValueOrDefault("deck_targets") is not Dictionary<string, object?> targets)
            return;
        if (targets["known"] is true && targets["rows"] is List<Dictionary<string, object?>> rows)
            sb.AppendLine($"  - Deck targets: {rows.Count} deck cards read");
        else if (targets["unavailable_reasons"] is Dictionary<string, string> reasons)
            sb.AppendLine($"  - Deck targets: unavailable ({string.Join(", ", reasons.Keys)})");
    }
}
