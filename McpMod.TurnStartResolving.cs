using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// Whether the local player's turn is still starting: block clear, energy reset, the hand
    /// draw and the start-of-turn hooks have not all finished, so the hand, energy and history
    /// read now are mid-way values.
    ///
    /// The draw does not go through the action queue and <c>PlayerActionsDisabled</c> is
    /// already false from the moment the phase becomes <c>Start</c>
    /// (<c>CombatManager.StartTurn</c>), so neither of the older signals sees it. Two public
    /// values of the game do:
    /// <list type="bullet">
    /// <item><c>PlayerCombatState.Phase</c> goes None → Start → AutoPrePlay → Play
    /// (<c>CombatManager.StartTurn</c> / <c>SetupPlayerTurn</c> / <c>RunAutoPrePlayPhase</c>).
    /// Anything before Play is the turn start, including a setup that resumed after a
    /// turn-start choice was answered.</item>
    /// <item><c>ActionQueueSynchronizer.CombatState</c> stays <c>NotPlayPhase</c> ("the enemy
    /// turn and the draw phase of the player's turn") until the last step of
    /// <c>StartTurn</c>, after every player's AutoPrePlay hooks and <c>CheckWinCondition</c>.
    /// It covers the frames between the phase becoming Play and the queues reopening.</item>
    /// </list>
    /// AutoPostPlay / End (and the synchronizer's NotPlayPhase while the player's turn is
    /// ending) are the end of the turn, not its start; the existing <c>player_actions_disabled</c>
    /// reason already covers them, so they return false here.
    ///
    /// Kept free of game objects - it takes only the values read off them - so it can be
    /// reasoned about and tested without a running game.
    /// </summary>
    /// <param name="side">The combat's <c>CurrentSide</c>.</param>
    /// <param name="localPlayerAlive">
    /// False for a dead local player (co-op): <c>StartTurn</c> skips their setup and AutoPrePlay,
    /// so their phase stays Start for the whole turn and must not be read as "still starting".
    /// </param>
    /// <param name="localPhase">The local player's <c>PlayerCombatState.Phase</c>; null when unreadable.</param>
    /// <param name="queueState">The run's <c>ActionQueueSynchronizer.CombatState</c>.</param>
    internal static bool IsLocalPlayerTurnStarting(
        CombatSide side, bool localPlayerAlive, PlayerTurnPhase? localPhase, ActionSynchronizerCombatState queueState)
    {
        if (side != CombatSide.Player)
            return false;

        if (localPhase is PlayerTurnPhase.AutoPostPlay or PlayerTurnPhase.End)
            return false;

        if (localPlayerAlive && localPhase is PlayerTurnPhase.None or PlayerTurnPhase.Start or PlayerTurnPhase.AutoPrePlay)
            return true;

        return queueState == ActionSynchronizerCombatState.NotPlayPhase;
    }
}
