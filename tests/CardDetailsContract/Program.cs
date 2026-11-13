using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;

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
        private static string Markdown(Dictionary<string, object?> row, bool shape = true)
        {
            var sb = new StringBuilder();
            AppendCardDetailsMarkdown(sb, row, includeShape: shape);
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
            Check(Markdown(plain).Trim() == "- type Attack, target AnyEnemy", "plain pile adds a short shape line");
            Check(Markdown(plain, false) == "", "plain hand shape already present in heading");
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
            var values = Object(snapshot, "dynamic_vars");
            Check(Equals(values["Damage"], 12.75m) && Equals(values["Zero"], 0m) && Equals(values["Negative"], -2m), "BaseValue decimals, zero and negative retained");
            Check(values.Count == 3 && !values.ContainsKey("Text"), "StringVar dummy numeric value omitted");
            Check(damage.PreviewValue == 999m && damage.EnchantedValue == 888m, "reading base does not change preview or enchanted values");
            Check(Markdown(snapshot).Contains("affliction WEAK (amount 0)") && Markdown(snapshot).Contains("enchantment SHARP (amount 3)"), "markdown attachments");
            Check(Markdown(snapshot).Contains("dynamic_vars.Damage 12.75") && Markdown(snapshot).Contains("dynamic_vars.Zero 0"), "markdown base values on separate lines");

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
            }
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(plain, options)))
                Check(json.RootElement.GetProperty("affliction").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("dynamic_vars").EnumerateObject().Count() == 0, "dictionary null survives production JSON settings");

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
                Check(Markdown(failedPlayable, false).Contains("can_play null (InvalidOperationException"), "playability failure reason shown in hand markdown");
                card.ThrowOn.Clear();
            }

            foreach (string field in new[] { "type", "target_type", "affliction", "enchantment", "dynamic_vars" })
            {
                BeginStateWarnings();
                card.ThrowOn.Add(field);
                var broken = Details(card);
                Check(broken[field] == null && Reasons(broken)[field].Contains($"{field} failed"), $"{field} failure is null with reason");
                Check(Equals(broken["uid"], "card-1") && Equals(broken["name"], "Test"), "failure preserves other row fields");
                Check(Markdown(broken).Contains("null (InvalidOperationException") && (field != "type" && field != "target_type" || Markdown(broken, false).Contains("null (InvalidOperationException")), "markdown read failure and hand shape failure reason");
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
            Check(Markdown(partial).Contains("amount null (InvalidOperationException: amount failed)") && Markdown(partial).Contains("dynamic_vars.Damage null (InvalidOperationException: base failed)"), "nested markdown reasons");
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
                Check(sb.ToString().Contains("- Test (1): Description") && sb.ToString().Contains("dynamic_vars.Damage 12.75"), $"{pile} pile keeps existing row and appends details");
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
                Check(builder.Contains($"BuildPileCardList({(pile == "Draw" ? "drawCards" : $"combatState.{pile}Pile.Cards")}, PileType.{pile})"), $"{pile} wiring");
            Check(builder.Contains("var drawCards = combatState.DrawPile.Cards.ToList();") && builder.Contains("drawCards.Sort((c1, c2) => c1.Rarity != c2.Rarity") && builder.Contains("c1.Rarity.CompareTo(c2.Rarity)") && builder.Contains("string.Compare(c1.Id.Entry, c2.Id.Entry, StringComparison.Ordinal)"), "draw order remains rarity/ID display sort on a copy");
            Check(!Slice("private static Dictionary<string, object?> BuildCardInfo", "private static Dictionary<string, object?>? BuildUpgradePreviewInfo").Contains("AttachCardDetails"), "noncombat deck and previews omit added combat fields");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("AppendCardDetailsMarkdown(sb, card)"), "hand markdown wiring");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("BuildPlayerState("), "multiplayer shares player builder");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "additive fields preserve schema 4");
            Console.WriteLine($"CardDetailsContract: {Checks} checks passed");
        }
    }
}
