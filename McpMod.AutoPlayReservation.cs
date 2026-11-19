using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

/// <summary>
/// One <c>CardPileCmd.AutoPlayFromDrawPile</c> (the 6-argument overload every caller ends in: Cascade, Havoc,
/// TheDevil, MayhemPower, IAmInvincible, DistilledChaos, LostLutePower). The game first moves up to
/// <c>count</c> cards from the draw pile to the Play pile one by one (each on screen, face up, flying to the
/// play area), then calls <c>CardCmd.AutoPlay</c> on them in that order. Free of Harmony and of game commands
/// so the contract test can drive it; the parent's model is reduced to strings by the patch.
/// </summary>
internal sealed class AutoPlayReservationCall
{
    public required int CallId { get; init; }
    public required int? ParentCallId { get; init; }
    public required Player Owner { get; init; }
    public required object Context { get; init; }
    /// <summary>"card" / "potion" / "power" / "relic" / "other", or null when the choice context names no model.</summary>
    public required string? ParentKind { get; init; }
    public required string? ParentModelId { get; init; }
    public required CardModel? ParentCard { get; init; }
    /// <summary>The parent card was in the draw pile when the call began (its place there is not public).</summary>
    public required bool ParentInDrawPile { get; init; }
    public required CardPlay? ParentPlay { get; init; }
    public required int RequestedCount { get; init; }
    public required string Position { get; init; }
    public required bool ForceExhaust { get; init; }
    /// <summary>The game's own <c>cards</c> list: every card the selection delegate saw, in order.</summary>
    public List<CardModel> Selected { get; } = new();
    public int NextDispatch { get; set; }
    public CardModel? PendingMove { get; set; }
    public int PendingIndex { get; set; } = -1;
    public bool Completed { get; set; }
}

/// <summary>One row the MOD adds to card_resolution_events, placed after the first <c>Position</c> History entries.</summary>
internal sealed class AutoPlayReservationRow
{
    public required int Position { get; init; }
    public required string Kind { get; init; }
    public required AutoPlayReservationCall Call { get; init; }
    public int? ReserveIndex { get; init; }
    public CardModel? Card { get; init; }
}

internal sealed class AutoPlayDispatch
{
    public required AutoPlayReservationCall Call { get; init; }
    public required int ReserveIndex { get; init; }
    public required CardModel Card { get; init; }
    public bool Open { get; set; } = true;
}

internal readonly record struct AutoPlayReservationTag(int CallId, int ReserveIndex);

/// <summary>
/// The reservations of the fight in progress. Every record is made on the main thread while the game runs the
/// call (prefix / postfix of read-only patches) and never changes afterwards except for the open / completed
/// flags that only decide which later game call a record matches. Cleared with <c>CombatHistory.Clear</c>.
/// </summary>
internal static class AutoPlayReservations
{
    internal const string KindReservation = "auto_play_reservation";
    internal const string KindReserved = "auto_play_reserved";
    internal const string KindDispatch = "auto_play_dispatch";

    private static readonly object Gate = new();
    private static readonly List<AutoPlayReservationCall> Calls = new();
    private static readonly List<AutoPlayReservationRow> Rows = new();
    private static readonly List<AutoPlayDispatch> OpenDispatches = new();
    private static readonly Dictionary<CardPlay, AutoPlayReservationTag> Tags = new(ReferenceEqualityComparer.Instance);
    private static volatile bool _anyPendingMove;

    internal static bool AnyPendingMove => _anyPendingMove;

    /// <summary>Why the patches are not installed (null once they are). The stream is unknown without them.</summary>
    internal static string? TrackingError { get; set; } = "not installed";

    /// <summary>A record of this fight failed: the stream stays unknown until the next fight.</summary>
    internal static string? Broken { get; set; }

    internal static void ThrowIfUnavailable()
    {
        if (TrackingError != null)
            throw new InvalidOperationException($"auto-play reservation tracking unavailable: {TrackingError}");
        if (Broken != null)
            throw new InvalidOperationException($"auto-play reservation record failed: {Broken}");
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            Broken = null;
            Calls.Clear();
            Rows.Clear();
            OpenDispatches.Clear();
            Tags.Clear();
            _anyPendingMove = false;
        }
    }

    /// <summary>The call whose dispatch is playing <paramref name="play"/>, if any.</summary>
    internal static AutoPlayReservationTag? TagOf(CardPlay play)
    {
        lock (Gate) return Tags.TryGetValue(play, out var tag) ? tag : null;
    }

    /// <summary>The game passed its <c>IsOverOrEnding</c> check and starts reserving: one reservation row.</summary>
    internal static AutoPlayReservationCall Begin(int position, Player owner, object context, string? parentKind,
        string? parentModelId, CardModel? parentCard, bool parentInDrawPile, CardPlay? parentPlay,
        int requestedCount, string reservePosition, bool forceExhaust)
    {
        lock (Gate)
        {
            var call = new AutoPlayReservationCall
            {
                CallId = Calls.Count + 1,
                ParentCallId = parentPlay != null && Tags.TryGetValue(parentPlay, out var tag) ? tag.CallId : null,
                Owner = owner,
                Context = context,
                ParentKind = parentKind,
                ParentModelId = parentModelId,
                ParentCard = parentCard,
                ParentInDrawPile = parentInDrawPile,
                ParentPlay = parentPlay,
                RequestedCount = requestedCount,
                Position = reservePosition,
                ForceExhaust = forceExhaust
            };
            Calls.Add(call);
            Rows.Add(new AutoPlayReservationRow { Position = position, Kind = KindReservation, Call = call });
            return call;
        }
    }

    /// <summary>The selection delegate ran for <paramref name="card"/>: the game adds it to its list and moves it to Play next.</summary>
    internal static void Selected(AutoPlayReservationCall call, CardModel card)
    {
        lock (Gate)
        {
            call.Selected.Add(card);
            call.PendingMove = card;
            call.PendingIndex = call.Selected.Count - 1;
            _anyPendingMove = true;
        }
    }

    /// <summary>A card entered <paramref name="owner"/>'s Play pile: a reserved row when it is a call's pending card.</summary>
    internal static void EnteredPlayPile(int position, Player owner, CardModel card)
    {
        lock (Gate)
        {
            for (int i = Calls.Count - 1; i >= 0; i--)
            {
                var call = Calls[i];
                if (call.Completed || !ReferenceEquals(call.Owner, owner) || !ReferenceEquals(call.PendingMove, card)) continue;
                Rows.Add(new AutoPlayReservationRow
                {
                    Position = position, Kind = KindReserved, Call = call, ReserveIndex = call.PendingIndex, Card = card
                });
                call.PendingMove = null;
                call.PendingIndex = -1;
                break;
            }
            _anyPendingMove = Calls.Any(c => c.PendingMove != null);
        }
    }

    /// <summary>
    /// <c>CardCmd.AutoPlay(context, card, null)</c> began. When it is the next card of a call's own loop, the
    /// call moves on; <paramref name="proceeds"/> is false when AutoPlay returns at once (the fight is over or
    /// the owner is dead), and then no row is made. Returns the dispatch to close when AutoPlay finishes.
    /// </summary>
    internal static AutoPlayDispatch? AutoPlayBegan(int position, object context, CardModel card, bool targetGiven, bool proceeds)
    {
        if (targetGiven) return null;
        lock (Gate)
        {
            for (int i = Calls.Count - 1; i >= 0; i--)
            {
                var call = Calls[i];
                if (call.Completed || !ReferenceEquals(call.Context, context)) continue;
                if (call.NextDispatch >= call.Selected.Count || !ReferenceEquals(call.Selected[call.NextDispatch], card)) continue;
                int index = call.NextDispatch++;
                if (!proceeds) return null;
                var dispatch = new AutoPlayDispatch { Call = call, ReserveIndex = index, Card = card };
                Rows.Add(new AutoPlayReservationRow
                {
                    Position = position, Kind = KindDispatch, Call = call, ReserveIndex = index, Card = card
                });
                OpenDispatches.Add(dispatch);
                return dispatch;
            }
            return null;
        }
    }

    internal static void AutoPlayEnded(AutoPlayDispatch dispatch)
    {
        lock (Gate)
        {
            dispatch.Open = false;
            OpenDispatches.Remove(dispatch);
        }
    }

    /// <summary><c>CombatHistory.CardPlayStarted</c> is about to record <paramref name="play"/>: tag it with its dispatch.</summary>
    internal static void PlayStarting(CardPlay play, bool isAutoPlay)
    {
        if (!isAutoPlay) return;
        lock (Gate)
        {
            for (int i = OpenDispatches.Count - 1; i >= 0; i--)
            {
                var dispatch = OpenDispatches[i];
                if (!dispatch.Open || !ReferenceEquals(dispatch.Card, play.Card)
                    || !ReferenceEquals(dispatch.Call.Owner, play.Player)) continue;
                Tags[play] = new AutoPlayReservationTag(dispatch.Call.CallId, dispatch.ReserveIndex);
                return;
            }
        }
    }

    internal static void CallCompleted(AutoPlayReservationCall call)
    {
        lock (Gate)
        {
            call.Completed = true;
            call.PendingMove = null;
            _anyPendingMove = Calls.Any(c => c.PendingMove != null);
        }
    }

    /// <summary>The rows of <paramref name="owner"/>'s calls, in record order.</summary>
    internal static List<AutoPlayReservationRow> RowsOf(Player owner)
    {
        lock (Gate) return Rows.Where(r => ReferenceEquals(r.Call.Owner, owner)).ToList();
    }
}
