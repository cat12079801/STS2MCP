using System;
using System.Collections.Generic;
using System.Linq;

namespace STS2_MCP;

/// <summary>
/// The rows of <c>battle.combat_log</c> for the fight in progress, in the order the MOD's read-only patches saw them.
/// A row is fixed once appended (its seq, values and order never change), so a later read of the same fight starts
/// where an earlier one ended. The store keeps the last <see cref="Capacity"/> rows of one fight: a new fight (another
/// combat token) empties it, and past the capacity the oldest rows go (<c>first_seq</c> moves on, <c>dropped</c> counts
/// them). Free of game types so the contract test can drive it.
/// </summary>
internal static class CombatLogStore
{
    /// <summary>Rows kept per fight. A typical player turn plus the enemy turn writes well under 100 rows.</summary>
    internal const int Capacity = 2000;

    /// <summary>Rows a state carries when the request does not ask from a seq (<c>?combat_log_since=</c>).</summary>
    internal const int DefaultWindow = 100;

    private static readonly object Gate = new();
    private static readonly List<Dictionary<string, object?>> Rows = new();
    private static CombatAttempt? _attempt;
    private static long _nextSeq;
    private static long _firstSeq;
    private static readonly List<string> BrokenReasons = new();
    private static readonly List<string> InstallErrorList = new();

    /// <summary>A patch group that could not be installed: its rows never appear, so every log is incomplete.</summary>
    internal static void AddInstallError(string error)
    {
        lock (Gate) InstallErrorList.Add(error);
    }

    internal static IReadOnlyList<string> InstallErrors
    {
        get { lock (Gate) return InstallErrorList.ToList(); }
    }

    /// <summary>Bind a combat observed as current on the game thread, returning its next seq.</summary>
    internal static long Bind(CombatAttempt attempt)
    {
        lock (Gate)
        {
            if (attempt.Matches(_attempt)) return _nextSeq;
            _attempt = attempt;
            Rows.Clear();
            BrokenReasons.Clear();
            _nextSeq = 0;
            _firstSeq = 0;
            return _nextSeq;
        }
    }

    // A delayed completion never binds a combat. The ownership check and seq allocation are atomic.
    internal static bool TryAppend(CombatAttempt attempt, Dictionary<string, object?> row)
    {
        lock (Gate)
        {
            if (!attempt.Matches(_attempt)) return false;
            row["seq"] = _nextSeq++;
            Rows.Add(row);
            while (Rows.Count > Capacity)
            {
                Rows.RemoveAt(0);
                _firstSeq++;
            }
            return true;
        }
    }

    /// <summary>A row this fight should have could not be made: the fight's log is incomplete from here on.</summary>
    internal static bool TryMarkBroken(CombatAttempt attempt, string reason)
    {
        lock (Gate)
        {
            if (!attempt.Matches(_attempt)) return false;
            if (BrokenReasons.Count < 8) BrokenReasons.Add(reason);
            return true;
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            _attempt = null;
            Rows.Clear();
            BrokenReasons.Clear();
            _nextSeq = 0;
            _firstSeq = 0;
        }
    }

    internal sealed class Snapshot
    {
        public required string? CombatId { get; init; }
        public required long FirstSeq { get; init; }
        public required long NextSeq { get; init; }
        public required long WindowFirstSeq { get; init; }
        public required List<Dictionary<string, object?>> Rows { get; init; }
        public required List<string> Reasons { get; init; }
        /// <summary>The request asked from a seq older than the oldest row kept: the rows in between are gone.</summary>
        public required bool SinceTruncated { get; init; }
    }

    /// <summary>
    /// The rows of <paramref name="attempt"/> from <paramref name="since"/> (the retained rows from that seq on), or
    /// the last <paramref name="window"/> rows when <paramref name="since"/> is null. Another fight's rows are never
    /// returned: a combat the store has not seen reads as empty.
    /// </summary>
    internal static Snapshot Read(CombatAttempt? attempt, long? since, int window)
    {
        lock (Gate)
        {
            var reasons = InstallErrorList.Select(e => $"install: {e}").ToList();
            if (attempt?.Matches(_attempt) != true)
                return new Snapshot { CombatId = null, FirstSeq = 0, NextSeq = 0, WindowFirstSeq = 0, Rows = new(), Reasons = reasons, SinceTruncated = false };
            reasons.AddRange(BrokenReasons);
            long from = since ?? Math.Max(_firstSeq, _nextSeq - Math.Max(window, 0));
            bool truncated = from < _firstSeq;
            if (truncated) from = _firstSeq;
            if (from > _nextSeq) from = _nextSeq;
            var rows = Rows.Skip((int)(from - _firstSeq)).ToList();
            return new Snapshot
            {
                CombatId = _nextSeq > 0 || BrokenReasons.Count > 0 ? attempt.CombatId : null,
                FirstSeq = _firstSeq,
                NextSeq = _nextSeq,
                WindowFirstSeq = from,
                Rows = rows,
                Reasons = reasons,
                SinceTruncated = since != null && truncated
            };
        }
    }
}
