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
    // Use OS randomness, never the game's seeded RNG or a first-seen counter: a
    // counter reveals hidden pile order even if the rows are sorted afterwards.
    private static readonly CardUidRegistry _cardUidRegistry = new(
        () => RandomNumberGenerator.GetInt32(int.MaxValue));

    /// <summary>
    /// Returns an instance's combat-stable "card_id#number". The number is an
    /// opaque random label, independent of discovery, creation and draw order.
    /// Generated cards and clones get their own label by the same rule.
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
        return pile == PileType.Draw
            ? cards.OrderBy(card => card.Rarity).ThenBy(card => card)
                .ThenBy(GetStableCardUid, StringComparer.Ordinal)
            : cards;
    }

    // The number source is a boundary so collision handling can be tested with
    // fixed values; production always uses the OS cryptographic RNG above.
    private sealed class CardUidRegistry(Func<int> nextNumber)
    {
        private readonly ConditionalWeakTable<CardModel, string> _uids = new();
        // Keep issued labels reserved even if their card is removed/collected:
        // combat history and clients can still hold references to those labels.
        private readonly HashSet<string> _issuedUids = new(StringComparer.Ordinal);
        private ICombatState? _combatToken;

        internal string GetUid(CardModel card)
        {
            if (_uids.TryGetValue(card, out var existing)) return existing;

            string baseId = SafeGetText(() => card.Id.Entry) ?? "unknown";
            string uid;
            do
            {
                uid = baseId + "#" + nextNumber().ToString(CultureInfo.InvariantCulture);
            } while (!_issuedUids.Add(uid));
            _uids.Add(card, uid);
            return uid;
        }

        internal void RefreshCombat(ICombatState combatState)
        {
            if (ReferenceEquals(_combatToken, combatState)) return;
            _combatToken = combatState;
            _uids.Clear();
            _issuedUids.Clear();
        }
    }
}
