using System.Collections.Generic;

namespace STS2_MCP;

/// <summary>
/// The local player's card_resolution_events as they stood when the last fight ended, kept for the action
/// response that ended it: after the fight the state has no combat_history and the game clears its History
/// (<c>CombatManager.EndCombatInternal</c> → <c>CombatHistory.Clear</c>). Captured on the main thread by the
/// read-only patches in McpMod.AutoPlayReservationPatches.cs; free of game types so ActionWait's tests link it.
/// </summary>
internal static class EndedCombatCaptures
{
    private static readonly object Gate = new();
    private static long _sequence;
    private static Dictionary<string, object?>? _latest;

    /// <summary>Why a lost fight is not captured (the ProcessPendingLoss patch is missing); null once installed.</summary>
    internal static string? LossCaptureError { get; set; } = "not installed";

    internal static long Sequence
    {
        get { lock (Gate) return _sequence; }
    }

    internal static void Record(Dictionary<string, object?> capture)
    {
        lock (Gate)
        {
            _sequence++;
            _latest = capture;
        }
    }

    /// <summary>The capture made after <paramref name="sequenceBefore"/>, or null when no fight ended since.</summary>
    internal static Dictionary<string, object?>? Since(long sequenceBefore)
    {
        lock (Gate) return _sequence > sequenceBefore ? _latest : null;
    }
}
