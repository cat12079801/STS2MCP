using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string EntropicBrewPath = "player.entropic_brew_pool";
    // Audit PotionFactory.CreateRandomPotions' thresholds and branches, and the
    // choice potion notification paths in McpMod.ChoicePotionContext.cs, for this DLL.
    private const string AuditedPotionAssembly = "ae309f852fb69204a59c04b7b5af63acc839829c65c1349777621ce616e65310";
    private static readonly (string Notification, string Method, Type[] Parameters, bool RunScope)[] EntropicBrewHooks =
    {
        BeforePotionUsedHook,
        ("should_procure_potion", nameof(AbstractModel.ShouldProcurePotion), new[] { typeof(PotionModel), typeof(Player) }, true),
        ("after_potion_procured", nameof(AbstractModel.AfterPotionProcured), new[] { typeof(PotionModel) }, true),
        AfterPotionUsedHook
    };

    private static Dictionary<string, object?> BuildEntropicBrewPool(Player player,
        Dictionary<string, object?> unlocked, string? screen)
    {
        var pool = new Dictionary<string, object?>
        {
            ["known"] = false,
            ["unavailable_reasons"] = new Dictionary<string, string>(),
            ["filter_version"] = 1,
            ["filter_source"] = "EntropicBrew.OnUse;PotionFactory.CreateRandomPotionOutOfCombat;PotionFactory.CreateRandomPotions;" + PotionGenerationPoolSource,
            ["game_assembly_sha256"] = null,
            ["player_id"] = null,
            ["character_id"] = null,
            ["players"] = null,
            ["rarity_thresholds"] = null,
            ["options"] = null,
            ["preview"] = null,
            ["listeners"] = null
        };
        try
        {
            var run = player.RunState;
            pool["player_id"] = player.NetId.ToString();
            pool["character_id"] = player.Character.Id.Entry;
            pool["players"] = run.Players.Count;
            pool["game_assembly_sha256"] = GenerationAssemblySha256.Value;
            RequireGenerationPoolOwner(player, run.Players);
            pool["rarity_thresholds"] = ReadEntropicBrewThresholds(GenerationAssemblySha256.Value);
        }
        catch (Exception ex)
        {
            RecordInternalStateFailure(pool, "contract", ex, EntropicBrewPath);
            return pool;
        }

        IReadOnlyList<PotionModel> models;
        try
        {
            models = PotionFactory.GetPotionOptions(player).OrderBy(model => model.Id.Entry, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            RecordInternalStateFailure(pool, "options", ex, EntropicBrewPath);
            return pool;
        }

        var combat = player.Creature.CombatState;
        List<FreshPotionSlot> slots = new();
        Dictionary<string, object?>? preview = null;
        string? previewUnavailable = null;
        if (combat == null)
        {
            previewUnavailable = "the owner has no combat state";
            RecordInternalStateFailure(pool, "listeners", new InvalidOperationException(previewUnavailable), EntropicBrewPath);
        }
        else
        {
            var listeners = BuildEntropicBrewListeners(player.RunState, combat);
            pool["listeners"] = listeners;
            try
            {
                if (listeners["known"] is not true)
                    previewUnavailable = "potion listener effects are unobserved";
                else if (!IsSettledPotionInput(player, combat, screen))
                    previewUnavailable = "the snapshot is not at a settled player play phase";
                else if (player.PlayerCombatState?.Hand.Cards.Count == 0
                    && player.Relics.Any(relic => relic is UnceasingTop && !relic.IsMelted))
                    // PotionModel.OnUseWrapper calls CombatManager.CheckForEmptyHand after AfterPotionUsed.
                    previewUnavailable = "UnceasingTop can draw after use from this empty hand";

                var belt = player.PotionSlots;
                var slotRows = new List<Dictionary<string, object?>>(belt.Count);
                var allSlots = new List<FreshPotionSlot>(belt.Count);
                for (int slot = 0; slot < belt.Count; slot++)
                {
                    var held = belt[slot];
                    slotRows.Add(new Dictionary<string, object?>
                    {
                        ["slot"] = slot,
                        ["current_potion_id"] = held?.Id.Entry,
                        ["requires"] = held == null ? "empty" : "remove_used_potion"
                    });
                    allSlots.Add(previewUnavailable == null ? ReadFreshPotionSlot(player, slot, held)
                        : new FreshPotionSlot(false, false, previewUnavailable));
                }
                preview = new Dictionary<string, object?>
                {
                    ["scope"] = "same_combat_play_phase_and_permission_and_target_inputs",
                    ["combat_id"] = CaptureCombatAttempt(combat)?.CombatId,
                    ["floor"] = player.RunState.TotalFloor,
                    ["round"] = combat.RoundNumber,
                    ["turn_number"] = player.PlayerCombatState?.TurnNumber,
                    ["unavailable_reason"] = previewUnavailable,
                    ["slots"] = slotRows
                };
                slots = allSlots;
            }
            catch (Exception ex)
            {
                RecordInternalStateFailure(pool, "preview", ex, EntropicBrewPath);
            }
        }
        pool["preview"] = preview;

        // Each group gets one verdict per model. Slot preconditions are in preview.slots.
        var groups = slots.Select((slot, index) => (slot, index)).GroupBy(x => x.slot)
            .Select(g => (Slot: g.Key, Indices: g.Select(x => x.index).ToList())).ToList();
        var options = models.Select(model => BuildEntropicBrewOption(player, model, groups, previewUnavailable)).ToList();
        pool["options"] = options;
        try
        {
            CheckEntropicBrewOptions(unlocked, options);
            pool["known"] = true;
        }
        catch (Exception ex)
        {
            RecordInternalStateFailure(pool, "options", ex, EntropicBrewPath);
        }
        return pool;
    }

    private static Dictionary<string, object?> ReadEntropicBrewThresholds(string hash)
    {
        if (!string.Equals(hash, AuditedPotionAssembly, StringComparison.Ordinal))
            throw new InvalidOperationException("the PotionFactory comparisons are unverified for this assembly");
        float Threshold(string field, float auditedValue)
        {
            var info = typeof(PotionFactory).GetField(field, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (info is not { IsLiteral: true } || info.GetRawConstantValue() is not float value || value != auditedValue)
                throw new InvalidOperationException($"PotionFactory.{field} differs from the audited constant");
            return value;
        }
        return new Dictionary<string, object?>
        {
            ["Rare"] = Threshold("_rareThreshold", 0.1f),
            ["Uncommon"] = Threshold("_uncommonThreshold", 0.35f),
            ["Common"] = 1f,
            ["comparison"] = "first_upper_bound_inclusive",
            ["order"] = new[] { "Rare", "Uncommon", "Common" },
            ["precision"] = "float32",
            ["within_rarity"] = "uniform_by_occurrence",
            ["between_slots"] = "with_replacement"
        };
    }

    private static void CheckEntropicBrewOptions(Dictionary<string, object?> unlocked, List<Dictionary<string, object?>> options)
    {
        if (unlocked.GetValueOrDefault("potions") is not Dictionary<string, object?> potions
            || potions.GetValueOrDefault("character") is not List<string> character
            || potions.GetValueOrDefault("shared") is not List<string> shared)
            throw new InvalidOperationException("unlocked_pools.potions.character/shared are unobserved");
        var ids = options.Select(row => row["id"] as string).OrderBy(id => id, StringComparer.Ordinal);
        if (!ids.SequenceEqual(character.Concat(shared).OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("PotionFactory.GetPotionOptions differs from the unlocked potion multiset");

        if (options.Any(row => row["id"] is not string { Length: > 0 }
            || row["rarity"] is not ("Rare" or "Uncommon" or "Common")))
            throw new InvalidOperationException("an option has an unknown id or rarity");
        foreach (string rarity in new[] { "Rare", "Uncommon", "Common" })
            if (!options.Any(row => Equals(row["rarity"], rarity)))
                throw new InvalidOperationException($"the {rarity} group has positive probability and no options");
    }

    private static Dictionary<string, object?> BuildEntropicBrewOption(Player player, PotionModel model,
        List<(FreshPotionSlot Slot, List<int> Indices)> groups, string? previewUnavailable)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = model.Id.Entry, ["rarity"] = null, ["usage"] = null, ["target_type"] = null,
            ["name"] = null, ["description"] = null, ["vars"] = null, ["slot_verdicts"] = null
        };
        string path = $"{EntropicBrewPath}.options.{model.Id.Entry}";
        PotionModel clone;
        try
        {
            if (model.GetType().Assembly != typeof(PotionModel).Assembly)
                throw new InvalidOperationException("external potion implementation is unverified");
            ReadInternalStateValue(row, "rarity", () => model.Rarity.ToString(), path);
            clone = CloneBrewOption(model, player);
        }
        catch (Exception ex)
        {
            RecordInternalStateFailure(row, "model", ex, path);
            return row;
        }
        ReadInternalStateValue(row, "usage", () => clone.Usage.ToString(), path);
        ReadInternalStateValue(row, "target_type", () => clone.TargetType.ToString(), path);
        ReadInternalStateValue(row, "name", () => StripRichTextTags(clone.Title.GetFormattedText()), path);
        ReadInternalStateValue(row, "description", () => StripRichTextTags(clone.DynamicDescription.GetFormattedText()), path);
        ReadInternalStateValue(row, "vars", () => ReadPotionPublicVars(clone), path);

        if (groups.Count > 0)
        {
            row["slot_verdicts"] = groups.Select(group =>
            {
                var verdict = previewUnavailable == null
                    ? DecidePotionUsability(BuildPotionUsabilityReads(player, clone, group.Indices[0], group.Slot))
                    : PotionUsabilityVerdict.Unobserved("preview_scope", previewUnavailable);
                var availability = new Dictionary<string, object?>
                {
                    ["slots"] = group.Indices,
                    ["can_use_if_generated"] = verdict.CanUse,
                    ["can_use_if_generated_reason"] = verdict.Reason,
                    ["valid_target_entity_ids"] = verdict.TargetIds,
                    ["target_optional"] = verdict.CanUse == true ? verdict.TargetOptional : null
                };
                if (verdict.Detail != null) availability["unavailable_detail"] = verdict.Detail;
                return availability;
            }).ToList();
        }
        return row;
    }

    private static PotionModel CloneBrewOption(PotionModel model, Player player)
    {
        var clone = model.ToMutable();
        clone.Owner = player;
        // PotionModel.ToMutable shallow-copies any already initialized DynamicVars.
        // DynamicDescription writes EnergyVar.ColorPrefix, so initialize a fresh set on this clone only.
        PotionPrivateField<DynamicVarSet>(typeof(PotionModel), "_dynamicVars").SetValue(clone, null);
        return clone;
    }

    private static Dictionary<string, object?> BuildEntropicBrewListeners(IRunState run, ICombatState combat)
    {
        var result = new Dictionary<string, object?> { ["known"] = false, ["notifications"] = null };
        var reasons = new List<string>();
        try
        {
            if (CountModHookSubscribers("_runHookSubscribers") + CountModHookSubscribers("_combatHookSubscribers") > 0)
                throw new InvalidOperationException("ModHelper hook subscribers cannot be tied to public state");

            RefreshEntityIdRegistry(combat);
            var models = run.IterateHookListeners(combat).ToList();
            if (models.Any(model => model.GetType().Assembly != typeof(PotionModel).Assembly))
                reasons.Add("an external model can change potion or targeting rules");
            var notifications = new Dictionary<string, object?>();
            foreach (var hook in EntropicBrewHooks)
            {
                var rows = BuildHookListenerRows(models, hook, reasons);
                foreach (var row in rows)
                {
                    if (row["order"] is not int order)
                    {
                        reasons.Add($"{hook.Method}: listener order in the draw pile is hidden");
                        continue;
                    }
                    var model = models[order];
                    int index = (hook.Method, model) switch
                    {
                        (nameof(AbstractModel.ShouldProcurePotion), Sozu relic) =>
                            IndexOfListener(relic.Owner.Relics, relic),
                        (nameof(AbstractModel.AfterPotionProcured), BeltBuckle relic) =>
                            IndexOfListener(relic.Owner.Relics, relic),
                        _ => PotionUseListenerOwnerIndex(hook.Method, model)
                    };
                    if (index >= 0) row["owner_index"] = index;
                    else reasons.Add($"{hook.Method}: unrecognized or unlocated listener {row["model_id"]}");
                }
                notifications[hook.Notification] = rows;
            }
            result["notifications"] = notifications;
            result["known"] = reasons.Count == 0;
        }
        catch (Exception ex)
        {
            reasons.Add($"{ex.GetType().Name}: {ex.Message}");
            Warn(EntropicBrewPath + ".listeners", ex);
        }
        result["unavailable_reasons"] = reasons;
        return result;
    }

    private static bool IsSettledPotionInput(Player player, ICombatState combat, string? screen, bool checkCombatEnding = true)
        => screen is "monster" or "elite" or "boss"
           && ReferenceEquals(CombatManager.Instance.DebugOnlyGetState(), combat)
           && IsPlayPhase(player) && !CombatManager.Instance.PlayerActionsDisabled && !CombatManager.Instance.IsPaused
           && (!checkCombatEnding || !CombatManager.Instance.IsOverOrEnding) && RunManager.Instance.ActionQueueSet.IsEmpty
           && RunManager.Instance.ActionExecutor.CurrentlyRunningAction == null
           && NPlayerHand.Instance is { InCardPlay: false, IsInCardSelection: false }
           && NOverlayStack.Instance is { ScreenCount: 0 }
           && NCapstoneContainer.Instance is { InUse: false };

    private static int PotionUseListenerOwnerIndex(string method, AbstractModel model) => (method, model) switch
    {
        (nameof(AbstractModel.BeforePotionUsed), SurroundedPower power) => IndexOfListener(power.Owner.Powers, power),
        (nameof(AbstractModel.AfterPotionUsed), BeltBuckle or ReptileTrinket) when model is RelicModel relic =>
            IndexOfListener(relic.Owner.Relics, relic),
        _ => -1
    };

    private static int IndexOfListener<T>(IReadOnlyList<T?> collection, T model) where T : class
    {
        for (int index = 0; index < collection.Count; index++)
            if (ReferenceEquals(collection[index], model)) return index;
        return -1;
    }
}
