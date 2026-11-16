using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace STS2_MCP;

/// <summary>
/// Whether a card selection overlay is actually on screen, and the player's own way back to it.
///
/// The game hides the top overlay instead of closing it when something covers the stack:
/// NOverlayStack.Push calls AfterOverlayHidden on a screen pushed while StackIsCovered
/// (NMapScreen.IsOpen or NCapstoneContainer.InUse), and the map's Opened signal runs
/// NOverlayStack.HideOverlays. The screen stays on top of the stack (state reports
/// card_select) but is invisible. Closing the map (its back arrow, NMapScreen.OnBackButtonPressed
/// -> Close -> Closed -> NOverlayStack.ShowOverlays -> AfterOverlayShown) brings it back.
/// In combat the screen's own NPeekButton can also hide its grid (SetPeeking).
/// </summary>
public static partial class McpMod
{
    internal readonly record struct CardSelectVisibility(bool Visible, string? HiddenBy, bool CanReveal, string? Detail);

    private static CardSelectVisibility GetCardSelectVisibility(NCardGridSelectionScreen screen)
    {
        bool visible = false;
        if (IsNodeVisible(screen) && TryGetCardSelectGrid(screen, out var grid, out _))
            visible = IsNodeVisible(grid);
        return ClassifyCardSelectVisibility(visible, GetCardSelectPeekButton(screen));
    }

    private static CardSelectVisibility GetCardSelectVisibility(NChooseACardSelectionScreen screen)
    {
        var peek = GetCardSelectPeekButton(screen);
        bool visible = IsNodeVisible(screen) && peek is not { IsPeeking: true };
        return ClassifyCardSelectVisibility(visible, peek);
    }

    private static NPeekButton? GetCardSelectPeekButton(object screen)
    {
        try
        {
            return GetInstanceFieldValue(screen, "_peekButton") as NPeekButton;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CardSelectVisibility ClassifyCardSelectVisibility(bool visible, NPeekButton? peek)
    {
        if (visible) return new(true, null, false, null);

        // A capstone (deck view, compendium, ...) covers the stack too, and closing the map
        // would not uncover it. Leave it to the player.
        if (NCapstoneContainer.Instance?.InUse == true)
            return new(false, "capstone", false, "a capstone screen (deck view etc.) covers the overlay stack");

        var map = NMapScreen.Instance;
        if (map is { IsOpen: true })
        {
            if (map.IsTraveling)
                return new(false, "map", false, "the map is traveling to a node");
            var back = GetMapBackButton(map);
            if (back is not { IsEnabled: true })
                return new(false, "map", false, "the map's back button is disabled, so the player cannot close the map either");
            return new(false, "map", true, null);
        }

        if (peek is { IsPeeking: true })
            return new(false, "peek", true, null);

        return new(false, "unknown", false, "the selection screen is not visible for an unrecognized reason");
    }

    private static NBackButton? GetMapBackButton(NMapScreen map)
    {
        try
        {
            return GetInstanceFieldValue(map, "_backButton") as NBackButton;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// State fields shared by the grid and choose-a-card screens.
    /// </summary>
    private static void AddCardSelectVisibilityFields(Dictionary<string, object?> state, CardSelectVisibility visibility)
    {
        state["screen_visible"] = visibility.Visible;
        state["hidden_by"] = visibility.HiddenBy;
        state["reveal_on_action"] = !visibility.Visible && visibility.CanReveal;
    }

    private static Dictionary<string, object?>? RevealCardSelectScreen(NCardGridSelectionScreen screen, List<string> revealed)
        => RevealCardSelectScreen(() => GetCardSelectVisibility(screen), GetCardSelectPeekButton(screen), revealed);

    private static Dictionary<string, object?>? RevealCardSelectScreen(NChooseACardSelectionScreen screen, List<string> revealed)
        => RevealCardSelectScreen(() => GetCardSelectVisibility(screen), GetCardSelectPeekButton(screen), revealed);

    /// <summary>
    /// Bring a hidden selection screen back the way the player would, before a selection
    /// action presses anything on it. The map is closed by pressing its own back button
    /// (only while that button is enabled), and a peek is ended by the peek button's
    /// SetPeeking(false) - the same method its release handler toggles. Returns null when the
    /// screen is visible (or became visible), or an error that leaves the screen untouched.
    /// </summary>
    private static Dictionary<string, object?>? RevealCardSelectScreen(Func<CardSelectVisibility> read, NPeekButton? peek, List<string> revealed)
    {
        // Map first (it can cover a peeking screen), then the peek. Each cover is undone at
        // most once; a cover that is still there afterwards is reported, not pressed again.
        while (true)
        {
            var visibility = read();
            if (visibility.Visible) return null;
            if (!visibility.CanReveal || revealed.Contains(visibility.HiddenBy!))
            {
                string why = !visibility.CanReveal
                    ? visibility.Detail ?? "the API cannot reveal it"
                    : "it stayed hidden after the reveal";
                return Error($"Card selection screen is hidden (hidden_by: {visibility.HiddenBy}) - {why}. " +
                    "Nothing was pressed on it; re-read state");
            }

            try
            {
                if (visibility.HiddenBy == "map")
                {
                    var back = NMapScreen.Instance is { } map ? GetMapBackButton(map) : null;
                    if (back is not { IsEnabled: true })
                        return Error("Card selection screen is hidden behind the map and the map's back button is not enabled. Nothing was pressed on it; re-read state");
                    back.ForceClick();
                    revealed.Add("map");
                }
                else if (visibility.HiddenBy == "peek" && peek != null)
                {
                    peek.SetPeeking(false);
                    revealed.Add("peek");
                }
                else
                {
                    return Error($"Card selection screen is hidden (hidden_by: {visibility.HiddenBy}). Nothing was pressed on it; re-read state");
                }
            }
            catch (Exception ex)
            {
                return Error($"Revealing the hidden card selection screen failed: {ex.GetType().Name}: {ex.Message}. Re-read state before another action");
            }
        }
    }

    private static Dictionary<string, object?> WithRevealed(Dictionary<string, object?> result, List<string> revealed)
    {
        if (revealed.Count > 0) result["revealed"] = revealed.ToArray();
        return result;
    }
}
