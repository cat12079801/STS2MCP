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
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int ChoiceSourceVersion = 1;
    private const string ChoiceSourceHarmonyId = "com.sts2mcp.choicesource";
    private static string? _choiceSourceTrackingError = "not installed";

    // The CardSelectCmd entry points that take a PlayerChoiceContext (v0.114.0). FromCombatPile's
    // 4-argument overload and FromHandForDiscard forward to another patched method: both are patched
    // so a JIT-inlined forwarder still leaves a record (the inner, later record wins).
    private static readonly (string Method, Type[] Parameters, bool SingleCard)[] ChoiceSourcePatchTargets =
    {
        ("FromChooseACardScreen", new[] { typeof(PlayerChoiceContext), typeof(IReadOnlyList<CardModel>), typeof(Player), typeof(bool) }, true),
        ("FromSimpleGridForRewards", new[] { typeof(PlayerChoiceContext), typeof(List<CardCreationResult>), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs) }, false),
        ("FromSimpleGrid", new[] { typeof(PlayerChoiceContext), typeof(IReadOnlyList<CardModel>), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs) }, false),
        ("FromCombatPile", new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs) }, false),
        ("FromCombatPile", new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs), typeof(Func<CardModel, bool>) }, false),
        ("FromHand", new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs), typeof(Func<CardModel, bool>), typeof(AbstractModel) }, false),
        ("FromHandForDiscard", new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs), typeof(Func<CardModel, bool>), typeof(AbstractModel) }, false),
        ("FromHandForUpgrade", new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(AbstractModel) }, true),
    };

    // The Hook methods that push each listener onto the PlayerChoiceContext's model stack while it runs
    // (Hook.cs: "choiceContext.PushModel(model); await model.X(...)"), with the list each one iterates:
    // "combat" = Hook.IterateCombatHookListeners (nothing while CombatManager.ShouldSkipCombatEffect),
    // "combat_all" = ICombatState.IterateHookListeners(), "run" = IRunState.IterateHookListeners(combat).
    private static readonly (string Method, string Scope)[] ChoiceSourceHooks =
    {
        ("AfterCardDiscarded", "combat"), ("AfterCardDrawnEarly", "combat"), ("AfterCardDrawn", "combat"),
        ("AfterCardExhausted", "combat"), ("AfterCardPlayed", "combat_all"), ("AfterCardPlayedLate", "combat_all"),
        ("AfterDamageGiven", "combat_all"), ("BeforeDamageReceived", "run"), ("AfterDamageReceived", "run"),
        ("AfterDamageReceivedLate", "run"), ("AfterFlush", "combat"), ("BeforeHandDraw", "combat"),
        ("BeforeHandDrawLate", "combat"), ("AfterHandEmptied", "combat"), ("AfterOrbChanneled", "combat"),
        ("AfterPlayerTurnStartEarly", "combat"), ("AfterPlayerTurnStart", "combat"), ("AfterPlayerTurnStartLate", "combat"),
        ("AfterAutoPostPlayPhaseEntered", "combat"), ("AfterAutoPrePlayPhaseEnteredEarly", "combat"),
        ("AfterAutoPrePlayPhaseEntered", "combat"), ("AfterAutoPrePlayPhaseEnteredLate", "combat"),
        ("AfterShuffle", "combat"), ("AfterSummon", "combat"),
    };

    private static void InstallChoiceSourceTracking()
    {
        var harmony = new Harmony(ChoiceSourceHarmonyId);
        try
        {
            foreach (var (method, parameters, singleCard) in ChoiceSourcePatchTargets)
            {
                var original = AccessTools.DeclaredMethod(typeof(CardSelectCmd), method, parameters)
                    ?? throw new MissingMethodException(nameof(CardSelectCmd), method);
                harmony.Patch(original,
                    prefix: new HarmonyMethod(typeof(McpMod), nameof(ChoiceSelectionOpened)),
                    postfix: new HarmonyMethod(typeof(McpMod), singleCard ? nameof(ChoiceSelectionTaskSingle) : nameof(ChoiceSelectionTaskMany)));
            }
            _choiceSourceTrackingError = null;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll(ChoiceSourceHarmonyId); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Choice source cleanup failed: {cleanupError.Message}"); }
            _choiceSourceTrackingError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] Choice source tracking unavailable: {ex}");
        }
        // The listener lists of AfterPlayerTurnStart / AfterShuffle for await_chain (patched on their own:
        // failing costs only the listener part of the chain).
        InstallHookListSnapshotTracking();
    }

    // Read-only prefix: records the arguments, never skips or changes the call.
    private static void ChoiceSelectionOpened(MethodBase __originalMethod, object[] __args, out OpenCardSelection? __state)
    {
        __state = null;
        try
        {
            var names = __originalMethod.GetParameters().Select(p => p.Name).ToList();
            object? Arg(string name) => names.IndexOf(name) is int i and >= 0 ? __args[i] : null;
            if (Arg("context") is not PlayerChoiceContext context || Arg("player") is not Player player) return;
            IReadOnlyList<object?>? candidates = (Arg("cards") ?? Arg("cardsIn")) switch
            {
                IEnumerable<CardCreationResult> results => results.Select(r => (object?)r.Card).ToList(),
                IEnumerable<CardModel> cards => cards.Cast<object?>().ToList(),
                _ => null
            };
            // The fight in progress (null on the reward screen after a fight, where the creature still points at it).
            var combat = GetLiveCombatStateForSelection();
            __state = OpenCardSelections.Add(__originalMethod.Name, context, player, combat,
                handSource: Arg("source"), candidates: candidates, pile: Arg("pile"), attempt: CaptureCombatAttempt(combat, liveOnly: true),
                chooserPlayerId: player.NetId.ToString());
            // decision_context.opener and the combat_log seq it opened at (McpMod.DecisionContext.cs).
            AttachSelectionOpener(__state);
            // What a hand / combat-pile selection offers, read now (the selection moves cards once it closes). The
            // pile's cards in its order (hand and discard / exhaust orders are on screen); never a draw pile's order.
            var offeredPile = Arg("pile") as CardPile
                ?? (__originalMethod.Name.StartsWith("FromHand", StringComparison.Ordinal) ? player.PlayerCombatState?.Hand : null);
            if (candidates == null && offeredPile != null)
            {
                __state.OfferedPile = offeredPile.Type.ToString();
                if (offeredPile.Type != PileType.Draw) __state.Offered = offeredPile.Cards.Cast<object?>().ToList();
            }
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Choice source record failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void ChoiceSelectionTaskSingle(Task<CardModel?> __result, OpenCardSelection? __state)
        => CloseChoiceSelectionWhenDone(__result, __state);

    private static void ChoiceSelectionTaskMany(Task<IEnumerable<CardModel>> __result, OpenCardSelection? __state)
        => CloseChoiceSelectionWhenDone(__result, __state);

    private static void CloseChoiceSelectionWhenDone(Task? task, OpenCardSelection? record)
    {
        if (record == null) return;
        record.SelectionTask = task;
        if (task == null || task.IsCompleted) { Closed(); return; }
        task.ContinueWith(_ => Closed(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        void Closed()
        {
            var completion = OpenCardSelections.Complete(record, task);
            if (completion == OpenCardSelections.Completion.Ignored || record.Attempt is not { } attempt) return;
            try
            {
                if (OS.GetThreadCallerId() == OS.GetMainThreadId()) Publish();
                else _ = RunOnMainThread(Publish);
            }
            catch (Exception ex) { GD.PrintErr($"[STS2 MCP] combat_log selection row failed: {ex.GetType().Name}: {ex.Message}"); }

            void Publish()
            {
                try
                {
                    if (!IsLiveCombatAttempt(attempt)) return;
                    if (completion == OpenCardSelections.Completion.Evicted)
                        CombatLogStore.TryMarkBroken(attempt, "selection: pending call evicted before completion");
                    else
                        LogSelectionResult(record, attempt, task!);
                }
                catch (Exception ex)
                {
                    CombatLogStore.TryMarkBroken(attempt, $"selection: {ex.GetType().Name}: {ex.Message}");
                    GD.PrintErr($"[STS2 MCP] combat_log selection row failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Who opened the selection on screen and what runs after it, from the open <c>CardSelectCmd</c> call:
    /// the <c>PlayerChoiceContext</c> chain (a <c>BranchingPlayerChoiceContext</c> wraps the context it was made
    /// from) with each context's model stack top first (<c>PlayerChoiceContext.ModelStack</c>: <c>Hook</c> pushes each
    /// listener while it runs, <c>CardModel.OnPlayWrapper</c> pushes the card), the card play still in progress
    /// (<c>CardPlayStartedEntry</c> without its <c>CardPlayFinishedEntry</c>), and for the top model the hook it runs and
    /// the listeners after it in that hook's list. Everything named here is a model the player can see
    /// (powers, relics, potions, cards in public piles; draw-pile listeners never expose their place).
    /// </summary>
    private static Dictionary<string, object?> BuildChoiceSource(string screenKind, IReadOnlyList<CardModel?>? shownCards, IRunState runState,
        OpenCardSelection? matchedChoose = null, string? chooseReason = null)
    {
        var result = new Dictionary<string, object?> { ["version"] = ChoiceSourceVersion, ["known"] = false };
        var reasons = new List<string>();
        try
        {
            if (_choiceSourceTrackingError != null)
                throw new InvalidOperationException($"choice source tracking unavailable: {_choiceSourceTrackingError}");
            var local = LocalContext.GetMe(runState) ?? throw new InvalidOperationException("local player not found");
            var combat = local.Creature.CombatState ?? throw new InvalidOperationException("local player has no combat state");
            var shown = shownCards?.Cast<object?>().ToList();
            var record = (screenKind == "choose" ? matchedChoose : OpenCardSelections.Latest(s => ReferenceEquals(s.Player, local)
                    && ReferenceEquals(s.CombatState, combat)
                    && OpenCardSelections.SelectorShowsScreen(screenKind, s.Selector)
                    && (shown == null || OpenCardSelections.ShowsCandidates(s, shown))))
                ?? throw new InvalidOperationException(chooseReason ?? $"no open CardSelectCmd call matches this {screenKind} screen");

            RefreshEntityIdRegistry(combat);
            result["selector"] = record.Selector;
            // FromHandForDiscard → FromHand and the like: the outer entry points of the same selection.
            result["outer_selectors"] = OpenCardSelections.OuterSelectors(record);
            result["chooser_player_id"] = record.ChooserPlayerId ?? local.NetId.ToString();
            result["hand_source"] = record.HandSource is AbstractModel handSource ? BuildHookListenerRow(handSource, reasons) : null;

            var contexts = new List<Dictionary<string, object?>>();
            var models = new List<Dictionary<string, object?>>();
            var modelObjects = new List<AbstractModel>();
            var contextObjects = new List<PlayerChoiceContext>();
            PlayerChoiceContext? context = (PlayerChoiceContext)record.Context;
            for (int index = 0; context != null; index++)
            {
                if (index >= 8) { reasons.Add("PlayerChoiceContext chain longer than 8"); break; }
                var row = new Dictionary<string, object?> { ["index"] = index, ["owner_player_id"] = context.OwnerId?.ToString() };
                PlayerChoiceContext? next = null;
                switch (context)
                {
                    case BranchingPlayerChoiceContext branching:
                        row["kind"] = "branching";
                        row["source"] = branching.Source is { } branchSource ? BuildHookListenerRow(branchSource, reasons) : null;
                        // Branched to another chooser's HookPlayerChoiceContext (co-op): the choice runs there.
                        row["branched"] = GetInstanceFieldValue(branching, "_createdContext") != null;
                        next = GetInstanceFieldValue(branching, "_originalContext") as PlayerChoiceContext;
                        if (next == null) reasons.Add("BranchingPlayerChoiceContext._originalContext not readable");
                        break;
                    case HookPlayerChoiceContext hook:
                        row["kind"] = "hook";
                        row["source"] = hook.Source is { } hookSource ? BuildHookListenerRow(hookSource, reasons) : null;
                        row["action"] = hook.GameAction is { } hookAction ? BuildChoiceActionRow(hookAction) : null;
                        break;
                    case GameActionPlayerChoiceContext gameAction:
                        row["kind"] = "game_action";
                        row["action"] = BuildChoiceActionRow(gameAction.Action);
                        break;
                    case BlockingPlayerChoiceContext:
                        row["kind"] = "blocking";
                        break;
                    case ThrowingPlayerChoiceContext:
                        row["kind"] = "throwing";
                        break;
                    default:
                        row["kind"] = null;
                        reasons.Add($"unrecognized PlayerChoiceContext {context.GetType().Name}");
                        break;
                }
                foreach (var model in context.ModelStack ?? Enumerable.Empty<AbstractModel>())
                {
                    var modelRow = BuildHookListenerRow(model, reasons);
                    modelRow["context_index"] = index;
                    models.Add(modelRow);
                    modelObjects.Add(model);
                }
                contexts.Add(row);
                contextObjects.Add(context);
                context = next;
            }
            result["contexts"] = contexts;
            result["models"] = models;

            var cardPlay = BuildChoiceSourceCardPlay(modelObjects);
            result["card_play"] = cardPlay;
            // Its own known / unavailable_reasons: the fields above stay as they are when the chain cannot be read.
            result["await_chain"] = BuildChoiceAwaitChain(record, contextObjects, modelObjects);
            var chain = (Dictionary<string, object?>)result["await_chain"]!;
            var beforeDraw = (chain.GetValueOrDefault("frames") as List<Dictionary<string, object?>>)?.FirstOrDefault(frame =>
                Equals(frame.GetValueOrDefault("kind"), "hook") && Equals(frame.GetValueOrDefault("type"), "Hook")
                && Equals(frame.GetValueOrDefault("method"), "BeforeHandDraw") && Equals(frame.GetValueOrDefault("model_index"), 0));
            result["hook"] = modelObjects.Count == 0 ? null
                : BuildChoiceSourceHook(modelObjects[0], cardPlay?.GetValueOrDefault("model_index") is 0, runState, combat, beforeDraw, reasons);
            result["known"] = reasons.Count == 0;
        }
        catch (Exception ex)
        {
            reasons.Add($"{ex.GetType().Name}: {ex.Message}");
            Warn("choice_source", ex);
        }
        if (reasons.Count > 0) result["unavailable_reasons"] = reasons;
        return result;
    }

    private static Dictionary<string, object?> BuildChoiceActionRow(GameAction action)
    {
        var row = new Dictionary<string, object?>
        {
            ["type"] = action.GetType().Name,
            ["id"] = action.Id,
            ["owner_player_id"] = action.OwnerId.ToString(),
            ["state"] = action.State.ToString()
        };
        if (action is PlayCardAction play)
        {
            row["card_uid"] = play.NetCombatCard.ToCardModelOrNull() is { } card ? GetStableCardUid(card) : null;
            row["target_combat_id"] = play.TargetId;
        }
        return row;
    }

    // The first card in the stack whose play has started and not finished (CardModel.OnPlayWrapper
    // records CardPlayStarted before OnPlay and CardPlayFinished after it). Same row shape as
    // player.combat_history.card_play_events_this_turn, plus its index in models.
    private static Dictionary<string, object?>? BuildChoiceSourceCardPlay(List<AbstractModel> models)
    {
        var history = CombatManager.Instance.History;
        var finished = new HashSet<CardPlay>(history.CardPlaysFinished.Select(e => e.CardPlay), ReferenceEqualityComparer.Instance);
        var started = history.CardPlaysStarted.Select(e => e.CardPlay).Where(p => !finished.Contains(p)).ToList();
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i] is not CardModel card) continue;
            var play = started.LastOrDefault(p => ReferenceEquals(p.Card, card));
            if (play == null) continue;
            var row = BuildCardPlayEvent("started", play);
            row["model_index"] = i;
            row["player_id"] = play.Player.NetId.ToString();
            return row;
        }
        return null;
    }

    private static Dictionary<string, object?> BuildChoiceSourceHook(AbstractModel top, bool topIsCardInPlay,
        IRunState runState, ICombatState combat, Dictionary<string, object?>? beforeDraw, List<string> reasons)
    {
        var declared = new List<(string Method, string Scope, Type[] Parameters)>();
        foreach (var (method, scope) in ChoiceSourceHooks)
        {
            var baseMethod = typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == method);
            var parameters = baseMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            if (HookOverrideDeclarer(top.GetType(), method, parameters) != null)
                declared.Add((method, scope, parameters));
        }
        // A card in the middle of its play is running OnPlay, not a hook. A potion in use
        // (PotionModel.OnUseWrapper) and an orb (OrbCmd) are pushed by their own commands too, so the hook
        // is not decided for them. Otherwise the hook is known only when the model overrides exactly one
        // of the hooks that push it.
        string? notHookReason = topIsCardInPlay ? "card_in_play"
            : top is PotionModel ? "potion_pushes_itself"
            : top is OrbModel ? "orb_pushes_itself"
            : declared.Count != 1 ? (declared.Count == 0 ? "no_declared_hook" : "several_declared_hooks")
            : null;
        var hook = notHookReason == null ? declared[0] : default;
        var row = new Dictionary<string, object?>
        {
            ["model_index"] = 0,
            ["declared"] = declared.Select(d => d.Method).ToList(),
            ["name"] = hook.Method,
            ["name_unavailable_reason"] = notHookReason,
            ["scope"] = hook.Scope,
            ["remaining_listeners"] = null
        };
        if (beforeDraw != null && beforeDraw.GetValueOrDefault("listener") is Dictionary<string, object?> listener
            && listener.GetValueOrDefault("remaining_listeners") is { } remaining)
        {
            row["name"] = beforeDraw["awaiting"];
            row["name_unavailable_reason"] = null;
            row["scope"] = "combat";
            row["remaining_listeners"] = remaining;
            return row;
        }
        // The current phase can use a different list from a fresh enumeration; its cursor belongs to await_chain.
        if (beforeDraw != null || AsyncChain.BeforeHandDrawStages.Contains(hook.Method))
        {
            row["remaining_listeners_unavailable_reason"] = "the suspended BeforeHandDraw listener list was not observed";
            return row;
        }
        if (hook.Method == null) return row;
        // The listeners after a draw-pile listener would be the cards behind it in the hidden draw order.
        if (DrawPileOf(top) != null)
        {
            reasons.Add($"{top.Id.Entry} is a draw-pile listener; the listeners after it would reveal the draw order");
            return row;
        }

        int modSubscribers = CountModHookSubscribers("_runHookSubscribers") + CountModHookSubscribers("_combatHookSubscribers");
        if (modSubscribers > 0)
        {
            reasons.Add($"{modSubscribers} ModHelper hook subscriber(s) registered; remaining listeners cannot be tied to public state");
            return row;
        }
        // Re-read now: the game iterates the list it took when the hook began. Listeners the earlier ones
        // added or removed in between differ; the selection itself has not moved anything yet.
        var listeners = hook.Scope switch
        {
            "combat" => CombatManager.Instance.ShouldSkipCombatEffect ? new List<AbstractModel>() : combat.IterateHookListeners().ToList(),
            "combat_all" => combat.IterateHookListeners().ToList(),
            _ => runState.IterateHookListeners(combat).ToList()
        };
        int at = listeners.FindIndex(m => ReferenceEquals(m, top));
        if (at < 0)
        {
            reasons.Add($"{top.Id.Entry} is not in the {hook.Method} listener list");
            return row;
        }
        row["remaining_listeners"] = BuildHookListenerRows(listeners.Skip(at + 1).ToList(),
            (hook.Method, hook.Method, hook.Parameters, hook.Scope == "run"), reasons);
        return row;
    }
}
