using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
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
        Patch(set, nameof(ActionQueueSet.PauseActionForPlayerChoice), prefix: nameof(PausedPrefix), postfix: nameof(PausedPostfix));
        Patch(set, nameof(ActionQueueSet.ResumeActionWithoutSynchronizing), postfix: nameof(ResumedPostfix));

        Patch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue), prefix: nameof(RequestEnqueuePrefix));
        Patch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.ReserveChoiceId), postfix: nameof(ReservePostfix));
        // BranchingPlayerChoiceContext only forwards to the context it wraps, which is patched itself.
        foreach (var ctx in new[] { typeof(GameActionPlayerChoiceContext), typeof(HookPlayerChoiceContext),
                                    typeof(BlockingPlayerChoiceContext), typeof(ThrowingPlayerChoiceContext) })
            Patch(ctx, nameof(PlayerChoiceContext.SignalPlayerChoiceBegun),
                prefix: nameof(ChoiceBegunPrefix), postfix: nameof(ChoiceBegunPostfix));
        // Reached through the action's AfterFinished delegate, so a call the JIT cannot inline.
        // Cancellation is read from GameAction.BeforeCancelled instead of patching the few-line
        // GameAction.Cancel, which could be inlined into its callers.
        Patch(typeof(ActionExecutor), "AfterActionFinished", prefix: nameof(ActionFinishedPrefix));
        Patch(typeof(RunManager), nameof(RunManager.CleanUp), prefix: nameof(CleanUpPrefix), postfix: nameof(CleanUpPostfix));
        Patch(typeof(MegaCrit.Sts2.Core.DevConsole.DevConsole), nameof(MegaCrit.Sts2.Core.DevConsole.DevConsole.ProcessCommand),
            prefix: nameof(ConsolePrefix), parameters: new[] { typeof(string) });
        // Counted only: AC2 asks that recording sends nothing to Sentry (docs/recording.md).
        foreach (var m in AccessTools.GetDeclaredMethods(typeof(SentryService)))
        {
            if (m.Name == nameof(SentryService.CaptureException))
            {
                Harmony.Patch(m, prefix: new HarmonyMethod(typeof(RecorderPatches), nameof(SentryPrefix)));
                AppliedList.Add($"SentryService.CaptureException({m.GetParameters().Length})");
            }
        }
    }

    internal static void RemoveAll()
    {
        try { Harmony.UnpatchAll(HarmonyId); }
        catch { /* nothing else to do */ }
        AppliedList.Clear();
    }

    private static void Patch(Type type, string method, string? prefix = null, string? postfix = null, Type[]? parameters = null)
    {
        var original = (parameters == null ? AccessTools.DeclaredMethod(type, method) : AccessTools.DeclaredMethod(type, method, parameters))
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

    private static void PausedPrefix(GameAction action, out uint? __state) => __state = action.Id;

    private static void PausedPostfix(GameAction action, PlayerChoiceOptions options, uint? __state)
        => CombatRecorder.OnPausedForChoice(action, options, __state);

    private static void ResumedPostfix(ActionQueueSet __instance, uint id)
        => CombatRecorder.OnResumed(id, __instance.NextActionId);

    private static void RequestEnqueuePrefix(GameAction action) => CombatRecorder.OnRequestEnqueue(action);

    private static void ReservePostfix(Player player, uint __result) => CombatRecorder.OnChoiceReserved(player, __result);

    private static void ChoiceBegunPrefix(Player chooser, out uint? __state)
    {
        __state = null;
        try { __state = CombatRecorder.OnChoiceBegunPrefix(chooser); }
        catch { /* never throw into the game */ }
    }

    private static void ChoiceBegunPostfix(PlayerChoiceContext __instance, Player chooser, uint? __state)
        => CombatRecorder.OnChoiceBegun(__instance, chooser, __state);

    private static void SentryPrefix() => CombatRecorder.OnSentryCapture();

    private static void ConsolePrefix(string inputValue) => CombatRecorder.OnConsoleCommand(inputValue);

    private static readonly System.Reflection.FieldInfo? QueuesField =
        AccessTools.Field(typeof(ActionQueueSet), "_actionQueues");

    /// <summary>Every action in the player queues (read through the private list; nothing is changed).</summary>
    internal static IEnumerable<GameAction> QueuedActions(ActionQueueSet? set)
    {
        if (set == null || QueuesField?.GetValue(set) is not System.Collections.IEnumerable queues)
            yield break;
        foreach (var q in queues)
        {
            if (q == null || AccessTools.Field(q.GetType(), "actions")?.GetValue(q) is not IEnumerable<GameAction> actions)
                continue;
            foreach (var a in actions.ToList())
                yield return a;
        }
    }

    private static void ActionFinishedPrefix(GameAction action) => CombatRecorder.OnActionFinished(action);

    private static void CleanUpPrefix() => CombatRecorder.OnCleanUpStart();

    private static void CleanUpPostfix() => CombatRecorder.OnCleanUpEnd();
}
