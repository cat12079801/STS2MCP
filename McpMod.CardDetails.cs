using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AttachCardPlayability(Dictionary<string, object?> state, CardModel card)
    {
        var reasons = (Dictionary<string, string>)state["unavailable_reasons"]!;
        state["unplayable_reason"] = null;
        // CanPlay also reads TargetType and card logic. Keep their failures local.
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
        ReadCardDetailValue(state, reasons, "affliction", () =>
        {
            var affliction = card.Affliction;
            return affliction == null ? null
                : BuildCardAttachmentState(affliction, () => affliction.Amount, reasons, "affliction");
        });
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
