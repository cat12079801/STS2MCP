using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
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
        Check(registry.GetUid(first) == "STRIKE#900", "random label retains card_id#decimal format");
        Check(registry.GetUid(second) == "STRIKE#100", "discovery order does not set numeric rank");
        Check(registry.GetUid(clone) == "STRIKE#600", "clone has distinct identity and retries a collision");
        Check(registry.GetUid(new CardModel("DEFEND")) == "DEFEND#100", "full labels are unique even with equal suffixes across IDs");
        registry.RefreshCombat(combat);
        first.DisplayRank++;
        first.Id.Entry = "TRANSFORMED";
        Check(registry.GetUid(first) == "STRIKE#900", "same combat refresh and card changes retain instance uid");
        var generatedInHand = new CardModel("STRIKE");
        Check(registry.GetUid(generatedInHand) == "STRIKE#0", "direct-to-hand generated card uses independent number, including zero");
        var removedReplacement = new CardModel("STRIKE");
        Check(registry.GetUid(removedReplacement) == "STRIKE#42", "previously issued label stays reserved after leaving piles");
        var otherOwner = new CardModel("STRIKE");
        Check(registry.GetUid(otherOwner) == "STRIKE#77", "same-ID cards from another player share uniqueness domain");
        registry.RefreshCombat(new CombatState());
        first.Id.Entry = "STRIKE";
        Check(registry.GetUid(first) == "STRIKE#900" && numbers.Count == 0, "new combat clears instance map and reserved labels");

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

        // Production RNG wiring and lifecycle: labels are decimal strings, are
        // stable across refreshes, and snapshots retain their old values.
        var sharedCombat = new CombatState();
        RefreshCardUidRegistry(sharedCombat);
        var card = new CardModel("STRIKE");
        string uid = GetStableCardUid(card);
        string snapshot = JsonSerializer.Serialize(new Dictionary<string, object?> { ["uid"] = uid });
        RefreshCardUidRegistry(sharedCombat);
        Check(GetStableCardUid(card) == uid, "production registry refresh preserves uid in same combat");
        Check(int.TryParse(uid.Split('#')[1], NumberStyles.None, CultureInfo.InvariantCulture, out int suffix) && suffix >= 0, "production OS random label uses nonnegative decimal suffix");
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
