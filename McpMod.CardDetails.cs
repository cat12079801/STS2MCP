using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
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
        // sts2-a671e9ef: HexPower.TryModifyKeywordsInCombat adds Ethereal to the owner's
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
    }

    private static Dictionary<string, object?> BuildCardAttachmentState(AbstractModel model,
        Func<int> readAmount, Dictionary<string, string> reasons, string field)
    {
        var state = new Dictionary<string, object?>();
        ReadCardDetailValue(state, reasons, "id", () => model.Id.Entry, $"{field}.id");
        ReadCardDetailValue(state, reasons, "amount", () => readAmount(), $"{field}.amount");
        if (model is EnchantmentModel enchantment)
            ReadCardDetailValue(state, reasons, "status", () => enchantment.Status.ToString(), "enchantment.status");
        // sts2-a671e9ef: Momentum.EnchantDamageAdditive returns the private _extraDamage
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
