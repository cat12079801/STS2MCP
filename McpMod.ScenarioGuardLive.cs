using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

// Live reads behind the out-of-combat setup guard (cat12079801/sts2#1037). Main thread only; reads
// only - no click, no RNG, no hook. The verdict itself is McpMod.ScenarioGuard.cs.
public static partial class McpMod
{
    /// <summary>
    /// The setup guard for a state just built in this frame: the screen and is_resolving come from
    /// that state, the rest from RunState.CurrentRoom's runtime type, RunState.IsGameOver,
    /// CombatManager.IsStarting / IsInProgress and (in an EventRoom) whether its event is Neow.
    /// </summary>
    internal static Dictionary<string, object?> BuildScenarioSetupGuard(Dictionary<string, object?> state)
    {
        var reads = new ScenarioSetupGuardReads
        {
            Screen = state.GetValueOrDefault("state_type") as string,
            IsResolving = state.GetValueOrDefault("is_resolving") as bool?
        };
        if (reads.IsResolving == null) reads.Unreadable.Add("is_resolving: absent from the state");

        RunState? runState = null;
        try
        {
            var run = RunManager.Instance;
            runState = run.IsInProgress ? run.DebugOnlyGetState() : null;
            reads.RunInProgress = runState != null;
        }
        catch (Exception ex) { reads.Unreadable.Add($"run: {ex.GetType().Name}: {ex.Message}"); }

        if (runState != null)
        {
            try
            {
                var room = runState.CurrentRoom;
                reads.Room = room?.GetType().Name;
                if (room is EventRoom eventRoom)
                {
                    try { reads.EventIsNeow = eventRoom.CanonicalEvent is Neow; }
                    catch (Exception ex) { reads.Unreadable.Add($"event: {ex.GetType().Name}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                reads.RoomRead = false;
                reads.Unreadable.Add($"room: {ex.GetType().Name}: {ex.Message}");
            }

            try { reads.RunOver = runState.IsGameOver; }
            catch (Exception ex) { reads.Unreadable.Add($"run_over: {ex.GetType().Name}: {ex.Message}"); }
        }

        try
        {
            var combat = CombatManager.Instance;
            reads.CombatStarting = combat.IsStarting;
            reads.CombatInProgress = combat.IsInProgress;
        }
        catch (Exception ex) { reads.Unreadable.Add($"combat: {ex.GetType().Name}: {ex.Message}"); }

        return DecideScenarioSetupGuard(reads);
    }

    /// <summary>
    /// Builds the passive state and its setup guard now, in the caller's main-thread frame. Used by a
    /// guarded dev_console immediately before the command runs, so the verdict is the one that holds
    /// when it executes, not the one the client saw on its GET.
    /// </summary>
    internal static Dictionary<string, object?> ReadScenarioSetupGuardNow()
    {
        bool previous = _passiveRead;
        _passiveRead = true;
        try { return BuildScenarioSetupGuard(BuildGameState()); }
        finally { _passiveRead = previous; }
    }
}
