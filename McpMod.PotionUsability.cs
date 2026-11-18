using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace STS2_MCP;

// The potion can-use-now contract (cat12079801/sts2#1012): player.potions[].can_use_now,
// valid_target_entity_ids and can_use_now_reason, and use_potion's expected_potion_id.
// Pure: no game, Godot or HTTP types, so the contract test links this file as is. The live reads
// that feed it (the belt holder, NPotionPopup.CanEnableUseButton, the targeting rules) are in
// McpMod.PotionUsabilityLive.cs.
//
// Four outcomes, never mixed:
//   usable, creature destinations   can_use_now true,  valid_target_entity_ids [ids...], reason null
//   usable, no creature destination true,  []  (all-enemy effects; the merchant)        reason null
//   known not usable                false, []                                          reason = the first ban
//   a read failed / unknown form    null,  null                                        reason "unobserved:<stage>"
// A failed read is never turned into false or [].
public static partial class McpMod
{
    internal const string ExpectedPotionIdKey = "expected_potion_id";
    internal const string PotionPlayerTargetPrefix = "player:";
    internal const string PotionUnobservedPrefix = "unobserved:";

    /// <summary>A player destination in valid_target_entity_ids: "player:" + player.player_id
    /// (Player.NetId in decimal). Kept apart from enemy entity_ids, which never contain ':'.</summary>
    internal static string PotionPlayerTargetId(string playerId) => PotionPlayerTargetPrefix + playerId;

    /// <summary>One creature the belt's targeting could hand to EnqueueManualUse.</summary>
    internal sealed class PotionTargetCandidate
    {
        /// <summary>Stable id (enemy entity_id or player:&lt;id&gt;); null when it has none.</summary>
        public string? Id;
        /// <summary>Stage reported when an allowed candidate has no stable id.</summary>
        public string IdStage = "entity_id";
        /// <summary>Name for messages only.</summary>
        public string Label = "?";
        /// <summary>The game's targeting rules for this creature (read only when the button is enabled).</summary>
        public Func<bool> Allowed = () => false;
    }

    /// <summary>What the target stage read. Exactly one of the four shapes.</summary>
    internal sealed class PotionTargetRead
    {
        /// <summary>A known ban at the target stage (targeting_blocked / no_valid_target).</summary>
        public string? Blocked;
        /// <summary>Usable with no creature destination (valid_target_entity_ids []).</summary>
        public bool NoCreatureTarget;
        /// <summary>Direct use (no selection): use_potion may omit target.</summary>
        public bool TargetOptional;
        public List<PotionTargetCandidate>? Candidates;
        /// <summary>A form the contract does not know how to enumerate: the stage name.</summary>
        public string? Unobserved;
        public string? UnobservedDetail;
    }

    /// <summary>
    /// The reads behind one held potion's verdict, as getters consulted in the verdict's order. A getter is
    /// called only when every earlier check passed, so a known ban never requires the later (UI) reads.
    /// </summary>
    internal sealed class PotionUsabilityReads
    {
        public Func<bool> HolderHoldsThisPotion = () => throw new NotImplementedException();
        public Func<bool> IsQueued = () => throw new NotImplementedException();
        public Func<bool> OwnerDead = () => throw new NotImplementedException();
        public Func<bool> IsGameOver = () => throw new NotImplementedException();
        public Func<bool> CanUseOrRemovePotions = () => throw new NotImplementedException();
        public Func<bool> IsPotionUseLocked = () => throw new NotImplementedException();
        public Func<bool> HolderUsable = () => throw new NotImplementedException();
        public Func<bool> HolderDisabledUntilPotionRemoved = () => throw new NotImplementedException();
        public Func<bool> PassesCustomUsabilityCheck = () => throw new NotImplementedException();
        /// <summary>PotionModel.Usage as its enum name.</summary>
        public Func<string> Usage = () => throw new NotImplementedException();
        // NPotionPopup.RefreshButtons' canUseInCombatNow, component by component (CombatOnly only).
        public Func<bool> CombatInProgress = () => throw new NotImplementedException();
        public Func<bool> OwnerSideActing = () => throw new NotImplementedException();
        public Func<bool> OwnerAlive = () => throw new NotImplementedException();
        public Func<bool> InCardSelection = () => throw new NotImplementedException();
        public Func<bool> PlayerActionsDisabled = () => throw new NotImplementedException();
        /// <summary>NPotionPopup.CanEnableUseButton(usage, canUseInCombatNow, passesCustomUsabilityCheck).</summary>
        public Func<bool, bool, bool> CanEnableUseButton = (_, _) => throw new NotImplementedException();
        /// <summary>The holder's live popup's IsUsable; null when no live popup is open.</summary>
        public Func<bool?> OpenPopupIsUsable = () => throw new NotImplementedException();
        public Func<PotionTargetRead> Targets = () => throw new NotImplementedException();
    }

    internal sealed class PotionUsabilityVerdict
    {
        public bool? CanUse;
        public List<string>? TargetIds;
        public string? Reason;
        /// <summary>Why the read failed (unobserved only).</summary>
        public string? Detail;
        /// <summary>Direct use: use_potion may omit target.</summary>
        public bool TargetOptional;

        internal static PotionUsabilityVerdict Usable(List<string> ids, bool optional)
            => new() { CanUse = true, TargetIds = ids, TargetOptional = optional };
        internal static PotionUsabilityVerdict Banned(string reason)
            => new() { CanUse = false, TargetIds = new List<string>(), Reason = reason };
        internal static PotionUsabilityVerdict Unobserved(string stage, string detail)
            => new() { CanUse = null, TargetIds = null, Reason = PotionUnobservedPrefix + stage, Detail = detail };
    }

    private sealed class PotionUnobservedException : Exception
    {
        public string Stage { get; }
        public PotionUnobservedException(string stage, string detail) : base(detail) { Stage = stage; }
    }

    private static T PotionProbe<T>(string stage, Func<T> read)
    {
        try { return read(); }
        catch (PotionUnobservedException) { throw; }
        catch (Exception ex) { throw new PotionUnobservedException(stage, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// The verdict for one held potion. Order (the first ban wins, and later reads are not made):
    /// the belt holder shows this potion, IsQueued, owner dead, game over, Player.CanUseOrRemovePotions,
    /// RunManager.IsPotionUseLocked, holder _isUsable, holder _disabledUntilPotionRemoved, then
    /// NPotionPopup.CanEnableUseButton (custom check, usage, CombatOnly's in-combat condition), an open
    /// popup's IsUsable, then the targets.
    /// </summary>
    internal static PotionUsabilityVerdict DecidePotionUsability(PotionUsabilityReads r)
    {
        try
        {
            // The holder for this slot has to show this very model; a UI that is not settled yet
            // (or shows another potion) is not a ban.
            if (!PotionProbe("holder", r.HolderHoldsThisPotion))
                return PotionUsabilityVerdict.Unobserved("holder_model",
                    "the belt holder for this slot does not show this potion");
            if (PotionProbe("is_queued", r.IsQueued)) return PotionUsabilityVerdict.Banned("queued");
            if (PotionProbe("owner_dead", r.OwnerDead)) return PotionUsabilityVerdict.Banned("owner_dead");
            if (PotionProbe("is_game_over", r.IsGameOver)) return PotionUsabilityVerdict.Banned("game_over");
            if (!PotionProbe("can_use_or_remove_potions", r.CanUseOrRemovePotions)) return PotionUsabilityVerdict.Banned("owner_locked");
            if (PotionProbe("is_potion_use_locked", r.IsPotionUseLocked)) return PotionUsabilityVerdict.Banned("room_locked");
            if (!PotionProbe("holder_is_usable", r.HolderUsable)) return PotionUsabilityVerdict.Banned("holder_not_usable");
            if (PotionProbe("holder_disabled_until_potion_removed", r.HolderDisabledUntilPotionRemoved))
                return PotionUsabilityVerdict.Banned("holder_disabled");

            bool passes = PotionProbe("custom_usability_check", r.PassesCustomUsabilityCheck);
            string usage = PotionProbe("usage", r.Usage);
            string? combatBlock = null;
            bool canUseInCombatNow = false;
            if (usage == "CombatOnly")
            {
                combatBlock = CombatOnlyBlock(r);
                canUseInCombatNow = combatBlock == null;
            }
            bool allowed = PotionProbe("can_enable_use_button", () => r.CanEnableUseButton(canUseInCombatNow, passes));
            if (!allowed)
            {
                if (!passes) return PotionUsabilityVerdict.Banned("custom_check_failed");
                if (usage == "Automatic") return PotionUsabilityVerdict.Banned("automatic");
                if (combatBlock != null) return PotionUsabilityVerdict.Banned(combatBlock);
                return PotionUsabilityVerdict.Unobserved("can_enable_use_button",
                    $"the button rule refused usage {usage} for no known reason");
            }

            // A live popup refreshes on its own events; if it disagrees, the rule above is not what
            // the player sees, so neither answer can be reported.
            if (PotionProbe("popup", r.OpenPopupIsUsable) is false)
                return PotionUsabilityVerdict.Unobserved("popup",
                    "the open potion popup's use button is disabled while the button rule allows it");

            var t = PotionProbe("targets", r.Targets);
            if (t.Unobserved != null)
                return PotionUsabilityVerdict.Unobserved(t.Unobserved, t.UnobservedDetail ?? "unknown target form");
            if (t.Blocked != null) return PotionUsabilityVerdict.Banned(t.Blocked);
            if (t.NoCreatureTarget) return PotionUsabilityVerdict.Usable(new List<string>(), optional: false);
            var ids = CollectPotionTargetIds(t.Candidates
                ?? throw new PotionUnobservedException("targets", "no target candidates were read"));
            if (ids.Count == 0) return PotionUsabilityVerdict.Banned("no_valid_target");
            return PotionUsabilityVerdict.Usable(ids, t.TargetOptional);
        }
        catch (PotionUnobservedException u)
        {
            return PotionUsabilityVerdict.Unobserved(u.Stage, u.Message);
        }
    }

    // NPotionPopup.RefreshButtons: creature.CombatState?.CurrentSide == creature.Side && IsInProgress &&
    // IsAlive && !InACardSelectScreen && !PlayerActionsDisabled, short-circuited in the game's order.
    private static string? CombatOnlyBlock(PotionUsabilityReads r)
    {
        if (!PotionProbe("combat_in_progress", r.CombatInProgress)) return "outside_combat";
        if (!PotionProbe("current_side", r.OwnerSideActing)) return "not_player_turn";
        if (!PotionProbe("owner_alive", r.OwnerAlive)) return "owner_dead";
        if (PotionProbe("card_selection", r.InCardSelection)) return "card_selection";
        if (PotionProbe("player_actions_disabled", r.PlayerActionsDisabled)) return "actions_disabled";
        return null;
    }

    /// <summary>
    /// The allowed candidates' ids, in the game's order, without duplicates. An allowed candidate with no
    /// stable id, or two candidates sharing one, makes the whole set unobserved (never an array position
    /// or unknown_0 standing in).
    /// </summary>
    private static List<string> CollectPotionTargetIds(List<PotionTargetCandidate> candidates)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (c.Id != null && !seen.Add(c.Id))
                throw new PotionUnobservedException("entity_id_duplicate", $"two targets share the id {c.Id}");
            if (!PotionProbe("target." + (c.Id ?? c.Label), c.Allowed)) continue;
            if (c.Id == null)
                throw new PotionUnobservedException(c.IdStage, $"target {c.Label} has no stable id");
            ids.Add(c.Id);
        }
        return ids;
    }

    /// <summary>Writes the three fields on a potion row; an unobserved verdict also goes to the row's
    /// unavailable_reasons and the top-level warnings with the slot.</summary>
    internal static void ApplyPotionUsability(Dictionary<string, object?> row, PotionUsabilityVerdict v, int slot)
    {
        row["can_use_now"] = v.CanUse;
        row["valid_target_entity_ids"] = v.TargetIds;
        row["can_use_now_reason"] = v.Reason;
        if (v.CanUse != null) return;
        if (row.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
            row["unavailable_reasons"] = reasons = new Dictionary<string, string>();
        reasons["can_use_now"] = $"{v.Reason}: {v.Detail}";
        Warn($"player.potions.{slot}.can_use_now", $"{v.Reason}: {v.Detail}");
    }

    /// <summary>" | Use now: yes|no (reason)|unobserved (reason)"; nothing for an old row without the key.</summary>
    internal static string FormatPotionUsability(Dictionary<string, object?> potion)
    {
        if (!potion.ContainsKey("can_use_now")) return "";
        string reason = potion.GetValueOrDefault("can_use_now_reason") as string ?? "?";
        switch (potion["can_use_now"])
        {
            case true:
                var ids = potion.GetValueOrDefault("valid_target_entity_ids") as List<string>;
                return ids is { Count: > 0 } ? $" | Use now: yes → {string.Join(", ", ids)}" : " | Use now: yes (no target)";
            case false:
                return $" | Use now: no ({reason})";
            default:
                string? detail = (potion.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>)?.GetValueOrDefault("can_use_now");
                return $" | Use now: unobserved ({detail ?? reason})";
        }
    }

    /// <summary>
    /// Reads the optional expected_potion_id. Absent → null and no error; present it must be a non-empty
    /// string (a PotionModel id entry such as FIRE_POTION).
    /// </summary>
    internal static string? ReadExpectedPotionId(Dictionary<string, JsonElement> data, out string? expected)
    {
        expected = null;
        if (!data.TryGetValue(ExpectedPotionIdKey, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            return $"'{ExpectedPotionIdKey}' must be a non-empty string potion id; nothing was used";
        expected = value.GetString();
        return null;
    }

    /// <summary>
    /// use_potion with expected_potion_id: the slot must still hold that potion, the verdict read in this
    /// frame must say usable, and target must be one of valid_target_entity_ids (omitted only for direct use
    /// or a potion with no creature destination; an enemy is never chosen for the caller). Null = enqueue
    /// with <paramref name="targetId"/>; otherwise the refusal (nothing used) and the failed stage.
    /// </summary>
    internal static string? PotionUseGuardRejection(int slot, string? actualPotionId, string expectedPotionId,
        PotionUsabilityVerdict? v, string? requestedTarget, out string? targetId, out string stage)
    {
        targetId = null;
        stage = "identity";
        if (actualPotionId == null)
            return $"slot {slot} is empty, expected {expectedPotionId}; nothing was used - re-read state";
        if (!string.Equals(actualPotionId, expectedPotionId, StringComparison.Ordinal))
            return $"slot {slot} holds {actualPotionId}, expected {expectedPotionId}; nothing was used - re-read state";
        stage = "can_use_now";
        if (v == null || v.CanUse == null)
            return $"whether {actualPotionId} in slot {slot} can be used cannot be read "
                + $"({v?.Reason ?? PotionUnobservedPrefix + "verdict"}: {v?.Detail ?? "no verdict"}); nothing was used";
        if (v.CanUse == false)
            return $"{actualPotionId} in slot {slot} cannot be used now ({v.Reason}); nothing was used";
        stage = "target";
        var ids = v.TargetIds ?? new List<string>();
        if (ids.Count == 0)
            return requestedTarget == null ? null
                : $"{actualPotionId} takes no creature target, got '{requestedTarget}'; nothing was used";
        if (requestedTarget == null)
        {
            if (v.TargetOptional && ids.Count == 1) { targetId = ids[0]; return null; }
            return $"{actualPotionId} needs a target: one of [{string.Join(", ", ids)}]; nothing was used";
        }
        if (requestedTarget.Length > 0 && requestedTarget.All(char.IsAsciiDigit))
            return $"'{requestedTarget}': with {ExpectedPotionIdKey} the target must be an entity_id from "
                + "valid_target_entity_ids (combat_id is not accepted); nothing was used";
        if (!ids.Contains(requestedTarget, StringComparer.Ordinal))
            return $"'{requestedTarget}' is not a valid target for {actualPotionId} now "
                + $"(valid: [{string.Join(", ", ids)}]); nothing was used - re-read state";
        targetId = requestedTarget;
        return null;
    }

    /// <summary>The refusal of a guarded use_potion: an error plus what was checked.</summary>
    internal static Dictionary<string, object?> PotionGuardError(string message, string stage, int slot,
        string expectedPotionId, string? actualPotionId, PotionUsabilityVerdict? v) => new()
    {
        ["status"] = "error",
        ["error"] = message,
        ["potion_check"] = new Dictionary<string, object?>
        {
            // identity / can_use_now / target: the game's button and targeting (the state's contract);
            // mod_acceptance: the game would allow it, but the MOD does not take the action now.
            ["stage"] = stage,
            ["slot"] = slot,
            ["expected_potion_id"] = expectedPotionId,
            ["actual_potion_id"] = actualPotionId,
            ["can_use_now"] = v?.CanUse,
            ["can_use_now_reason"] = v?.Reason,
            ["valid_target_entity_ids"] = v?.TargetIds
        }
    };
}
