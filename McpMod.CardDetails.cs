using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AttachCardPlayability(Dictionary<string, object?> state, CardModel card)
    {
        var reasons = (Dictionary<string, string>)state["unavailable_reasons"]!;
        state["unplayable_reason"] = null;
        // CanPlay also reads TargetType and card logic. Keep their failures local.
        // CardModel.CanPlay has no pile/location check, so pile rows get the same judgment
        // at their current position; manual play still needs the card in hand.
        ReadCardDetailValue(state, reasons, "can_play", () =>
        {
            card.CanPlay(out var reason, out _);
            state["unplayable_reason"] = reason != UnplayableReason.None ? reason.ToString() : null;
            return reason == UnplayableReason.None;
        });
        if (state["can_play"] == null) reasons["unplayable_reason"] = reasons["can_play"];
    }

    // sts2-d009478a: read the live card, independently of target previews/history.
    private static void AttachCardDetails(Dictionary<string, object?> state, CardModel card)
    {
        var reasons = new Dictionary<string, string>();
        state["unavailable_reasons"] = reasons;
        ReadCardDetailValue(state, reasons, "type", () => card.Type.ToString());
        ReadCardDetailValue(state, reasons, "target_type", () => card.TargetType.ToString());
        // CardModel.IsUpgraded / IsUpgradable read CurrentUpgradeLevel against MaxUpgradeLevel.
        ReadCardDetailValue(state, reasons, "is_upgraded", () => card.IsUpgraded);
        ReadCardDetailValue(state, reasons, "is_upgradable", () => card.IsUpgradable);
        // CardModel.Tags (CardTag enum names). Ordered by enum value: the game holds a HashSet.
        ReadCardDetailValue(state, reasons, "tags", () => card.Tags
            .Distinct().OrderBy(tag => (int)tag).Select(tag => tag.ToString()).ToList());
        bool hexed = false;
        ReadCardDetailValue(state, reasons, "affliction", () =>
        {
            var affliction = card.Affliction;
            hexed = affliction is Hexed;
            return affliction == null ? null
                : BuildCardAttachmentState(affliction, () => affliction.Amount, reasons, "affliction");
        });
        // sts2-ae309f85: HexPower.TryModifyKeywordsInCombat adds Ethereal to the owner's
        // Hexed cards as a Global keyword, so the hand/pile keywords cannot tell whether the
        // card is Ethereal by itself. HexPower.AfterRemoved clears Hexed and the Global
        // Ethereal goes with it; the card's own (Local: canonical + CardCmd.ApplyKeyword
        // from Music Box, Ghost Seed...) Ethereal stays. Only Hexed cards get the key; a
        // card with no or another affliction omits it. GetKeywordsWithSources(Local) skips
        // Hook.ModifyKeywordsInCombat and returns the card's own set; a mutable combat card already built it in
        // CardModel.DeepCloneFields (MutableClone), so this read creates nothing; it is only queried.
        if (hexed)
            ReadCardDetailValue(state, reasons, "local_ethereal",
                () => card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal));
        ReadCardDetailValue(state, reasons, "enchantment", () =>
        {
            var enchantment = card.Enchantment;
            return enchantment == null ? null
                : BuildCardAttachmentState(enchantment, () => enchantment.Amount, reasons, "enchantment");
        });
        ReadCardDetailValue(state, reasons, "dynamic_vars", () =>
        {
            var values = new Dictionary<string, object?>();
            foreach (var (name, dynamicVar) in card.DynamicVars)
            {
                // StringVar's numeric base is a dummy, as in relic.values.
                if (dynamicVar is StringVar) continue;
                ReadCardDetailValue(values, reasons, name, () => dynamicVar.BaseValue, $"dynamic_vars.{name}");
            }
            return values;
        });
        AttachCardTurnState(state, card, reasons);
    }

    /// <summary>
    /// The card's per-instance play inputs that the text does not carry (sts2#1019): single-turn
    /// Retain / Sly, the replay counts, and the Mad Science rider. Every key is present on every
    /// row; a failed read is null with its reason (a read 0 / false is a real value).
    ///
    /// Sources (v0.114.0, sts2-ae309f85), all plain getters or private fields:
    /// CardModel.ShouldRetainThisTurn = Keywords has Retain || _hasSingleTurnRetain;
    /// CardModel.IsSlyThisTurn = Keywords has Sly || _hasSingleTurnSly (GiveSingleTurnSly, i.e.
    /// CardCmd.ApplySingleTurnSly). EndOfTurnCleanup clears both private flags and resets
    /// ExtraReplayThisTurn to 0; BaseReplayCount stays (SoldiersStew, Transfigure, HiddenGem,
    /// SwordSagePower raise it; SwordSagePower.AfterRemoved also lowers it on SovereignBlade).
    /// CardModel.GetEnchantedReplayCount =
    /// Enchantment?.EnchantPlayCount(BaseReplayCount + ExtraReplayThisTurn) ?? the sum: the
    /// extra executions OnPlay gets from the card and its enchantment (Glam adds its Times
    /// while its Status is not Disabled, Spiral always adds Times), before other models'
    /// ModifyCardPlayCount hooks. The private flags are read as declared fields: no getter with
    /// AssertMutable, no setter, no hook.
    /// </summary>
    private static void AttachCardTurnState(Dictionary<string, object?> state, CardModel card,
        Dictionary<string, string> reasons)
    {
        ReadCardDetailValue(state, reasons, "retain_this_turn", () => card.ShouldRetainThisTurn);
        ReadCardDetailValue(state, reasons, "single_turn_retain",
            () => ReadDeclaredStateField<bool>(card, typeof(CardModel), "_hasSingleTurnRetain"));
        ReadCardDetailValue(state, reasons, "sly_this_turn", () => card.IsSlyThisTurn);
        ReadCardDetailValue(state, reasons, "single_turn_sly",
            () => ReadDeclaredStateField<bool>(card, typeof(CardModel), "_hasSingleTurnSly"));
        ReadCardDetailValue(state, reasons, "base_replay_count", () => card.BaseReplayCount);
        ReadCardDetailValue(state, reasons, "extra_replay_this_turn", () => card.ExtraReplayThisTurn);
        ReadCardDetailValue(state, reasons, "enchanted_replay_count", () =>
        {
            // EnchantPlayCount reads the enchantment's DynamicVars ("Times"), whose getter builds
            // the set when it is still null. A live (mutable) enchantment always has it
            // (EnchantmentModel.DeepCloneFields); refuse instead of letting the read create it.
            if (card.Enchantment is { } enchantment)
                _ = ReadEnchantmentDynamicVarSet(enchantment);
            return card.GetEnchantedReplayCount();
        });
        // MadScience.TinkerTimeRider (TinkerTime.RiderEffect, saved per instance): OnPlay picks the
        // rider's branch, and DynamicVars holds every rider's numbers, so the vars alone cannot say
        // which applies. Only Mad Science rows carry the key; None is a real value.
        if (card is MadScience madScience)
            ReadCardDetailValue(state, reasons, "tinker_time_rider", () => madScience.TinkerTimeRider.ToString());
    }

    /// <summary>EnchantmentModel._dynamicVars as it is: never the lazily-building getter.</summary>
    private static DynamicVarSet ReadEnchantmentDynamicVarSet(EnchantmentModel enchantment)
        => ReadDeclaredStateField<DynamicVarSet>(enchantment, typeof(EnchantmentModel), "_dynamicVars");

    private static Dictionary<string, object?> BuildCardAttachmentState(AbstractModel model,
        Func<int> readAmount, Dictionary<string, string> reasons, string field)
    {
        var state = new Dictionary<string, object?>();
        ReadCardDetailValue(state, reasons, "id", () => model.Id.Entry, $"{field}.id");
        ReadCardDetailValue(state, reasons, "amount", () => readAmount(), $"{field}.amount");
        if (model is EnchantmentModel enchantment)
        {
            ReadCardDetailValue(state, reasons, "status", () => enchantment.Status.ToString(), "enchantment.status");
            // The enchantment's own numbers, by name (Glam / Spiral "Times": the extra plays
            // EnchantPlayCount adds; Glam adds them only while status is not Disabled, which
            // Glam.AfterCardPlayed sets after the card's first play). BaseValue, StringVar
            // excluded, as in the card's dynamic_vars. An enchantment without vars is {}.
            ReadCardDetailValue(state, reasons, "dynamic_vars", () =>
            {
                var values = new Dictionary<string, object?>();
                foreach (var (name, dynamicVar) in ReadEnchantmentDynamicVarSet(enchantment))
                {
                    if (dynamicVar is StringVar) continue;
                    ReadCardDetailValue(values, reasons, name, () => dynamicVar.BaseValue, $"{field}.dynamic_vars.{name}");
                }
                return values;
            }, $"{field}.dynamic_vars");
        }
        // sts2-ae309f85: Momentum.EnchantDamageAdditive returns the private _extraDamage
        // (PoweredAttack only) and Momentum.OnPlay adds Amount to it after each play.
        // Read the declared field only: no private getter, setter (AssertMutable) or hook.
        // model.GetType() is Momentum itself (sealed); DeclaredOnly rejects a moved field.
        if (model is Momentum)
            ReadCardDetailValue(state, reasons, "extra_damage",
                () => ReadDeclaredStateField<int>(model, model.GetType(), "_extraDamage"), $"{field}.extra_damage");
        return state;
    }

    private static void ReadCardDetailValue(Dictionary<string, object?> state,
        Dictionary<string, string> reasons, string key, Func<object?> read, string? path = null)
    {
        try { state[key] = read(); }
        catch (Exception ex)
        {
            state[key] = null;
            reasons[path ?? key] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"card.{path ?? key}", ex);
        }
    }
}
