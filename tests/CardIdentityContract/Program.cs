using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }
    private static string? SafeGetText(Func<string?> read) => read();
    private static Dictionary<string, object?> BuildGameStateCore() => new();
    private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

    public static void Main()
    {
        // Fixed independent number source: proves labels need not increase with
        // discovery and forces collision retries without probabilistic assertions.
        var numbers = new Queue<int>(new[] { 900, 100, 900, 600, 100, 0, 900, 42, 77, 900 });
        var registry = new CardUidRegistry(() => numbers.Dequeue());
        var combat = new CombatState();
        registry.RefreshCombat(combat);
        var first = new CardModel("STRIKE");
        var second = new CardModel("STRIKE");
        var clone = new CardModel("STRIKE");
        Check(registry.GetUid(first) == "STRIKE#g900", "generated label uses card_id#gdecimal format");
        Check(registry.GetUid(second) == "STRIKE#g100", "discovery order does not set numeric rank");
        Check(registry.GetUid(clone) == "STRIKE#g600", "clone has distinct identity and retries a collision");
        Check(registry.GetUid(new CardModel("DEFEND")) == "DEFEND#g100", "full labels are unique even with equal suffixes across IDs");
        registry.RefreshCombat(combat);
        first.DisplayRank++;
        first.Id.Entry = "TRANSFORMED";
        Check(registry.GetUid(first) == "STRIKE#g900", "same combat refresh and card changes retain instance uid");
        var generatedInHand = new CardModel("STRIKE");
        Check(registry.GetUid(generatedInHand) == "STRIKE#g0", "direct-to-hand generated card uses independent number, including zero");
        var removedReplacement = new CardModel("STRIKE");
        Check(registry.GetUid(removedReplacement) == "STRIKE#g42", "previously issued label stays reserved after leaving piles");
        var otherOwner = new CardModel("STRIKE");
        Check(registry.GetUid(otherOwner) == "STRIKE#g77", "same-ID cards from another player share uniqueness domain");
        registry.RefreshCombat(new CombatState());
        first.Id.Entry = "STRIKE";
        Check(registry.GetUid(first) == "STRIKE#g900" && numbers.Count == 0, "new combat clears instance map and reserved labels");

        // Recreate every engine object as save-and-continue does. DeckVersion,
        // not combat instance identity or observation order, restores deck UIDs.
        static (CombatState State, CardModel[] Cards) DeckCombat()
        {
            var player = new Player { NetId = 17 };
            player.Deck.Cards.AddRange(new[] { new CardModel("STRIKE"), new CardModel("DEFEND"), new CardModel("STRIKE") });
            var cards = player.Deck.Cards.Select(c => new CardModel(c.Id.Entry) { DeckVersion = c }).ToArray();
            return (new CombatState { Players = new[] { player } }, cards);
        }
        var deckRegistry = new CardUidRegistry(() => throw new Exception("deck UIDs must not consume randomness"));
        var initial = DeckCombat();
        deckRegistry.RefreshCombat(initial.State);
        string thirdUid = deckRegistry.GetUid(initial.Cards[2]);
        string firstUid = deckRegistry.GetUid(initial.Cards[0]);
        Check(thirdUid == "STRIKE#2" && firstUid == "STRIKE#0", "absolute deck position determines UID despite reversed discovery and duplicate ID");
        Check(deckRegistry.GetUid(initial.Cards[1]) == "DEFEND#1", "deck index spans all card IDs");
        initial.State.Players[0].Deck.Cards.Reverse();
        deckRegistry.RefreshCombat(initial.State);
        Check(deckRegistry.GetUid(initial.Cards[1]) == "DEFEND#1", "same-combat refresh preserves deck UIDs");
        var resumed = DeckCombat();
        deckRegistry.RefreshCombat(resumed.State);
        Check(deckRegistry.GetUid(resumed.Cards[0]) == firstUid && deckRegistry.GetUid(resumed.Cards[2]) == thirdUid,
            "new combat state, player, deck and combat card instances retain deck UIDs after reload");
        Check(deckRegistry.GetUid(resumed.Cards[1]) == "DEFEND#1", "reloaded hand UID multiset is reproducible for turn_sig");
        var mixedRegistry = new CardUidRegistry(() => 0);
        mixedRegistry.RefreshCombat(resumed.State);
        Check(mixedRegistry.GetUid(resumed.Cards[0]) == "STRIKE#0" && mixedRegistry.GetUid(new CardModel("STRIKE")) == "STRIKE#g0",
            "generated and deck cards have disjoint suffix spaces even at equal numbers");
        var multiplayer = DeckCombat();
        var teammate = new Player { NetId = 23 };
        var teammateSource = new CardModel("STRIKE");
        teammate.Deck.Cards.Add(teammateSource);
        multiplayer.State.Players = new[] { multiplayer.State.Players[0], teammate };
        deckRegistry.RefreshCombat(multiplayer.State);
        Check(deckRegistry.GetUid(multiplayer.Cards[0]) == "STRIKE#0p17"
            && deckRegistry.GetUid(new CardModel("STRIKE") { DeckVersion = teammateSource }) == "STRIKE#0p23",
            "multiplayer equal deck positions use distinct deterministic owner namespaces");

        RefreshCardUidRegistry(combat);
        var baseCard = new CardModel("STRIKE", displayRank: 0);
        var upgraded = new CardModel("STRIKE", displayRank: 1);
        var enchanted = new CardModel("STRIKE", displayRank: 2);
        var afflicted = new CardModel("STRIKE", displayRank: 3);
        var modifiedCost = new CardModel("STRIKE", displayRank: 4);
        var replay = new CardModel("STRIKE", displayRank: 5);
        var changedBaseVar = new CardModel("STRIKE", displayRank: 6);
        var expected = new[] { new CardModel("Z_LOW_RARITY", -1), new CardModel("A"),
            baseCard, upgraded, enchanted, afflicted, modifiedCost, replay, changedBaseVar,
            new CardModel("A_HIGH_RARITY", 1) };
        var source = expected.Reverse().ToList();
        var original = source.ToArray();
        Check(OrderPileCardsForDisplay(source, PileType.Draw).SequenceEqual(expected), "rarity precedes full engine comparison for same-ID variants");
        Check(source.SequenceEqual(original), "display sorting never mutates actual draw pile");
        Check(expected.Sum(c => c.CompareCalls) > 0, "delegates to virtual CardModel.CompareTo via inherited IComparable");
        foreach (var pile in new[] { PileType.Discard, PileType.Exhaust, PileType.Play, PileType.Hand })
            Check(OrderPileCardsForDisplay(source, pile).SequenceEqual(original), $"{pile} preserves input order");
        Check(!OrderPileCardsForDisplay(Array.Empty<CardModel>(), PileType.Draw).Any(), "empty draw pile");

        BeginStateWarnings();
        var failedCompare = new CardModel("STRIKE") { ThrowCompare = true };
        var tiedFallback = new CardModel("STRIKE");
        var fallbackSource = new[] { failedCompare, new CardModel("A", 1), tiedFallback, new CardModel("Z", -1), new CardModel("A") };
        var fallbackExpected = fallbackSource.OrderBy(c => c.Rarity).ThenBy(c => c.Id.Entry, StringComparer.Ordinal)
            .ThenBy(GetStableCardUid, StringComparer.Ordinal).ToArray();
        var fallback = OrderPileCardsForDisplay(fallbackSource, PileType.Draw).ToArray();
        Check(fallback.SequenceEqual(fallbackExpected), "throwing engine comparator falls back to rarity, ID, UID");
        Check(fallbackSource[0] == failedCompare, "fallback leaves game pile intact");
        Check(_stateWarnings.Count == 1 && _stateWarnings[0].StartsWith("draw_pile.order:"), "failed order reports warning through production collector");
        Check(OrderPileCardsForDisplay(fallbackSource.Reverse(), PileType.Draw).SequenceEqual(fallback), "fallback also conceals hidden tie order");
        var warningResponse = new Dictionary<string, object?>();
        EndStateWarnings(warningResponse);
        Check(warningResponse.ContainsKey("warnings"), "order warning survives in state response");
        BeginStateWarnings();
        OrderPileCardsForDisplay(expected, PileType.Draw).ToArray();
        Check(_stateWarnings.Count == 0, "valid comparison recovers without a warning");

        // Ties remain distinguishable by uid even when the game's comparison is
        // exactly equal. Exercise first discovery in Draw, small and large piles,
        // permutations and subsequent generated cards added directly to Draw.
        foreach (int count in new[] { 2, 10, 16, 24 })
        {
            var tieCombat = new CombatState();
            RefreshCardUidRegistry(tieCombat);
            var tied = Enumerable.Range(0, count).Select(_ => new CardModel("TIED")).ToList();
            var sorted = OrderPileCardsForDisplay(tied, PileType.Draw).ToArray();
            var uids = sorted.Select(GetStableCardUid).ToArray();
            Check(uids.Distinct().Count() == count, $"{count} equal cards have distinct uids");
            Check(uids.SequenceEqual(uids.Order(StringComparer.Ordinal)), $"{count} ties use ordinal uid order");
            for (int offset = 0; offset < count; offset++)
            {
                var permuted = tied.Skip(offset).Concat(tied.Take(offset)).Reverse().ToList();
                var before = permuted.ToArray();
                Check(OrderPileCardsForDisplay(permuted, PileType.Draw).SequenceEqual(sorted), $"{count} ties ignore hidden order, permutation {offset}");
                Check(permuted.SequenceEqual(before), "permutation input stays intact");
            }
            var generatedInDraw = new CardModel("TIED");
            tied.Insert(0, generatedInDraw);
            var withGenerated = OrderPileCardsForDisplay(tied, PileType.Draw).ToArray();
            string generatedUid = GetStableCardUid(generatedInDraw);
            tied.Reverse();
            Check(OrderPileCardsForDisplay(tied, PileType.Draw).SequenceEqual(withGenerated), "new draw-pile card and old cards have order-independent ties");
            Check(sorted.Select(GetStableCardUid).SequenceEqual(uids), "adding a card never relabels existing cards");
            // Move the same instances through every public representation.
            foreach (var pile in new[] { PileType.Hand, PileType.Discard, PileType.Exhaust, PileType.Play, PileType.Draw })
            {
                RefreshCardUidRegistry(tieCombat);
                var movedCard = OrderPileCardsForDisplay(new[] { generatedInDraw }, pile).Single();
                Check(GetStableCardUid(movedCard) == generatedUid, $"generated uid survives {pile}");
            }
        }

        // Production RNG wiring and lifecycle: generated labels have a g prefix, are
        // stable across refreshes, and snapshots retain their old values.
        var sharedCombat = new CombatState();
        RefreshCardUidRegistry(sharedCombat);
        var card = new CardModel("STRIKE");
        string uid = GetStableCardUid(card);
        string snapshot = JsonSerializer.Serialize(new Dictionary<string, object?> { ["uid"] = uid });
        RefreshCardUidRegistry(sharedCombat);
        Check(GetStableCardUid(card) == uid, "production registry refresh preserves uid in same combat");
        Check(uid.StartsWith("STRIKE#g", StringComparison.Ordinal)
            && int.TryParse(uid.Split('#')[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int suffix) && suffix >= 0,
            "production generated label uses g followed by a nonnegative decimal suffix");
        RefreshCardUidRegistry(new CombatState());
        using (var json = JsonDocument.Parse(snapshot))
            Check(json.RootElement.GetProperty("uid").GetString() == uid, "previous snapshot detached from registry reset");

        string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
        string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
        foreach (string pile in new[] { "Draw", "Discard", "Exhaust", "Play" })
            Check(builder.Contains($"BuildPileCardList(combatState.{pile}Pile.Cards, PileType.{pile})"), $"{pile} uses shared ordered serializer");
        Check(builder.Contains("foreach (var card in OrderPileCardsForDisplay(cards, pile))"), "serialization always applies display ordering before producing rows");
        int playerStart = builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal);
        string player = builder[playerStart..builder.IndexOf("private static Dictionary<string, object?> BuildCardState", playerStart, StringComparison.Ordinal)];
        int refresh = player.IndexOf("RefreshCardUidRegistry(creature.CombatState)", StringComparison.Ordinal);
        Check(refresh >= 0 && refresh < player.IndexOf("BuildCardState(card, cardIndex)", StringComparison.Ordinal), "refresh before first hand and pile observation");
        foreach (string file in new[] { "McpMod.StateBuilder.cs", "McpMod.Actions.cs", "McpMod.CardSelection.cs", "McpMod.CombatHistory.cs" })
            Check(File.ReadAllText(Path.Combine(sources, file)).Contains("GetStableCardUid("), $"{file} shares card identity");
        Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("BuildPlayerState(localPlayer)"), "multiplayer uses shared player serializer");
        Console.WriteLine($"CardIdentityContract: {Checks} checks passed");
    }
}
