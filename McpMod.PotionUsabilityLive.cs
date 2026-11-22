using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

// Live reads for the potion can-use-now contract (cat12079801/sts2#1012); the verdict itself is in
// McpMod.PotionUsability.cs. Read only: no popup is created or refreshed, no targeting is started,
// no rejection VFX is played, nothing is enqueued and no shop inventory is opened or closed.
// Sources are sts2-a671e9ef (v0.113.1): NPotionHolder, NPotionPopup, NTargetManager, PotionModel,
// UsePotionAction, FoulPotion.
public static partial class McpMod
{
    private const BindingFlags PotionPrivateFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<(Type, string), FieldInfo> _potionFieldCache = new();

    /// <summary>A private field of a fixed game type, with its declared type checked. A renamed or
    /// retyped field throws (the slot becomes unobserved), never reads as a default.</summary>
    private static FieldInfo PotionPrivateField<T>(Type declaring, string name)
    {
        if (!_potionFieldCache.TryGetValue((declaring, name), out var field))
        {
            field = declaring.GetField(name, PotionPrivateFields)
                ?? throw new MissingFieldException(declaring.Name, name);
            if (field.FieldType != typeof(T))
                throw new InvalidCastException($"{declaring.Name}.{name} is {field.FieldType.Name}, expected {typeof(T).Name}");
            _potionFieldCache[(declaring, name)] = field;
        }
        return field;
    }

    private static T? ReadPotionPrivateField<T>(object owner, Type declaring, string name)
        => PotionPrivateField<T>(declaring, name).GetValue(owner) is T value ? value : default;

    private static Dictionary<string, decimal> ReadPotionPublicVars(PotionModel potion)
        => potion.DynamicVars.Where(pair => pair.Value is not StringVar)
            .ToDictionary(pair => pair.Key, pair => pair.Value.BaseValue);

    /// <summary>
    /// One player.potions[] row. The three can-use-now fields are on every held row; a getter that throws
    /// leaves only its own field null (with unavailable_reasons and a warning), not the row or the player.
    /// </summary>
    private static Dictionary<string, object?> BuildPotionRow(Player player, PotionModel potion, int slot)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = potion.Id.Entry,
            ["name"] = SafeGetText(() => potion.Title),
            ["description"] = SafeGetText(() => potion.DynamicDescription),
            ["slot"] = slot
        };
        void Field(string key, Func<object?> read)
        {
            try { row[key] = read(); }
            catch (Exception ex)
            {
                row[key] = null;
                if (row.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
                    row["unavailable_reasons"] = reasons = new Dictionary<string, string>();
                reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
                Warn($"player.potions.{slot}.{key}", ex);
            }
        }
        Field("can_use_in_combat", () => potion.Usage == PotionUsage.CombatOnly || potion.Usage == PotionUsage.AnyTime);
        Field("target_type", () => potion.TargetType.ToString());
        Field("keywords", () => BuildHoverTips(potion.ExtraHoverTips));
        Field("vars", () => ReadPotionPublicVars(potion));

        PotionUsabilityVerdict verdict;
        try { verdict = DecidePotionUsability(BuildPotionUsabilityReads(player, potion, slot)); }
        catch (Exception ex) { verdict = PotionUsabilityVerdict.Unobserved("reads", $"{ex.GetType().Name}: {ex.Message}"); }
        ApplyPotionUsability(row, verdict, slot);
        return row;
    }

    private static NPotionHolder GetPotionHolder(Player player, int slot)
    {
        // NPotionContainer.Holders is the local player's belt (GrowPotionHolders / Add).
        if (!LocalContext.IsMe(player))
            throw new InvalidOperationException("the potion belt UI is the local player's only");
        var holders = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer?.Holders
            ?? throw new InvalidOperationException("NRun.GlobalUi.TopBar.PotionContainer is not ready");
        if (slot < 0 || slot >= holders.Count || !GodotObject.IsInstanceValid(holders[slot]))
            throw new InvalidOperationException($"no valid belt holder for slot {slot}");
        return holders[slot];
    }

    private sealed record FreshPotionSlot(bool Usable, bool Disabled, string? Unavailable = null);

    private static FreshPotionSlot ReadFreshPotionSlot(Player player, int slot, PotionModel? held)
    {
        try
        {
            var holder = GetPotionHolder(player, slot);
            if (!ReferenceEquals(holder.Potion?.Model, held))
                throw new InvalidOperationException("the belt holder does not match the current slot");
            // NPotionHolder.RemoveUsedPotion resets the flag; AddPotion leaves both flags alone.
            // On an already empty slot there is no removal, so keep its actual flag.
            return new FreshPotionSlot(
                ReadPotionPrivateField<bool>(holder, typeof(NPotionHolder), "_isUsable"),
                held == null && ReadPotionPrivateField<bool>(holder, typeof(NPotionHolder), "_disabledUntilPotionRemoved"));
        }
        catch (Exception ex)
        {
            Warn($"player.entropic_brew_pool.preview.slots.{slot}", ex);
            return new FreshPotionSlot(false, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static PotionUsabilityReads BuildPotionUsabilityReads(Player player, PotionModel potion, int slot,
        FreshPotionSlot? freshSlot = null)
    {
        NPotionHolder? holder = null;
        PotionUsage usage = PotionUsage.None;
        NPotionHolder Holder() => holder ?? throw new InvalidOperationException("the belt holder was not read");
        return new PotionUsabilityReads
        {
            // NPotionContainer.Holders is the local player's belt, one holder per slot (GrowPotionHolders / Add).
            HolderHoldsThisPotion = () =>
            {
                if (freshSlot != null)
                {
                    if (freshSlot.Unavailable is { } detail)
                        throw new PotionUnobservedException("holder_model", detail);
                    return true; // Hypothesis: an independently checked holder receives this new model.
                }
                holder = GetPotionHolder(player, slot);
                return ReferenceEquals(holder.Potion?.Model, potion);
            },
            // NPotionPopup._Ready: a queued potion or a dead owner disables use.
            IsQueued = () => potion.IsQueued,
            OwnerDead = () => potion.Owner.Creature.IsDead,
            // NPotionHolder.OpenPotionPopup: no popup once the run is over.
            IsGameOver = () => potion.Owner.RunState.IsGameOver,
            // NPotionPopup._Ready / RefreshButtons.
            CanUseOrRemovePotions = () => potion.Owner.CanUseOrRemovePotions,
            IsPotionUseLocked = () => RunManager.Instance.IsPotionUseLocked,
            // NPotionHolder.OnRelease (_isUsable) and OpenPotionPopup (_disabledUntilPotionRemoved).
            HolderUsable = () => freshSlot?.Usable ?? ReadPotionPrivateField<bool>(Holder(), typeof(NPotionHolder), "_isUsable"),
            HolderDisabledUntilPotionRemoved = () => freshSlot?.Disabled ?? ReadPotionPrivateField<bool>(Holder(), typeof(NPotionHolder), "_disabledUntilPotionRemoved"),
            PassesCustomUsabilityCheck = () => potion.PassesCustomUsabilityCheck,
            Usage = () => (usage = potion.Usage).ToString(),
            CombatInProgress = () => CombatManager.Instance.IsInProgress,
            OwnerSideActing = () =>
            {
                var creature = potion.Owner.Creature;
                return creature.CombatState?.CurrentSide == creature.Side;
            },
            OwnerAlive = () => potion.Owner.Creature.IsAlive,
            // NPotionPopup.InACardSelectScreen.
            InCardSelection = () => NPlayerHand.Instance is { IsInCardSelection: true }
                                    || NOverlayStack.Instance?.Peek() is ICardSelectionScreen,
            PlayerActionsDisabled = () => CombatManager.Instance.PlayerActionsDisabled,
            CanEnableUseButton = (canUseInCombatNow, passes) => NPotionPopup.CanEnableUseButton(usage, canUseInCombatNow, passes),
            // Only a live popup is compared; the closed holder's IsPotionUsable dereferences _popup, so it is not called.
            OpenPopupIsUsable = () =>
            {
                if (freshSlot != null) return null;
                var popup = ReadPotionPrivateField<NPotionPopup>(Holder(), typeof(NPotionHolder), "_popup");
                if (popup == null || !GodotObject.IsInstanceValid(popup) || popup.IsMarkedForRemoval) return null;
                return popup.IsUsable;
            },
            Targets = () => ReadPotionTargets(potion)
        };
    }

    /// <summary>
    /// Where pressing use would send the potion, per NPotionHolder.UsePotion: AnyEnemy, TargetedNoCreature and
    /// CanThrowAtAlly start a selection (NTargetManager), anything else is used directly.
    /// </summary>
    private static PotionTargetRead ReadPotionTargets(PotionModel potion)
    {
        TargetType targetType = potion.TargetType;
        bool inCombat = CombatManager.Instance.IsInProgress;
        Creature owner = potion.Owner.Creature;
        bool selects = targetType == TargetType.AnyEnemy || targetType == TargetType.TargetedNoCreature || potion.CanThrowAtAlly();

        if (!selects)
        {
            // Direct use: Self passes the owner, everything else null; EnqueueManualUse turns null into the
            // owner when the owner is a valid target (solo AnyPlayer).
            Creature? target = targetType == TargetType.Self ? owner : null;
            if (target == null && potion.IsValidTarget(owner)) target = owner;
            if (target != null)
                return new PotionTargetRead
                {
                    TargetOptional = true,
                    Candidates = new List<PotionTargetCandidate>
                    {
                        new()
                        {
                            Id = PotionPlayerTargetId(potion.Owner.NetId.ToString()),
                            Label = "owner",
                            // UsePotionAction.ExecuteAction re-resolves the owner and cancels on !IsValidTarget.
                            Allowed = () => potion.IsValidTarget(target)
                        }
                    }
                };
            // A null target reaches UsePotionAction.ExecuteAction: in combat it stays null for a
            // non-single-target type (GetCreatureAsync(null)) and becomes the owner (refused above) for a
            // single-target one; outside combat every type but TargetedNoCreature gets the owner.
            return inCombat && potion.IsValidTarget(null)
                ? new PotionTargetRead { NoCreatureTarget = true }
                : new PotionTargetRead { Blocked = "no_valid_target" };
        }

        // NPotionHolder.ShouldCancelTargeting: in combat a selection ends at once while an overlay or a
        // capstone screen is up.
        if (inCombat)
        {
            bool cancelled = PotionProbe("targeting_cancel_condition",
                () => (NOverlayStack.Instance ?? throw new InvalidOperationException("NOverlayStack is not ready")).ScreenCount > 0
                      || (NCapstoneContainer.Instance ?? throw new InvalidOperationException("NCapstoneContainer is not ready")).InUse);
            if (cancelled) return new PotionTargetRead { Blocked = "targeting_blocked" };
        }

        switch (targetType)
        {
            case TargetType.AnyEnemy:
                if (!inCombat)
                    return new PotionTargetRead { Unobserved = "target_type", UnobservedDetail = "AnyEnemy outside combat" };
                return new PotionTargetRead { Candidates = EnemyPotionTargets(potion, owner) };
            case TargetType.TargetedNoCreature:
            {
                // Only Foul Potion uses it (outside combat): the selection ends on the merchant button, which
                // hands EnqueueManualUse a null creature.
                if (inCombat || potion is not FoulPotion)
                    return new PotionTargetRead { Unobserved = "target_type", UnobservedDetail = $"TargetedNoCreature for {potion.Id.Entry} (in combat: {inCombat})" };
                var room = potion.Owner.RunState.CurrentRoom
                    ?? throw new PotionUnobservedException("current_room", "the run has no current room");
                var (button, _) = FoulPotion.GetFoulPotionMerchantTarget(room);
                return button != null && potion.IsValidTarget(null)
                    ? new PotionTargetRead { NoCreatureTarget = true }
                    : new PotionTargetRead { Blocked = "no_valid_target" };
            }
            case TargetType.AnyPlayer:
                return new PotionTargetRead { Candidates = AllyPotionTargets(potion) };
            default:
                return new PotionTargetRead { Unobserved = "target_type", UnobservedDetail = $"selection for {targetType}" };
        }
    }

    /// <summary>
    /// Every enemy of the owner's combat, in CombatState.Enemies order, with the stable entity_id the battle
    /// rows use. An enemy without CombatId or monster has no id (unobserved if it would be allowed).
    /// </summary>
    private static List<PotionTargetCandidate> EnemyPotionTargets(PotionModel potion, Creature owner)
    {
        var combatState = owner.CombatState ?? throw new PotionUnobservedException("combat_state", "the owner has no combat state");
        var room = NCombatRoom.Instance ?? throw new PotionUnobservedException("combat_room", "NCombatRoom is not ready");
        RefreshEntityIdRegistry(combatState);
        var seen = new HashSet<Creature>(ReferenceEqualityComparer.Instance);
        var candidates = new List<PotionTargetCandidate>();
        foreach (var enemy in combatState.Enemies)
        {
            if (!seen.Add(enemy)) continue;
            var creature = enemy;
            string? id = creature.CombatId != null && creature.Monster != null ? GetStableEntityId(creature) : null;
            candidates.Add(new PotionTargetCandidate
            {
                Id = id,
                IdStage = "entity_id",
                Label = id ?? $"enemy#{candidates.Count}",
                Allowed = () =>
                    // NTargetManager.AllowedToTargetCreature (AnyEnemy).
                    creature.Side == CombatSide.Enemy && !creature.IsDead
                    // PotionModel.IsValidTarget, re-checked by UsePotionAction.ExecuteAction.
                    && potion.IsValidTarget(creature)
                    // Hovering needs the creature's node, and an interactable one: NCreature.SetIsInteractableInternal
                    // turns the hitbox's mouse filter off (e.g. ThievingHopper.EscapeMove / FatGremlin.FleeMove).
                    && room.GetCreatureNode(creature) is { IsInteractable: true }
                    // NTargetManager.OnCreatureHovered: Hook.ShouldAllowTargeting (the refusal VFX is not played here).
                    && Hook.ShouldAllowTargeting(creature.CombatState!, creature, out _)
                    // NTargetManager._Process cancels the selection on a hovered creature that is not hittable.
                    && creature.IsHittable
            });
        }
        return candidates;
    }

    /// <summary>
    /// Multiplayer AnyPlayer in combat (PotionModel.CanThrowAtAlly): a player is selected through its
    /// NMultiplayerPlayerState (no hook) or its creature node (Hook.ShouldAllowTargeting, IsHittable);
    /// NTargetManager.AllowedToTargetCreature (AnyPlayer) and PotionModel.IsValidTarget apply to both.
    /// </summary>
    private static List<PotionTargetCandidate> AllyPotionTargets(PotionModel potion)
    {
        var container = NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer
            ?? throw new PotionUnobservedException("player_state_container", "NGlobalUi.MultiplayerPlayerContainer is not ready");
        var stateNodes = ReadPotionPrivateField<List<NMultiplayerPlayerState>>(container, typeof(NMultiplayerPlayerStateContainer), "_nodes")
            ?? throw new PotionUnobservedException("player_state_container", "NMultiplayerPlayerStateContainer._nodes is null");
        var room = NCombatRoom.Instance;
        var candidates = new List<PotionTargetCandidate>();
        foreach (var p in potion.Owner.RunState.Players)
        {
            var target = p;
            var creature = target.Creature;
            candidates.Add(new PotionTargetCandidate
            {
                Id = PotionPlayerTargetId(target.NetId.ToString()),
                Label = "player " + target.NetId,
                Allowed = () =>
                    creature.IsPlayer && !creature.IsDead && potion.IsValidTarget(creature)
                    && (stateNodes.Any(n => GodotObject.IsInstanceValid(n) && ReferenceEquals(n.Player, target))
                        || (room?.GetCreatureNode(creature) is { IsInteractable: true }
                            && Hook.ShouldAllowTargeting(creature.CombatState!, creature, out _)
                            && creature.IsHittable))
            });
        }
        return candidates;
    }

    /// <summary>
    /// use_potion with expected_potion_id: the verdict is read again on the game thread right before the
    /// enqueue, the slot must still hold that potion, the target must be in valid_target_entity_ids, and the
    /// MOD's own action conditions are checked separately. No enemy is picked for the caller and no shop
    /// panel is closed. Any refusal uses nothing.
    /// </summary>
    private static Dictionary<string, object?> ExecuteGuardedUsePotion(Player player, int slot, PotionModel? potion,
        string expectedPotionId, Dictionary<string, JsonElement> data)
    {
        PotionUsabilityVerdict? verdict = null;
        if (potion != null)
        {
            try { verdict = DecidePotionUsability(BuildPotionUsabilityReads(player, potion, slot)); }
            catch (Exception ex) { verdict = PotionUsabilityVerdict.Unobserved("reads", $"{ex.GetType().Name}: {ex.Message}"); }
        }
        string? requested = TryReadTargetId(data);
        string? rejection = PotionUseGuardRejection(slot, potion?.Id.Entry, expectedPotionId, verdict, requested,
            out string? targetId, out string stage);
        if (rejection != null || potion == null || verdict == null)
            return PotionGuardError(rejection ?? "no potion", stage, slot, expectedPotionId, potion?.Id.Entry, verdict);

        // The MOD's own action conditions, separate from the game's button: an AnyTime potion the button
        // allows can still be refused here.
        bool inCombat = CombatManager.Instance.IsInProgress;
        string? modBlock = null;
        if (potion.Usage == PotionUsage.CombatOnly && !IsPlayPhase(player))
            modBlock = "the MOD takes combat-only potions in the play phase only";
        else if (inCombat && CombatManager.Instance.PlayerActionsDisabled)
            modBlock = "the MOD does not take actions while player actions are disabled";
        if (modBlock != null)
            return PotionGuardError(modBlock + "; nothing was used", "mod_acceptance", slot, expectedPotionId, potion.Id.Entry, verdict);

        Creature? target;
        try { target = ResolveGuardedPotionTarget(player, potion, verdict, targetId); }
        catch (Exception ex)
        {
            return PotionGuardError($"target '{targetId}' could not be resolved ({ex.Message}); nothing was used",
                "target", slot, expectedPotionId, potion.Id.Entry, verdict);
        }

        using (Recorder.CombatRecorder.ExpectApiAction(typeof(UsePotionAction)))
            potion.EnqueueManualUse(target);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Using potion '{SafeGetText(() => potion.Title)}' from slot {slot}"
                + (targetId != null ? $" on {targetId}" : ""),
            ["potion_check"] = new Dictionary<string, object?>
            {
                ["stage"] = "passed",
                ["slot"] = slot,
                ["expected_potion_id"] = expectedPotionId,
                ["actual_potion_id"] = potion.Id.Entry,
                ["target"] = targetId
            }
        };
    }

    /// <summary>The creature NPotionHolder would hand to EnqueueManualUse for the checked target id.</summary>
    private static Creature? ResolveGuardedPotionTarget(Player player, PotionModel potion, PotionUsabilityVerdict verdict, string? targetId)
    {
        // Direct use (NPotionHolder.UsePotion's else branch): Self passes the owner, the rest null.
        if (verdict.TargetOptional)
            return potion.TargetType == TargetType.Self ? potion.Owner.Creature : null;
        if (targetId == null) return null; // all-enemy effect or the merchant
        if (targetId.StartsWith(PotionPlayerTargetPrefix, StringComparison.Ordinal))
        {
            string netId = targetId[PotionPlayerTargetPrefix.Length..];
            var matches = player.RunState.Players.Where(p => p.NetId.ToString() == netId).ToList();
            if (matches.Count != 1) throw new InvalidOperationException($"{matches.Count} players match");
            return matches[0].Creature;
        }
        var combatState = player.Creature.CombatState ?? throw new InvalidOperationException("no combat state");
        RefreshEntityIdRegistry(combatState);
        var enemies = combatState.Enemies.Distinct(ReferenceEqualityComparer.Instance).Cast<Creature>()
            .Where(c => c.CombatId != null && c.Monster != null && GetStableEntityId(c) == targetId).ToList();
        if (enemies.Count != 1) throw new InvalidOperationException($"{enemies.Count} enemies match");
        return enemies[0];
    }

    /// <summary>
    /// shop.sellable_potions on a passive read: the same question as CanSellPotionToMerchant, asked
    /// without closing the inventory first. While the panel is open Foul Potion has no merchant target
    /// (FoulPotion.GetFoulPotionMerchantTarget), so nothing is listed; sell_potion closes it itself.
    /// </summary>
    internal static bool CanSellPotionToMerchantNow(Player player, PotionModel potion)
    {
        try
        {
            if (!IsInMerchantRoom(player)) return false;
            if (potion.Usage == PotionUsage.CombatOnly || potion.Usage == PotionUsage.Automatic) return false;
            return potion.PassesCustomUsabilityCheck && HasMerchantInteraction(player, potion);
        }
        catch { return false; }
    }
}
