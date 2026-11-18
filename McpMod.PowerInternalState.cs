using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
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
            case VigorPower:
                // The binding decides whether the current Amount is added to, and later
                // subtracted by, an attack; the HUD flag does not change it, so hidden and
                // minimal rows carry it too.
                AttachVigorAttackState(state, power, context);
                break;
            case HexPower:
                // HexPower.AfterDeath removes the power when the dying creature is
                // PowerModel.Applier (unless removal was prevented), whatever IsVisible
                // says, so every emitted row carries the applier.
                AttachHexApplierState(state, power, context);
                break;
            case RitualPower:
                // v0.113.1 (sts2-a671e9ef) RitualPower.AfterApplied sets the flag when an enemy
                // owner gets the power during the enemy side's turn; AfterSideTurnEnd then clears
                // it instead of applying Strength once (sts2#1018). The private field is read as
                // is (false is a real value) on every emitted row, like the applier above.
                // Never derived from the round or the move history.
                Read("was_just_applied_by_enemy",
                    () => ReadDeclaredStateField<bool>(power, typeof(RitualPower), "_wasJustAppliedByEnemy"));
                break;
            // sts2#1019, v0.113.1 (sts2-a671e9ef): per-instance numbers the power's effect uses
            // that Amount does not hold. They act whatever the HUD shows, so every emitted row
            // carries them. The set is read from PowerModel._dynamicVars as it is (the getter
            // builds it when null; DeepCloneFields always has on a live power): a null set or a
            // missing var is a failed read (null and a reason), never a default.
            case TheBombPower:
                // TheBombPower is Instanced; Amount counts the turns left. SetDamage writes the
                // damage to DynamicVars.Damage.BaseValue and BeforeSideTurnEnd deals that var
                // when Amount reaches 1.
                Read("damage", () => ReadPowerDynamicVarSet(power).Damage.BaseValue);
                break;
            case InfernoPower:
            case CrimsonMantlePower:
                // AfterPlayerTurnStart damages the owner by DynamicVars["SelfDamage"].BaseValue
                // (IncrementSelfDamage raises it each time the card is played again).
                Read("self_damage", () => ReadPowerDynamicVarSet(power)["SelfDamage"].BaseValue);
                break;
            case PanachePower:
                // PanachePower is Instanced. AfterCardPlayed counts CardsLeft down only once
                // Data.alreadyApplied is set (the first notification, the Panache card itself,
                // just sets it), deals Amount at 0 and resets CardsLeft to 5; AfterSideTurnEnd
                // resets CardsLeft to 5 and leaves alreadyApplied as it is. DisplayAmount is
                // CardsLeft.IntValue. Data is read as is, never initialized.
                Read("cards_left", () => ReadPowerDynamicVarSet(power)["CardsLeft"].IntValue);
                Read("already_applied", () => ReadPowerDataField<bool>(power, "alreadyApplied"));
                break;
            case DampenPower:
                // sts2#1031, v0.113.1 (sts2-a671e9ef): private DampenPower.Data. MagiKnight.DampenMove
                // calls AddCaster (casters.Add) on the target's existing power, or on a new one it
                // then applies; AfterApplied records every upgraded combat card's
                // CurrentUpgradeLevel and downgrades it; AfterDeath drops a dead caster (unless
                // removal was prevented) and removes the power when none is left; AfterRemoved
                // upgrades each recorded card that many times. Removal does not hang on the HUD,
                // so every emitted row carries both. Data is read as is, never initialized.
                Read("caster_combat_ids", () => ReadDampenCasterCombatIds(power));
                Read("downgraded_cards", () => ReadDampenDowngradedCards(power, context));
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

    /// <summary>PowerModel._dynamicVars as it is: never the lazily-building getter.</summary>
    private static DynamicVarSet ReadPowerDynamicVarSet(PowerModel power)
        => ReadDeclaredStateField<DynamicVarSet>(power, typeof(PowerModel), "_dynamicVars");

    // v0.113.1 (sts2-a671e9ef): PowerCmd.Apply sets PowerModel.Applier only on a new
    // instance (HexPower keeps InstanceType None, so FindExistingInstanceForStacking sends a
    // re-apply to ModifyAmount and the first applier stays); SpectralKnight.HexMove passes its own Creature. The id is the same
    // Creature.CombatId as enemies[].combat_id. The public getter only returns
    // _applier (AssertMutable is on the setter only; the failure path is defensive); nothing is initialized or written.
    // applier_readable=true with a null id is a known "no applier", distinct from a
    // failed read (readable=false, null id, reason). An applier without a CombatId is a
    // failed read too: no id is substituted.
    private static void AttachHexApplierState(Dictionary<string, object?> state, PowerModel power,
        string context)
    {
        state["applier_readable"] = false;
        ReadInternalStateValue(state, "applier_combat_id", () =>
        {
            var applier = power.Applier;
            uint? id = null;
            if (applier != null)
                id = applier.CombatId ?? throw new InvalidOperationException("Applier に CombatId がありません");
            state["applier_readable"] = true;
            return id;
        }, context);
    }

    // v0.113.1 (sts2-a671e9ef) VigorPower.Data: BeforeAttack binds the owner's first
    // powered attack (commandToModify, amountWhenAttackStarted = Amount) while unbound;
    // ModifyDamageAdditive skips a bound command whose Attacker is not the dealer;
    // AfterAttack subtracts amountWhenAttackStarted only for that same command and never
    // clears the reference. Read the two fields as they are: no InitInternalData, no
    // GetInternalData, no hook. A readable null command (unbound) is reported as
    // has_command=false and stays distinct from a failed read (readable=false, nulls).
    private static void AttachVigorAttackState(Dictionary<string, object?> state, PowerModel power,
        string context)
    {
        var vigor = new Dictionary<string, object?>
        {
            ["readable"] = false,
            ["has_command"] = null,
            ["attacker_is_owner"] = null,
            ["amount_when_started"] = null
        };
        state["vigor_attack"] = vigor;
        void Fail(string key, Exception ex) => RecordInternalStateFailure(state, "vigor_attack." + key, ex, context);

        object data;
        try { data = ReadPowerData(power); }
        catch (Exception ex)
        {
            // Without Data neither the binding nor its attacker is known.
            Fail("has_command", ex);
            Fail("attacker_is_owner", ex);
            Fail("amount_when_started", ex);
            return;
        }

        bool ok = true;
        try { vigor["amount_when_started"] = ReadDeclaredStateField<int>(data, data.GetType(), "amountWhenAttackStarted"); }
        catch (Exception ex) { ok = false; Fail("amount_when_started", ex); }

        AttackCommand? command;
        try { command = ReadVigorCommand(data); }
        catch (Exception ex)
        {
            Fail("has_command", ex);
            Fail("attacker_is_owner", ex);
            return;
        }
        vigor["has_command"] = command != null;
        if (command != null)
        {
            // Creature has no operator overload: the same reference comparison as
            // VigorPower.ModifyDamageAdditive. Attacker is an auto-property and
            // PowerModel.Owner only asserts the model is mutable.
            try { vigor["attacker_is_owner"] = command.Attacker == power.Owner; }
            catch (Exception ex) { ok = false; Fail("attacker_is_owner", ex); }
        }
        vigor["readable"] = ok;
    }

    private static AttackCommand? ReadVigorCommand(object data)
    {
        var field = data.GetType().GetField("commandToModify",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException(data.GetType().Name, "commandToModify");
        if (field.FieldType != typeof(AttackCommand))
            throw new InvalidOperationException("Expected AttackCommand field Data.commandToModify");
        return field.GetValue(data) as AttackCommand;
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

    // Creature.CombatId of each Data.casters entry, the same id as enemies[].combat_id, in
    // ascending order (HashSet order is not part of the game state). A caster without a
    // CombatId fails the whole read: no id is substituted and no caster is dropped.
    private static List<uint> ReadDampenCasterCombatIds(PowerModel power)
    {
        var casters = ReadPowerDataField<HashSet<Creature>>(power, "casters");
        var ids = new List<uint>(casters.Count);
        foreach (var creature in casters)
            ids.Add(creature.CombatId ?? throw new InvalidOperationException("caster に CombatId がありません"));
        ids.Sort();
        return ids;
    }

    // Data.downgradedCardsToOldUpgradeLevels as rows. The card is matched to the uid this
    // combat's registry already gave it (pile rows are built before status rows); a card
    // the registry has not seen gets uid=null, uid_known=false and a reason instead of a
    // freshly minted uid. Rows are sorted by uid (then card id and level for unknown
    // ones): the dictionary's insertion order follows PlayerCombatState.AllCards and so
    // the hidden draw order.
    private static List<Dictionary<string, object?>> ReadDampenDowngradedCards(PowerModel power, string context)
    {
        var levels = ReadPowerDataField<Dictionary<CardModel, int>>(power, "downgradedCardsToOldUpgradeLevels");
        var rows = new List<Dictionary<string, object?>>(levels.Count);
        foreach (var (card, level) in levels)
        {
            string? cardId = SafeGetText(() => card.Id.Entry);
            var row = new Dictionary<string, object?>
            {
                ["uid"] = null,
                ["uid_known"] = false,
                ["card_id"] = cardId,
                ["original_upgrade_level"] = level
            };
            if (TryGetRegisteredCardUid(card, out string uid))
            {
                row["uid"] = uid;
                row["uid_known"] = true;
            }
            else
            {
                string reason = $"{cardId ?? "unknown"} は戦闘の uid registry に無い（新たに採番しない）";
                row["uid_unavailable_reason"] = reason;
                Warn($"{context}.downgraded_cards.uid", reason);
            }
            rows.Add(row);
        }
        rows.Sort((a, b) =>
        {
            bool aKnown = a["uid_known"] is true, bKnown = b["uid_known"] is true;
            if (aKnown != bKnown) return aKnown ? -1 : 1;
            int byUid = StringComparer.Ordinal.Compare(a["uid"] as string, b["uid"] as string);
            if (byUid != 0) return byUid;
            int byId = StringComparer.Ordinal.Compare(a["card_id"] as string, b["card_id"] as string);
            return byId != 0 ? byId : ((int)a["original_upgrade_level"]!).CompareTo((int)b["original_upgrade_level"]!);
        });
        return rows;
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
