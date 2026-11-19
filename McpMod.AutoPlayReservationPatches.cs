using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string AutoPlayReservationHarmonyId = "com.sts2mcp.autoplayreservation";
    private const string EndedCombatLossHarmonyId = "com.sts2mcp.endedcombatloss";

    // Read-only patches (v0.113.1, decompile sts2-a671e9ef). None skips, reorders or repeats a game call, and
    // none draws from an RNG. The one argument they touch is AutoPlayFromDrawPile's cardModification: it is
    // replaced by a delegate that calls the original with the same card first, then notes the card.
    private static void InstallAutoPlayReservationTracking()
    {
        var harmony = new Harmony(AutoPlayReservationHarmonyId);
        try
        {
            var autoPlayFromDrawPile = AccessTools.DeclaredMethod(typeof(CardPileCmd), nameof(CardPileCmd.AutoPlayFromDrawPile),
                    new[] { typeof(PlayerChoiceContext), typeof(Player), typeof(int), typeof(CardPilePosition), typeof(bool), typeof(Action<CardModel>) })
                ?? throw new MissingMethodException(nameof(CardPileCmd), "AutoPlayFromDrawPile(6)");
            harmony.Patch(autoPlayFromDrawPile,
                prefix: new HarmonyMethod(typeof(McpMod), nameof(AutoPlayFromDrawPileStarting)),
                postfix: new HarmonyMethod(typeof(McpMod), nameof(AutoPlayFromDrawPileReturned)));

            var addInternal = AccessTools.DeclaredMethod(typeof(CardPile), nameof(CardPile.AddInternal),
                    new[] { typeof(CardModel), typeof(int), typeof(bool) })
                ?? throw new MissingMethodException(nameof(CardPile), "AddInternal");
            harmony.Patch(addInternal, postfix: new HarmonyMethod(typeof(McpMod), nameof(CardPileAddedInternal)));

            var autoPlay = AccessTools.DeclaredMethod(typeof(CardCmd), nameof(CardCmd.AutoPlay),
                    new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(Creature), typeof(AutoPlayType), typeof(bool), typeof(bool) })
                ?? throw new MissingMethodException(nameof(CardCmd), "AutoPlay");
            harmony.Patch(autoPlay,
                prefix: new HarmonyMethod(typeof(McpMod), nameof(CardAutoPlayStarting)),
                postfix: new HarmonyMethod(typeof(McpMod), nameof(CardAutoPlayReturned)));

            var playStarted = AccessTools.DeclaredMethod(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted),
                    new[] { typeof(ICombatState), typeof(CardPlay) })
                ?? throw new MissingMethodException(nameof(CombatHistory), "CardPlayStarted");
            harmony.Patch(playStarted, prefix: new HarmonyMethod(typeof(McpMod), nameof(HistoryCardPlayStarting)));

            var clear = AccessTools.DeclaredMethod(typeof(CombatHistory), nameof(CombatHistory.Clear), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(CombatHistory), "Clear");
            harmony.Patch(clear,
                prefix: new HarmonyMethod(typeof(McpMod), nameof(HistoryClearing)),
                postfix: new HarmonyMethod(typeof(McpMod), nameof(HistoryCleared)));

            AutoPlayReservations.TrackingError = null;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll(AutoPlayReservationHarmonyId); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Auto-play reservation cleanup failed: {cleanupError.Message}"); }
            AutoPlayReservations.TrackingError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] Auto-play reservation tracking unavailable: {ex}");
        }
        InstallEndedCombatLossCapture();
    }

    // A loss is captured on its own patch: failing to install it only costs a lost fight's ended_combat,
    // never card_resolution_events (which needs only the reservation patches above).
    private static void InstallEndedCombatLossCapture()
    {
        var harmony = new Harmony(EndedCombatLossHarmonyId);
        try
        {
            var pendingLoss = AccessTools.DeclaredMethod(typeof(CombatManager), "ProcessPendingLoss")
                ?? throw new MissingMethodException(nameof(CombatManager), "ProcessPendingLoss");
            harmony.Patch(pendingLoss, prefix: new HarmonyMethod(typeof(McpMod), nameof(CombatLossProcessing)));
            EndedCombatCaptures.LossCaptureError = null;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll(EndedCombatLossHarmonyId); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Ended combat loss capture cleanup failed: {cleanupError.Message}"); }
            EndedCombatCaptures.LossCaptureError = $"{ex.GetType().Name}: {ex.Message}";
            GD.PrintErr($"[STS2 MCP] Ended combat capture of a loss unavailable: {ex}");
        }
    }

    private static int HistoryPosition() => CombatManager.Instance.History.Entries.Count();

    // CardPileCmd.AutoPlayFromDrawPile (6 arguments) returns at once while CombatManager.IsOverOrEnding.
    // Otherwise the call is recorded with the model that made it: PlayerChoiceContext.LastInvolvedModel is the
    // card whose OnPlay runs (CardModel.OnPlayWrapper pushes the card onto the BranchingPlayerChoiceContext it
    // passes to OnPlay), the hook listener (Hook pushes each listener while it runs: MayhemPower,
    // IAmInvincible, LostLutePower) or the potion (PotionModel.OnUseWrapper: DistilledChaos).
    private static void AutoPlayFromDrawPileStarting(PlayerChoiceContext choiceContext, Player player, int count,
        CardPilePosition position, bool forceExhaust, ref Action<CardModel> cardModification,
        out AutoPlayReservationCall? __state)
    {
        __state = null;
        try
        {
            if (CombatManager.Instance.IsOverOrEnding) return;
            var model = choiceContext.LastInvolvedModel;
            CardModel? parentCard = model as CardModel;
            CardPlay? parentPlay = null;
            if (parentCard != null)
            {
                // The parent's play still in progress: CardPlayStartedEntry without its CardPlayFinishedEntry.
                var history = CombatManager.Instance.History;
                var finished = new HashSet<CardPlay>(history.CardPlaysFinished.Select(e => e.CardPlay), ReferenceEqualityComparer.Instance);
                parentPlay = history.CardPlaysStarted.Select(e => e.CardPlay)
                    .LastOrDefault(p => ReferenceEquals(p.Card, parentCard) && !finished.Contains(p));
            }
            string? kind = model switch
            {
                null => null,
                CardModel => "card",
                PotionModel => "potion",
                PowerModel => "power",
                RelicModel => "relic",
                _ => "other"
            };
            var call = AutoPlayReservations.Begin(HistoryPosition(), player, choiceContext, kind, model?.Id.Entry,
                parentCard, parentCard?.Pile?.Type == PileType.Draw, parentPlay, count,
                position.ToString().ToLowerInvariant(), forceExhaust);
            var original = cardModification;
            cardModification = card =>
            {
                // The game's own delegate runs first and its exceptions pass through untouched.
                original(card);
                try { AutoPlayReservations.Selected(call, card); }
                catch (Exception ex) { ReservationTrackingFailed("cardModification", ex); }
            };
            __state = call;
        }
        catch (Exception ex) { ReservationTrackingFailed("AutoPlayFromDrawPile", ex); }
    }

    private static void AutoPlayFromDrawPileReturned(Task __result, AutoPlayReservationCall? __state)
    {
        if (__state == null) return;
        if (__result == null || __result.IsCompleted) { AutoPlayReservations.CallCompleted(__state); return; }
        __result.ContinueWith(_ => AutoPlayReservations.CallCompleted(__state), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CardPileCmd.Add puts the card into the Play pile with CardPile.AddInternal right after the delegate, before
    // the tween that flies it to the play area face up. A failed Add (the owner died, CombatManager.IsEnding)
    // never reaches AddInternal, so that card is not named.
    private static void CardPileAddedInternal(CardPile __instance, CardModel card)
    {
        if (!AutoPlayReservations.AnyPendingMove) return;
        try
        {
            if (__instance.Type != PileType.Play || card.Owner is not { } owner) return;
            AutoPlayReservations.EnteredPlayPile(HistoryPosition(), owner, card);
        }
        catch (Exception ex) { ReservationTrackingFailed("CardPile.AddInternal", ex); }
    }

    // AutoPlayFromDrawPile's loop calls CardCmd.AutoPlay(choiceContext, item, null) per reserved card. AutoPlay
    // returns before doing anything while CombatManager.IsOverOrEnding or the owner is dead.
    private static void CardAutoPlayStarting(PlayerChoiceContext choiceContext, CardModel card, Creature? target,
        out AutoPlayDispatch? __state)
    {
        __state = null;
        try
        {
            bool proceeds = !CombatManager.Instance.IsOverOrEnding && !card.Owner.Creature.IsDead;
            __state = AutoPlayReservations.AutoPlayBegan(HistoryPosition(), choiceContext, card, target != null, proceeds);
        }
        catch (Exception ex) { ReservationTrackingFailed("CardCmd.AutoPlay", ex); }
    }

    private static void CardAutoPlayReturned(Task __result, AutoPlayDispatch? __state)
    {
        if (__state == null) return;
        if (__result == null || __result.IsCompleted) { AutoPlayReservations.AutoPlayEnded(__state); return; }
        __result.ContinueWith(_ => AutoPlayReservations.AutoPlayEnded(__state), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void HistoryCardPlayStarting(CardPlay cardPlay)
    {
        try { AutoPlayReservations.PlayStarting(cardPlay, cardPlay.IsAutoPlay); }
        catch (Exception ex) { ReservationTrackingFailed("CombatHistory.CardPlayStarted", ex); }
    }

    // CombatManager.EndCombatInternal (a win) clears History while its turn state still holds the fight;
    // CancelCombat drops the turn state first, so DebugOnlyGetState() is null there and nothing is captured.
    private static void HistoryClearing()
    {
        try
        {
            if (CombatManager.Instance.DebugOnlyGetState() is { } combat) CaptureEndedCombat(combat, "victory");
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Ended combat capture failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void HistoryCleared() => AutoPlayReservations.Reset();

    // CombatManager.ProcessPendingLoss(CombatTurnState turnState) ends a lost fight without clearing History
    // (CancelCombat clears it later). Read the turn state it was given, not the current one: a stale turn state
    // resuming to end its own fight must not capture another fight. CombatTurnState is internal: by reflection.
    private static void CombatLossProcessing(object[] __args)
    {
        try
        {
            object turnState = __args.Length > 0 && __args[0] != null ? __args[0]
                : throw new InvalidOperationException("ProcessPendingLoss has no turn state argument");
            var type = turnState.GetType();
            object? pendingLoss = (AccessTools.Property(type, "PendingLoss")
                ?? throw new MissingMemberException(type.Name, "PendingLoss")).GetValue(turnState);
            var combat = (AccessTools.Property(type, "State")
                ?? throw new MissingMemberException(type.Name, "State")).GetValue(turnState) as CombatState;
            if (pendingLoss != null && combat != null) CaptureEndedCombat(combat, "loss");
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Ended combat capture failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void CaptureEndedCombat(CombatState combat, string outcome)
    {
        var capture = new Dictionary<string, object?> { ["outcome"] = outcome };
        var reasons = new Dictionary<string, string>();
        try
        {
            var local = LocalContext.GetMe(combat) ?? throw new InvalidOperationException("local player not found");
            RefreshCardUidRegistry(combat);
            capture["card_resolution_events"] = BuildCardResolutionEvents(local, combat);
        }
        catch (Exception ex)
        {
            capture["card_resolution_events"] = null;
            reasons["card_resolution_events"] = $"{ex.GetType().Name}: {ex.Message}";
        }
        capture["unavailable_reasons"] = reasons;
        EndedCombatCaptures.Record(capture);
    }

    private static void ReservationTrackingFailed(string where, Exception ex)
    {
        // A missed record would leave a shorter stream that looks complete: make the stream unknown instead.
        AutoPlayReservations.Broken ??= $"{where}: {ex.GetType().Name}: {ex.Message}";
        GD.PrintErr($"[STS2 MCP] Auto-play reservation record failed in {where}: {ex}");
    }
}
