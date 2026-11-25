using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int ChoiceAwaitChainVersion = 1;
    private const string HookListSnapshotHarmonyId = "com.sts2mcp.hooklistsnapshot";
    private static string? _hookListSnapshotError = "not installed";
    private static FieldInfo? _hookListIteratorState;
    private static FieldInfo? _hookListIteratorCombat;

    // The async methods the chain describes (v0.114.0, sts2-ae309f85), with what each await of the method body
    // runs, in the order of the compiler's <>1__state (checked against the IL of each MoveNext): the state a
    // suspended frame holds is the index of the step it waits on; the steps after it run once that one completes.
    private static readonly Dictionary<(Type Type, string Method), string[]> AwaitChainSteps = new()
    {
        [(typeof(Hook), "AfterPlayerTurnStart")] = new[] { "AfterPlayerTurnStartEarly", "AfterPlayerTurnStart", "AfterPlayerTurnStartLate" },
        [(typeof(Hook), "AfterShuffle")] = new[] { "AfterShuffle" },
        [(typeof(Hook), nameof(Hook.BeforeHandDraw))] = AsyncChain.BeforeHandDrawStages,
        [(typeof(CardPileCmd), "DrawInternal")] = new[] { "AfterPreventingDraw", "ShuffleIfNecessary", "AddToHand", "AfterCardDrawn" },
        [(typeof(CardPileCmd), "AutoPlayFromDrawPile")] = new[] { "ShuffleIfNecessary", "AddToPlay", "AutoPlay" },
        [(typeof(CardPileCmd), "Shuffle")] = new[] { "AddToDrawPile", "Wait", "Tween", "CustomScaledWait", "AfterShuffle" },
        [(typeof(CardPileCmd), "ShuffleIfNecessary")] = new[] { "ShuffleFtueCheck", "Shuffle" },
        [(typeof(CombatManager), "SetupPlayerTurn")] = new[] { "AfterEnergyReset", "BeforeHandDraw", "AfterModifyingHandDraw", "HandDraw", "AfterPlayerTurnStart" },
        [(typeof(CombatManager), "RunAutoPrePlayPhase")] = new[] { "SetupPlayerTurn", "CheckForEmptyHand", "AfterAutoPrePlayPhaseEntered" },
    };

    // The async method a step awaits directly, where the chain can check it (CardPileCmd.Draw returns
    // DrawInternal's Task unchanged): a frame found waiting there must be this method, or the table is stale.
    private static readonly Dictionary<(Type Type, string Method, string Step), (Type Type, string Method)> AwaitChainCallees = new()
    {
        [(typeof(CardPileCmd), "DrawInternal", "ShuffleIfNecessary")] = (typeof(CardPileCmd), "ShuffleIfNecessary"),
        [(typeof(CardPileCmd), "AutoPlayFromDrawPile", "ShuffleIfNecessary")] = (typeof(CardPileCmd), "ShuffleIfNecessary"),
        [(typeof(CardPileCmd), "ShuffleIfNecessary", "Shuffle")] = (typeof(CardPileCmd), "Shuffle"),
        [(typeof(CardPileCmd), "Shuffle", "AfterShuffle")] = (typeof(Hook), "AfterShuffle"),
        [(typeof(CombatManager), "SetupPlayerTurn", "BeforeHandDraw")] = (typeof(Hook), nameof(Hook.BeforeHandDraw)),
        [(typeof(CombatManager), "SetupPlayerTurn", "HandDraw")] = (typeof(CardPileCmd), "DrawInternal"),
        [(typeof(CombatManager), "SetupPlayerTurn", "AfterPlayerTurnStart")] = (typeof(Hook), "AfterPlayerTurnStart"),
        [(typeof(CombatManager), "RunAutoPrePlayPhase", "SetupPlayerTurn")] = (typeof(CombatManager), "SetupPlayerTurn"),
    };

    private static readonly Dictionary<(Type Type, string Method), string> AwaitChainKinds = new()
    {
        [(typeof(Hook), "AfterPlayerTurnStart")] = "hook",
        [(typeof(Hook), "AfterShuffle")] = "hook",
        [(typeof(Hook), nameof(Hook.BeforeHandDraw))] = "hook",
        [(typeof(CardPileCmd), "DrawInternal")] = "draw",
        [(typeof(CardPileCmd), "AutoPlayFromDrawPile")] = "auto_play_from_draw_pile",
        [(typeof(CardPileCmd), "Shuffle")] = "shuffle",
        [(typeof(CardPileCmd), "ShuffleIfNecessary")] = "shuffle_if_necessary",
        [(typeof(CombatManager), "SetupPlayerTurn")] = "setup_player_turn",
        [(typeof(CombatManager), "RunAutoPrePlayPhase")] = "auto_pre_play_phase",
    };

    // Read-only patches for the listener list a Hook stage took (HookListSnapshots): Hook.AfterPlayerTurnStart /
    // AfterShuffle / BeforeHandDraw mark the fight while they run, and the first MoveNext of CombatState.IterateHookListeners (it
    // builds the list there) notes which cards are in a draw pile at that moment. Nothing is skipped or changed.
    private static void InstallHookListSnapshotTracking()
    {
        var harmony = new Harmony(HookListSnapshotHarmonyId);
        try
        {
            foreach (var method in new[] { nameof(Hook.AfterPlayerTurnStart), nameof(Hook.AfterShuffle), nameof(Hook.BeforeHandDraw) })
            {
                var parameters = method == nameof(Hook.BeforeHandDraw)
                    ? new[] { typeof(ICombatState), typeof(Player), typeof(PlayerChoiceContext) }
                    : new[] { typeof(ICombatState), typeof(PlayerChoiceContext), typeof(Player) };
                var original = AccessTools.DeclaredMethod(typeof(Hook), method, parameters)
                    ?? throw new MissingMethodException(nameof(Hook), method);
                harmony.Patch(original,
                    prefix: new HarmonyMethod(typeof(McpMod), nameof(SnapshotHookRunStarting)),
                    postfix: new HarmonyMethod(typeof(McpMod), nameof(SnapshotHookRunReturned)));
            }
            var iterators = typeof(CombatState).GetNestedTypes(BindingFlags.NonPublic)
                .Where(t => t.Name.StartsWith("<IterateHookListeners>d__", StringComparison.Ordinal)).ToList();
            if (iterators.Count != 1) throw new MissingMemberException(nameof(CombatState), $"<IterateHookListeners>d__* ({iterators.Count} found)");
            var iterator = iterators[0];
            _hookListIteratorState = AccessTools.Field(iterator, "<>1__state") ?? throw new MissingFieldException(iterator.Name, "<>1__state");
            _hookListIteratorCombat = AccessTools.Field(iterator, "<>4__this") ?? throw new MissingFieldException(iterator.Name, "<>4__this");
            var moveNext = AccessTools.DeclaredMethod(iterator, "MoveNext") ?? throw new MissingMethodException(iterator.Name, "MoveNext");
            harmony.Patch(moveNext, prefix: new HarmonyMethod(typeof(McpMod), nameof(SnapshotHookListBuilding)));
            _hookListSnapshotError = null;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll(HookListSnapshotHarmonyId); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Hook list snapshot cleanup failed: {cleanupError.Message}"); }
            _hookListSnapshotError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] Hook list snapshot unavailable: {ex}");
        }
    }

    private static void SnapshotHookRunStarting(ICombatState combatState, out HookListSnapshots.Run? __state)
    {
        __state = null;
        try { __state = HookListSnapshots.Begin(combatState); }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Hook list snapshot begin failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void SnapshotHookRunReturned(Task __result, HookListSnapshots.Run? __state)
    {
        if (__state == null) return;
        if (__result == null || __result.IsCompleted) { HookListSnapshots.End(__state); return; }
        __result.ContinueWith(_ => HookListSnapshots.End(__state), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // The first MoveNext (<>1__state 0) builds the list from the piles as they are now: note the draw-pile cards.
    private static void SnapshotHookListBuilding(object __instance)
    {
        if (!HookListSnapshots.AnyOpen) return;
        try
        {
            // Only the first MoveNext builds the list: every later one returns before any lock.
            if (_hookListIteratorState?.GetValue(__instance) is not 0) return;
            if (_hookListIteratorCombat?.GetValue(__instance) is not CombatState combat || !HookListSnapshots.IsActiveFor(combat)) return;
            HookListSnapshots.Record(__instance, combat.Players
                .SelectMany(p => p.PlayerCombatState?.DrawPile.Cards ?? (IEnumerable<CardModel>)Array.Empty<CardModel>()));
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Hook list snapshot record failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// The async methods waiting on the open selection, inside out (<c>choice_source.await_chain</c>): each one's
    /// declaring type and method, and for the game methods that decide what runs after the choice (the Hook stage
    /// running <c>models[0]</c>, the shuffle and the draw or auto-play reservation that shuffled, the turn start) the
    /// step it waits on, the steps after it and the public values of its loop. Read from the live tasks only.
    /// </summary>
    private static Dictionary<string, object?> BuildChoiceAwaitChain(OpenCardSelection record, List<PlayerChoiceContext> contexts,
        List<AbstractModel> models)
    {
        var result = new Dictionary<string, object?>
        {
            ["version"] = ChoiceAwaitChainVersion, ["known"] = false, ["frames"] = null, ["end"] = null, ["shuffle_origin"] = null
        };
        var reasons = new List<string>();
        try
        {
            if (record.SelectionTask is not Task selection)
                throw new InvalidOperationException("the selection's Task was not recorded");
            var (frames, end, inconsistent) = AsyncChain.Walk(selection);
            result["end"] = end;
            // A box registered on the task but suspended elsewhere: the chain may be cut short, so it is not known.
            if (inconsistent) reasons.Add($"await chain inconsistent: {end}");
            var rows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < frames.Count; i++)
                rows.Add(BuildAwaitChainFrame(i, frames, contexts, models, record, reasons));
            bool openedByToolbox = record.Opener is Dictionary<string, object?> opener
                && Equals(opener.GetValueOrDefault("model_type"), nameof(Toolbox))
                && Equals(opener.GetValueOrDefault("method"), nameof(Toolbox.BeforeHandDraw));
            bool hasBeforeDrawListener = frames.Any(f => typeof(AbstractModel).IsAssignableFrom(f.DeclaringType)
                && AsyncChain.BeforeHandDrawStages.Contains(f.Method));
            if ((openedByToolbox || hasBeforeDrawListener)
                && !frames.Any(f => f.DeclaringType == typeof(Hook) && f.Method == nameof(Hook.BeforeHandDraw)))
                reasons.Add("the BeforeHandDraw listener has no Hook.BeforeHandDraw frame in the selection's await chain");
            result["frames"] = rows;
            result["shuffle_origin"] = AwaitChainShuffleOrigin(rows);
            result["known"] = reasons.Count == 0;
        }
        catch (Exception ex)
        {
            result["frames"] = null;
            reasons.Add($"{ex.GetType().Name}: {ex.Message}");
            Warn("choice_source.await_chain", ex);
        }
        if (reasons.Count > 0) result["unavailable_reasons"] = reasons;
        return result;
    }

    private static Dictionary<string, object?> BuildAwaitChainFrame(int index, List<AsyncFrame> frames,
        List<PlayerChoiceContext> contexts, List<AbstractModel> models, OpenCardSelection record, List<string> reasons)
    {
        var frame = frames[index];
        var row = new Dictionary<string, object?>
        {
            ["index"] = index,
            ["type"] = frame.DeclaringType?.Name,
            ["method"] = frame.Method,
            ["kind"] = null
        };
        var key = (frame.DeclaringType ?? typeof(object), frame.Method);
        // AutoPlayFromDrawPile's 5-argument overload only awaits the 6-argument one: describe the latter only.
        bool sixArgAutoPlay = key == (typeof(CardPileCmd), "AutoPlayFromDrawPile")
            && frame.StateMachineType.GetField("cardModification") != null;
        if (!AwaitChainSteps.TryGetValue(key, out var steps) || (key.Item2 == "AutoPlayFromDrawPile" && !sixArgAutoPlay)) return row;

        row["kind"] = AwaitChainKinds[key];
        if (row["kind"] is "hook") row["listener"] = UnobservedHookListener();
        string label = $"{key.Item1.Name}.{key.Item2}";
        if (frame.State < 0 || frame.State >= steps.Length)
        {
            reasons.Add($"{label} is at state {frame.State}, not one of its {steps.Length} awaits");
            return row;
        }
        string step = steps[frame.State];
        row["awaiting"] = step;
        row["following"] = steps.Skip(frame.State + 1).ToList();
        if (index > 0 && AwaitChainCallees.TryGetValue((key.Item1, key.Item2, step), out var callee)
            && (frames[index - 1].DeclaringType != callee.Type || frames[index - 1].Method != callee.Method))
            reasons.Add($"{label} waits on {step} but the frame inside it is {frames[index - 1].DeclaringType?.Name}.{frames[index - 1].Method}");

        try { AddAwaitChainFrameDetails(row, index, frames, steps, step, label, contexts, models, record, reasons); }
        catch (Exception ex) { reasons.Add($"frame {index} ({label}): {ex.GetType().Name}: {ex.Message}"); }
        return row;
    }

    // The public values of a described frame: who it runs for, and its loop's place.
    private static void AddAwaitChainFrameDetails(Dictionary<string, object?> row, int index, List<AsyncFrame> frames,
        string[] steps, string step, string label, List<PlayerChoiceContext> contexts, List<AbstractModel> models,
        OpenCardSelection record, List<string> reasons)
    {
        var frame = frames[index];
        var sm = frame.StateMachine;
        switch (row["kind"])
        {
            case "hook":
                bool beforeHandDraw = frame.Method == nameof(Hook.BeforeHandDraw);
                var player = AsyncChain.Field(sm, frame.Method == "AfterShuffle" ? "shuffler" : "player") as Player;
                row["player_id"] = player?.NetId.ToString();
                var context = AsyncChain.Field(sm, beforeHandDraw ? "playerChoiceContext" : "choiceContext");
                int contextIndex = contexts.FindIndex(c => ReferenceEquals(c, context));
                row["context_index"] = contextIndex >= 0 ? contextIndex : null;
                var model = AsyncChain.Hoisted(sm, "model") as AbstractModel
                    ?? throw new InvalidOperationException($"{label} runs no listener");
                int modelIndex = models.FindIndex(m => ReferenceEquals(m, model));
                row["model_index"] = modelIndex >= 0 ? modelIndex : null;
                if (beforeHandDraw)
                {
                    ValidateBeforeHandDrawFrame(index, frames, step, record, player, contexts, contextIndex, model, modelIndex);
                }
                if (index > 0 && steps.Contains(frames[index - 1].Method) && frames[index - 1].Method != step)
                    reasons.Add($"{label} is at {step} but its listener runs {frames[index - 1].Method}");
                row["listener"] = BuildAwaitChainListener(frame, step, model, record.CombatState, reasons);
                break;
            case "draw":
                row["player_id"] = (AsyncChain.Field(sm, "player") as Player)?.NetId.ToString();
                row["from_hand_draw"] = AsyncChain.Field(sm, "fromHandDraw") is true;
                if (frame.State == 0)
                {
                    // Hook.ShouldDraw refused the draw: AfterPreventingDraw runs, then the draw returns empty.
                    row["draws_requested"] = null;
                    row["draw_index"] = null;
                    row["remaining_draws"] = 0;
                    break;
                }
                int requested = AsyncChain.Hoisted(sm, "drawsRequested") is int r ? r : throw new InvalidCastException("drawsRequested");
                int drawIndex = AsyncChain.Hoisted(sm, "i") is int d ? d : throw new InvalidCastException("i");
                row["draws_requested"] = requested;
                row["draw_index"] = drawIndex;
                // ShuffleIfNecessary comes before this draw takes its card; AddToHand / AfterCardDrawn after it.
                row["remaining_draws"] = Math.Max(0, requested - drawIndex - (frame.State == 1 ? 0 : 1));
                break;
            case "auto_play_from_draw_pile":
                var owner = AsyncChain.Field(sm, "player") as Player;
                row["player_id"] = owner?.NetId.ToString();
                int count = AsyncChain.Field(sm, "count") is int c ? c : throw new InvalidCastException("count");
                string position = AsyncChain.Field(sm, "position")?.ToString()?.ToLowerInvariant() ?? throw new InvalidCastException("position");
                bool forceExhaust = AsyncChain.Field(sm, "forceExhaust") is true;
                row["count"] = count;
                row["position"] = position;
                row["force_exhaust"] = forceExhaust;
                if (frame.State == 2)
                {
                    // All reservations are made; the cards are played one by one (auto_play_dispatch rows).
                    row["reserve_index"] = null;
                    row["remaining_reservations"] = 0;
                }
                else
                {
                    int reserveIndex = AsyncChain.Hoisted(sm, "i") is int ri ? ri : throw new InvalidCastException("i");
                    row["reserve_index"] = reserveIndex;
                    row["remaining_reservations"] = Math.Max(0, count - reserveIndex - (frame.State == 0 ? 0 : 1));
                }
                row["reservation_call_id"] = owner == null ? null
                    : AwaitChainReservationCallId(AsyncChain.Field(sm, "choiceContext"), owner, count, position, forceExhaust, reasons);
                break;
            default:
                row["player_id"] = (AsyncChain.Field(sm, "player") as Player)?.NetId.ToString();
                break;
        }
    }

    private static void ValidateBeforeHandDrawFrame(int index, List<AsyncFrame> frames, string step, OpenCardSelection record,
        Player? player, List<PlayerChoiceContext> contexts, int contextIndex, AbstractModel model, int modelIndex)
    {
        if (record.Attempt == null || !IsLiveCombatAttempt(record.Attempt))
            throw new InvalidOperationException("the selection has no matching live combat attempt");
        var combat = record.Attempt.Combat;
        if (player == null || contextIndex < 0 || modelIndex < 0 || !ReferenceEquals(contexts[contextIndex].ModelStack?.FirstOrDefault(), model)
            || !ReferenceEquals(AsyncChain.Field(frames[index].StateMachine, "combatState"), combat))
            throw new InvalidOperationException("Hook.BeforeHandDraw does not match the selection's combat, player or model stack");
        if (model is Toolbox toolbox && (!ReferenceEquals(toolbox.Owner, player) || !ReferenceEquals(record.Player, player)))
            throw new InvalidOperationException("Toolbox owner, hand draw player and chooser differ");
        var declarer = HookOverrideDeclarer(model.GetType(), step,
            new[] { typeof(Player), typeof(PlayerChoiceContext), typeof(ICombatState) })
            ?? throw new InvalidOperationException($"the running model does not override {step}");
        AsyncChain.ValidateBeforeHandDraw(frames, index, typeof(CombatManager), step, declarer, model, player, contexts[contextIndex], combat);
    }

    private static int? AwaitChainReservationCallId(object? context, Player owner, int count, string position, bool forceExhaust, List<string> reasons)
    {
        try
        {
            AutoPlayReservations.ThrowIfUnavailable();
            var ids = AutoPlayReservations.OpenCallIds(context, owner, count, position, forceExhaust);
            if (ids.Count == 1) return ids[0];
            reasons.Add($"{ids.Count} open auto-play reservation records match this AutoPlayFromDrawPile call");
        }
        catch (Exception ex) { reasons.Add($"auto-play reservation: {ex.GetType().Name}: {ex.Message}"); }
        return null;
    }

    // Which call shuffled: the frame awaiting the shuffle is ShuffleIfNecessary (from a draw, from an auto-play
    // reservation, or from another caller: KiChant, ForegoneConclusionPower) or the caller itself (an explicit
    // CardPileCmd.Shuffle: Reboot, KiChant, BottledPotential).
    private static string? AwaitChainShuffleOrigin(List<Dictionary<string, object?>> rows)
    {
        int shuffle = rows.FindIndex(r => Equals(r["kind"], "shuffle"));
        if (shuffle < 0 || shuffle + 1 >= rows.Count) return null;
        if (!Equals(rows[shuffle + 1]["kind"], "shuffle_if_necessary")) return "explicit";
        if (shuffle + 2 >= rows.Count) return null;
        return rows[shuffle + 2]["kind"] switch
        {
            "draw" => "draw",
            "auto_play_from_draw_pile" => "auto_play_from_draw_pile",
            _ => "if_necessary"
        };
    }

    /// <summary>
    /// The list the Hook stage took when it began (<c>Hook.IterateCombatHookListeners</c> →
    /// <c>CombatState.IterateHookListeners</c>, a List&lt;AbstractModel&gt; walked by its Enumerator), the place of the
    /// running listener in it and the listeners after it, read from the running enumerators without moving them.
    /// </summary>
    private static Dictionary<string, object?> BuildAwaitChainListener(AsyncFrame hook, string stage, AbstractModel model, object? expectedCombat,
        List<string> reasons)
    {
        var row = UnobservedHookListener();
        if (_hookListSnapshotError != null)
        {
            reasons.Add($"hook list snapshot unavailable: {_hookListSnapshotError}");
            return row;
        }
        var (list, next, inner) = HookListSnapshots.ReadCursor(hook, model, typeof(Hook), typeof(CombatState));
        var combat = AsyncChain.Field(inner, "<>4__this") as CombatState ?? throw new InvalidOperationException("IterateHookListeners has no CombatState");
        if (!ReferenceEquals(combat, expectedCombat))
            throw new InvalidOperationException("IterateHookListeners is enumerating a different combat");

        var drawAtStart = HookListSnapshots.DrawPileCardsAtStart(inner);
        if (drawAtStart == null)
        {
            reasons.Add($"the draw piles when {stage}'s listener list was built were not recorded");
            return row;
        }
        if (ListenerCard(model) is { } own && (drawAtStart.Contains(own) || DrawPileOf(model) != null))
        {
            reasons.Add($"{model.Id.Entry} was a draw-pile listener; its place would reveal the draw order");
            return row;
        }
        int modSubscribers = CountModHookSubscribers("_runHookSubscribers") + CountModHookSubscribers("_combatHookSubscribers");
        if (modSubscribers > 0)
        {
            reasons.Add($"{modSubscribers} ModHelper hook subscriber(s) registered; the listeners after the list cannot be tied to public state");
            return row;
        }

        row["position"] = next - 1;
        row["count"] = list.Count;
        var parameters = typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == stage).GetParameters().Select(p => p.ParameterType).ToArray();
        var contains = typeof(CombatState).GetMethod("Contains", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(AbstractModel) }, null)
            ?? throw new MissingMethodException(nameof(CombatState), "Contains(AbstractModel)");
        var drawRanks = new Dictionary<CardModel, int>(ReferenceEqualityComparer.Instance);
        foreach (var player in combat.Players)
        {
            int rank = 0;
            if (player.PlayerCombatState is { } pcs)
                foreach (var card in OrderPileCardsForDisplay(pcs.DrawPile.Cards, PileType.Draw))
                    drawRanks[card] = rank++;
        }
        var rest = new List<object>();
        for (int i = next; i < list.Count; i++) rest.Add(list[i]!);
        var ordered = HookListSnapshots.OrderRemaining(rest,
            m => HookOverrideDeclarer(m.GetType(), stage, parameters) != null,
            m => contains.Invoke(combat, new[] { m }) is true,
            m => ListenerCard((AbstractModel)m),
            drawAtStart,
            m =>
            {
                // Public order only: the draw pile's display order, then the uid; the listener kind breaks ties.
                var card = ListenerCard((AbstractModel)m)!;
                int kind = m is CardModel ? 0 : m is AfflictionModel ? 1 : 2;
                return drawRanks.TryGetValue(card, out int rank) ? (0, rank, "", kind) : (1, 0, GetStableCardUid(card), kind);
            });
        var rows = new List<Dictionary<string, object?>>(ordered.Count);
        foreach (var (listener, order, groupStart) in ordered)
        {
            var model2 = (AbstractModel)listener;
            var r = BuildHookListenerRow(model2, reasons);
            r["declared_by"] = HookOverrideDeclarer(model2.GetType(), stage, parameters)!.Name;
            r["order"] = order;
            if (groupStart is int start)
            {
                r["order_group"] = "draw_pile_at_stage_start";
                r["order_group_start"] = start;
            }
            rows.Add(r);
        }
        row["remaining_listeners"] = rows;
        return row;
    }

    private static Dictionary<string, object?> UnobservedHookListener() =>
        new() { ["position"] = null, ["count"] = null, ["remaining_listeners"] = null };
}
