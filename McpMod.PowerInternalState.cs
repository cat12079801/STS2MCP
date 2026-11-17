using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    // sts2-d009478a: explicit allowlist; never invoke InitInternalData or game hooks.
    private static void AttachPowerInternalState(Dictionary<string, object?> state, PowerModel power,
        bool visible)
    {
        string context = $"status.{state["id"]}";
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key, read, context);
        // v0.113.1 (sts2-a671e9ef): Thievery/Heist are Instanced powers, one per targeted
        // player. PowerModel.IsVisible is false when Target is another player, but the row
        // is still emitted with its live stack, so its target and stolen gold are attached
        // to every row BuildPowersState emits, not only to HUD-visible ones.
        switch (power)
        {
            case ThieveryPower thievery:
                // ThieveryPower.Steal adds the amount actually lost (min(Amount, gold)) to
                // Gold.BaseValue; SurprisePower.AfterDeath reads Gold.IntValue. Amount is
                // only the per-steal cap, so never derive this from it.
                Read("stolen_gold", () => thievery.DynamicVars.Gold.IntValue);
                Read("target_player_id", () => ReadPowerTargetPlayerId(power));
                break;
            case HeistPower:
                // HeistPower.BeforeDeath returns Amount gold to Target.Player.
                Read("target_player_id", () => ReadPowerTargetPlayerId(power));
                break;
        }

        // Hidden bookkeeping powers have no observable application history.
        if (!visible) return;
        Read("skip_next_duration_tick", () => power.SkipNextDurationTick);
        switch (power)
        {
            case ToricToughnessPower toric:
                Read("block_base_value", () => toric.DynamicVars.Block.BaseValue);
                break;
            case SurroundedPower surrounded:
                Read("facing", () => surrounded.Facing.ToString());
                break;
            case PossessStrengthPower:
                Read("stolen_strength", () => ReadStolenPowerAmounts(power, "stolenStrength"));
                break;
            case PossessSpeedPower:
                Read("stolen_dexterity", () => ReadStolenPowerAmounts(power, "stolenDexterity"));
                break;
            case SkittishPower:
                Read("has_gained_block_this_turn", () => ReadPowerDataField<bool>(power, "hasGainedBlockThisTurn"));
                break;
            case ChainsOfBindingPower:
                Read("bound_card_played", () => ReadPowerDataField<bool>(power, "boundCardPlayed"));
                break;
            case JugglingPower:
                Read("attacks_played_this_turn", () => ReadPowerDataField<int>(power, "attacksPlayedThisTurn"));
                ReadCardUid("card_to_clone_uid", "card_to_clone_readable", "cardToClone");
                break;
            case CurlUpPower:
                ReadCardUid("played_card_uid", "played_card_readable", "playedCard");
                break;
        }

        // A nullable card reference needs a separate success flag: a readable null
        // (nothing pending/tracked) must stay distinguishable from a failed read.
        void ReadCardUid(string uidKey, string readableKey, string field)
        {
            state[readableKey] = false;
            Read(uidKey, () =>
            {
                string? uid = ReadPowerDataCardUid(power, field);
                state[readableKey] = true;
                return uid;
            });
        }
    }

    // Target is null for untargeted powers and may be a non-player creature. Neither is
    // replaced with the local player or a default id.
    private static string ReadPowerTargetPlayerId(PowerModel power)
    {
        var target = power.Target ?? throw new InvalidOperationException("Target がありません");
        var player = target.Player ?? throw new InvalidOperationException("Target が player ではありません");
        return player.NetId.ToString();
    }

    private static T ReadPowerDataField<T>(PowerModel power, string field)
    {
        object data = ReadPowerData(power);
        return ReadDeclaredStateField<T>(data, data.GetType(), field);
    }

    private static object ReadPowerData(PowerModel power)
    {
        var dataType = power.GetType().GetNestedType("Data", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(power.GetType().Name, "Data");
        object data = ReadDeclaredStateField<object>(power, typeof(PowerModel), "_internalData");
        if (data.GetType() != dataType)
            throw new InvalidOperationException($"Expected {power.GetType().Name}.Data");
        return data;
    }

    private static string? ReadPowerDataCardUid(PowerModel power, string name)
    {
        object data = ReadPowerData(power);
        var field = data.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException(data.GetType().Name, name);
        if (field.FieldType != typeof(CardModel))
            throw new InvalidOperationException($"Expected CardModel field Data.{name}");
        // A readable null means no reference (Juggling: no pending clone; CurlUp: no
        // tracked attack). Never initialize Data or derive the reference from counts
        // or history. Reuse the hand/pile/history registry.
        return field.GetValue(data) is CardModel card ? GetStableCardUid(card) : null;
    }

    private static List<Dictionary<string, object?>> ReadStolenPowerAmounts(PowerModel power, string field)
    {
        var amounts = ReadPowerDataField<Dictionary<Creature, decimal>>(power, field);
        var result = new List<Dictionary<string, object?>>();
        foreach (var (creature, amount) in amounts)
        {
            // Possess only records players. Use their existing player.player_id, not an
            // enemy entity id or a sum that loses the recipient in multiplayer.
            var player = creature.Player ?? throw new InvalidOperationException("Stolen power target is not a player");
            result.Add(new Dictionary<string, object?>
            {
                ["player_id"] = player.NetId.ToString(),
                ["amount"] = amount
            });
        }
        return result;
    }
}
