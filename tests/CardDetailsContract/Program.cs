using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;

STS2_MCP.McpMod.RunCardDetailsContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static void Check(bool condition, string label)
        {
            Checks++;
            if (!condition) throw new Exception(label);
        }
        private static Dictionary<string, object?> Details(CardModel card)
        {
            var row = new Dictionary<string, object?> { ["uid"] = "card-1", ["name"] = "Test", ["cost"] = "1", ["description"] = "Description" };
            AttachCardDetails(row, card);
            return row;
        }
        private static Dictionary<string, object?> Object(Dictionary<string, object?> row, string key)
            => (Dictionary<string, object?>)row[key]!;
        private static Dictionary<string, string> Reasons(Dictionary<string, object?> row)
            => (Dictionary<string, string>)row["unavailable_reasons"]!;
        private static string Markdown(Dictionary<string, object?> row)
        {
            var sb = new StringBuilder();
            AppendCardDetailsMarkdown(sb, row);
            return sb.ToString();
        }
        private static IEnumerable<KeyValuePair<string, DynamicVar>> BrokenEnumeration()
        {
            yield return new("Damage", new DynamicVar(3m));
            throw new InvalidOperationException("enumeration failed");
        }
        public static void RunCardDetailsContract()
        {
            BeginStateWarnings();
            var card = new CardModel();
            var plain = Details(card);
            Check(plain["affliction"] == null && plain["enchantment"] == null, "known absence is null");
            Check(Object(plain, "dynamic_vars").Count == 0 && Reasons(plain).Count == 0 && _stateWarnings.Count == 0, "known empty values have no failure reasons or warnings");
            Check(Markdown(plain) == "", "plain details omit empty values and leave shape in main row");
            foreach (var type in Enum.GetValues<CardType>())
            {
                card.CardType = type;
                Check(Equals(Details(card)["type"], type.ToString()), $"type {type} uses game enum name");
            }
            foreach (var target in Enum.GetValues<TargetType>())
            {
                card.Target = target;
                Check(Equals(Details(card)["target_type"], target.ToString()), $"target {target} uses game enum name");
            }
            card.CardType = CardType.Attack;
            card.Target = TargetType.AnyEnemy;
            var damage = new DynamicVar(12.75m);
            card.Vars = new Dictionary<string, DynamicVar>
            {
                ["Damage"] = damage, ["Zero"] = new(0m), ["Negative"] = new(-2m), ["Text"] = new StringVar()
            };
            card.Afflicted = new() { ModelId = new("WEAK"), Value = 0 };
            card.Enchanted = new() { ModelId = new("SHARP"), Value = 3 };
            var snapshot = Details(card);
            Check(Equals(Object(snapshot, "affliction")["id"], "WEAK") && Equals(Object(snapshot, "affliction")["amount"], 0), "affliction ID and real zero amount");
            Check(Equals(Object(snapshot, "enchantment")["id"], "SHARP") && Equals(Object(snapshot, "enchantment")["amount"], 3), "enchantment ID and amount");
            Check(Equals(Object(snapshot, "enchantment")["status"], "Normal") && !Object(snapshot, "affliction").ContainsKey("status"), "status is the game enum name on enchantments only");
            var values = Object(snapshot, "dynamic_vars");
            Check(Equals(values["Damage"], 12.75m) && Equals(values["Zero"], 0m) && Equals(values["Negative"], -2m), "BaseValue decimals, zero and negative retained");
            Check(values.Count == 3 && !values.ContainsKey("Text"), "StringVar dummy numeric value omitted");
            Check(damage.PreviewValue == 999m && damage.EnchantedValue == 888m, "reading base does not change preview or enchanted values");
            Check(Markdown(snapshot).Contains("affliction WEAK (amount 0)") && Markdown(snapshot).Contains("enchantment SHARP (amount 3)"), "markdown attachments");
            Check(Markdown(snapshot).Contains("  - base Damage 12.75, Zero 0, Negative -2") && Markdown(snapshot).Split('\n').Count(line => line.StartsWith("  - base ")) == 1, "all base values share one labeled line");

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, options)))
            {
                Check(json.RootElement.GetProperty("dynamic_vars").GetProperty("Damage").GetDecimal() == 12.75m, "wire retains decimal and game variable key casing");
                Check(json.RootElement.GetProperty("affliction").GetProperty("amount").GetInt32() == 0, "wire amount is numeric zero");
                Check(json.RootElement.GetProperty("enchantment").GetProperty("status").GetString() == "Normal", "wire status is an enum name string");
            }
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(plain, options)))
                Check(json.RootElement.GetProperty("affliction").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("dynamic_vars").EnumerateObject().Count() == 0, "dictionary null survives production JSON settings");

            foreach (var status in Enum.GetValues<EnchantmentStatus>())
            {
                card.Enchanted.StatusValue = status;
                var statusRow = Details(card);
                Check(Equals(Object(statusRow, "enchantment")["status"], status.ToString()) && Reasons(statusRow).Count == 0, $"status {status} is read from game");
                Check(Markdown(statusRow).Contains("(disabled)") == (status == EnchantmentStatus.Disabled), "only Disabled adds a markdown marker");
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(statusRow, options));
                Check(json.RootElement.GetProperty("enchantment").GetProperty("status").GetString() == status.ToString(), "status wire name follows game value");
            }
            Check(Equals(Object(snapshot, "enchantment")["status"], "Normal"), "status snapshot independent of game-owned status changes");
            BeginStateWarnings();
            card.Enchanted.ThrowStatus = true;
            var failedStatus = Details(card);
            var failedEnchantment = Object(failedStatus, "enchantment");
            Check(failedEnchantment["status"] == null && Reasons(failedStatus).Count == 1 && Reasons(failedStatus)["enchantment.status"].Contains("status failed"), "status read failure is null with its own reason");
            Check(Equals(failedEnchantment["id"], "SHARP") && Equals(failedEnchantment["amount"], 3) && Equals(Object(failedStatus, "dynamic_vars")["Damage"], 12.75m), "status failure preserves attachment and other card values");
            Check(Markdown(failedStatus).Contains("enchantment status null (InvalidOperationException: status failed)") && !Markdown(failedStatus).Contains("(disabled)"), "unknown status shows its reason");
            var statusResponse = new Dictionary<string, object?> { ["enchantment"] = failedEnchantment };
            EndStateWarnings(statusResponse);
            Check(statusResponse["warnings"] is List<string> statusWarnings && statusWarnings.Any(warning => warning.StartsWith("card.enchantment.status:")), "status read failure is attached to surviving state warnings");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failedStatus, options)))
                Check(json.RootElement.GetProperty("enchantment").GetProperty("status").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("enchantment.status", out _), "status null and reason survive wire serialization");
            BeginStateWarnings();
            card.Enchanted.ThrowStatus = false;
            card.Enchanted.StatusValue = EnchantmentStatus.Normal;
            Check(Equals(Object(Details(card), "enchantment")["status"], "Normal") && _stateWarnings.Count == 0, "status recovers without stale warnings");

            // Model changes are game-owned. Each state reads them again, irrespective of pile/history.
            damage.Value = 20m;
            card.Enchanted.Value = 4;
            var moved = Details(card);
            Check(Equals(moved["uid"], snapshot["uid"]) && Equals(Object(moved, "dynamic_vars")["Damage"], 20m), "same row uid keeps current growth after movement");
            Check(Equals(values["Damage"], 12.75m) && Equals(Object(snapshot, "enchantment")["amount"], 3), "previous snapshot independent of game changes");
            card.Afflicted = null;
            Check(Details(card)["affliction"] == null && Reasons(Details(card)).Count == 0, "game-owned removal is known absence");

            var playable = Details(card);
            AttachCardPlayability(playable, card);
            Check(playable["can_play"] is true && playable["unplayable_reason"] == null, "known playable keeps normal reason semantics");
            card.PlayReason = UnplayableReason.BlockedByCardLogic;
            AttachCardPlayability(playable, card);
            Check(playable["can_play"] is false && Equals(playable["unplayable_reason"], "BlockedByCardLogic"), "known unplayable retains game enum name");
            card.PlayReason = UnplayableReason.None;
            foreach (string field in new[] { "target_type", "can_play" })
            {
                card.ThrowOn.Add(field);
                var failedPlayable = Details(card);
                AttachCardPlayability(failedPlayable, card);
                Check(failedPlayable["can_play"] == null && failedPlayable["unplayable_reason"] == null && Reasons(failedPlayable).ContainsKey("can_play") && Reasons(failedPlayable).ContainsKey("unplayable_reason"), "CanPlay failure caused by target/card logic is unknown and preserves row");
                Check(Markdown(failedPlayable).Contains("can_play null (InvalidOperationException"), "playability failure reason shown in hand markdown");
                card.ThrowOn.Clear();
            }

            foreach (string field in new[] { "type", "target_type", "affliction", "enchantment", "dynamic_vars" })
            {
                BeginStateWarnings();
                card.ThrowOn.Add(field);
                var broken = Details(card);
                Check(broken[field] == null && Reasons(broken)[field].Contains($"{field} failed"), $"{field} failure is null with reason");
                Check(Equals(broken["uid"], "card-1") && Equals(broken["name"], "Test"), "failure preserves other row fields");
                Check(Markdown(broken).Contains("null (InvalidOperationException"), "markdown read failure remains visible");
                var response = new Dictionary<string, object?> { ["state_type"] = "combat", ["hand"] = new[] { broken } };
                EndStateWarnings(response);
                Check(response["warnings"] is List<string> warnings && warnings.Any(w => w.Contains($"card.{field}:")), "warning collector attaches surviving field failure");
                using (var json = JsonDocument.Parse(JsonSerializer.Serialize(broken, options)))
                    Check(json.RootElement.GetProperty(field).ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty(field, out _), "wire null and reason both present");
                card.ThrowOn.Clear();
            }

            card.Afflicted = new() { ModelId = new("WEAK"), Value = 2, ThrowAmount = true };
            card.Enchanted.ThrowId = true;
            damage.ThrowBase = true;
            var partial = Details(card);
            Check(Object(partial, "affliction")["amount"] == null && Equals(Object(partial, "affliction")["id"], "WEAK") && Reasons(partial).ContainsKey("affliction.amount"), "affliction amount failure preserves ID");
            Check(Object(partial, "enchantment")["id"] == null && Equals(Object(partial, "enchantment")["amount"], 4) && Reasons(partial).ContainsKey("enchantment.id"), "enchantment ID failure preserves amount");
            Check(Object(partial, "dynamic_vars")["Damage"] == null && Equals(Object(partial, "dynamic_vars")["Zero"], 0m) && Reasons(partial).ContainsKey("dynamic_vars.Damage"), "single variable failure preserves other values");
            Check(Markdown(partial).Contains("amount null (InvalidOperationException: amount failed)") && Markdown(partial).Contains("base Damage null (InvalidOperationException: base failed), Zero 0"), "nested markdown reasons remain on a single base line");
            card.Afflicted.ThrowAmount = false;
            card.Afflicted.ThrowId = true;
            card.Enchanted.ThrowId = false;
            card.Enchanted.ThrowAmount = true;
            var otherPartial = Details(card);
            Check(Object(otherPartial, "affliction")["id"] == null && Reasons(otherPartial).ContainsKey("affliction.id"), "affliction ID failure");
            Check(Object(otherPartial, "enchantment")["amount"] == null && Reasons(otherPartial).ContainsKey("enchantment.amount"), "enchantment amount failure");

            card.Vars = BrokenEnumeration();
            var enumeration = Details(card);
            Check(enumeration["dynamic_vars"] == null && Reasons(enumeration)["dynamic_vars"].Contains("enumeration failed"), "incomplete enumeration cannot claim a complete value map");
            card.Vars = null!;
            Check(Details(card)["dynamic_vars"] == null && Reasons(Details(card)).ContainsKey("dynamic_vars"), "null collection is unavailable");
            card.Vars = new Dictionary<string, DynamicVar> { ["Broken"] = null!, ["Valid"] = new(1m) };
            var nullVar = Details(card);
            Check(Object(nullVar, "dynamic_vars")["Broken"] == null && Equals(Object(nullVar, "dynamic_vars")["Valid"], 1m) && Reasons(nullVar).ContainsKey("dynamic_vars.Broken"), "null entry preserves other entries");

            BeginStateWarnings();
            var recovered = Details(new());
            Check(Reasons(recovered).Count == 0 && _stateWarnings.Count == 0, "later clean snapshot has no stale errors");
            Check(Markdown(new()).Length == 0, "legacy row without new fields still formats");
            foreach (string pile in new[] { "draw", "discard", "exhaust", "play" })
            {
                var player = new Dictionary<string, object?> { ["draw_pile_count"] = 0, ["discard_pile_count"] = 0, ["exhaust_pile_count"] = 0, ["play_pile_count"] = 0 };
                player[$"{pile}_pile_count"] = 1;
                player[$"{pile}_pile"] = new List<Dictionary<string, object?>> { snapshot };
                var sb = new StringBuilder();
                string before = JsonSerializer.Serialize(player, options);
                FormatDeckPilesMarkdown(sb, player);
                Check(sb.ToString().Contains("display order, ties hide draw order"), "draw pile heading explains intentional tie concealment");
                Check(sb.ToString().Split('\n').Any(line => line.TrimEnd('\r') == "- Test (1): Description [Attack/AnyEnemy]") && sb.ToString().Contains("base Damage 12.75, Zero 0, Negative -2"), $"{pile} pile puts shape at row end and consolidates base values");
                Check(!sb.ToString().Contains("  - type ") && sb.ToString().Split('\n').Count(line => line.StartsWith("  - base ")) == 1, "pile details add one base line and no shape child line");
                Check(JsonSerializer.Serialize(player, options) == before, "formatting does not modify JSON");
            }

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string Slice(string start, string end) => builder[builder.IndexOf(start, StringComparison.Ordinal)..builder.IndexOf(end, builder.IndexOf(start, StringComparison.Ordinal), StringComparison.Ordinal)];
            string hand = Slice("private static Dictionary<string, object?> BuildCardState", "private static List<Dictionary<string, object?>> BuildCardTargetPreviews");
            Check(hand.Contains("BuildCardInfo(card, includeType: false)") && hand.Contains("AttachCardDetails(state, card)"), "hand shares safe detail reader and avoids earlier unsafe type read");
            Check(!hand.Contains("card.TargetType.ToString()") && hand.Contains("state[\"target_type\"] != null ? BuildCardTargetPreviews(card) : []"), "failed target getter skips target preview");
            Check(Slice("private static List<Dictionary<string, object?>> BuildPileCardList", "// entity_id registry").Contains("AttachCardDetails(state, card)"), "all pile rows share detail reader");
            foreach (string pile in new[] { "Draw", "Discard", "Exhaust", "Play" })
                Check(builder.Contains($"BuildPileCardList(combatState.{pile}Pile.Cards, PileType.{pile})"), $"{pile} wiring");
            Check(Slice("private static List<Dictionary<string, object?>> BuildPileCardList", "// entity_id registry").Contains("OrderPileCardsForDisplay(cards, pile)"), "pile details use display ordering covered by CardIdentityContract");
            Check(!Slice("private static Dictionary<string, object?> BuildCardInfo", "private static Dictionary<string, object?>? BuildUpgradePreviewInfo").Contains("AttachCardDetails"), "noncombat deck and previews omit added combat fields");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("AppendCardDetailsMarkdown(sb, card)"), "hand markdown wiring");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("BuildPlayerState("), "multiplayer shares player builder");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "additive fields preserve schema 4");
            Console.WriteLine($"CardDetailsContract: {Checks} checks passed");
        }
    }
}
