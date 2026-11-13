using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

STS2_MCP.McpMod.RunCardSelectContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }

        public static void RunCardSelectContract()
        {
            static CardModel[] Cards(int count) => Enumerable.Range(0, count).Select(i => new CardModel($"CARD_{i}")).ToArray();
            static void Open(NCardGridSelectionScreen screen) => NOverlayStack.Instance!.Top = screen;
            static bool Ok(Dictionary<string, object?> result) => Equals(result["status"], "ok");
            static string Markdown(List<Dictionary<string, object?>>? rows, string type = "select")
            {
                var text = new StringBuilder();
                FormatCardSelectMarkdown(text, new() { ["screen_type"] = type, ["cards"] = rows });
                return text.ToString();
            }

            foreach (int count in new[] { 0, 1, 25, 26, 35, 172 })
            {
                var models = Cards(count);
                var grid = new NCardGrid(models);
                grid.Holders.AddRange(models.Take(25).Select(card => (card, true)));
                var screen = new TestSelectScreen(grid);
                Check(TryGetCardSelectCandidates(screen, out var owner, out var candidates, out _), $"read {count}");
                Check(ReferenceEquals(owner, grid) && candidates.SequenceEqual(models), $"full ordered snapshot {count}");
                Check(!ReferenceEquals(candidates, models), "copy list, preserve models");
                var rows = BuildCardSelectCardRows(screen, GetCardSelectSelection(screen))!;
                Check(rows.Count == count, $"full rows {count}");
                Check(rows.Select(row => (int)row["index"]!).SequenceEqual(Enumerable.Range(0, count)), "contiguous indices");
                Check(rows.All(row => !row.ContainsKey("uid") && !row.ContainsKey("exhaust_on_next_play") && !row.ContainsKey("energy_cost") && !row.ContainsKey("upgrade_preview")), "noncombat nonupgrade fields");
                Check(Markdown(rows).Contains($"Cards ({count} candidates)"), "candidate count including empty list");
                grid.Holders.Reverse();
                grid.Holders.Add((new CardModel("STALE_HOLDER"), false));
                Check(TryGetCardSelectCandidates(screen, out _, out var scrolled, out _) && scrolled.SequenceEqual(models), "holder reorder/visibility/stale model independent");
            }

            var cards = Cards(35);
            cards[30] = new("DUPLICATE");
            cards[31] = new("DUPLICATE");
            (cards[0], cards[34]) = (cards[34], cards[0]); // intentionally non-ID-sorted display order
            var fullGrid = new NCardGrid(cards);
            fullGrid.Holders.AddRange(cards.Take(25).Select(card => (card, true)));
            var current = new TestSelectScreen(fullGrid) { ReplaceSelection = true };
            Open(current);
            Check(TryGetCardSelectCandidates(current, out _, out var snapshot, out _) && snapshot.SequenceEqual(cards), "empty screen list does not override grid list / original ordering");
            var beforeSet = GetCardSelectSelection(current);
            var selected = ExecuteSelectGridCard(current, 30);
            Check(Ok(selected) && selected["selected"] is true && Equals(selected["selected_count"], 1), "offscreen index 30 selected and read back");
            Check(current.Calls == 1 && ReferenceEquals(current.LastCard, cards[30]), "one concrete model call without holder");
            Check(!ReferenceEquals(beforeSet, GetCardSelectSelection(current)), "game replaced selection HashSet");
            Check(beforeSet!.Count == 0, "no mutation of old set by MOD");
            var rows35 = BuildCardSelectCardRows(current, GetCardSelectSelection(current))!;
            Check(rows35[30]["selected"] is true && rows35[31]["selected"] is false, "same ID distinct instances");
            Check(rows35.Count(row => row["selected"] is true) == (int)selected["selected_count"]!, "all row selections match count");
            var markdown = Markdown(rows35);
            Check(markdown.Split('\n').Count(line => line.StartsWith("- ")) == 35 && markdown.Contains("[34]") && markdown.Contains("[x] [30]"), "35 markdown rows, last index, selection");
            Check(Ok(ExecuteSelectGridCard(current, 30)) && GetCardSelectSelection(current)!.Count == 0, "toggle off readback");
            var calls = current.Calls;
            Check(!Ok(ExecuteSelectGridCard(current, -1)) && !Ok(ExecuteSelectGridCard(current, 35)) && current.Calls == calls, "invalid indices do not call game");
            Check(ExecuteSelectGridCard(current, 35)["message"]!.ToString()!.Contains("35 cards available"), "error full count");

            current.SetPrefs(1, 1);
            Check(Ok(ExecuteSelectGridCard(current, 30)), "select at limit");
            Check(!Ok(ExecuteSelectGridCard(current, 31)), "rejected no-op is error");
            current.Preview = true;
            calls = current.Calls;
            Check(!Ok(ExecuteSelectGridCard(current, 30)) && current.Calls == calls, "preview blocks game call");
            Check(BuildCardSelectCardRows(current, GetCardSelectSelection(current))![30]["selected"] is true, "preview retains selected row");
            current.Preview = false;
            fullGrid.Visible = false;
            Check(!Ok(ExecuteSelectGridCard(current, 30)) && current.Calls == calls, "hidden peeking grid blocks call");
            fullGrid.Visible = true;
            fullGrid.InTree = false;
            Check(!Ok(ExecuteSelectGridCard(current, 30)) && current.Calls == calls, "uninitialized grid blocks call");
            fullGrid.InTree = true;
            NOverlayStack.Instance!.Top = null;
            Check(!Ok(ExecuteSelectGridCard(current, 30)) && current.Calls == calls, "stale overlay blocks call");
            Open(current);
            current.Live = false;
            Check(!Ok(ExecuteSelectGridCard(current, 30)), "dead screen blocks call");
            current.Live = true;

            var previewScreen = new TestSelectScreen(fullGrid) { PreviewAtLimit = true };
            previewScreen.SetPrefs(1, 1);
            Open(previewScreen);
            var previewResult = ExecuteSelectGridCard(previewScreen, 30);
            Check(Ok(previewResult) && previewResult["preview_showing"] is true && Equals(previewResult["max_select"], 1), "preview result fields");
            var immediate = new TestSelectScreen(fullGrid) { CloseImmediately = true, ThrowOnReadAfterClose = true };
            Open(immediate);
            Check(Ok(ExecuteSelectGridCard(immediate, 30)) && immediate.Calls == 1 && ClosedReads == 0, "immediate close succeeds without screen readback");

            var unknown = new TestSelectScreen(fullGrid);
            unknown.SetSelection(null);
            Open(unknown);
            var unknownResult = ExecuteSelectGridCard(unknown, 30);
            Check(Ok(unknownResult) && !unknownResult.ContainsKey("selected"), "unknown selection not guessed");
            Check(BuildCardSelectCardRows(unknown, null)!.All(row => !row.ContainsKey("selected")), "unknown row selection omitted");
            var throwing = new TestSelectScreen(fullGrid) { ThrowOnClick = true };
            Open(throwing);
            var failed = ExecuteSelectGridCard(throwing, 30);
            Check(!Ok(failed) && throwing.Calls == 1, "click exception never retries");
            Check(failed["message"]!.ToString()!.Contains("InvalidOperationException: click failure"), "unwrap invocation cause");
            Check(FindCardSelectClickMethod(typeof(NCardGridSelectionScreen)) == null, "abstract method is not invocable");
            Check(FindCardSelectClickMethod(typeof(string)) == null, "missing method resolves to null");
            Check(FindCardSelectClickMethod(typeof(WrongSignatureScreen)) == null, "object overload must not match CardModel");

            foreach (object? badList in new object?[] { null, "wrong type", new CardModel[] { null! } })
            {
                var bad = new TestSelectScreen(new(badList));
                Open(bad);
                Warnings.Clear();
                Check(BuildCardSelectCardRows(bad, []) == null && Warnings.Single().StartsWith("card_select.cards:"), "unreadable list is null plus warning");
                Check(!Ok(ExecuteSelectGridCard(bad, 0)) && bad.Calls == 0, "unreadable action fails before game call");
            }
            var noGrid = new TestSelectScreen(null);
            Open(noGrid);
            Check(BuildCardSelectCardRows(noGrid, []) == null && !Ok(ExecuteSelectGridCard(noGrid, 0)), "no fallback when grid missing");
            var empty = new TestSelectScreen(new(Array.Empty<CardModel>()));
            Warnings.Clear();
            Open(empty);
            Check(BuildCardSelectCardRows(empty, [])!.Count == 0 && Warnings.Count == 0, "empty is successful, not unknown");
            Check(!Ok(ExecuteSelectGridCard(empty, 0)), "no selectable index for empty list");
            Check(Markdown(null).Contains("candidates unavailable") && !Markdown(null).Contains("0 candidates"), "null markdown distinct from zero");

            var upgraded = new NDeckUpgradeSelectScreen(fullGrid);
            cards[0].IsUpgradable = false;
            cards[1].CurrentUpgradeLevel = 1;
            cards[1].MaxUpgradeLevel = 2;
            cards[2].CurrentUpgradeLevel = 1;
            var upgradeRows = BuildCardSelectCardRows(upgraded, [])!;
            Check(!upgradeRows[0].ContainsKey("upgrade_preview") && upgradeRows[1].ContainsKey("upgrade_preview") && !upgradeRows[2].ContainsKey("upgrade_preview") && upgradeRows[30].ContainsKey("upgrade_preview"), "upgrade attachment across full candidate range");
            Open(upgraded);
            Check(Ok(ExecuteSelectGridCard(upgraded, 30)) && upgraded.Calls == 1, "resolve inherited concrete override");
            Check(Markdown(upgradeRows, "upgrade").Contains("→ **Upgraded**"), "linked preview markdown");

            CombatManager.Instance = new() { IsInProgress = true, State = new() };
            cards[30].ExhaustOnNextPlay = true;
            var combatRows = BuildCardSelectCardRows(current, GetCardSelectSelection(current))!;
            Check(RefreshCalls == 1 && combatRows.All(row => row.ContainsKey("uid") && row.ContainsKey("exhaust_on_next_play")), "combat registry refresh before all rows");
            Check(combatRows[30]["exhaust_on_next_play"] is true && combatRows[31]["exhaust_on_next_play"] is false, "exhaust flags retain true and false");
            Check(combatRows.All(row => row.ContainsKey("energy_cost")), "combat rows expose energy cost");
            Check(!Markdown(combatRows).Contains("energy cost:"), "grid omits unchanged energy cost markdown");
            ((Dictionary<string, object?>)combatRows[30]["energy_cost"]!)["with_all_modifiers"] = 0;
            Check(Markdown(combatRows).Contains("energy cost: base 1; local 1; all 0"), "grid shows changed energy cost markdown");
            Check(Markdown(combatRows).Contains("raw 1; canonical 1; captured X null (not_x_cost)"), "grid shows new cost details");
            Check(!Equals(combatRows[30]["uid"], combatRows[31]["uid"]), "same ID retains distinct uids");
            var combatAgain = BuildCardSelectCardRows(current, [])!;
            Check(Equals(combatRows[30]["uid"], combatAgain[30]["uid"]), "stable instance uid across reads");
            Check(Markdown(combatRows).Contains($"`{combatRows[30]["uid"]}`") && Markdown(combatRows).Contains("[exhaust-next]"), "combat markdown attributes");
            CombatManager.Instance.State = null;
            Check(BuildCardSelectCardRows(current, [])!.All(row => !row.ContainsKey("uid")), "live combat missing state omits uid");
            CombatManager.Instance.IsInProgress = false;
            CombatManager.Instance.State = new();
            Check(BuildCardSelectCardRows(current, [])!.All(row => !row.ContainsKey("exhaust_on_next_play") && !row.ContainsKey("energy_cost")), "inactive combat omits combat fields");
            CombatManager.Instance = null;
            Warnings.Clear();
            BuildCardSelectCardRows(current, [new("OUTSIDE_CANDIDATES")]);
            Check(Warnings.Single().StartsWith("card_select.selected:"), "selection mismatch warns without correction");
            Check(!Markdown(rows35, "choose").Contains("candidates") && Markdown(rows35, "choose").Contains("Choose immediately"), "choose keeps immediate semantics and heading");

            var changed = new[] { cards[31], cards[30] };
            fullGrid.SetCards(changed);
            current.SetSelection([cards[31]]);
            var changedRows = BuildCardSelectCardRows(current, GetCardSelectSelection(current))!;
            Check(changedRows.Count == 2 && changedRows[0]["selected"] is true && changedRows[1]["selected"] is false, "new pile candidates and replacement selection on next read");
            Open(current);
            current.SetPrefs(1, 2);
            Check(Ok(ExecuteSelectGridCard(current, 1)) && ReferenceEquals(current.LastCard, cards[30]), "action fetches current ordering without cached snapshot");

            CombatManager.Instance = new() { IsInProgress = true, State = new() };
            var tieCards = new[] { new CardModel("STRIKE"), new CardModel("STRIKE"), new CardModel("STRIKE") };
            Uids[tieCards[0]] = "STRIKE#2";
            Uids[tieCards[1]] = "STRIKE#0";
            Uids[tieCards[2]] = "STRIKE#1";
            var drawGrid = new NCardGrid(tieCards);
            var drawScreen = new NCombatPileCardSelectScreen(drawGrid, new CardPile(PileType.Draw));
            drawScreen.SetSelection([tieCards[0]]);
            drawScreen.SetPrefs(0, 3);
            Open(drawScreen);
            var drawRows = BuildCardSelectCardRows(drawScreen, GetCardSelectSelection(drawScreen))!;
            Check(drawRows.Select(row => (string)row["uid"]!).SequenceEqual(new[] { "STRIKE#0", "STRIKE#1", "STRIKE#2" }), "draw selector conceals identical-card order using UID");
            Check(drawRows.Select(row => (int)row["index"]!).SequenceEqual(new[] { 0, 1, 2 }), "normalized selection indices stay contiguous");
            Check(drawRows[2]["selected"] is true && drawRows[0]["selected"] is false, "selection flags follow normalized original instances");
            Check(Ok(ExecuteSelectGridCard(drawScreen, 0)) && ReferenceEquals(drawScreen.LastCard, tieCards[1]), "normalized row UID and clicked model agree");
            Check(Ok(ExecuteSelectGridCard(drawScreen, 0)) && !GetCardSelectSelection(drawScreen)!.Contains(tieCards[1]), "normalized index toggles same original model");
            Check(tieCards[0] != drawScreen.LastCard && ReferenceEquals(GetInstanceFieldValue(drawGrid, "_cards"), tieCards)
                && tieCards[0] == GetCardSelectSelection(drawScreen)!.Single(), "normalization leaves the game's grid list intact");
            drawGrid.SetCards(tieCards.Reverse().ToArray());
            var reorderedRows = BuildCardSelectCardRows(drawScreen, GetCardSelectSelection(drawScreen))!;
            Check(reorderedRows.Select(row => row["uid"]).SequenceEqual(drawRows.Select(row => row["uid"])), "hidden tie permutation yields same exposed UID order");
            Check(Ok(ExecuteSelectGridCard(drawScreen, 1)) && ReferenceEquals(drawScreen.LastCard, tieCards[2]), "click recomputes same mapping after grid permutation");
            Check(Markdown(reorderedRows).Contains("STRIKE#0"), "normalized UIDs reach selection Markdown");

            var differentRank = new CardModel("STRIKE") { DisplayRank = 1 };
            var differentTitle = new CardModel("STRIKE") { TitleOverride = "Strike+" };
            Uids[differentRank] = "STRIKE#9";
            Uids[differentTitle] = "STRIKE#8";
            var separated = new[] { tieCards[0], differentRank, tieCards[1], differentTitle, tieCards[2] };
            drawGrid.SetCards(separated);
            Check(TryGetCardSelectCandidates(drawScreen, out _, out var separatedCandidates, out _)
                && separatedCandidates.SequenceEqual(new[] { tieCards[1], differentRank, tieCards[0], differentTitle, tieCards[2] }),
                "separated equivalent cards use UID order within a maximal grid-key interval");

            var secondRank = new CardModel("STRIKE") { DisplayRank = 1 };
            Uids[secondRank] = "STRIKE#3";
            var interleaved = new[] { tieCards[0], differentRank, tieCards[1], secondRank, tieCards[2] };
            var originalInterleaved = interleaved.ToArray();
            var expectedInterleaved = new[] { tieCards[1], secondRank, tieCards[2], differentRank, tieCards[0] };
            drawGrid.SetCards(interleaved);
            drawScreen.SetSelection([tieCards[0], secondRank]);
            Open(drawScreen);
            var interleavedRows = BuildCardSelectCardRows(drawScreen, GetCardSelectSelection(drawScreen))!;
            Check(interleavedRows.Select(row => (string)row["uid"]!).SequenceEqual(expectedInterleaved.Select(GetStableCardUid)),
                "each separated equivalence class is independently ordered by ordinal UID");
            Check(TryGetCardSelectCandidates(drawScreen, out _, out var interleavedCandidates, out _)
                && interleavedCandidates.Select(c => c.DisplayRank).SequenceEqual(originalInterleaved.Select(c => c.DisplayRank)),
                "equivalence classes keep their original occupied positions");
            Check(interleavedRows[1]["selected"] is true && interleavedRows[4]["selected"] is true
                && interleavedRows.Count(row => row["selected"] is true) == 2, "selection flags follow separated normalized instances");
            Check(interleaved.SequenceEqual(originalInterleaved) && ReferenceEquals(GetInstanceFieldValue(drawGrid, "_cards"), interleaved),
                "normalizing separated members preserves the game's grid list");
            for (int index = 0; index < expectedInterleaved.Length; index++)
            {
                Check(Ok(ExecuteSelectGridCard(drawScreen, index)) && ReferenceEquals(drawScreen.LastCard, expectedInterleaved[index]),
                    $"separated normalized row {index} clicks its exposed UID's original model");
                Check(Ok(ExecuteSelectGridCard(drawScreen, index)), "same normalized index toggles the original instance back");
            }
            drawGrid.SetCards(new[] { tieCards[2], secondRank, tieCards[1], differentRank, tieCards[0] });
            Check(BuildCardSelectCardRows(drawScreen, null)!.Select(row => row["uid"])
                .SequenceEqual(interleavedRows.Select(row => row["uid"])), "independent permutations of interleaved classes expose the same UIDs");
            foreach (var boundary in new[] { new CardModel("STRIKE") { Rarity = 1 }, new CardModel("OTHER") { TitleOverride = "STRIKE" } })
            {
                var split = new[] { tieCards[0], boundary, tieCards[1] };
                drawGrid.SetCards(split);
                Check(TryGetCardSelectCandidates(drawScreen, out _, out var splitCandidates, out _)
                    && splitCandidates.SequenceEqual(split), "different rarity or ID ends the maximal grid-key interval");
            }
            foreach (var pile in new[] { PileType.Discard, PileType.Exhaust, PileType.Play })
            {
                var otherPileScreen = new NCombatPileCardSelectScreen(new NCardGrid(tieCards), new CardPile(pile));
                Check(TryGetCardSelectCandidates(otherPileScreen, out _, out var otherCandidates, out _)
                    && otherCandidates.SequenceEqual(tieCards), $"{pile} selection preserves grid order even for identical cards");
            }
            var ordinary = new TestSelectScreen(new NCardGrid(tieCards));
            Check(TryGetCardSelectCandidates(ordinary, out _, out var ordinaryCandidates, out _)
                && ordinaryCandidates.SequenceEqual(tieCards), "other grid screens retain original candidates");
            var generated = new CardModel("STRIKE");
            Uids[generated] = "STRIKE#g100";
            drawGrid.SetCards(new[] { generated, tieCards[0], tieCards[1] });
            Check(BuildCardSelectCardRows(drawScreen, null)!.Select(row => (string)row["uid"]!)
                .SequenceEqual(new[] { "STRIKE#0", "STRIKE#2", "STRIKE#g100" }), "generated and deck UIDs share ordinal tie ordering");
            tieCards[0].ThrowCompare = true;
            drawGrid.SetCards(new[] { tieCards[0], tieCards[1] });
            Warnings.Clear();
            int drawCalls = drawScreen.Calls;
            Check(BuildCardSelectCardRows(drawScreen, null) == null && Warnings.Single().StartsWith("card_select.cards:"), "failed tie comparison yields unavailable candidates and warning");
            Check(!Ok(ExecuteSelectGridCard(drawScreen, 0)) && drawScreen.Calls == drawCalls, "failed normalization cannot click with an uncertain mapping");
            var unknownPile = new NCombatPileCardSelectScreen(new NCardGrid(tieCards), null);
            Open(unknownPile);
            Warnings.Clear();
            Check(BuildCardSelectCardRows(unknownPile, null) == null && Warnings.Single().StartsWith("card_select.cards:"), "unknown source pile cannot expose unnormalized UIDs");
            Check(!Ok(ExecuteSelectGridCard(unknownPile, 0)) && unknownPile.Calls == 0, "unknown source pile cannot select with an uncertain mapping");

            // Lightweight integration assertions for entry points not linked into this
            // small boundary project (the entire state/action builders depend on Godot).
            var sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "published schema is 4");
            var stateSource = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            Check(stateSource.Contains("state[\"cards\"] = BuildCardSelectCardRows(screen, selection);") && !stateSource.Contains("selected_outside_grid"), "state uses linked rows and removes old field");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Actions.cs")).Contains("return ExecuteSelectGridCard(gridScreen, index);"), "action dispatches linked implementation");
            Console.WriteLine($"CardSelectContract: {Checks} checks passed");
        }
    }
}
