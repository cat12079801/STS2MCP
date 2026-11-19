using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace STS2_MCP;

public static partial class McpMod
{
    // "Choice source: FromHand; models ENTROPY_POWER > ...; hook AfterPlayerTurnStart (2 listeners left)". Details in JSON.
    internal static void AppendChoiceSourceMarkdown(StringBuilder sb, Dictionary<string, object?> selection)
    {
        if (selection.GetValueOrDefault("choice_source") is not Dictionary<string, object?> source) return;
        if (source.GetValueOrDefault("known") is not true)
        {
            var reasons = source.GetValueOrDefault("unavailable_reasons") as List<string> ?? new List<string>();
            sb.AppendLine($"Choice source: unknown ({string.Join("; ", reasons)})");
            sb.AppendLine();
            return;
        }
        var parts = new List<string> { $"{source.GetValueOrDefault("selector")}" };
        if (source.GetValueOrDefault("models") is List<Dictionary<string, object?>> models && models.Count > 0)
            parts.Add("models " + string.Join(" > ", models.Select(m => m.GetValueOrDefault("uid") is string uid ? uid : $"{m.GetValueOrDefault("model_id")}")));
        if (source.GetValueOrDefault("card_play") is Dictionary<string, object?> play)
            parts.Add($"card play {play.GetValueOrDefault("uid")} ({(play.GetValueOrDefault("play_index") is int i ? i + 1 : 0)}/{play.GetValueOrDefault("play_count")})");
        if (source.GetValueOrDefault("hook") is Dictionary<string, object?> hook && hook.GetValueOrDefault("name") is string name)
            parts.Add($"hook {name}" + (hook.GetValueOrDefault("remaining_listeners") is System.Collections.ICollection rest ? $" ({rest.Count} listeners left)" : ""));
        sb.AppendLine("Choice source: " + string.Join("; ", parts));
        sb.AppendLine();
    }
}

/// <summary>
/// One card selection the game has opened through <c>CardSelectCmd</c> and not finished yet:
/// the method that opened it, the <c>PlayerChoiceContext</c> it was given (its model stack names
/// the models that started the selection), the choosing player, and what the selection was
/// built from (the cards shown, the combat pile, the hand selection's <c>source</c> model).
/// Free of game types so the contract test can drive it; the game objects are kept as object.
/// </summary>
internal sealed class OpenCardSelection
{
    public required string Selector { get; init; }
    public required object Context { get; init; }
    public required object Player { get; init; }
    public object? CombatState { get; init; }
    public object? HandSource { get; init; }
    public IReadOnlyList<object?>? Candidates { get; init; }
    public object? Pile { get; init; }
    public long Sequence { get; init; }
}

internal static class OpenCardSelections
{
    // A selection whose task never completes (the combat was torn down while it was open) must
    // not pile up for the rest of the run: the oldest records are dropped past this count.
    internal const int Capacity = 32;

    private static readonly object Gate = new();
    private static readonly List<OpenCardSelection> Open = new();
    private static long _next;

    internal static OpenCardSelection Add(string selector, object context, object player, object? combatState,
        object? handSource = null, IReadOnlyList<object?>? candidates = null, object? pile = null)
    {
        lock (Gate)
        {
            var record = new OpenCardSelection
            {
                Selector = selector,
                Context = context,
                Player = player,
                CombatState = combatState,
                HandSource = handSource,
                Candidates = candidates,
                Pile = pile,
                Sequence = ++_next
            };
            // A selection opened in another fight means the old fight's selections can no longer complete.
            if (combatState != null) Open.RemoveAll(r => r.CombatState != null && !ReferenceEquals(r.CombatState, combatState));
            Open.Add(record);
            while (Open.Count > Capacity) Open.RemoveAt(0);
            return record;
        }
    }

    internal static void Remove(OpenCardSelection record)
    {
        lock (Gate) Open.Remove(record);
    }

    /// <summary>
    /// Drops the records of any fight other than <paramref name="liveCombat"/> (all fight records when no fight is
    /// in progress): their tasks never complete once the fight or the run is gone (save and quit mid-selection).
    /// Records opened outside a fight (no combat state) are kept.
    /// </summary>
    internal static void DropOtherCombats(object? liveCombat)
    {
        lock (Gate) Open.RemoveAll(r => r.CombatState != null && !ReferenceEquals(r.CombatState, liveCombat));
    }

    /// <summary>
    /// The entry points that opened the same selection further out: an outer CardSelectCmd method that forwards
    /// to another with the same context (FromHandForDiscard → FromHand, FromCombatPile's 4-argument overload)
    /// leaves an older open record with the same context. Oldest first.
    /// </summary>
    internal static List<string> OuterSelectors(OpenCardSelection record)
    {
        lock (Gate)
            return Open.Where(r => r.Sequence < record.Sequence && ReferenceEquals(r.Context, record.Context)
                    && ReferenceEquals(r.Player, record.Player))
                .OrderBy(r => r.Sequence).Select(r => r.Selector).ToList();
    }

    internal static void Clear()
    {
        lock (Gate) Open.Clear();
    }

    internal static int Count
    {
        get { lock (Gate) return Open.Count; }
    }

    /// <summary>The most recently opened selection that is still open and matches.</summary>
    internal static OpenCardSelection? Latest(Func<OpenCardSelection, bool> match)
    {
        lock (Gate)
        {
            for (int i = Open.Count - 1; i >= 0; i--)
                if (match(Open[i])) return Open[i];
            return null;
        }
    }

    /// <summary>
    /// Which <c>CardSelectCmd</c> methods put up which screen (v0.113.1): <c>FromHand</c> and its
    /// wrappers the hand's select mode, <c>FromChooseACardScreen</c> the choose-a-card screen,
    /// <c>FromCombatPile</c> <c>NCombatPileCardSelectScreen</c>, <c>FromSimpleGrid</c> /
    /// <c>FromSimpleGridForRewards</c> <c>NSimpleCardSelectScreen</c>.
    /// </summary>
    internal static bool SelectorShowsScreen(string screenKind, string selector) => screenKind switch
    {
        "hand" => selector is "FromHand" or "FromHandForDiscard" or "FromHandForUpgrade",
        "choose" => selector == "FromChooseACardScreen",
        "combat_pile" => selector == "FromCombatPile",
        "simple_grid" => selector is "FromSimpleGrid" or "FromSimpleGridForRewards",
        _ => false
    };

    /// <summary>
    /// The screen shows exactly the cards the selection was opened with, in order (reference
    /// equality: the choose-a-card screen holds the same CardModel instances). Null holders are
    /// skipped, as the state's rows skip them.
    /// </summary>
    internal static bool ShowsCandidates(OpenCardSelection record, IReadOnlyList<object?> shown)
    {
        if (record.Candidates == null) return false;
        var visible = shown.Where(card => card != null).ToList();
        return visible.Count == record.Candidates.Count
            && visible.Zip(record.Candidates).All(pair => ReferenceEquals(pair.First, pair.Second));
    }
}
