using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // Deck positions survive reloads without depending on hidden draw order.
    // Cards created in combat use OS randomness, independent of the game's RNG.
    private static readonly CardUidRegistry _cardUidRegistry = new(
        () => RandomNumberGenerator.GetInt32(int.MaxValue));

    /// <summary>
    /// Returns an instance's combat-stable uid. DeckVersion identifies its public
    /// deck position. Generated cards and clones use a separate "#g<number>" space.
    /// </summary>
    internal static string GetStableCardUid(CardModel card) => _cardUidRegistry.GetUid(card);

    internal static void RefreshCardUidRegistry(ICombatState combatState) =>
        _cardUidRegistry.RefreshCombat(combatState);

    private static IEnumerable<CardModel> OrderPileCardsForDisplay(IEnumerable<CardModel> cards, PileType pile)
    {
        // v0.113.0 NCardPileScreen.OnPileContentsChanged sorts only Draw by
        // Rarity, then CardModel.CompareTo. NCardGrid.SetCards(Ascending) keeps
        // that order; Discard/Exhaust (and our Play pile) keep their input order.
        // The UI's stable ties must not preserve hidden draw order in state:
        // resolve them with independent, fixed UIDs. Never mutate a game pile.
        if (pile != PileType.Draw) return cards;
        var snapshot = cards.ToList();
        try
        {
            return snapshot.OrderBy(card => card.Rarity).ThenBy(card => card)
                .ThenBy(GetStableCardUid, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Warn("draw_pile.order", ex);
            return snapshot.OrderBy(card => card.Rarity)
                .ThenBy(card => card.Id.Entry, StringComparer.Ordinal)
                .ThenBy(GetStableCardUid, StringComparer.Ordinal).ToList();
        }
    }

    // The number source is a boundary so collision handling can be tested with
    // fixed values; production always uses the OS cryptographic RNG above.
    private sealed class CardUidRegistry(Func<int> nextNumber)
    {
        private readonly ConditionalWeakTable<CardModel, string> _uids = new();
        private Dictionary<CardModel, string> _deckUids = new(ReferenceEqualityComparer.Instance);
        // Keep issued labels reserved even if their card is removed/collected:
        // combat history and clients can still hold references to those labels.
        private readonly HashSet<string> _issuedUids = new(StringComparer.Ordinal);
        private ICombatState? _combatToken;

        internal string GetUid(CardModel card)
        {
            if (_uids.TryGetValue(card, out var existing)) return existing;

            if (card.DeckVersion is { } deckVersion && _deckUids.TryGetValue(deckVersion, out var deckUid)
                && _issuedUids.Add(deckUid))
            {
                _uids.Add(card, deckUid);
                return deckUid;
            }

            string baseId = SafeGetText(() => card.Id.Entry) ?? "unknown";
            string uid;
            do
            {
                uid = baseId + "#g" + nextNumber().ToString(CultureInfo.InvariantCulture);
            } while (!_issuedUids.Add(uid));
            _uids.Add(card, uid);
            return uid;
        }

        internal void RefreshCombat(ICombatState combatState)
        {
            if (ReferenceEquals(_combatToken, combatState)) return;
            var deckUids = new Dictionary<CardModel, string>(ReferenceEqualityComparer.Instance);
            foreach (var player in combatState.Players)
            {
                var deck = player.Deck.Cards;
                for (int index = 0; index < deck.Count; index++)
                {
                    string suffix = index.ToString(CultureInfo.InvariantCulture);
                    // Separate players' equal deck positions deterministically.
                    if (combatState.Players.Count > 1)
                        suffix += "p" + player.NetId.ToString(CultureInfo.InvariantCulture);
                    deckUids.Add(deck[index], deck[index].Id.Entry + "#" + suffix);
                }
            }
            _combatToken = combatState;
            _uids.Clear();
            _issuedUids.Clear();
            _deckUids = deckUids;
        }
    }
}
