using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string CombatLogHarmonyId = "com.sts2mcp.combatlog";

    // The notifications combat_log records (v0.113.1, sts2-a671e9ef, Hook.cs): the Hook method, the listener list it
    // iterates ("combat" = Hook.IterateCombatHookListeners, nothing while CombatManager.ShouldSkipCombatEffect;
    // "combat_all" = ICombatState.IterateHookListeners(); "run" = IRunState.IterateHookListeners(combat)) and the
    // AbstractModel methods it calls on each listener, pass by pass (Early / normal / Late). Generated from
    // tools/event_inventory.py --dump (the hook:* rows classified public-missing in phase 1).
    private static readonly (string Hook, string Scope, string[] Passes)[] CombatLogHooks =
    {
        ("AfterAttack", "combat", new[] { "AfterAttack" }),
        ("AfterAutoPostPlayPhaseEntered", "combat", new[] { "AfterAutoPostPlayPhaseEntered" }),
        ("AfterAutoPrePlayPhaseEntered", "combat", new[] { "AfterAutoPrePlayPhaseEnteredEarly", "AfterAutoPrePlayPhaseEntered", "AfterAutoPrePlayPhaseEnteredLate" }),
        ("AfterBlockBroken", "combat_all", new[] { "AfterBlockBroken" }),
        ("AfterBlockCleared", "combat", new[] { "AfterBlockCleared" }),
        ("AfterBlockGained", "combat", new[] { "AfterBlockGained" }),
        ("AfterCardChangedPiles", "run", new[] { "AfterCardChangedPiles", "AfterCardChangedPilesLate" }),
        ("AfterCardDrawn", "combat", new[] { "AfterCardDrawnEarly", "AfterCardDrawn" }),
        ("AfterCardEnteredCombat", "combat", new[] { "AfterCardEnteredCombat" }),
        ("AfterCardExhausted", "combat", new[] { "AfterCardExhausted" }),
        ("AfterCardGeneratedForCombat", "combat", new[] { "AfterCardGeneratedForCombat" }),
        ("AfterCardPlayed", "combat_all", new[] { "AfterCardPlayed", "AfterCardPlayedLate" }),
        ("AfterCombatEnd", "run", new[] { "AfterCombatEnd" }),
        ("AfterCombatVictory", "run", new[] { "AfterCombatVictoryEarly", "AfterCombatVictory" }),
        ("AfterCreatureAddedToCombat", "combat_all", new[] { "AfterCreatureAddedToCombat" }),
        ("AfterCurrentHpChanged", "run", new[] { "AfterCurrentHpChanged" }),
        ("AfterDamageGiven", "combat_all", new[] { "AfterDamageGiven" }),
        ("AfterDamageReceived", "run", new[] { "AfterDamageReceived", "AfterDamageReceivedLate" }),
        ("AfterDeath", "run", new[] { "AfterDeath" }),
        ("AfterEnergyReset", "combat", new[] { "AfterEnergyReset", "AfterEnergyResetLate" }),
        ("AfterHandEmptied", "combat", new[] { "AfterHandEmptied" }),
        ("AfterModifyingBlockAmount", "combat", new[] { "AfterModifyingBlockAmount" }),
        ("AfterModifyingCardPlayCount", "combat", new[] { "AfterModifyingCardPlayCount" }),
        ("AfterModifyingDamageAmount", "run", new[] { "AfterModifyingDamageAmount" }),
        ("AfterModifyingHandDraw", "combat", new[] { "AfterModifyingHandDraw" }),
        ("AfterModifyingHpLostAfterOsty", "run", new[] { "AfterModifyingHpLostAfterOsty" }),
        ("AfterModifyingHpLostBeforeOsty", "run", new[] { "AfterModifyingHpLostBeforeOsty" }),
        ("AfterModifyingPowerAmountReceived", "combat", new[] { "AfterModifyingPowerAmountReceived" }),
        ("AfterPlayerTurnStart", "combat", new[] { "AfterPlayerTurnStartEarly", "AfterPlayerTurnStart", "AfterPlayerTurnStartLate" }),
        ("AfterPotionDiscarded", "run", new[] { "AfterPotionDiscarded" }),
        ("AfterPotionProcured", "run", new[] { "AfterPotionProcured" }),
        ("AfterPotionUsed", "run", new[] { "AfterPotionUsed" }),
        ("AfterPowerAmountChanged", "combat", new[] { "AfterPowerAmountChanged" }),
        ("AfterPreventingBlockClear", "combat", new[] { "AfterPreventingBlockClear" }),
        ("AfterPreventingDeath", "run", new[] { "AfterPreventingDeath" }),
        ("AfterPreventingDraw", "combat", new[] { "AfterPreventingDraw" }),
        ("AfterShuffle", "combat", new[] { "AfterShuffle" }),
        ("AfterSideTurnEnd", "combat", new[] { "AfterSideTurnEnd", "AfterSideTurnEndLate" }),
        ("AfterSideTurnStart", "combat", new[] { "AfterSideTurnStart", "AfterSideTurnStartLate" }),
        ("AfterTakingExtraTurn", "combat", new[] { "AfterTakingExtraTurn" }),
        ("BeforeAttack", "combat", new[] { "BeforeAttack" }),
        ("BeforeBlockGained", "combat", new[] { "BeforeBlockGained" }),
        ("BeforeCardAutoPlayed", "combat", new[] { "BeforeCardAutoPlayed" }),
        ("BeforeCardPlayed", "combat", new[] { "BeforeCardPlayed" }),
        ("BeforeCombatStart", "run", new[] { "BeforeCombatStart", "BeforeCombatStartLate" }),
        ("BeforeDamageReceived", "run", new[] { "BeforeDamageReceived" }),
        ("BeforeDeath", "run", new[] { "BeforeDeath" }),
        ("BeforeFlush", "combat", new[] { "BeforeFlush", "BeforeFlushLate" }),
        ("BeforeHandDraw", "combat", new[] { "BeforeHandDraw", "BeforeHandDrawLate" }),
        ("BeforePotionUsed", "run", new[] { "BeforePotionUsed" }),
        ("BeforePowerAmountChanged", "combat", new[] { "BeforePowerAmountChanged" }),
        ("BeforeSideTurnEnd", "combat", new[] { "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd" }),
        ("BeforeSideTurnStart", "combat", new[] { "BeforeSideTurnStart" }),
    };

    // The CombatManager turn steps combat_log marks with a turn_phase row when each begins (private async methods,
    // CombatManager.cs v0.113.1): the phase:* rows of the inventory, with the parameter type names of the overload that
    // does the work. EndCombatInternal / EndPlayerTurnPhaseOneInternal / EndPlayerTurnPhaseTwoInternal also have an
    // internal parameterless overload that only forwards the current CombatTurnState (an internal type, hence names).
    private static readonly (string Method, string[] Parameters)[] CombatLogPhases =
    {
        ("StartCombatInternal", new[] { "CombatTurnState" }),
        ("StartTurn", new[] { "CombatTurnState", "Func`1" }),
        ("RunAutoPrePlayPhase", new[] { "CombatTurnState", "HookPlayerChoiceContext", "Task", "Player" }),
        ("SetupPlayerTurn", new[] { "CombatTurnState", "Player", "HookPlayerChoiceContext" }),
        ("EndPlayerTurnPhaseOneInternal", new[] { "CombatTurnState" }),
        ("DoTurnEnd", new[] { "CombatTurnState", "Player", "PlayerChoiceContext" }),
        ("ResolveTurnEndCardEffects", new[] { "CardModel", "PlayerChoiceContext", "Task" }),
        ("FlushPlayerHand", new[] { "CombatTurnState", "Player", "HookPlayerChoiceContext" }),
        ("EndPlayerTurnPhaseTwoInternal", new[] { "CombatTurnState" }),
        ("SwitchFromPlayerToEnemySide", new[] { "CombatTurnState" }),
        ("EndEnemyTurnInternal", new[] { "CombatTurnState" }),
        ("EndCombatInternal", new[] { "CombatTurnState" }),
    };

    // The one declared CombatManager method with this name and exactly these parameter type names (else it throws).
    internal static MethodInfo ResolveCombatLogPhase(string method, string[] parameters)
    {
        var matches = typeof(CombatManager).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == method && m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(parameters))
            .ToList();
        return matches.Count == 1 ? matches[0]
            : throw new MissingMethodException(nameof(CombatManager), $"{method}({string.Join(", ", parameters)}) ({matches.Count} found)");
    }

    private static readonly Dictionary<string, (string Scope, (string Method, Type[] Parameters)[] Passes)> _combatLogHookTable = new();
    private static FieldInfo? _playerCombatStatePlayer;

    // Scopes that follow an async call into the awaits it makes: the prefix sets the value before the async method's
    // builder captures the ExecutionContext, so the method's continuations see it; the postfix (run when the sync part
    // returns) puts back what the caller had.
    private sealed class PileAddScope
    {
        public required string Position { get; init; }
        public required PileType Requested { get; init; }
        public AbstractModel? ClonedBy { get; init; }
        // The cards of the call when they come as a collection (List, array, the single-card overloads' list): a card
        // another command lands in a listener of this Add (CardCmd.Transform calls CardPile.AddInternal itself) is not
        // one of them. Null for a lazy sequence, which is not enumerated a second time.
        public HashSet<CardModel>? Cards { get; init; }
    }

    private static readonly AsyncLocal<PileAddScope?> _pileAddScope = new();

    private sealed class SuppressedRows
    {
        public int Count;
    }

    // Set for the CardPileCmd.Add that CardPileCmd.Shuffle makes: every row it would write (the pile moves, the
    // AfterCardChangedPiles notifications and whatever their listeners do) comes card by card in the shuffled order.
    private static readonly AsyncLocal<SuppressedRows?> _combatLogSuppressed = new();

    private sealed class PileAddState
    {
        public PileAddScope? PreviousScope { get; init; }
        public SuppressedRows? PreviousSuppressed { get; init; }
        public SuppressedRows? Suppressed { get; init; }
    }
    // One-shot marks. CardPileCmd.Shuffle's prefix leaves one for its own CardPileCmd.Add (the only Add before it awaits
    // anything; Hook.ModifyShuffleOrder runs no Add): that Add puts every card back in the shuffled order, so its rows
    // are counted, not written. The AfterShuffle listeners after it (Stratagem's Draw → Hand, Soot) find the mark used
    // and write their rows. CardPileCmd.ShuffleIfNecessary's prefix leaves one for the Shuffle it calls (reason
    // draw_pile_empty); a Shuffle in a listener after that one is explicit.
    private sealed class OneShot
    {
        public bool Used;
        public bool TryUse()
        {
            if (Used) return false;
            Used = true;
            return true;
        }
    }

    private static readonly AsyncLocal<OneShot?> _shuffleAddPending = new();
    private static readonly AsyncLocal<OneShot?> _shuffleIfNecessaryPending = new();
    [ThreadStatic] private static CardModel? _lastRemovedCard;
    [ThreadStatic] private static PileType _lastRemovedPile;

    /// <summary>
    /// Read-only patches for battle.combat_log (v0.113.1, decompile sts2-a671e9ef). None skips, repeats or reorders a
    /// game call, changes an argument or a result, or draws from an RNG. Each group installs on its own: a group that
    /// fails is reported in combat_log.unavailable_reasons (the log is then known=false), the others keep working.
    /// </summary>
    private static void InstallCombatLogTracking()
    {
        var harmony = new Harmony(CombatLogHarmonyId);
        void Group(string name, Action install)
        {
            try { install(); }
            catch (Exception ex)
            {
                CombatLogStore.AddInstallError($"{name}: {ex.GetType().Name}: {ex.Message}");
                GD.PrintErr($"[STS2 MCP] combat_log {name} patches unavailable: {ex}");
            }
        }
        HarmonyMethod M(string name) => new(typeof(McpMod), name);
        MethodInfo Declared(Type type, string name, params Type[] parameters)
            => AccessTools.DeclaredMethod(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);

        Group("history", () =>
        {
            // Every public recording method of CombatHistory (each wraps one *Entry in the private Add), not the private
            // Add itself: a method another group patched earlier (CardPlayStarted, by the auto-play reservation tracking)
            // was compiled into its Harmony replacement with Add inlined, so a later patch on Add never saw its entries.
            var recorders = CombatLogHistoryRecorders();
            foreach (var original in recorders)
                harmony.Patch(original, prefix: M(nameof(CombatLogHistoryRecording)), postfix: M(nameof(CombatLogHistoryRecorded)));
        });

        Group("hooks", () =>
        {
            var hookMethods = new List<MethodInfo>();
            foreach (var (hook, scope, passes) in CombatLogHooks)
            {
                var originals = typeof(Hook).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == hook && typeof(Task).IsAssignableFrom(m.ReturnType)).ToList();
                if (originals.Count == 0) throw new MissingMethodException(nameof(Hook), hook);
                var passRows = passes.Select(pass =>
                {
                    var baseMethod = typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => m.Name == pass).ToList();
                    if (baseMethod.Count != 1) throw new MissingMethodException(nameof(AbstractModel), $"{pass} ({baseMethod.Count} found)");
                    return (pass, baseMethod[0].GetParameters().Select(p => p.ParameterType).ToArray());
                }).ToArray();
                _combatLogHookTable[hook] = (scope, passRows);
                hookMethods.AddRange(originals);
            }
            foreach (var original in hookMethods)
                harmony.Patch(original, prefix: M(nameof(CombatLogHookStarting)));
        });

        Group("modify", () =>
        {
            harmony.Patch(Declared(typeof(Hook), nameof(Hook.ModifyHandDraw), typeof(ICombatState), typeof(Player), typeof(decimal), typeof(IEnumerable<AbstractModel>).MakeByRefType()),
                postfix: M(nameof(CombatLogHandDrawModified)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.ModifyHpLost)) ?? throw new MissingMethodException(nameof(Hook), "ModifyHpLost"),
                postfix: M(nameof(CombatLogHpLostModified)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.ModifyPowerAmountGiven)) ?? throw new MissingMethodException(nameof(Hook), "ModifyPowerAmountGiven"),
                postfix: M(nameof(CombatLogPowerAmountGivenModified)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.ModifyPowerAmountReceived)) ?? throw new MissingMethodException(nameof(Hook), "ModifyPowerAmountReceived"),
                postfix: M(nameof(CombatLogPowerAmountReceivedModified)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.ModifyCardPlayResultLocation)) ?? throw new MissingMethodException(nameof(Hook), "ModifyCardPlayResultLocation"),
                postfix: M(nameof(CombatLogCardPlayResultLocationModified)));
        });

        Group("piles", () =>
        {
            harmony.Patch(Declared(typeof(CardPileCmd), nameof(CardPileCmd.Add), typeof(IEnumerable<CardModel>), typeof(CardPile),
                    typeof(CardPilePosition), typeof(AbstractModel), typeof(bool), typeof(bool)),
                prefix: M(nameof(CombatLogPileAddStarting)), postfix: M(nameof(CombatLogPileAddReturned)));
            harmony.Patch(Declared(typeof(CardPile), nameof(CardPile.RemoveInternal), typeof(CardModel), typeof(bool)),
                prefix: M(nameof(CombatLogCardRemovedFromPile)));
            harmony.Patch(Declared(typeof(CardPile), nameof(CardPile.AddInternal), typeof(CardModel), typeof(int), typeof(bool)),
                prefix: M(nameof(CombatLogCardAddedToPile)));
            harmony.Patch(Declared(typeof(CardPileCmd), nameof(CardPileCmd.RemoveFromCombat), typeof(IEnumerable<CardModel>), typeof(bool)),
                prefix: M(nameof(CombatLogRemovingFromCombat)));
            harmony.Patch(Declared(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle), typeof(PlayerChoiceContext), typeof(Player)),
                prefix: M(nameof(CombatLogShuffleStarting)), postfix: M(nameof(CombatLogShuffleReturned)));
            harmony.Patch(Declared(typeof(CardPileCmd), nameof(CardPileCmd.ShuffleIfNecessary), typeof(PlayerChoiceContext), typeof(Player)),
                prefix: M(nameof(CombatLogShuffleIfNecessaryStarting)), postfix: M(nameof(CombatLogShuffleIfNecessaryReturned)));
        });

        Group("cards", () =>
        {
            harmony.Patch(Declared(typeof(CardCmd), nameof(CardCmd.Exhaust), typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)),
                prefix: M(nameof(CombatLogExhausting)));
            harmony.Patch(Declared(typeof(CardCmd), nameof(CardCmd.DiscardAndDraw), typeof(PlayerChoiceContext), typeof(IEnumerable<CardModel>), typeof(int)),
                prefix: M(nameof(CombatLogDiscardAndDrawing)));
            // CardTransformation.GetReplacement has one overload (its argument is the transform's random source).
            harmony.Patch(AccessTools.DeclaredMethod(typeof(CardTransformation), nameof(CardTransformation.GetReplacement))
                    ?? throw new MissingMethodException(nameof(CardTransformation), nameof(CardTransformation.GetReplacement)),
                postfix: M(nameof(CombatLogTransformReplacement)));
        });

        Group("creatures", () =>
        {
            harmony.Patch(Declared(typeof(CreatureCmd), nameof(CreatureCmd.Add), typeof(Creature)), prefix: M(nameof(CombatLogCreatureAdding)));
            harmony.Patch(Declared(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(CardModel), typeof(bool)),
                prefix: M(nameof(CombatLogCreaturesKilling)));
            harmony.Patch(Declared(typeof(CreatureCmd), nameof(CreatureCmd.Escape), typeof(Creature), typeof(bool)), prefix: M(nameof(CombatLogCreatureEscaping)));
            foreach (var (name, parameters) in new (string, Type[])[]
            {
                (nameof(CreatureCmd.Heal), new[] { typeof(Creature), typeof(decimal), typeof(bool) }),
                (nameof(CreatureCmd.SetCurrentHp), new[] { typeof(Creature), typeof(decimal) }),
                (nameof(CreatureCmd.GainMaxHp), new[] { typeof(Creature), typeof(decimal) }),
                (nameof(CreatureCmd.LoseMaxHp), new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(CardModel) }),
                (nameof(CreatureCmd.SetMaxHp), new[] { typeof(Creature), typeof(decimal) }),
                (nameof(CreatureCmd.SetMaxAndCurrentHp), new[] { typeof(Creature), typeof(decimal) }),
                (nameof(CreatureCmd.LoseBlock), new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(Creature) }),
            })
                harmony.Patch(Declared(typeof(CreatureCmd), name, parameters), prefix: M(nameof(CombatLogCreatureValueChanging)));
        });

        Group("energy", () =>
        {
            _playerCombatStatePlayer = AccessTools.Field(typeof(PlayerCombatState), "_player")
                ?? throw new MissingFieldException(nameof(PlayerCombatState), "_player");
            var setter = AccessTools.PropertySetter(typeof(PlayerCombatState), nameof(PlayerCombatState.Energy))
                ?? throw new MissingMethodException(nameof(PlayerCombatState), "set_Energy");
            harmony.Patch(setter, prefix: M(nameof(CombatLogEnergySetting)), postfix: M(nameof(CombatLogEnergySet)));
        });

        Group("potions", () =>
        {
            harmony.Patch(Declared(typeof(PotionCmd), nameof(PotionCmd.TryToProcure), typeof(PotionModel), typeof(Player), typeof(int)),
                prefix: M(nameof(CombatLogPotionProcuring)));
            harmony.Patch(Declared(typeof(Player), "AddPotionInternal", typeof(PotionModel), typeof(int), typeof(bool)),
                postfix: M(nameof(CombatLogPotionAdded)));
            harmony.Patch(Declared(typeof(PotionCmd), nameof(PotionCmd.Discard), typeof(PotionModel)), prefix: M(nameof(CombatLogPotionDiscarding)));
        });

        Group("powers", () =>
        {
            harmony.Patch(Declared(typeof(PowerCmd), nameof(PowerCmd.Apply), typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
                    typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)),
                prefix: M(nameof(CombatLogPowerApplying)));
            harmony.Patch(Declared(typeof(PowerCmd), nameof(PowerCmd.ModifyAmount), typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal),
                    typeof(Creature), typeof(CardModel), typeof(bool)),
                prefix: M(nameof(CombatLogPowerModifying)));
            harmony.Patch(Declared(typeof(PowerCmd), nameof(PowerCmd.Decrement), typeof(PowerModel)), prefix: M(nameof(CombatLogPowerDecrementing)));
            harmony.Patch(Declared(typeof(PowerCmd), nameof(PowerCmd.TickDownDuration), typeof(PowerModel)), prefix: M(nameof(CombatLogPowerTickingDown)));
            harmony.Patch(Declared(typeof(PowerCmd), nameof(PowerCmd.Remove), typeof(PowerModel)), prefix: M(nameof(CombatLogPowerRemoving)));
        });

        Group("flash", () =>
        {
            harmony.Patch(Declared(typeof(RelicModel), nameof(RelicModel.Flash), typeof(IEnumerable<Creature>)), prefix: M(nameof(CombatLogModelFlashing)));
            harmony.Patch(Declared(typeof(PowerModel), "Flash"), prefix: M(nameof(CombatLogModelFlashing)));
        });

        Group("phases", () =>
        {
            // Resolve all first: a stale entry patches nothing rather than half of the phases.
            var originals = CombatLogPhases.Select(p => ResolveCombatLogPhase(p.Method, p.Parameters)).ToList();
            foreach (var original in originals)
                harmony.Patch(original, prefix: M(nameof(CombatLogPhaseStarting)));
        });
    }

    // ---- History (the public recording methods of CombatHistory) ----

    // CombatHistory's public void instance methods other than Clear (v0.113.1: CardPlayStarted … Summoned, 18 methods):
    // each builds one *Entry and passes it to the private Add. A new recording method is picked up by name.
    internal static List<MethodInfo> CombatLogHistoryRecorders() =>
        typeof(CombatHistory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(void) && !m.IsSpecialName && m.Name != nameof(CombatHistory.Clear))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    private static void CombatLogHistoryRecording(CombatHistory __instance, out int __state)
    {
        __state = -1;
        try { __state = __instance.Entries.Count(); }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] combat_log history count failed: {ex.Message}"); }
    }

    // CombatHistory.Add keeps the entry only while combatState.IsLiveCombat(): exactly one new entry means it was kept.
    private static void CombatLogHistoryRecorded(MethodBase __originalMethod, CombatHistory __instance, int __state)
    {
        CombatHistoryEntry? entry = null;
        try
        {
            if (__state < 0) throw new InvalidOperationException("entry count before the call was not read");
            int count = __instance.Entries.Count();
            if (count == __state) return;
            if (count != __state + 1) throw new InvalidOperationException($"{count - __state} entries added by one call");
            entry = __instance.Entries.Last();
        }
        catch (Exception ex)
        {
            if (CombatManager.Instance?.DebugOnlyGetState() is { } combat)
                CombatLogStore.MarkBroken(combat, CombatLogCombatId(), $"history {__originalMethod.Name}: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        LogCombatRow(HistoryLogKind(entry), (row, _) => FillHistoryRow(row, entry));
    }

    private static string HistoryLogKind(CombatHistoryEntry entry) => entry switch
    {
        CardDrawnEntry => "draw",
        CardGeneratedEntry => "generated",
        CardPlayStartedEntry or CardPlayFinishedEntry => "play",
        CardDiscardedEntry => "card_discarded",
        CardExhaustedEntry => "card_exhausted",
        CardAfflictedEntry => "card_afflicted",
        BlockGainedEntry => "block_gained",
        DamageReceivedEntry => "damage_received",
        CreatureAttackedEntry => "attacked",
        EnergySpentEntry => "energy_spent",
        MonsterPerformedMoveEntry => "monster_move",
        PotionUsedEntry => "potion_used",
        PowerReceivedEntry => "power_received",
        _ => "history"
    };

    private static void FillHistoryRow(Dictionary<string, object?> row, CombatHistoryEntry entry)
    {
        row["source"] = "history";
        row["entry"] = entry.GetType().Name;
        row["actor"] = LogCreatureRef(entry.Actor);
        switch (entry)
        {
            case CardDrawnEntry drawn:
                row["card"] = LogCardRef(drawn.Card);
                row["from_hand_draw"] = drawn.FromHandDraw;
                break;
            case CardGeneratedEntry generated:
                row["card"] = LogCardRef(generated.Card);
                row["creator_player_id"] = generated.Creator?.NetId.ToString();
                break;
            case CardPlayStartedEntry started:
                foreach (var (key, value) in BuildCardPlayEvent("started", started.CardPlay)) row[key] = value;
                break;
            case CardPlayFinishedEntry finished:
                foreach (var (key, value) in BuildCardPlayEvent("finished", finished.CardPlay)) row[key] = value;
                break;
            case CardDiscardedEntry discarded:
                row["card"] = LogCardRef(discarded.Card);
                break;
            case CardExhaustedEntry exhausted:
                row["card"] = LogCardRef(exhausted.Card);
                break;
            case CardAfflictedEntry afflicted:
                row["card"] = LogCardRef(afflicted.Card);
                row["affliction_id"] = afflicted.Affliction.Id.Entry;
                break;
            case BlockGainedEntry block:
                row["amount"] = block.Amount;
                row["props"] = block.Props.ToString();
                row["card_play_uid"] = block.CardPlay is { } blockPlay ? GetStableCardUid(blockPlay.Card) : null;
                break;
            case DamageReceivedEntry damage:
                row["dealer"] = LogCreatureRef(damage.Dealer);
                row["card_source"] = LogCardRef(damage.CardSource);
                row["result"] = LogDamageResult(damage.Result);
                break;
            case CreatureAttackedEntry attacked:
                row["hits"] = attacked.DamageResults.Select(LogDamageResult).ToList();
                break;
            case EnergySpentEntry spent:
                row["amount"] = spent.Amount;
                break;
            case MonsterPerformedMoveEntry move:
                row["monster_id"] = move.Monster.Id.Entry;
                row["move_id"] = move.Move.StateId;
                row["targets"] = move.Targets?.Select(LogCreatureRef).ToList();
                break;
            case PotionUsedEntry potion:
                row["potion_id"] = potion.Potion.Id.Entry;
                row["target"] = LogCreatureRef(potion.Target);
                break;
            case PowerReceivedEntry power:
                row["power"] = LogPowerRef(power.Power);
                row["amount"] = power.Amount;
                row["applier"] = LogCreatureRef(power.Applier);
                break;
        }
    }

    private static Dictionary<string, object?> LogDamageResult(DamageResult result) => new()
    {
        ["receiver"] = LogCreatureRef(result.Receiver),
        // Hook.ModifyPreBlockDamageTarget / ModifyUnblockedDamageTarget: who the damage was aimed at before a redirect.
        ["original_target"] = LogCreatureRef(result.OriginalTarget),
        ["props"] = result.Props.ToString(),
        ["blocked"] = result.BlockedDamage,
        ["unblocked"] = result.UnblockedDamage,
        ["overkill"] = result.OverkillDamage,
        ["block_broken"] = result.WasBlockBroken,
        ["fully_blocked"] = result.WasFullyBlocked,
        ["killed"] = result.WasTargetKilled
    };

    // ---- Hook notifications ----

    // Prefix of each Hook method in CombatLogHooks: one row when the notification begins, listing per pass the
    // listeners that override that pass's method (others run AbstractModel's no-op). The list is the one each pass
    // would take now; a pass builds its own list when it starts, so a listener an earlier pass adds or removes can
    // differ. Draw-pile listeners never reveal their place (BuildHookListenerRows).
    private static void CombatLogHookStarting(MethodBase __originalMethod, object[] __args)
    {
        if (!_combatLogHookTable.TryGetValue(__originalMethod.Name, out var hook)) return;
        LogCombatRowIf("hook", (row, live) =>
        {
            var parameters = __originalMethod.GetParameters();
            ICombatState? combat = __args.OfType<ICombatState>().FirstOrDefault();
            IRunState? run = __args.OfType<IRunState>().FirstOrDefault();
            List<AbstractModel> listeners;
            if (hook.Scope == "run")
                listeners = (run ?? live.RunState).IterateHookListeners(combat).ToList();
            else if (hook.Scope == "combat" && CombatManager.Instance.ShouldSkipCombatEffect)
                listeners = new List<AbstractModel>();
            else
                listeners = (combat ?? live).IterateHookListeners().ToList();
            // Most notifications reach no listener that overrides them: no row, and nothing else is built.
            if (!listeners.Any(model => hook.Passes.Any(pass => HookOverrideDeclarer(model.GetType(), pass.Method, pass.Parameters) != null)))
                return false;

            var reasons = new List<string>();
            var args = new Dictionary<string, object?>();
            for (int i = 0; i < parameters.Length && i < __args.Length; i++)
            {
                var type = parameters[i].ParameterType;
                if (type == typeof(ICombatState) || type == typeof(IRunState) || type == typeof(PlayerChoiceContext)
                    || __args[i] is ICombatState or IRunState or PlayerChoiceContext) continue;
                args[LogSnake(parameters[i].Name ?? $"arg{i}")] = LogHookArgument(__args[i], reasons);
            }
            var passes = new List<Dictionary<string, object?>>();
            foreach (var (method, passParameters) in hook.Passes)
            {
                var rows = BuildHookListenerRows(listeners, (__originalMethod.Name, method, passParameters, hook.Scope == "run"), reasons);
                passes.Add(new Dictionary<string, object?> { ["method"] = method, ["listeners"] = rows });
            }
            row["hook"] = __originalMethod.Name;
            row["scope"] = hook.Scope;
            row["args"] = args;
            row["passes"] = passes;
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
            return true;
        });
    }

    private static object? LogHookArgument(object? value, List<string> reasons) => value switch
    {
        null => null,
        CardModel card => LogCardRef(card),
        Creature creature => LogCreatureRef(creature),
        Player player => new Dictionary<string, object?> { ["player_id"] = player.NetId.ToString() },
        PowerModel power => LogPowerRef(power),
        PotionModel potion => LogPotionRef(potion),
        CardPlay play => new Dictionary<string, object?>
        {
            ["uid"] = GetStableCardUid(play.Card), ["play_index"] = play.PlayIndex, ["play_count"] = play.PlayCount,
            ["is_auto_play"] = play.IsAutoPlay
        },
        DamageResult result => LogDamageResult(result),
        AbstractModel model => LogModelRef(model, reasons),
        IEnumerable<AbstractModel> models => LogModelRefs(models, reasons),
        IEnumerable<DamageResult> results => results.Select(LogDamageResult).ToList(),
        IEnumerable<Creature> creatures => creatures.Select(LogCreatureRef).ToList(),
        bool or int or long or decimal or float or double or string => value,
        Enum e => e.ToString(),
        _ => value.GetType().Name
    };

    // ---- Decision-time values (Hook.Modify*) ----

    // Hook.ModifyHandDraw: called once per player turn by CombatManager.SetupPlayerTurn.
    private static void CombatLogHandDrawModified(decimal __result, Player player, decimal originalCardCount, ref IEnumerable<AbstractModel> modifiers)
    {
        var mods = modifiers?.ToList();
        LogCombatRow("modify", (row, _) =>
        {
            var reasons = new List<string>();
            row["hook"] = "ModifyHandDraw";
            row["player_id"] = player.NetId.ToString();
            row["original"] = originalCardCount;
            row["result"] = __result;
            row["modifiers"] = LogModelRefs(mods, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    // Hook.ModifyHpLost: only CreatureCmd.Damage calls it. A row only when some listener changed the amount.
    private static void CombatLogHpLostModified(decimal __result, Creature target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource, HpLossHookPhase phases, ref IEnumerable<AbstractModel> modifiers)
    {
        var mods = modifiers?.ToList();
        if (mods == null || mods.Count == 0) return;
        LogCombatRow("modify", (row, _) =>
        {
            var reasons = new List<string>();
            row["hook"] = "ModifyHpLost";
            row["phase"] = phases.ToString();
            row["target"] = LogCreatureRef(target);
            row["dealer"] = LogCreatureRef(dealer);
            row["card_source"] = LogCardRef(cardSource);
            row["props"] = props.ToString();
            row["original"] = amount;
            row["result"] = __result;
            row["modifiers"] = LogModelRefs(mods, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    // Hook.ModifyPowerAmountGiven: PowerCmd.Apply / ModifyAmount pass the mutable power being applied; PowerVar's card
    // preview passes the canonical model (ModelDb.Power<T>()), which is not an event and is skipped.
    private static void CombatLogPowerAmountGivenModified(decimal __result, PowerModel power, Creature giver, decimal amount,
        Creature? target, CardModel? cardSource, ref IEnumerable<AbstractModel> modifiers)
    {
        var mods = modifiers?.ToList();
        if (!power.IsMutable || mods == null || mods.Count == 0) return;
        LogPowerAmountModify("ModifyPowerAmountGiven", power, giver, target, amount, __result, mods);
    }

    private static void CombatLogPowerAmountReceivedModified(decimal __result, PowerModel canonicalPower, Creature target, decimal amount,
        Creature? giver, ref IEnumerable<AbstractModel> modifiers)
    {
        var mods = modifiers?.ToList();
        if (!canonicalPower.IsMutable || mods == null || mods.Count == 0) return;
        LogPowerAmountModify("ModifyPowerAmountReceived", canonicalPower, giver, target, amount, __result, mods);
    }

    private static void LogPowerAmountModify(string hook, PowerModel power, Creature? giver, Creature? target, decimal amount,
        decimal result, List<AbstractModel> mods)
    {
        LogCombatRow("modify", (row, _) =>
        {
            var reasons = new List<string>();
            row["hook"] = hook;
            row["power_id"] = power.Id.Entry;
            row["giver"] = LogCreatureRef(giver);
            row["target"] = LogCreatureRef(target);
            row["original"] = amount;
            row["result"] = result;
            row["modifiers"] = LogModelRefs(mods, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    // Hook.ModifyCardPlayResultLocation: CardModel's play decides where the played card goes (Corruption, Rebound...).
    private static void CombatLogCardPlayResultLocationModified(CardLocation __result, CardModel card, bool isAutoPlay,
        CardLocation cardLocation, ref IEnumerable<AbstractModel> modifiers)
    {
        var mods = modifiers?.ToList();
        if (mods == null || mods.Count == 0) return;
        LogCombatRow("modify", (row, _) =>
        {
            var reasons = new List<string>();
            row["hook"] = "ModifyCardPlayResultLocation";
            row["card"] = LogCardRef(card);
            row["is_auto_play"] = isAutoPlay;
            row["original"] = new Dictionary<string, object?> { ["pile"] = cardLocation.pileType.ToString(), ["position"] = cardLocation.position.ToString().ToLowerInvariant() };
            row["result"] = new Dictionary<string, object?> { ["pile"] = __result.pileType.ToString(), ["position"] = __result.position.ToString().ToLowerInvariant() };
            row["modifiers"] = LogModelRefs(mods, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    // ---- Pile moves ----

    private static void CombatLogPileAddStarting(IEnumerable<CardModel> cards, CardPile newPile, CardPilePosition position,
        AbstractModel? clonedBy, out PileAddState? __state)
    {
        __state = null;
        var previousScope = _pileAddScope.Value;
        var previousSuppressed = _combatLogSuppressed.Value;
        SuppressedRows? suppressed = null;
        try
        {
            // Fail closed: once the shuffle's one-shot mark is used, its rows are hidden before anything else can throw.
            if (_shuffleAddPending.Value?.TryUse() == true && previousSuppressed == null)
            {
                suppressed = new SuppressedRows();
                _combatLogSuppressed.Value = suppressed;
            }
            __state = new PileAddState { PreviousScope = previousScope, PreviousSuppressed = previousSuppressed, Suppressed = suppressed };
            _pileAddScope.Value = new PileAddScope
            {
                Position = position.ToString().ToLowerInvariant(),
                Requested = newPile.Type,
                ClonedBy = clonedBy,
                Cards = cards is IReadOnlyCollection<CardModel> collection
                    ? new HashSet<CardModel>(collection, ReferenceEqualityComparer.Instance) : null
            };
        }
        catch (Exception ex)
        {
            // The postfix still puts the caller's scopes back; the suppression set above stays for this Add.
            __state ??= new PileAddState { PreviousScope = previousScope, PreviousSuppressed = previousSuppressed, Suppressed = suppressed };
            GD.PrintErr($"[STS2 MCP] combat_log pile scope failed: {ex.Message}");
        }
    }

    // The sync part of CardPileCmd.Add has returned: the caller's scopes come back (its continuations keep the ones set
    // above). The shuffle's Add writes one hidden_rows row with the count once its Task completes.
    private static void CombatLogPileAddReturned(Task<IReadOnlyList<CardPileAddResult>> __result, PileAddState? __state)
    {
        if (__state == null) return;
        _pileAddScope.Value = __state.PreviousScope;
        _combatLogSuppressed.Value = __state.PreviousSuppressed;
        if (__state.Suppressed is not { } suppressed) return;
        void Summarize()
        {
            if (suppressed.Count == 0) return;
            LogCombatRow("hidden_rows", (row, _) =>
            {
                row["reason"] = "shuffle_order";
                row["count"] = suppressed.Count;
            });
        }
        if (__result == null || __result.IsCompleted) { Summarize(); return; }
        __result.ContinueWith(_ => Summarize(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CardModel.RemoveFromCurrentPile → CardPile.RemoveInternal runs right before CardPile.AddInternal for the same card.
    private static void CombatLogCardRemovedFromPile(CardPile __instance, CardModel card)
    {
        _lastRemovedCard = card;
        _lastRemovedPile = __instance.Type;
    }

    // CardPile.AddInternal is where a card lands (CardPileCmd.Add sends a card to Discard instead of a full Hand). Only
    // moves made by CardPileCmd.Add and the manual play's Hand → Play move (CardPileCmd.AddDuringManualCardPlay) are
    // rows: CardCmd.Transform has its own card_change row, and the combat setup / deck adds are not fight events.
    // The index is never published: CardPilePosition.Random's index comes from the shuffle RNG.
    private static void CombatLogCardAddedToPile(CardPile __instance, CardModel card)
    {
        PileType? from = ReferenceEquals(_lastRemovedCard, card) ? _lastRemovedPile : null;
        _lastRemovedCard = null;
        var scope = _pileAddScope.Value;
        // A card that is not one of the enclosing Add's cards is landed by another command in one of its listeners.
        if (scope?.Cards != null && !scope.Cards.Contains(card)) scope = null;
        if (scope == null && __instance.Type != PileType.Play) return;
        if (!__instance.IsCombatPile || !CombatManager.Instance.IsInProgress) return;
        var to = __instance.Type;
        LogCombatRow("pile_move", (row, _) =>
        {
            // A move inside the draw pile names a card whose place there is hidden.
            bool hidden = from == PileType.Draw && to == PileType.Draw;
            row["card"] = hidden ? null : new Dictionary<string, object?> { ["card_id"] = card.Id.Entry, ["uid"] = GetStableCardUid(card) };
            if (hidden) row["hidden_reason"] = "draw_pile";
            row["owner_player_id"] = card.Owner?.NetId.ToString();
            row["from"] = from?.ToString();
            row["to"] = to.ToString();
            row["requested_to"] = scope?.Requested.ToString() ?? to.ToString();
            row["position"] = scope?.Position ?? "bottom";
            row["via"] = scope == null ? "manual_play" : "card_pile_cmd_add";
            var reasons = new List<string>();
            row["cloned_by"] = LogModelRef(scope?.ClonedBy, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    private static void CombatLogRemovingFromCombat(IEnumerable<CardModel> cards)
    {
        var list = cards?.ToList();
        if (list == null || list.Count == 0) return;
        LogCombatRow("removed_from_combat", (row, _) =>
        {
            row["cards"] = list.Select(card => card.Pile?.Type == PileType.Draw
                ? new Dictionary<string, object?> { ["card_id"] = null, ["uid"] = null, ["pile"] = "Draw", ["hidden_reason"] = "draw_pile" }
                : LogCardRef(card)).ToList();
        });
    }

    private static void CombatLogShuffleStarting(Player player, out OneShot? __state)
    {
        __state = _shuffleAddPending.Value;
        string reason = _shuffleIfNecessaryPending.Value?.TryUse() == true ? "draw_pile_empty" : "explicit";
        try
        {
            if (!CombatManager.Instance.IsOverOrEnding)
            {
                int discard = PileType.Discard.GetPile(player).Cards.Count;
                int draw = PileType.Draw.GetPile(player).Cards.Count;
                LogCombatRow("shuffle", (row, _) =>
                {
                    row["player_id"] = player.NetId.ToString();
                    row["reason"] = reason;
                    row["discard_count"] = discard;
                    row["draw_count"] = draw;
                });
            }
            _shuffleAddPending.Value = new OneShot();
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] combat_log shuffle failed: {ex.Message}"); }
    }

    private static void CombatLogShuffleReturned(OneShot? __state) => _shuffleAddPending.Value = __state;

    private static void CombatLogShuffleIfNecessaryStarting(out OneShot? __state)
    {
        __state = _shuffleIfNecessaryPending.Value;
        _shuffleIfNecessaryPending.Value = new OneShot();
    }

    private static void CombatLogShuffleIfNecessaryReturned(OneShot? __state) => _shuffleIfNecessaryPending.Value = __state;

    // ---- Card commands ----

    private static void CombatLogExhausting(CardModel card, bool causedByEthereal)
    {
        if (CombatManager.Instance.IsOverOrEnding) return;
        LogCombatRow("exhaust", (row, _) =>
        {
            row["card"] = LogCardRef(card);
            row["caused_by_ethereal"] = causedByEthereal;
        });
    }

    private static void CombatLogDiscardAndDrawing(IEnumerable<CardModel> cardsToDiscard, int cardsToDraw)
    {
        if (CombatManager.Instance.IsOverOrEnding) return;
        var list = cardsToDiscard?.ToList() ?? new List<CardModel>();
        if (list.Count == 0) return;
        LogCombatRow("discard_and_draw", (row, _) =>
        {
            row["cards"] = list.Select(LogCardRef).ToList();
            row["draw_count"] = cardsToDraw;
        });
    }

    // CardCmd.Transform asks each CardTransformation for its replacement right before moving it into the original's place.
    private static void CombatLogTransformReplacement(ref CardTransformation __instance, CardModel? __result)
    {
        if (__result == null || !CombatManager.Instance.IsInProgress) return;
        var original = __instance.Original;
        if (original.Pile is not { IsCombatPile: true } pile) return;
        LogCombatRow("card_change", (row, _) =>
        {
            row["change"] = "transform";
            row["pile"] = pile.Type.ToString();
            row["from"] = LogCardRef(original);
            row["to"] = new Dictionary<string, object?> { ["card_id"] = __result.Id.Entry, ["uid"] = GetStableCardUid(__result) };
        });
    }

    // ---- Creatures ----

    private static void CombatLogCreatureAdding(Creature creature)
    {
        if (!CombatManager.Instance.IsInProgress) return;
        LogCombatRow("creature", (row, _) =>
        {
            row["action"] = "added";
            row["creature"] = LogCreatureRef(creature);
            row["monster_id"] = creature.Monster?.Id.Entry;
            row["creature_side"] = creature.Side.ToString().ToLowerInvariant();
        });
    }

    private static void CombatLogCreaturesKilling(IReadOnlyCollection<Creature> creatures, CardModel? cardSource, bool force)
    {
        if (creatures == null || creatures.Count == 0) return;
        var list = creatures.ToList();
        LogCombatRow("creature", (row, _) =>
        {
            row["action"] = "kill";
            row["creatures"] = list.Select(LogCreatureRef).ToList();
            row["card_source"] = LogCardRef(cardSource);
            row["force"] = force;
        });
    }

    private static void CombatLogCreatureEscaping(Creature creature)
    {
        LogCombatRow("creature", (row, _) =>
        {
            row["action"] = "escape";
            row["creature"] = LogCreatureRef(creature);
        });
    }

    // CreatureCmd.Heal / SetCurrentHp / GainMaxHp / LoseMaxHp / SetMaxHp / SetMaxAndCurrentHp / LoseBlock: what was asked and
    // the values before. The values after are the next state's (or the next row's).
    private static void CombatLogCreatureValueChanging(MethodBase __originalMethod, object[] __args)
    {
        string method = __originalMethod.Name;
        // The target is the first Creature argument (LoseBlock's remover comes after it), the amount the decimal one.
        var creature = __args.OfType<Creature>().FirstOrDefault();
        var amount = __args.OfType<decimal>().FirstOrDefault();
        var remover = method == nameof(CreatureCmd.LoseBlock) ? __args.OfType<Creature>().Skip(1).FirstOrDefault() : null;
        if (creature == null) return;
        LogCombatRow(method == nameof(CreatureCmd.LoseBlock) ? "block" : "hp", (row, _) =>
        {
            row["action"] = LogSnake(method);
            row["target"] = LogCreatureRef(creature);
            row["amount"] = amount;
            row["hp_before"] = creature.CurrentHp;
            row["max_hp_before"] = creature.MaxHp;
            row["block_before"] = creature.Block;
            if (remover != null) row["remover"] = LogCreatureRef(remover);
        });
    }

    // ---- Energy (PlayerCombatState.Energy's setter: every change, whatever command made it) ----

    private static void CombatLogEnergySetting(PlayerCombatState __instance, out int __state) => __state = __instance.Energy;

    private static void CombatLogEnergySet(PlayerCombatState __instance, int __state)
    {
        int after = __instance.Energy;
        if (after == __state) return;
        LogCombatRow("energy", (row, _) =>
        {
            row["player_id"] = (_playerCombatStatePlayer?.GetValue(__instance) as Player)?.NetId.ToString();
            row["before"] = __state;
            row["after"] = after;
        });
    }

    // ---- Potions ----

    private static void CombatLogPotionProcuring(PotionModel potion, Player player, int slotIndex)
    {
        LogCombatRow("potion", (row, _) =>
        {
            row["action"] = "procure";
            row["potion_id"] = potion.Id.Entry;
            row["player_id"] = player.NetId.ToString();
            row["slot_requested"] = slotIndex;
        });
    }

    private static void CombatLogPotionAdded(Player __instance, PotionProcureResult __result)
    {
        if (__result == null) return;
        LogCombatRow("potion", (row, _) =>
        {
            row["action"] = "added";
            row["potion"] = LogPotionRef(__result.potion);
            row["player_id"] = __instance.NetId.ToString();
            row["success"] = __result.success;
            row["failure_reason"] = __result.success ? null : __result.failureReason.ToString();
        });
    }

    private static void CombatLogPotionDiscarding(PotionModel potion)
    {
        LogCombatRow("potion", (row, _) =>
        {
            row["action"] = "discard";
            row["potion"] = LogPotionRef(potion);
        });
    }

    // ---- Powers ----

    private static void CombatLogPowerApplying(PowerModel power, Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        LogCombatRow("power", (row, _) =>
        {
            row["action"] = "apply";
            row["power_id"] = power.Id.Entry;
            row["target"] = LogCreatureRef(target);
            row["amount"] = amount;
            row["applier"] = LogCreatureRef(applier);
            row["card_source"] = LogCardRef(cardSource);
        });
    }

    private static void CombatLogPowerModifying(PowerModel power, decimal offset, Creature? applier, CardModel? cardSource)
    {
        LogCombatRow("power", (row, _) =>
        {
            row["action"] = "modify_amount";
            row["power"] = LogPowerRef(power);
            row["offset"] = offset;
            row["applier"] = LogCreatureRef(applier);
            row["card_source"] = LogCardRef(cardSource);
        });
    }

    private static void CombatLogPowerDecrementing(PowerModel power)
    {
        LogCombatRow("power", (row, _) =>
        {
            row["action"] = "decrement";
            row["power"] = LogPowerRef(power);
        });
    }

    // PowerCmd.TickDownDuration skips one tick when SkipNextDurationTick (a debuff the enemy applied in its turn).
    private static void CombatLogPowerTickingDown(PowerModel power)
    {
        LogCombatRow("power", (row, _) =>
        {
            row["action"] = "tick_down";
            row["power"] = LogPowerRef(power);
            row["skipped"] = power.SkipNextDurationTick;
        });
    }

    private static void CombatLogPowerRemoving(PowerModel? power)
    {
        if (power == null) return;
        LogCombatRow("power", (row, _) =>
        {
            row["action"] = "remove";
            row["power"] = LogPowerRef(power);
        });
    }

    // ---- Flash (RelicModel.Flash / PowerModel.Flash: the model shows it acted) ----

    private static void CombatLogModelFlashing(AbstractModel __instance)
    {
        LogCombatRow("flash", (row, _) =>
        {
            var reasons = new List<string>();
            row["model"] = LogModelRef(__instance, reasons);
            if (reasons.Count > 0) row["unavailable_reasons"] = reasons;
        });
    }

    // ---- Turn phases (CombatManager) ----

    private static void CombatLogPhaseStarting(MethodBase __originalMethod, object[] __args)
    {
        LogCombatRow("turn_phase", (row, _) =>
        {
            row["phase"] = LogSnake(__originalMethod.Name);
            foreach (var arg in __args)
            {
                if (arg is Player player) row["player_id"] = player.NetId.ToString();
                else if (arg is CardModel card) row["card"] = LogCardRef(card);
            }
        });
    }
}
