namespace STS2_MCP;

public static partial class McpMod
{
    internal const string CombatStartingReason = "combat_starting";
    internal const string CombatEndingReason = "combat_ending";

    /// <summary>
    /// What a combat room is doing while <c>CombatManager.IsInProgress</c> is false and nothing
    /// (map, rewards, game over, a choice screen) is in front of it: the state has no <c>battle</c>,
    /// and the game is moving on by itself.
    ///
    /// The game's own order (decompile <c>sts2-ae309f85</c>):
    /// <list type="bullet">
    /// <item><b>Starting.</b> <c>RunManager.EnterRoomInternal</c> pushes the room (<c>RunState.PushRoom</c>)
    /// before <c>CombatRoom.EnterInternal</c> → <c>StartCombat</c> loads the assets and calls
    /// <c>CombatManager.SetUpCombat</c>, so for a while the room is current with no turn state at all.
    /// <c>SetUpCombat</c> then creates the turn state with <c>IsStarting = true</c>, and only
    /// <c>StartCombatInternal</c> (the turn loop, started by <c>AfterCombatRoomLoaded</c>) sets
    /// <c>IsInProgress = true</c> and <c>IsStarting = false</c> together. Both windows are the start.</item>
    /// <item><b>Ending.</b> <c>EndCombatInternal</c> (a win) sets <c>IsInProgress = false</c> and, after the
    /// combat-end hooks and just before the save, <c>CombatRoom.MarkPreFinished</c>; <c>NCombatUi.OnCombatWon</c> then
    /// waits for the death animations (<c>ShowRewards</c>) and pushes <c>NRewardsScreen</c>
    /// (<c>CombatRoom.OfferRoomEndRewards</c> → <c>RewardsSet.Offer</c>), or, for an encounter without
    /// rewards, <c>ProceedWithoutRewards</c> waits a second and leaves the fight
    /// (<c>RunManager.ProceedFromTerminalRewardsScreenAsync</c>: map, or back to the parent event).
    /// <c>ProcessPendingLoss</c> (a loss) also clears <c>IsInProgress</c>; the game over screen is pushed
    /// by <c>CreatureCmd.Kill</c> already. A loaded save of a finished fight
    /// (<c>CombatRoom.StartPreFinishedCombat</c>) never sets up a turn state and offers the rewards again.</item>
    /// </list>
    /// So a combat room without a turn state is still starting unless it is pre-finished, and a turn state
    /// that is neither starting nor in progress belongs to a fight that is over. <c>CombatManager.CancelCombat</c>
    /// (<c>Reset</c>) drops the turn state only when the room is exited.
    ///
    /// Returns null while the combat is in progress (the in-combat reasons apply then). Kept free of game
    /// objects - it takes only the values read off them - so it can be tested without a running game.
    /// </summary>
    /// <param name="combatInProgress"><c>CombatManager.IsInProgress</c>.</param>
    /// <param name="hasTurnState"><c>CombatManager.CurrentCombatId</c> is not null (a turn state exists).</param>
    /// <param name="combatStarting"><c>CombatManager.IsStarting</c>.</param>
    /// <param name="roomPreFinished">The current <c>CombatRoom.IsPreFinished</c>.</param>
    internal static string? CombatBoundaryReason(
        bool combatInProgress, bool hasTurnState, bool combatStarting, bool roomPreFinished)
    {
        if (combatInProgress)
            return null;
        if (combatStarting)
            return CombatStartingReason;
        if (!hasTurnState && !roomPreFinished)
            return CombatStartingReason;
        return CombatEndingReason;
    }

    /// <summary>The <c>message</c> of the battle-less combat state for a <see cref="CombatBoundaryReason"/>.</summary>
    /// <param name="reason">The value <see cref="CombatBoundaryReason"/> returned.</param>
    /// <param name="encounterGivesRewards"><c>EncounterModel.ShouldGiveRewards</c> of the room's encounter.</param>
    internal static string CombatBoundaryMessage(string reason, bool encounterGivesRewards)
    {
        if (reason == CombatStartingReason)
            return "Combat is starting. Waiting for the first turn...";
        return encounterGivesRewards
            ? "Combat ended. Waiting for rewards..."
            : "Combat ended. This encounter gives no rewards; waiting to leave the fight...";
    }
}
