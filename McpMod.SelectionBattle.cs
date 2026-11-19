using System;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// Whether a selection state is one that takes a decision from the player: every
    /// <c>state_type</c> ending in <c>_select</c> (<c>hand_select</c>, <c>card_select</c> for the
    /// grid and the choose-a-card screens, <c>bundle_select</c>, <c>relic_select</c>, and any later one).
    /// Decided by the state type, not by the screen class, so a new selection screen mapped to an
    /// existing state type (as <c>NCombatPileCardSelectScreen</c> is to <c>card_select</c>) is covered.
    /// </summary>
    internal static bool IsSelectionStateType(string? stateType)
        => stateType != null && stateType.EndsWith("_select", StringComparison.Ordinal);

    /// <summary>
    /// Whether the builder attaches <c>battle</c> to this state: a selection state shown while a fight is
    /// in progress in the current combat room, which carries the same battle snapshot as the fight's own
    /// screen. Never outside a live fight (no fabricated battle before it starts or after it ends), never
    /// to a non-selection screen (map, menus, game over, unhandled overlays), and never twice.
    /// Kept free of game types so the contract test can run it without the game.
    /// </summary>
    internal static bool ShouldAttachSelectionBattle(
        string? stateType, bool hasBattle, bool inCombatRoom, bool combatInProgress)
        => !hasBattle && inCombatRoom && combatInProgress && IsSelectionStateType(stateType);
}
