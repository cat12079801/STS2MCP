using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace STS2_MCP;

public static partial class McpMod
{
    private const BindingFlags CardSelectFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static bool TryGetCardSelectGrid(NCardGridSelectionScreen screen, out NCardGrid? grid, out string reason)
    {
        grid = null;
        reason = "";
        try
        {
            grid = typeof(NCardGridSelectionScreen).GetField("_grid", CardSelectFields)?.GetValue(screen) as NCardGrid;
            if (grid != null && IsLiveNode(grid)) return true;
            reason = "selection screen has no live, initialized _grid";
        }
        catch (Exception ex)
        {
            reason = $"{ex.GetType().Name}: {ex.Message}";
        }
        return false;
    }

    // Read the owning grid, never the screen's similarly named _cards (empty on
    // combat-pile screens) or its recycled, partially instantiated holders.
    private static bool TryGetCardSelectCandidates(NCardGridSelectionScreen screen,
        out NCardGrid? grid, out CardModel[] candidates, out string reason)
    {
        grid = null;
        candidates = Array.Empty<CardModel>();
        reason = "";
        try
        {
            if (!TryGetCardSelectGrid(screen, out grid, out reason)) return false;
            if (typeof(NCardGrid).GetField("_cards", CardSelectFields)?.GetValue(grid) is not IReadOnlyList<CardModel> cards)
            {
                reason = "grid has no readable _cards list";
                return false;
            }
            var snapshot = new CardModel[cards.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (cards[i] is not { } card)
                {
                    reason = $"grid _cards contains null at index {i}";
                    return false;
                }
                snapshot[i] = card;
            }
            candidates = snapshot;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static List<Dictionary<string, object?>>? BuildCardSelectCardRows(
        NCardGridSelectionScreen screen, HashSet<CardModel>? selection)
    {
        if (!TryGetCardSelectCandidates(screen, out _, out var candidates, out var reason))
        {
            Warn("card_select.cards", reason);
            return null;
        }

        // Overlays can bypass BuildBattleState. Use the existing instance registry
        // only during a live combat with an available state.
        var combatManager = CombatManager.Instance;
        var combatState = combatManager is { IsInProgress: true } ? combatManager.DebugOnlyGetState() : null;
        if (combatState != null) RefreshCardUidRegistry(combatState);

        var rows = new List<Dictionary<string, object?>>(candidates.Length);
        int selectedCount = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            var card = candidates[i];
            var row = BuildCardInfo(card);
            row["index"] = i;
            if (combatState != null)
            {
                row["uid"] = GetStableCardUid(card);
                row["exhaust_on_next_play"] = SafeGetBool(() => card.ExhaustOnNextPlay);
            }
            if (selection != null)
            {
                bool selected = selection.Contains(card);
                row["selected"] = selected;
                if (selected) selectedCount++;
            }
            if (screen is NDeckUpgradeSelectScreen) AttachUpgradePreview(row, card);
            rows.Add(row);
        }
        if (selection != null && selectedCount != selection.Count)
            Warn("card_select.selected", $"candidate selected count {selectedCount} differs from screen selected count {selection.Count}");
        return rows;
    }

    private static MethodInfo? FindCardSelectClickMethod(Type screenType)
    {
        for (Type? type = screenType; type != null && typeof(NCardGridSelectionScreen).IsAssignableFrom(type); type = type.BaseType)
        {
            foreach (var method in type.GetMethods(CardSelectFields | BindingFlags.Public))
            {
                var parameters = method.GetParameters();
                if (method.Name == "OnCardClicked" && !method.IsAbstract && !method.ContainsGenericParameters
                    && method.ReturnType == typeof(void) && parameters.Length == 1 && parameters[0].ParameterType == typeof(CardModel))
                    return method;
            }
        }
        return null;
    }

    private static Dictionary<string, object?> ExecuteSelectGridCard(NCardGridSelectionScreen screen, int index)
    {
        if (!IsCardSelectScreenStillOpen(screen) || !screen.IsInsideTree())
            return Error("Card selection screen is not the live current overlay");
        // Inspect only the owning grid before checking preview/candidates.
        if (!TryGetCardSelectGrid(screen, out var grid, out var reason))
            return Error($"Cannot read card selection grid: {reason}");
        if (!IsNodeVisible(grid) || !grid!.IsInsideTree())
            return Error("Card selection grid is not initialized or visible (it may be peeking)");
        if (FindVisibleCardSelectPreview(screen) != null)
            return Error("A selection preview is open - use confirm_selection to apply it, or cancel_selection to go back to the grid");
        if (!TryGetCardSelectCandidates(screen, out _, out var candidates, out reason))
            return Error($"Cannot read card selection candidates: {reason}");
        if (index < 0 || index >= candidates.Length)
            return Error($"Card index {index} out of range ({candidates.Length} cards available)");
        var method = FindCardSelectClickMethod(screen.GetType());
        if (method == null)
            return Error("No concrete OnCardClicked(CardModel) method on selection screen");

        var card = candidates[index];
        string cardName = SafeGetText(() => card.Title) ?? "unknown";
        var before = GetCardSelectSelection(screen);
        bool? wasSelected = before?.Contains(card);
        try
        {
            // Same endpoint as the game's HolderPressed receiver, once, with the
            // original model. Highlighting and completion are owned by the game.
            method.Invoke(screen, new object[] { card });
        }
        catch (Exception ex)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return Error($"OnCardClicked failed: {cause.GetType().Name}: {cause.Message}. Re-read state before another action");
        }

        // Immediate completion can dispose the screen; do not read its fields again.
        if (!IsCardSelectScreenStillOpen(screen))
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Selected {cardName} - selection complete, screen closed"
            };

        // Combat-pile updates can replace the HashSet during the call.
        var after = GetCardSelectSelection(screen);
        if (!wasSelected.HasValue || after == null)
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Toggling card selection: {cardName}" };
        bool isSelected = after.Contains(card);
        var prefs = GetCardSelectPrefs(screen);
        if (wasSelected.Value == isSelected)
            return Error(isSelected
                ? $"{cardName} is still selected - the screen did not accept the press"
                : prefs.HasValue
                    ? $"{cardName} was not selected - at most {prefs.Value.MaxSelect} card(s) can be selected, deselect one first"
                    : $"{cardName} was not selected - the screen did not accept the press");

        var result = new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"{(isSelected ? "Selected" : "Deselected")} {cardName}",
            ["selected"] = isSelected,
            ["selected_count"] = after.Count
        };
        if (prefs.HasValue)
        {
            result["min_select"] = prefs.Value.MinSelect;
            result["max_select"] = prefs.Value.MaxSelect;
        }
        if (FindVisibleCardSelectPreview(screen) != null) result["preview_showing"] = true;
        return result;
    }

    /// <summary>
    /// The cards the screen currently has selected. Every subclass keeps them in its own
    /// private `_selectedCards` HashSet - the same set OnCardClicked toggles - so this is
    /// the game's own answer, not a guess reconstructed from highlights.
    /// Null means the field could not be read (a renamed field in a future game build).
    /// </summary>
    internal static HashSet<CardModel>? GetCardSelectSelection(NCardGridSelectionScreen screen)
    {
        try
        {
            return GetInstanceFieldValue(screen, "_selectedCards") as HashSet<CardModel>;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// How many cards the screen wants: `_prefs` carries MinSelect/MaxSelect, which is what
    /// the game itself checks before it will complete the selection.
    /// </summary>
    internal static CardSelectorPrefs? GetCardSelectPrefs(NCardGridSelectionScreen screen)
    {
        try
        {
            return GetInstanceFieldValue(screen, "_prefs") as CardSelectorPrefs?;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a selection screen is still the screen being driven. The screens take
    /// themselves off the overlay stack (NOverlayStack.Remove) the moment they complete,
    /// so this going false is the proof that a confirm press actually landed.
    /// </summary>
    private static bool IsCardSelectScreenStillOpen(NCardGridSelectionScreen screen)
        => IsLiveNode(screen) && ReferenceEquals(NOverlayStack.Instance?.Peek(), screen);
}
