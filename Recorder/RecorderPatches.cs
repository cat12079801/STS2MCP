using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_MCP.Recorder;

/// <summary>
/// The recorder's Harmony patches. Applied by hand (not [HarmonyPatch] + PatchAll) so that
/// "record": false in the config means none of them exists, and so that a single target that
/// cannot be found fails the whole install instead of leaving a recorder with holes in it.
///
/// Every patch only reads: prefixes return void (never skip the original), nothing assigns
/// __result or ref arguments, and each body forwards to CombatRecorder, which catches everything.
/// </summary>
internal static class RecorderPatches
{
    private const string HarmonyId = "com.sts2mcp.recorder";
    private static readonly Harmony Harmony = new(HarmonyId);
    private static readonly List<string> AppliedList = new();

    private static readonly AccessTools.FieldRef<CombatReplayWriter, CombatReplay?> ReplayField =
        AccessTools.FieldRefAccess<CombatReplayWriter, CombatReplay?>("_replay");
    private static readonly AccessTools.FieldRef<RunManager, long> StartTimeField =
        AccessTools.FieldRefAccess<RunManager, long>("_startTime");

    internal static IReadOnlyList<string> Applied => AppliedList;

    internal static CombatReplay? ReplayOf(CombatReplayWriter writer) => ReplayField(writer);

    internal static long RunStartTime() => StartTimeField(RunManager.Instance);

    internal static void Apply()
    {
        var w = typeof(CombatReplayWriter);
        Patch(w, nameof(CombatReplayWriter.RecordInitialState), postfix: nameof(InitialStatePostfix));
        Patch(w, "RecordGameAction", prefix: nameof(EventCountPrefix), postfix: nameof(GameActionPostfix));
        Patch(w, "RecordActionResume", prefix: nameof(EventCountPrefix), postfix: nameof(ResumePostfix));
        Patch(w, "RecordPlayerChoice", prefix: nameof(EventCountPrefix), postfix: nameof(ChoicePostfix));
        Patch(w, "RecordChecksum", prefix: nameof(ChecksumCountPrefix), postfix: nameof(ChecksumPostfix));
        Patch(w, nameof(CombatReplayWriter.WriteReplay), prefix: nameof(WriteReplayPrefix));

        var set = typeof(ActionQueueSet);
        Patch(set, nameof(ActionQueueSet.EnqueueWithoutSynchronizing), postfix: nameof(EnqueuedPostfix));
        Patch(set, nameof(ActionQueueSet.PauseActionForPlayerChoice), postfix: nameof(PausedPostfix));
        Patch(set, nameof(ActionQueueSet.ResumeActionWithoutSynchronizing), postfix: nameof(ResumedPostfix));

        Patch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue), prefix: nameof(RequestEnqueuePrefix));
        Patch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.ReserveChoiceId), postfix: nameof(ReservePostfix));
        // BranchingPlayerChoiceContext only forwards to the context it wraps, which is patched itself.
        foreach (var ctx in new[] { typeof(GameActionPlayerChoiceContext), typeof(HookPlayerChoiceContext),
                                    typeof(BlockingPlayerChoiceContext), typeof(ThrowingPlayerChoiceContext) })
            Patch(ctx, nameof(PlayerChoiceContext.SignalPlayerChoiceBegun), postfix: nameof(ChoiceBegunPostfix));
        Patch(typeof(GameAction), nameof(GameAction.Cancel), postfix: nameof(CancelPostfix));
        Patch(typeof(ActionExecutor), "AfterActionFinished", prefix: nameof(ActionFinishedPrefix));
        Patch(typeof(RunManager), nameof(RunManager.CleanUp), prefix: nameof(CleanUpPrefix), postfix: nameof(CleanUpPostfix));
    }

    internal static void RemoveAll()
    {
        try { Harmony.UnpatchAll(HarmonyId); }
        catch { /* nothing else to do */ }
        AppliedList.Clear();
    }

    private static void Patch(Type type, string method, string? prefix = null, string? postfix = null)
    {
        var original = AccessTools.DeclaredMethod(type, method)
            ?? throw new MissingMethodException(type.FullName, method);
        Harmony.Patch(original,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(RecorderPatches), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(RecorderPatches), postfix));
        AppliedList.Add($"{type.Name}.{method}");
    }

    // --- CombatReplayWriter --------------------------------------------------------------------

    private static void InitialStatePostfix(CombatReplayWriter __instance, SerializableRun serializableRun)
        => CombatRecorder.OnInitialState(__instance, serializableRun);

    private static void EventCountPrefix(CombatReplayWriter __instance, out int __state)
    {
        __state = -1;
        try { __state = ReplayOf(__instance)?.events.Count ?? -1; }
        catch { /* never throw into the game */ }
    }

    private static void GameActionPostfix(CombatReplayWriter __instance, GameAction gameAction, int __state)
        => CombatRecorder.OnGameEvent(__instance, __state, gameAction);

    private static void ResumePostfix(CombatReplayWriter __instance, int __state)
        => CombatRecorder.OnGameEvent(__instance, __state, null);

    private static void ChoicePostfix(CombatReplayWriter __instance, int __state)
        => CombatRecorder.OnGameEvent(__instance, __state, null);

    private static void ChecksumCountPrefix(CombatReplayWriter __instance, out int __state)
    {
        __state = -1;
        try { __state = ReplayOf(__instance)?.checksumData.Count ?? -1; }
        catch { /* never throw into the game */ }
    }

    private static void ChecksumPostfix(CombatReplayWriter __instance, int __state)
        => CombatRecorder.OnChecksum(__instance, __state);

    private static void WriteReplayPrefix(CombatReplayWriter __instance, bool stopRecording)
        => CombatRecorder.OnWriteReplay(__instance, stopRecording);

    // --- actions and choices -------------------------------------------------------------------

    private static void EnqueuedPostfix(GameAction gameAction) => CombatRecorder.OnEnqueued(gameAction);

    private static void PausedPostfix(GameAction action, PlayerChoiceOptions options)
        => CombatRecorder.OnPausedForChoice(action, options);

    private static void ResumedPostfix(ActionQueueSet __instance, uint id)
        => CombatRecorder.OnResumed(id, __instance.NextActionId);

    private static void RequestEnqueuePrefix(GameAction action) => CombatRecorder.OnRequestEnqueue(action);

    private static void ReservePostfix(Player player, uint __result) => CombatRecorder.OnChoiceReserved(player, __result);

    private static void ChoiceBegunPostfix(PlayerChoiceContext __instance, Player chooser)
        => CombatRecorder.OnChoiceBegun(__instance, chooser);

    private static void CancelPostfix(GameAction __instance) => CombatRecorder.OnCancelled(__instance);

    private static void ActionFinishedPrefix(GameAction action) => CombatRecorder.OnActionFinished(action);

    private static void CleanUpPrefix() => CombatRecorder.OnCleanUpStart();

    private static void CleanUpPostfix() => CombatRecorder.OnCleanUpEnd();
}
