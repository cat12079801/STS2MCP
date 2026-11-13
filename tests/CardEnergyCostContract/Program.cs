using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

STS2_MCP.McpMod.RunCardEnergyCostContract();

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
        private static List<Dictionary<string, object?>> Modifiers(Dictionary<string, object?> state)
            => (List<Dictionary<string, object?>>)state["local_modifiers"]!;
        private static Dictionary<string, string> Reasons(Dictionary<string, object?> state)
            => (Dictionary<string, string>)state["unavailable_reasons"]!;
        private static string Markdown(Dictionary<string, object?> state)
        {
            var sb = new StringBuilder();
            AppendCardEnergyCostMarkdown(sb, new() { ["energy_cost"] = state });
            return sb.ToString();
        }
        private static void CheckCostDisplay(Dictionary<string, object?> state, bool expected, string label)
        {
            string jsonBefore = JsonSerializer.Serialize(state);
            Check(Markdown(state).Contains("energy cost:") == expected, label);
            Check(JsonSerializer.Serialize(state) == jsonBefore, $"{label}: JSON unchanged");
        }
        public static void RunCardEnergyCostContract()
        {
            BeginStateWarnings();
            var card = new CardModel();
            var cost = card.Cost;
            var plain = BuildCardEnergyCostState(card);
            Check(plain["costs_x"] is false && Equals(plain["base"], 2), "base and cost kind");
            Check(Modifiers(plain).Count == 0 && Reasons(plain).Count == 0, "empty is known, never null");
            Check(cost.Calls.SequenceEqual(new[] { CostModifiers.None, CostModifiers.Local, CostModifiers.All }), "game calculations requested once each in order");
            CheckCostDisplay(plain, false, "unchanged fixed cost is omitted");
            CheckCostDisplay(new(plain) { ["base"] = 0, ["with_local_modifiers"] = 0, ["with_all_modifiers"] = 0 }, false, "unchanged zero cost is omitted");
            CheckCostDisplay(new(plain) { ["with_local_modifiers"] = 1 }, true, "local-only difference is shown without local list entries");
            CheckCostDisplay(new(plain) { ["with_all_modifiers"] = 0 }, true, "global-only difference is shown, including zero");
            var plainX = new CardModel();
            plainX.Cost.X = true;
            CheckCostDisplay(BuildCardEnergyCostState(plainX), false, "X without modifiers is omitted");
            var plainNoCost = new CardModel();
            plainNoCost.Cost.Values[CostModifiers.None] = -1;
            CheckCostDisplay(BuildCardEnergyCostState(plainNoCost), false, "no-energy-cost without modifiers is omitted");
            var unreadableList = new CardModel();
            unreadableList.Cost.SetModifiers(null);
            CheckCostDisplay(BuildCardEnergyCostState(unreadableList), true, "unreadable list with unchanged numeric costs is shown");
            BeginStateWarnings();

            var relative = new LocalCostModifier(-1, LocalCostType.Relative, LocalCostModifierExpiration.WhenPlayed, false);
            var turn = new LocalCostModifier(1, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfTurn | LocalCostModifierExpiration.WhenPlayed, true);
            var combat = new LocalCostModifier(1, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfCombat, true);
            var local = new List<LocalCostModifier> { relative, turn, combat, relative };
            cost.SetModifiers(local);
            cost.Values[CostModifiers.Local] = 0;
            cost.Values[CostModifiers.All] = 1; // distinguish global from local
            var state = BuildCardEnergyCostState(card);
            var rows = Modifiers(state);
            Check(Equals(state["base"], 2) && Equals(state["with_local_modifiers"], 0) && Equals(state["with_all_modifiers"], 1), "base, local and global are independent game results (zero retained)");
            Check(rows.Count == 4 && Equals(rows[0]["amount"], -1) && Equals(rows[0]["type"], "Relative") && Equals(rows[0]["expiration"], "WhenPlayed") && rows[0]["reduce_only"] is false, "ExpectAFight-shaped modifier");
            Check(Equals(rows[1]["type"], "Absolute") && Equals(rows[1]["amount"], 1) && Equals(rows[1]["expiration"], "EndOfTurn, WhenPlayed") && rows[1]["reduce_only"] is true, "Enlightenment-shaped combined flags");
            Check(Equals(rows[2]["expiration"], "EndOfCombat"), "zero expiration is EndOfCombat, never empty/ThisCombat");
            Check(rows[0].SequenceEqual(rows[3]), "order and duplicate modifiers retained");
            Check(local.Count == 4 && relative.Amount == -1, "read does not mutate game list or entries");
            Check(Markdown(state).Contains("Relative -1, WhenPlayed, reduce_only=false") && Markdown(state).Contains("Absolute 1, EndOfTurn, WhenPlayed, reduce_only=true"), "markdown exposes amount, kind, flags and reduceOnly");
            CheckCostDisplay(new(state) { ["with_local_modifiers"] = 2, ["with_all_modifiers"] = 2 }, true, "nonempty modifiers are shown even when costs are unchanged");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(state)))
            {
                Check(json.RootElement.GetProperty("local_modifiers")[0].GetProperty("amount").GetInt32() == -1, "wire amount is numeric");
                Check(json.RootElement.GetProperty("local_modifiers")[1].GetProperty("reduce_only").GetBoolean(), "wire reduce_only is boolean");
            }

            // Simulate game-owned cleanup/replacement at the boundary; MOD never expires entries.
            local.Remove(turn);
            Check(Modifiers(BuildCardEnergyCostState(card)).Count == 3, "next state sees turn cleanup, WhenPlayed remains");
            cost.SetModifiers(new List<LocalCostModifier> { combat });
            Check(Modifiers(BuildCardEnergyCostState(card)).Single()["expiration"]!.Equals("EndOfCombat"), "next state reads replaced list after play");
            relative.Amount = -2;
            Check(rows.Count == 4 && Equals(rows[0]["amount"], -1), "previous snapshot detached from cleanup and entry mutation");

            cost.Calls.Clear();
            cost.X = true;
            var x = BuildCardEnergyCostState(card);
            Check(new[] { "base", "with_local_modifiers", "with_all_modifiers" }.All(key => x[key] == null && Reasons(x)[key] == "x_cost"), "X has null numeric costs and reasons");
            Check(cost.Calls.Count == 0 && Modifiers(x).Count == 1, "X does not calculate fixed cost, still reports actual modifiers");
            CheckCostDisplay(x, true, "X with local modifiers is shown");
            cost.X = false;
            cost.Values[CostModifiers.None] = -1;
            var noCost = BuildCardEnergyCostState(card);
            Check(new[] { "base", "with_local_modifiers", "with_all_modifiers" }.All(key => noCost[key] == null && Reasons(noCost)[key] == "no_energy_cost"), "negative sentinel becomes null with reason");
            CheckCostDisplay(noCost, true, "no-energy-cost with local modifiers is shown");
            Check(_stateWarnings.Count == 0, "normal X/no-cost are not warnings");
            cost.Values[CostModifiers.None] = 0;
            Check(Equals(BuildCardEnergyCostState(card)["base"], 0), "zero base is a cost");

            foreach (object? brokenList in new object?[] { null, new object(), new List<LocalCostModifier> { null! } })
            {
                cost.SetModifiers(brokenList);
                var broken = BuildCardEnergyCostState(card);
                Check(broken["local_modifiers"] == null && Reasons(broken).ContainsKey("local_modifiers"), "bad reflection/list produces null and reason");
                Check(Equals(broken["base"], 0) && Equals(broken["with_all_modifiers"], 1), "reflection failure preserves public cost fields");
                Check(Markdown(broken).Contains("local cost modifiers: null ("), "markdown shows failure reason");
            }
            var missing = new Dictionary<string, object?> { ["local_modifiers"] = null };
            var missingReasons = new Dictionary<string, string>();
            ReadEnergyCostValue(missing, missingReasons, "local_modifiers", () => ReadLocalEnergyCostModifiers(new object()));
            Check(missing["local_modifiers"] == null && missingReasons["local_modifiers"].Contains("MissingFieldException"), "renamed/missing private field degrades gracefully");
            cost.SetModifiers(new List<LocalCostModifier>());
            cost.ThrowOn.Add(CostModifiers.Local);
            var failedLocal = BuildCardEnergyCostState(card);
            Check(failedLocal["with_local_modifiers"] == null && Reasons(failedLocal).ContainsKey("with_local_modifiers") && Equals(failedLocal["with_all_modifiers"], 1), "one calculation failure preserves other calculation");
            cost.ThrowOn.Add(CostModifiers.All);
            var failedAll = BuildCardEnergyCostState(card);
            Check(failedAll["with_all_modifiers"] == null && Reasons(failedAll).ContainsKey("with_all_modifiers"), "global hook failure is null/reason");
            cost.ThrowOn.Add(CostModifiers.None);
            var failedBase = BuildCardEnergyCostState(card);
            Check(failedBase["base"] == null && Reasons(failedBase)["base"].Contains("calculation failed") && Reasons(failedBase)["with_local_modifiers"] == "base_unavailable", "base read failure does not claim no cost");
            cost.ThrowCostsX = true;
            var failedKind = BuildCardEnergyCostState(card);
            Check(failedKind["costs_x"] == null && Reasons(failedKind)["base"] == "costs_x_unavailable", "cost kind failure is unknown");
            card.ThrowEnergyCost = true;
            var failedGetter = BuildCardEnergyCostState(card);
            Check(Reasons(failedGetter).Count == 5 && failedGetter["local_modifiers"] == null, "EnergyCost getter failure preserves object contract");
            CheckCostDisplay(failedGetter, true, "all-null read failure is shown");
            CheckCostDisplay(new(plain) { ["costs_x"] = null, ["unavailable_reasons"] = new Dictionary<string, string> { ["costs_x"] = "read_failed" } }, true, "cost-kind-only failure is shown");
            CheckCostDisplay(new(plain) { ["local_modifiers"] = null }, true, "null without reason is shown as unavailable");
            var response = new Dictionary<string, object?> { ["state_type"] = "combat", ["energy_cost"] = failedGetter };
            EndStateWarnings(response);
            Check(response["state_type"]!.Equals("combat") && ((List<string>)response["warnings"]!).Any(w => w.Contains("card.energy_cost.local_modifiers")), "real warning collector attaches failures to surviving state");
            BeginStateWarnings();
            Check(_stateWarnings.Count == 0 && Modifiers(BuildCardEnergyCostState(new CardModel())).Count == 0, "later state recovers without stale errors");

            var player = new Dictionary<string, object?>
            {
                ["draw_pile_count"] = 0, ["discard_pile_count"] = 0, ["exhaust_pile_count"] = 0,
                ["play_pile"] = new List<Dictionary<string, object?>>()
            };
            foreach (object? count in new object?[] { null, -1, 0, 1, 2 })
            {
                if (count == null) player.Remove("play_pile_count");
                else player["play_pile_count"] = count;
                player["play_pile"] = Enumerable.Range(0, count is int n ? Math.Max(0, n) : 0)
                    .Select(i => new Dictionary<string, object?> { ["name"] = $"Played card {i}", ["cost"] = "1", ["description"] = "In play", ["energy_cost"] = plain }).ToList();
                string jsonBefore = JsonSerializer.Serialize(player);
                var sb = new StringBuilder();
                FormatDeckPilesMarkdown(sb, player);
                string text = sb.ToString();
                bool show = count is int cards && cards >= 1;
                Check(text.Contains("#### Play Pile") == show, $"Play Pile visibility for count {count ?? "missing"}");
                if (show)
                    Check(text.Contains($"Play Pile ({count} cards)") && text.Contains("Played card 0"), "nonempty Play Pile keeps heading, count and cards");
                Check(text.Contains("#### Draw Pile (0 cards") && text.Contains("#### Discard Pile (0 cards)") && text.Contains("#### Exhaust Pile (0 cards)"), "other pile sections remain visible");
                Check(JsonSerializer.Serialize(player) == jsonBefore, "pile Markdown leaves JSON unchanged");
            }

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string Slice(string start, string end) => builder[builder.IndexOf(start, StringComparison.Ordinal)..builder.IndexOf(end, builder.IndexOf(start, StringComparison.Ordinal), StringComparison.Ordinal)];
            Check(Slice("private static Dictionary<string, object?> BuildCardState", "private static List<Dictionary<string, object?>> BuildCardTargetPreviews").Contains("state[\"energy_cost\"] = BuildCardEnergyCostState(card)"), "hand wiring");
            Check(Slice("private static List<Dictionary<string, object?>> BuildPileCardList", "// entity_id registry").Contains("[\"energy_cost\"] = BuildCardEnergyCostState(card)"), "shared pile wiring");
            foreach (string pile in new[] { "Draw", "Discard", "Exhaust", "Play" })
                Check(builder.Contains($"BuildPileCardList({(pile == "Draw" ? "drawCards" : $"combatState.{pile}Pile.Cards")}, PileType.{pile})"), $"{pile} pile uses shared builder");
            Check(!Slice("private static Dictionary<string, object?> BuildCardInfo", "private static Dictionary<string, object?>? BuildUpgradePreviewInfo").Contains("energy_cost"), "common deck/preview card builder omits combat-only data");
            Check(!Slice("private static Dictionary<string, object?> BuildHandSelectState", "// Confirm button state").Contains("energy_cost"), "hand selection reads same card from hand, no extra field");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "additive change preserves schema 4");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("BuildPlayerState("), "multiplayer uses shared player builder");
            Console.WriteLine($"CardEnergyCostContract: {Checks} checks passed");
        }
    }
}
