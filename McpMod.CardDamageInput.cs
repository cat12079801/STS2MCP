using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string CardDamageInputHarmonyId = "com.sts2mcp.carddamageinput";
    private static string? _cardDamageInputTrackingError;

    // The builder's request and the dispatch's variable are both consumed at entry.
    // A recursive game call can match the same references without owning either observation.
    [ThreadStatic] private static DamageInputCapture? _requestedDamageInput;
    [ThreadStatic] private static DamageInputCapture? _cardDamageInput;
    [ThreadStatic] private static DamageInputObservation? _pendingDamageVar;
    [ThreadStatic] private static DamageInputObservation? _activeDamageVar;

    private sealed class DamageInputCapture(CardModel card, Creature target, DynamicVarSet vars)
    {
        public readonly CardModel Card = card;
        public readonly Creature Target = target;
        public readonly DynamicVarSet Vars = vars;
        public readonly Dictionary<string, DamageInputObservation> Observations = vars
            .Where(pair => pair.Key is "CalculatedDamage" or "Damage")
            .ToDictionary(pair => pair.Key, pair => new DamageInputObservation(card, target, pair.Value));
    }

    private sealed class DamageInputObservation(CardModel card, Creature target, DynamicVar variable)
    {
        public readonly DynamicVar Variable = variable;
        public DamageInputArgs? Modifier;
        public decimal? Modified;
        public DamageInputArgs? Preview;

        public bool Matches(CardModel? source, Creature? receiver) =>
            ReferenceEquals(card, source) && ReferenceEquals(target, receiver);
    }

    private sealed record DamageInputArgs(decimal Damage, ValueProp Props, Creature? Dealer);

    private static void AddDamageInput(Dictionary<string, object?> row, DamageInputCapture? capture, string? name)
    {
        if (capture == null || name == null) return;

        const string context = "card.vs_targets.damage_input";
        var input = new Dictionary<string, object?> { ["var_name"] = name };
        row["damage_input"] = input;
        capture.Observations.TryGetValue(name, out var observation);
        var modifier = observation?.Modifier;
        var preview = observation?.Preview;

        DamageInputArgs RequireModifier() => modifier ??
            throw new InvalidOperationException("No matching Hook.ModifyDamage call in the card variable preview");
        DamageInputArgs RequirePreview()
        {
            if (preview == null)
                throw new InvalidOperationException("No matching CreatureCmd.GetDamageResultsPreview call in the card variable preview");
            if (observation!.Modified != preview.Damage || RequireModifier().Props != preview.Props)
                throw new InvalidOperationException("Damage/props did not match across the two preview stages");
            return preview;
        }
        static uint? DealerId(DamageInputArgs args) => args.Dealer == null ? null : RequireCombatId(args.Dealer);

        ReadInternalStateValue(input, "base_for_target", () => RequireModifier().Damage, context);
        ReadInternalStateValue(input, "modifier_dealer_combat_id", () => DealerId(RequireModifier()), context);
        ReadInternalStateValue(input, "before_block", () => RequirePreview().Damage, context);
        ReadInternalStateValue(input, "props", () => (int)RequirePreview().Props, context);
        ReadInternalStateValue(input, "preview_dealer_combat_id", () => DealerId(RequirePreview()), context);
    }

    private static void InstallCardDamageInputTracking()
    {
        try { InstallCardDamageInputPatches(new Harmony(CardDamageInputHarmonyId)); }
        catch (Exception ex)
        {
            _cardDamageInputTrackingError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] damage_input patches unavailable: {ex}");
        }
    }

    private static void InstallCardDamageInputPatches(Harmony harmony)
    {
        HarmonyMethod M(string name) => new(typeof(McpMod), name);
        // Install after the combat log preview guard. Harmony can compile the callees while patching.
        harmony.Patch(AccessTools.DeclaredMethod(typeof(CardModel), nameof(CardModel.UpdateDynamicVarPreview),
                [typeof(CardPreviewMode), typeof(Creature), typeof(DynamicVarSet)]),
            prefix: new HarmonyMethod(typeof(McpMod), nameof(DamageInputCardStarting)) { priority = Priority.First },
            finalizer: M(nameof(DamageInputCardFinished)),
            transpiler: M(nameof(PatchDamageInputDispatch)));
        foreach (var type in new[] { typeof(DamageVar), typeof(CalculatedDamageVar) })
        {
            harmony.Patch(AccessTools.DeclaredMethod(type, nameof(DynamicVar.UpdateCardPreview),
                    [typeof(CardModel), typeof(CardPreviewMode), typeof(Creature), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(McpMod), nameof(DamageInputVarStarting)) { priority = Priority.First },
                finalizer: M(nameof(DamageInputVarFinished)),
                transpiler: M(nameof(PatchDamageInputCalls)));
        }
    }

    private static void DamageInputCardStarting(CardModel __instance, Creature? target, DynamicVarSet dynamicVarSet,
        out (DamageInputCapture? Request, DamageInputCapture? Card) __state)
    {
        __state = (_requestedDamageInput, _cardDamageInput);
        var request = _requestedDamageInput;
        _requestedDamageInput = null;
        _cardDamageInput = request != null && ReferenceEquals(request.Card, __instance) &&
            ReferenceEquals(request.Target, target) && ReferenceEquals(request.Vars, dynamicVarSet) ? request : null;
    }

    private static void DamageInputCardFinished((DamageInputCapture? Request, DamageInputCapture? Card) __state)
        => (_requestedDamageInput, _cardDamageInput) = __state;

    private static void UpdateObservedDamageVar(DynamicVar variable, CardModel card, CardPreviewMode previewMode,
        Creature? target, bool runGlobalHooks)
    {
        var previous = _pendingDamageVar;
        _pendingDamageVar = null;
        if (runGlobalHooks && _cardDamageInput is { } capture &&
            capture.Observations.TryGetValue(variable.Name, out var observation) &&
            ReferenceEquals(observation.Variable, variable) && observation.Matches(card, target))
            _pendingDamageVar = observation;
        try { variable.UpdateCardPreview(card, previewMode, target, runGlobalHooks); }
        finally { _pendingDamageVar = previous; }
    }

    private static void DamageInputVarStarting(DynamicVar __instance, CardModel card, Creature? target,
        bool runGlobalHooks, out DamageInputObservation? __state)
    {
        __state = _activeDamageVar;
        var pending = _pendingDamageVar;
        _pendingDamageVar = null;
        _activeDamageVar = runGlobalHooks && pending != null && ReferenceEquals(pending.Variable, __instance) &&
            pending.Matches(card, target) ? pending : null;
    }

    private static void DamageInputVarFinished(DamageInputObservation? __state) => _activeDamageVar = __state;

    // Instrument only the call sites in the game's preview methods. A listener, another variable
    // or a description redraw can call the same hooks, even with the same card and target.
    private static IEnumerable<CodeInstruction> PatchDamageInputDispatch(IEnumerable<CodeInstruction> code) =>
        ReplaceDamageInputCall(code, typeof(DynamicVar), nameof(DynamicVar.UpdateCardPreview),
            nameof(UpdateObservedDamageVar));

    private static IEnumerable<CodeInstruction> PatchDamageInputCalls(IEnumerable<CodeInstruction> code) =>
        ReplaceDamageInputCall(
            ReplaceDamageInputCall(code, typeof(Hook), nameof(Hook.ModifyDamage), nameof(ObserveDamageModifier)),
            typeof(CreatureCmd), nameof(CreatureCmd.GetDamageResultsPreview), nameof(ObserveDamageResultsPreview));

    private static IEnumerable<CodeInstruction> ReplaceDamageInputCall(IEnumerable<CodeInstruction> code,
        Type originalType, string originalName, string observerName)
    {
        var instructions = code.ToList();
        var original = AccessTools.DeclaredMethod(originalType, originalName);
        var calls = instructions.Where(instruction => instruction.Calls(original)).ToList();
        if (calls.Count != 1)
            throw new InvalidOperationException($"Expected one preview call to {originalType.Name}.{originalName}, found {calls.Count}");
        calls[0].opcode = System.Reflection.Emit.OpCodes.Call;
        calls[0].operand = AccessTools.DeclaredMethod(typeof(McpMod), observerName);
        return instructions;
    }

    private static decimal ObserveDamageModifier(IRunState runState, ICombatState? combatState, Creature? target,
        Creature? dealer, decimal damage, ValueProp props, CardModel? cardSource, CardPlay? cardPlay,
        ModifyDamageHookType hookType, CardPreviewMode previewMode, out IEnumerable<AbstractModel> modifiers)
    {
        var observation = _activeDamageVar;
        if (observation?.Matches(cardSource, target) != true) observation = null;
        if (observation != null) observation.Modifier = new(damage, props, dealer);
        var modified = Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource,
            cardPlay, hookType, previewMode, out modifiers);
        if (observation != null) observation.Modified = modified;
        return modified;
    }

    private static List<DamageResult> ObserveDamageResultsPreview(IRunState runState, ICombatState? combatState,
        Creature target, decimal damage, ValueProp props, Creature? dealer, CardModel cardSource)
    {
        if (_activeDamageVar is { } observation && observation.Matches(cardSource, target))
            observation.Preview = new(damage, props, dealer);
        return CreatureCmd.GetDamageResultsPreview(runState, combatState, target, damage, props, dealer, cardSource);
    }
}
