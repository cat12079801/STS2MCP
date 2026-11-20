using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int DecisionContextVersion = 1;
    private const string DecisionContextHarmonyId = "com.sts2mcp.decisioncontext";
    private static string? _decisionContextTrackingError = "not installed";

    // The selection entry points without a PlayerChoiceContext (v0.113.1, CardSelectCmd.cs / RelicSelectCmd.cs): the
    // deck screens out of combat, the bundle screen and the relic screen. FromDeckForRemoval forwards to FromDeckGeneric
    // and the Player overloads of FromDeckForEnchantment to the card-list one: all are patched, the inner record wins.
    private static readonly (Type Type, string Method, Type[] Parameters)[] DecisionContextPatchTargets =
    {
        (typeof(CardSelectCmd), "FromDeckForUpgrade", new[] { typeof(Player), typeof(CardSelectorPrefs) }),
        (typeof(CardSelectCmd), "FromDeckForTransformation", new[] { typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, CardTransformation>) }),
        (typeof(CardSelectCmd), "FromDeckForEnchantment", new[] { typeof(Player), typeof(EnchantmentModel), typeof(int), typeof(CardSelectorPrefs) }),
        (typeof(CardSelectCmd), "FromDeckForEnchantment", new[] { typeof(Player), typeof(EnchantmentModel), typeof(int), typeof(Func<CardModel?, bool>), typeof(CardSelectorPrefs) }),
        (typeof(CardSelectCmd), "FromDeckForEnchantment", new[] { typeof(IReadOnlyList<CardModel>), typeof(EnchantmentModel), typeof(int), typeof(CardSelectorPrefs) }),
        (typeof(CardSelectCmd), "FromDeckForRemoval", new[] { typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>) }),
        (typeof(CardSelectCmd), "FromDeckGeneric", new[] { typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>), typeof(Func<CardModel, int>) }),
        (typeof(CardSelectCmd), "FromChooseABundleScreen", new[] { typeof(Player), typeof(IReadOnlyList<IReadOnlyList<CardModel>>), typeof(bool) }),
        (typeof(RelicSelectCmd), "FromChooseARelicScreen", new[] { typeof(Player), typeof(IReadOnlyList<RelicModel>) }),
    };

    private static void InstallDecisionContextTracking()
    {
        var harmony = new Harmony(DecisionContextHarmonyId);
        try
        {
            foreach (var (type, method, parameters) in DecisionContextPatchTargets)
            {
                var original = AccessTools.DeclaredMethod(type, method, parameters) ?? throw new MissingMethodException(type.Name, method);
                string postfix = original.ReturnType == typeof(Task<RelicModel?>) ? nameof(DecisionSelectionTaskRelic)
                    : original.ReturnType == typeof(Task<IEnumerable<CardModel>>) ? nameof(DecisionSelectionTaskMany)
                    : throw new InvalidOperationException($"{type.Name}.{method} returns {original.ReturnType.Name}");
                harmony.Patch(original,
                    prefix: new HarmonyMethod(typeof(McpMod), nameof(DecisionSelectionOpened)),
                    postfix: new HarmonyMethod(typeof(McpMod), postfix));
            }
            _decisionContextTrackingError = null;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll(DecisionContextHarmonyId); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Decision context cleanup failed: {cleanupError.Message}"); }
            _decisionContextTrackingError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] Decision context tracking unavailable: {ex}");
        }
    }

    // Read-only prefix: records the arguments and who called, never skips or changes the call. Without a
    // PlayerChoiceContext the player stands in for the context (FromDeckForRemoval → FromDeckGeneric share it).
    private static void DecisionSelectionOpened(MethodBase __originalMethod, object[] __args, out OpenCardSelection? __state)
    {
        __state = null;
        try
        {
            var names = __originalMethod.GetParameters().Select(p => p.Name).ToList();
            object? Arg(string name) => names.IndexOf(name) is int i and >= 0 ? __args[i] : null;
            var cards = Arg("cards") as IReadOnlyList<CardModel>;
            var player = Arg("player") as Player ?? cards?.FirstOrDefault()?.Owner;
            if (player == null) return;
            IReadOnlyList<object?>? candidates = Arg("relics") is IReadOnlyList<RelicModel> relics ? relics.Cast<object?>().ToList()
                : cards?.Cast<object?>().ToList();
            // The fight in progress, not player.Creature.CombatState: on the reward screen after a fight the creature still
            // points at the finished fight (CombatManager.DetachCreatures runs at CombatRoom.Exit) while no fight is live.
            var record = OpenCardSelections.Add(__originalMethod.Name, player, player, GetLiveCombatStateForSelection(), candidates: candidates);
            AttachSelectionOpener(record);
            __state = record;
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Decision context record failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void DecisionSelectionTaskMany(Task<IEnumerable<CardModel>> __result, OpenCardSelection? __state)
        => CloseChoiceSelectionWhenDone(__result, __state);

    private static void DecisionSelectionTaskRelic(Task<RelicModel?> __result, OpenCardSelection? __state)
        => CloseChoiceSelectionWhenDone(__result, __state);

    // Who opened the selection and where in the fight's log it began. Called from both trackers' prefixes.
    private static void AttachSelectionOpener(OpenCardSelection record)
    {
        try { record.Opener = FindSelectionOpener(); }
        catch (Exception ex) { record.Opener = new Dictionary<string, object?> { ["kind"] = null, ["unavailable_reason"] = $"{ex.GetType().Name}: {ex.Message}" }; }
        if (record.CombatState != null) record.OpenedAtLogSeq = CombatLogStore.NextSeqOf(record.CombatState);
    }

    /// <summary>
    /// The nearest game method on the call stack of the selection's entry point (the synchronous part of an async method
    /// runs on the caller's stack, so the relic's <c>AfterObtained</c>, the event's option, the rest site option's
    /// <c>OnSelect</c> or the card's <c>OnPlay</c> is there), with up to six game frames from the inside out. A frame of a
    /// compiler-made state machine or lambda is named by the method it belongs to. Frames of the MOD, Harmony, the runtime
    /// and the selection commands themselves are skipped. A frame the JIT inlined is not on the stack, so the opener is
    /// the nearest frame that is.
    /// </summary>
    private static Dictionary<string, object?> FindSelectionOpener()
    {
        var gameAssembly = typeof(CardModel).Assembly;
        var frames = new List<string>();
        Dictionary<string, object?>? opener = null;
        foreach (var frame in new StackTrace(1, false).GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method == null || type == null || type.Assembly != gameAssembly) continue;
            string methodName = method.Name;
            // <AfterObtained>d__5.MoveNext → AfterObtained; <>c__DisplayClass3_0.<OnSelect>b__0 → OnSelect.
            if (methodName.StartsWith('<') && methodName.IndexOf('>') is int close and > 1) methodName = methodName[1..close];
            while (type.IsNested && type.Name.StartsWith('<'))
            {
                if (type.Name.IndexOf('>') is int end and > 1) methodName = type.Name[1..end];
                type = type.DeclaringType!;
            }
            if (type == typeof(CardSelectCmd) || type == typeof(RelicSelectCmd)) continue;
            string label = $"{type.Name}.{methodName}";
            if (frames.Count < 6 && (frames.Count == 0 || frames[^1] != label)) frames.Add(label);
            opener ??= ClassifyOpener(type, methodName);
        }
        opener ??= new Dictionary<string, object?> { ["kind"] = null, ["unavailable_reason"] = "no game frame on the stack" };
        opener["call_stack"] = frames;
        return opener;
    }

    private static Dictionary<string, object?> ClassifyOpener(Type type, string methodName)
    {
        string kind = typeof(RelicModel).IsAssignableFrom(type) ? "relic"
            : typeof(CardModel).IsAssignableFrom(type) ? "card"
            : typeof(PotionModel).IsAssignableFrom(type) ? "potion"
            : typeof(PowerModel).IsAssignableFrom(type) ? "power"
            : typeof(EventModel).IsAssignableFrom(type) ? "event"
            : typeof(EnchantmentModel).IsAssignableFrom(type) ? "enchantment"
            : typeof(AfflictionModel).IsAssignableFrom(type) ? "affliction"
            : typeof(MonsterModel).IsAssignableFrom(type) ? "monster"
            : typeof(AbstractModel).IsAssignableFrom(type) ? "model"
            : typeof(RestSiteOption).IsAssignableFrom(type) ? "rest_site_option"
            : "other";
        return new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["model_type"] = type.Name,
            // ModelDb.GetEntry(Type): the same slug as AbstractModel.Id.Entry for a model of this type.
            ["model_id"] = typeof(AbstractModel).IsAssignableFrom(type) && !type.IsAbstract ? ModelDb.GetEntry(type) : null,
            ["method"] = methodName
        };
    }

    /// <summary>
    /// <c>decision_context</c> on a selection screen: the entry point that opened it (and the ones around it), who opened it
    /// (<see cref="FindSelectionOpener"/>), whether a fight is in progress, where in the fight's combat_log it opened, and
    /// in a fight the screen's <c>choice_source</c> (the same object as the screen's own <c>choice_source</c>, whose
    /// <c>await_chain</c> says which stage of Entropy / Stratagem and which draw it belongs to).
    /// </summary>
    private static Dictionary<string, object?> BuildDecisionContext(string screenKind, IReadOnlyList<object?>? shown,
        IRunState runState, Dictionary<string, object?>? choiceSource)
    {
        var result = new Dictionary<string, object?> { ["version"] = DecisionContextVersion, ["known"] = false, ["screen"] = screenKind };
        try
        {
            bool deckLike = screenKind is "deck" or "bundle" or "relic";
            if (deckLike && _decisionContextTrackingError != null)
                throw new InvalidOperationException($"decision context tracking unavailable: {_decisionContextTrackingError}");
            if (!deckLike && _choiceSourceTrackingError != null)
                throw new InvalidOperationException($"choice source tracking unavailable: {_choiceSourceTrackingError}");
            var local = LocalContext.GetMe(runState) ?? throw new InvalidOperationException("local player not found");
            object? live = GetLiveCombatStateForSelection();
            var record = OpenCardSelections.Latest(s => ReferenceEquals(s.Player, local)
                    && ReferenceEquals(s.CombatState, live)
                    && OpenCardSelections.SelectorShowsScreen(screenKind, s.Selector)
                    && (shown == null || OpenCardSelections.ShowsCandidates(s, shown)))
                ?? throw new InvalidOperationException($"no open selection entry matches this {screenKind} screen");
            result["in_combat"] = live != null;
            result["selector"] = record.Selector;
            result["outer_selectors"] = OpenCardSelections.OuterSelectors(record);
            result["chooser_player_id"] = local.NetId.ToString();
            result["opener"] = record.Opener;
            result["opened_at_combat_log_seq"] = record.OpenedAtLogSeq;
            result["choice_source"] = choiceSource;
            result["known"] = record.Opener is Dictionary<string, object?> opener && opener.GetValueOrDefault("kind") != null;
            if (result["known"] is false) result["unavailable_reasons"] = new List<string> { "opener not found on the call stack" };
        }
        catch (Exception ex)
        {
            result["unavailable_reasons"] = new List<string> { $"{ex.GetType().Name}: {ex.Message}" };
            Warn("decision_context", ex);
        }
        return result;
    }

    // combat_log "selection" row when a selection the fight opened completes (the innermost entry point only).
    private static void LogSelectionResult(OpenCardSelection record, Task task)
    {
        if (record.HasInner || record.CombatState == null || !task.IsCompletedSuccessfully) return;
        object? chosen = task switch
        {
            Task<CardModel?> single => single.Result,
            Task<IEnumerable<CardModel>> many => many.Result?.ToList(),
            Task<RelicModel?> relic => relic.Result,
            _ => null
        };
        LogCombatRowIf("selection", (row, combat) =>
        {
            if (!ReferenceEquals(combat, record.CombatState)) return false;
            var reasons = new List<string>();
            row["selector"] = record.Selector;
            row["outer_selectors"] = OpenCardSelections.OuterSelectors(record);
            row["opener"] = record.Opener;
            row["opened_at_seq"] = record.OpenedAtLogSeq;
            if (record.Pile is CardPile { Type: PileType.Draw })
            {
                // The candidates of a draw-pile selection, in the order given, would be the draw order.
                row["offered"] = null;
                row["offered_hidden_reason"] = "draw_pile";
            }
            else
            {
                // The cards passed to the entry point, or (no card list) the hand / combat pile when it opened. The game
                // decides which of a pile's cards can be picked (FromHandForUpgrade: upgradable ones; a filter argument).
                var offered = record.Candidates ?? record.Offered;
                row["offered"] = offered?.Select(c => c switch
                {
                    CardModel card => LogCardRef(card),
                    RelicModel relic => new Dictionary<string, object?> { ["relic_id"] = relic.Id.Entry },
                    _ => null
                }).ToList();
                row["offered_from"] = record.Candidates != null ? "arguments" : record.OfferedPile;
                if (offered == null) row["offered_unavailable_reason"] = "no card list or pile recorded";
            }
            bool fromDraw = record.Pile is CardPile { Type: PileType.Draw };
            var picked = chosen switch
            {
                CardModel card => new List<object?> { LogCardRef(card) },
                // A draw-pile selection the game decides itself (FromCombatPile with no more cards than MinSelect) returns
                // them in the pile's order: list them in the draw pile's display order instead.
                List<CardModel> cards => (fromDraw ? OrderPileCardsForDisplay(cards, PileType.Draw) : cards)
                    .Select(c => (object?)LogCardRef(c)).ToList(),
                RelicModel relic => new List<object?> { new Dictionary<string, object?> { ["relic_id"] = relic.Id.Entry } },
                _ => new List<object?>()
            };
            row["chosen"] = picked;
            row["skipped"] = picked.Count == 0;
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
            return true;
        });
    }
}
