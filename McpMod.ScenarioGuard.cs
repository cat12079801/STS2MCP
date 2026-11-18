using System;
using System.Collections.Generic;
using System.Text.Json;

namespace STS2_MCP;

// Contracts for the verification-run capture tool (cat12079801/sts2#1016, #1037). Pure: no game,
// Godot or HTTP types, so the contract tests link this file as is. The live reads that feed it are
// in McpMod.ScenarioGuardLive.cs.
public static partial class McpMod
{
    internal const int ScenarioSetupGuardVersion = 1;
    internal const string ScenarioSetupGuardKey = "scenario_setup_guard";
    internal const string ExpectedUidKey = "expected_uid";

    /// <summary>
    /// The verification contracts this build implements, as contract versions (0 = not implemented).
    /// A client gates on these instead of the schema number or on a field happening to be present:
    /// an older build ignores an unknown request parameter, so sending the guard or expected_uid to
    /// one would execute the request unguarded.
    /// </summary>
    internal static Dictionary<string, object?> BuildScenarioCapabilities() => new()
    {
        // GET ?passive=1&scenario_setup_guard=1 and the dev_console scenario_setup_guard body.
        ["setup_guard"] = ScenarioSetupGuardVersion,
        // hand_select rows/selected_cards by uid at the holder index, and expected_uid on
        // combat_select_card / select_card (grid and choose-a-card).
        ["selection_uid"] = 1,
        // The potion can-use-now contract (sts2#1012) is not in this build.
        ["potion_availability"] = 0
    };

    // Screens a setup console command may run from, and the room each one has to be in.
    // A map opened over any other room (a fight, an event, the rest site) is not a setup screen.
    internal static readonly IReadOnlyDictionary<string, string> ScenarioSetupScreenRooms =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["map"] = "MapRoom",
            ["event"] = "EventRoom",
            ["rest_site"] = "RestSiteRoom"
        };

    /// <summary>
    /// What the setup guard is decided from, read on the main thread in one frame. A null value is a
    /// read that failed; <see cref="Unreadable"/> names it and why.
    /// </summary>
    internal sealed class ScenarioSetupGuardReads
    {
        /// <summary>RunManager.IsInProgress with a run state; null when it could not be read.</summary>
        public bool? RunInProgress;
        public string? Screen;
        public bool? IsResolving;
        /// <summary>RunState.CurrentRoom's runtime type name; null with RoomRead=true means no room.</summary>
        public string? Room;
        public bool RoomRead = true;
        /// <summary>Only for an EventRoom: whether its canonical event is Neow.</summary>
        public bool? EventIsNeow;
        public bool? CombatStarting;
        public bool? CombatInProgress;
        public bool? RunOver;
        public List<string> Unreadable = new();
    }

    /// <summary>
    /// The out-of-combat setup verdict. allowed is true only when the run is in progress and not
    /// over, no fight is starting or in progress, nothing is resolving, and the front screen is the
    /// setup screen of the room it stands in (map in a MapRoom, Neow's event in its EventRoom, the
    /// rest site in a RestSiteRoom). Anything unread is not allowed. reason is null when allowed.
    /// </summary>
    internal static Dictionary<string, object?> DecideScenarioSetupGuard(ScenarioSetupGuardReads reads)
    {
        var guard = new Dictionary<string, object?>
        {
            ["version"] = ScenarioSetupGuardVersion,
            ["screen"] = reads.Screen,
            ["room"] = reads.Room,
            ["combat_starting"] = reads.CombatStarting,
            ["combat_in_progress"] = reads.CombatInProgress,
            ["run_over"] = reads.RunOver,
            ["allowed"] = false,
            ["reason"] = null
        };
        string? reason = ScenarioSetupGuardReason(reads);
        guard["allowed"] = reason == null;
        guard["reason"] = reason;
        if (reads.Unreadable.Count > 0)
            guard["unavailable_reasons"] = new List<string>(reads.Unreadable);
        return guard;
    }

    private static string? ScenarioSetupGuardReason(ScenarioSetupGuardReads reads)
    {
        if (reads.RunInProgress == null) return "unreadable";
        if (reads.RunInProgress == false) return "no_run";
        // Definite blockers first, so a failed read elsewhere does not hide them.
        if (reads.RunOver == true) return "run_over";
        if (reads.CombatInProgress == true) return "combat_in_progress";
        if (reads.CombatStarting == true) return "combat_starting";
        if (reads.Screen == null || !ScenarioSetupScreenRooms.TryGetValue(reads.Screen, out var expectedRoom))
            return reads.Screen == null ? "unreadable" : "screen_not_setup";
        if (reads.IsResolving == true) return "resolving";
        if (reads.RunOver == null || reads.CombatInProgress == null || reads.CombatStarting == null
            || reads.IsResolving == null || !reads.RoomRead)
            return "unreadable";
        if (reads.Room == null) return "no_current_room";
        if (!IsScenarioSetupRoom(reads.Room)) return "room_not_setup";
        if (!string.Equals(reads.Room, expectedRoom, StringComparison.Ordinal)) return "screen_room_mismatch";
        if (reads.Room == "EventRoom")
        {
            if (reads.EventIsNeow == null) return "unreadable";
            if (reads.EventIsNeow == false) return "event_not_neow";
        }
        return null;
    }

    private static bool IsScenarioSetupRoom(string room)
    {
        foreach (var setupRoom in ScenarioSetupScreenRooms.Values)
            if (string.Equals(setupRoom, room, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>A parsed dev_console scenario_setup_guard: the screen the caller verified.</summary>
    internal sealed record ScenarioSetupGuardRequest(int Version, string ExpectedScreen);

    /// <summary>
    /// Parses the dev_console body's scenario_setup_guard. Returns an error message for anything
    /// but exactly {version: 1, expected_screen: map|event|rest_site}; unknown keys, versions and
    /// screens are refused rather than ignored.
    /// </summary>
    internal static string? ParseScenarioSetupGuardRequest(JsonElement value, out ScenarioSetupGuardRequest? guard)
    {
        guard = null;
        const string prefix = "dev_console scenario_setup_guard";
        if (value.ValueKind != JsonValueKind.Object)
            return $"{prefix} must be an object {{version, expected_screen}}; the command was not executed.";
        int? version = null;
        string? screen = null;
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int v))
                        return $"{prefix}.version must be an integer; the command was not executed.";
                    version = v;
                    break;
                case "expected_screen":
                    if (property.Value.ValueKind != JsonValueKind.String)
                        return $"{prefix}.expected_screen must be a string; the command was not executed.";
                    screen = property.Value.GetString();
                    break;
                default:
                    return $"{prefix} has unknown key '{property.Name}'; the command was not executed.";
            }
        }
        if (version == null)
            return $"{prefix}.version is required; the command was not executed.";
        if (version != ScenarioSetupGuardVersion)
            return $"{prefix} version {version} is not supported (this build supports {ScenarioSetupGuardVersion}); the command was not executed.";
        if (screen == null || !ScenarioSetupScreenRooms.ContainsKey(screen))
            return $"{prefix}.expected_screen must be one of {string.Join(", ", ScenarioSetupScreenRooms.Keys)}; the command was not executed.";
        guard = new ScenarioSetupGuardRequest(version.Value, screen);
        return null;
    }

    /// <summary>
    /// Why a guarded dev_console must not run, judged from the guard read on the main thread in the
    /// frame that would execute it. Null means run it.
    /// </summary>
    internal static string? ScenarioSetupGuardConsoleRejection(
        Dictionary<string, object?>? current, ScenarioSetupGuardRequest request)
    {
        if (current == null) return "the setup guard could not be read";
        if (current.GetValueOrDefault("allowed") is not true)
            return $"setup is not allowed now ({current.GetValueOrDefault("reason") ?? "unknown"})";
        if (current.GetValueOrDefault("screen") is not string screen
            || !string.Equals(screen, request.ExpectedScreen, StringComparison.Ordinal))
            return $"the screen moved: expected {request.ExpectedScreen}, now {current.GetValueOrDefault("screen") ?? "unknown"}";
        return null;
    }

    /// <summary>
    /// Reads the optional expected_uid. Absent → null uid and no error; present it must be a
    /// non-empty string.
    /// </summary>
    internal static string? ReadExpectedUid(Dictionary<string, JsonElement> data, out string? expectedUid)
    {
        expectedUid = null;
        if (!data.TryGetValue(ExpectedUidKey, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            return $"'{ExpectedUidKey}' must be a non-empty string uid; nothing was pressed";
        expectedUid = value.GetString();
        return null;
    }

    /// <summary>
    /// Compares the uid of the card currently at the requested index with expected_uid. Null means
    /// it matches; otherwise the refusal message (nothing is pressed).
    /// </summary>
    internal static string? ExpectedUidMismatch(string expectedUid, int index, string? actualUid, string? unavailableReason = null)
    {
        if (actualUid == null)
            return $"expected_uid {expectedUid} cannot be checked at index {index}: "
                + $"{unavailableReason ?? "the card has no uid"}; nothing was pressed - re-read state";
        if (!string.Equals(actualUid, expectedUid, StringComparison.Ordinal))
            return $"expected_uid {expectedUid} does not match the card at index {index} ({actualUid}); "
                + "nothing was pressed - re-read state";
        return null;
    }

    /// <summary>
    /// hand_select rows in NPlayerHand.ActiveHolders order. index is the holder's position in that
    /// list - the one combat_select_card presses - so a holder without a card leaves a gap instead
    /// of shifting the cards after it. Returns the positions of the holders with a card.
    /// </summary>
    internal static List<(int Index, T Card)> HandSelectHolderRows<T>(IReadOnlyList<T?> holderCards) where T : class
    {
        var rows = new List<(int, T)>(holderCards.Count);
        for (int index = 0; index < holderCards.Count; index++)
            if (holderCards[index] is { } card) rows.Add((index, card));
        return rows;
    }

    /// <summary>
    /// Why a press on any hand holder would be a silent no-op right now, mirroring the early returns
    /// of NPlayerHand.OnHolderPressed: a peek in progress (it only wiggles the peek button), an overlay
    /// on the stack (NOverlayStack.ScreenCount &gt; 0) or no combat in progress. A value that could not
    /// be read (null) counts as blocked. Null = presses land. The per-holder condition (CardNode == null)
    /// is the holder without a card.
    /// </summary>
    internal static string? HandPressBlocker(bool? peeking, int? overlayScreenCount, bool? combatInProgress)
    {
        if (peeking != false)
            return peeking == null
                ? "whether the hand is being peeked at cannot be read"
                : "the hand is being peeked at; the game ignores card presses until the peek ends";
        if (overlayScreenCount == null) return "the overlay stack cannot be read";
        if (overlayScreenCount > 0)
            return $"an overlay is open on top of the hand ({overlayScreenCount}); the game ignores card presses";
        if (combatInProgress != true)
            return combatInProgress == null ? "whether combat is in progress cannot be read"
                : "no combat is in progress; the game ignores card presses";
        return null;
    }

    /// <summary>
    /// Why combat_select_card must not press holder <paramref name="index"/>: out of range, no card,
    /// a press the game would ignore (<paramref name="pressBlocked"/>, from <see cref="HandPressBlocker"/>),
    /// or - when the caller sent expected_uid - a uid mismatch. Null = press it.
    /// </summary>
    internal static string? HandSelectPressRejection<T>(IReadOnlyList<T?> holderCards, int index, string? pressBlocked,
        string? expectedUid, Func<T, string?> uidOf, string? uidUnavailableReason = null) where T : class
    {
        if (index < 0 || index >= holderCards.Count)
            return $"Card index {index} out of range ({holderCards.Count} selectable cards)";
        if (holderCards[index] is not { } card)
            return $"Card index {index} has no card in the hand - re-read state";
        if (pressBlocked != null)
            return $"Card index {index} not pressed: {pressBlocked} - re-read state";
        if (expectedUid == null) return null;
        return ExpectedUidMismatch(expectedUid, index, uidOf(card), uidUnavailableReason);
    }
}
