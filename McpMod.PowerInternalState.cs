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
        // sts2#1034, v0.113.1 (sts2-a671e9ef): Rend.ShouldCountPower counts a target power when
        // power.TypeForCurrentAmount == PowerType.Debuff && !(power is ITemporaryPower), over
        // Creature.Powers whatever the HUD shows, so every emitted row carries both.
        // TypeForCurrentAmount is GetTypeForAmount(Amount): Debuff for a Counter power that
        // AllowNegative below 0 (Strength / Dexterity / Focus, whose Type is Buff), Buff for a
        // !AllowNegative Debuff below 0, else Type. The row's existing "type" stays PowerModel.Type.
        // It only reads _amount and the Type / StackType / AllowNegative getters (MonologuePower's
        // StackType reads DynamicVars, which a live power already holds); no hook, RNG or write.
        // A failed read is null and a reason, never Type in its place.
        Read("type_for_current_amount", () => power.TypeForCurrentAmount.ToString());
        // A type test on the instance: TemporaryStrength/Dexterity/Focus/ThornsPower and their
        // subclasses (FlexPotionPower, ManglePower, ...). It cannot fail; read the same way.
        Read("is_temporary_power", () => power is ITemporaryPower);
        // sts2#1020: every power's own numbers (PowerModel.DynamicVars) by name, so a client never
        // reads them from the description. They act whatever the HUD shows, so hidden and minimal
        // rows carry them too.
        AttachPowerDynamicVars(state, power, context);
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
            case RitualPower:
                // v0.113.1 (sts2-a671e9ef) RitualPower.AfterApplied sets the flag when an enemy
                // owner gets the power during the enemy side's turn; AfterSideTurnEnd then clears
                // it instead of applying Strength once (sts2#1018). The private field is read as
                // is (false is a real value) on every emitted row.
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
            // sts2#1020, v0.113.1 (sts2-a671e9ef): private Data / fields the effect reads that neither
            // Amount nor DynamicVars hold. The effects do not look at the HUD, so every emitted row
            // carries them. Data is read as is (never InitInternalData / GetInternalData); a card
            // reference becomes the uid this combat's registry already gave it (TryGetRegisteredCardUid,
            // never a new one), with uid_known=false and a reason for a card it has not seen.
            case RupturePower:
                // BeforeCardPlayed adds the owner's card (own side's turn) with 0; AfterDamageReceived adds
                // Amount per unblocked hit whose source is that card instead of applying Strength at once;
                // AfterCardPlayed removes the card and applies the summed Strength.
                Read("played_cards", () => ReadPowerCardAmountRows(power, "playedCards", "pending_strength", $"{context}.played_cards"));
                break;
            case CalamityPower:
            case StranglePower:
                // Calamity: BeforeCardPlayed records the owner's Attack with Amount; AfterCardPlayed removes
                // it and adds Amount random Attacks to the hand. Strangle (InstancedPerApplier, on the
                // enemy): BeforeCardPlayed records each card of the applier's player with Amount;
                // AfterCardPlayed removes it and deals the recorded value to the owner.
                Read("amounts_for_played_cards", () => ReadPowerCardAmountRows(power, "amountsForPlayedCards", "amount",
                    $"{context}.amounts_for_played_cards"));
                break;
            case HellraiserPower:
                // AfterCardDrawnEarly auto-plays a drawn Strike; while every hittable enemy shows infinite
                // HP it counts infiniteAutoPlaysThisTurn and stops at 9 (AfterSideTurnEnd and a draw against
                // a finite enemy reset it). autoPlayingCards holds the Strike being auto-played.
                // showedCapReachedMessage only gates a ThinkCmd line and is left out.
                Read("infinite_auto_plays_this_turn", () => ReadPowerDataField<int>(power, "infiniteAutoPlaysThisTurn"));
                Read("auto_playing_cards", () => ReadPowerCardSetRows(power, "autoPlayingCards", $"{context}.auto_playing_cards"));
                break;
            case AutomationPower:
                // Instanced. AfterCardDrawn counts the owner's draws down from 10; at 0 it gains Amount
                // energy and resets to 10 (DisplayAmount is this value; BaseCards is only the text's 10).
                Read("cards_left", () => ReadPowerDataField<int>(power, "cardsLeft"));
                break;
            case DarkEmbracePower:
                // AfterCardExhausted counts the owner's Ethereal exhausts; AfterSideTurnEnd draws
                // Amount x the count and sets 0 (other exhausts draw at once).
                Read("ethereal_count", () => ReadPowerDataField<int>(power, "etherealCount"));
                break;
            case GigantificationPower:
                // BeforeAttack binds the owner's first powered Attack card; ModifyDamageMultiplicative
                // triples only that card while bound (any while unbound); AfterAttack clears it and
                // decrements. A readable false is "unbound".
                Read("has_command_to_modify", () => ReadVigorCommand(ReadPowerData(power)) != null);
                break;
            case HardenedShellPower:
                // ModifyHpLostBeforeOstyLate caps HP loss at Amount - this; AfterDamageReceived adds the
                // unblocked damage; BeforeSideTurnStart (either side) sets 0.
                Read("damage_received_this_turn", () => ReadPowerDataField<decimal>(power, "damageReceivedThisTurn"));
                break;
            case AdaptablePower:
            case ReattachPower:
            case IllusionPower:
                // Set while the owner is dead and waiting to revive (ShouldAllowHitting is false then).
                Read("is_reviving", () => ReadPowerDataField<bool>(power, "isReviving"));
                if (power is IllusionPower illusion)
                {
                    // IllusionPower.FollowUpStateId (a readable null is "none recorded"). v0.113.1 has no
                    // caller of its setter, so it is always null: AfterDeath builds REVIVE_MOVE with
                    // MoveState.FollowUpStateId = this ?? StateLog.Last().Id, and the real destination is
                    // that move's follow-up in battle.enemies[].move_state. Exposed as is, not as the target.
                    state["follow_up_state_readable"] = false;
                    Read("follow_up_state_id", () =>
                    {
                        string? id = illusion.FollowUpStateId;
                        state["follow_up_state_readable"] = true;
                        return id;
                    });
                }
                break;
            case NemesisPower:
                // AfterSideTurnEnd (owner participating) flips it, applies Intangible 1 when it becomes
                // true and removes Intangible when it becomes false.
                Read("should_apply_intangible", () => ReadDeclaredStateField<bool>(power, typeof(NemesisPower), "_shouldApplyIntangible"));
                break;
            case SlothPower:
                // ShouldPlay refuses the owner's cards once this reaches Amount; BeforeCardPlayed adds 1,
                // BeforeSideTurnStart sets 0 (DisplayAmount is this value).
                Read("cards_played_this_turn", () => ReadDeclaredStateField<int>(power, typeof(SlothPower), "_cardsPlayedThisTurn"));
                break;
            case TenderPower:
                // AfterCardPlayed adds 1 (and -1 Strength / Dexterity); AfterSideTurnEnd gives that much
                // back and sets 0 (DisplayAmount is this value).
                Read("cards_played_this_turn", () => ReadDeclaredStateField<int>(power, typeof(TenderPower), "_cardsPlayedThisTurn"));
                break;
            case BeaconOfHopePower:
                // True only while AfterBlockGained hands half the Block to teammates (stops a loop).
                Read("has_already_been_given_block", () => ReadDeclaredStateField<bool>(power, typeof(BeaconOfHopePower), "_hasAlreadyBeenGivenBlock"));
                break;
            case SwipePower:
                // Steal sets the stolen card (and removes its deck version); BeforeDeath returns the deck
                // version as a reward. A readable null is "nothing stolen".
                state["stolen_card_readable"] = false;
                Read("stolen_card", () =>
                {
                    var field = typeof(SwipePower).GetField("_stolenCard",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        ?? throw new MissingFieldException(nameof(SwipePower), "_stolenCard");
                    if (field.FieldType != typeof(CardModel))
                        throw new InvalidOperationException("Expected CardModel field SwipePower._stolenCard");
                    var row = field.GetValue(power) is CardModel card ? BuildRegisteredCardRow(card, $"{context}.stolen_card") : null;
                    state["stolen_card_readable"] = true;
                    return row;
                });
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

    // sts2#1020, v0.113.1 (sts2-a671e9ef): PowerModel.DynamicVars is built from CanonicalVars
    // (an empty set for the many powers without vars) and DeepCloneFields copies it to every live
    // power; the effects read and write DynamicVar.BaseValue (a plain field: InfernoPower's
    // SelfDamage, TheBombPower's Damage, VulnerablePower's DamageIncrease, SlowPower's SlowAmount,
    // ...). dynamic_vars is { name -> BaseValue } in the set's own order. StringVar is left out:
    // its numeric base is a dummy (as in the card's and the enchantment's dynamic_vars). The set
    // is read from the private field, never the lazily-building getter: a null field fails the
    // whole key (null, reason, warning), never {}; a readable empty set is {}. A var whose value
    // cannot be read is null under its own name with a reason at dynamic_vars.<name>; the others
    // are kept.
    private static void AttachPowerDynamicVars(Dictionary<string, object?> state, PowerModel power,
        string context)
    {
        ReadInternalStateValue(state, "dynamic_vars", () =>
        {
            var values = new Dictionary<string, object?>();
            foreach (var (name, dynamicVar) in ReadPowerDynamicVarSet(power))
            {
                if (dynamicVar is StringVar) continue;
                try { values[name] = dynamicVar.BaseValue; }
                catch (Exception ex)
                {
                    values[name] = null;
                    RecordInternalStateFailure(state, "dynamic_vars." + name, ex, context);
                }
            }
            return values;
        }, context);
    }

    /// <summary>PowerModel._dynamicVars as it is: never the lazily-building getter.</summary>
    private static DynamicVarSet ReadPowerDynamicVarSet(PowerModel power)
        => ReadDeclaredStateField<DynamicVarSet>(power, typeof(PowerModel), "_dynamicVars");

    // v0.113.1 (sts2-a671e9ef): PowerCmd.Apply sets PowerModel.Applier only on a new instance
    // (HexPower keeps InstanceType None, so FindExistingInstanceForStacking sends a re-apply to
    // ModifyAmount and the first applier stays); SpectralKnight.HexMove passes its own Creature.
    // HexPower.AfterDeath removes the power when the dying creature is the applier (unless removal
    // was prevented), whatever IsVisible says. The generated allowlist reads PowerModel._applier
    // once for every power row (internal_state.applier_combat_id; Shrink / Constrict / Covered /
    // ... also act on it). The engine (tools/turnplan/hexpower.py) reads the Hex row's own
    // applier_combat_id / applier_readable, so that shape is copied here from the allowlist value
    // instead of reading Applier a second time: readable=true with a null id is a known "no
    // applier"; a failed read (an applier without a CombatId included) is readable=false, a null
    // id and the same reason. No id is substituted. Call after AttachAllowlistedState.
    private static void AttachHexApplierCompat(Dictionary<string, object?> state, PowerModel power)
    {
        if (power is not HexPower) return;
        CopyAllowlistedReference(state, "applier_combat_id", "applier_readable", $"status.{state["id"]}");
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

    // { uid, uid_known, card_id } for a card the effect holds: the uid this combat's registry already
    // gave it, or uid=null, uid_known=false and a reason (also a warning) for one it has not seen.
    // Never mints a uid.
    private static Dictionary<string, object?> BuildRegisteredCardRow(CardModel card, string warnPath)
    {
        string? cardId = SafeGetText(() => card.Id.Entry);
        var row = new Dictionary<string, object?> { ["uid"] = null, ["uid_known"] = false, ["card_id"] = cardId };
        if (TryGetRegisteredCardUid(card, out string uid))
        {
            row["uid"] = uid;
            row["uid_known"] = true;
        }
        else
        {
            string reason = $"{cardId ?? "unknown"} は戦闘の uid registry に無い（新たに採番しない）";
            row["uid_unavailable_reason"] = reason;
            Warn(warnPath + ".uid", reason);
        }
        return row;
    }

    // Known uids first in uid order, then unknown ones by card id: dictionary / set order follows play
    // or draw order, which the rows do not need to carry.
    private static int CompareRegisteredCardRows(Dictionary<string, object?> a, Dictionary<string, object?> b, string? valueKey)
    {
        bool aKnown = a["uid_known"] is true, bKnown = b["uid_known"] is true;
        if (aKnown != bKnown) return aKnown ? -1 : 1;
        int byUid = StringComparer.Ordinal.Compare(a["uid"] as string, b["uid"] as string);
        if (byUid != 0) return byUid;
        int byId = StringComparer.Ordinal.Compare(a["card_id"] as string, b["card_id"] as string);
        return byId != 0 || valueKey == null ? byId : ((int)a[valueKey]!).CompareTo((int)b[valueKey]!);
    }

    // A private Data Dictionary<CardModel, int> as { uid, uid_known, card_id, <valueKey> } rows.
    private static List<Dictionary<string, object?>> ReadPowerCardAmountRows(PowerModel power, string field,
        string valueKey, string warnPath)
    {
        var map = ReadPowerDataField<Dictionary<CardModel, int>>(power, field);
        var rows = new List<Dictionary<string, object?>>(map.Count);
        foreach (var (card, value) in map)
        {
            var row = BuildRegisteredCardRow(card, warnPath);
            row[valueKey] = value;
            rows.Add(row);
        }
        rows.Sort((a, b) => CompareRegisteredCardRows(a, b, valueKey));
        return rows;
    }

    // A private Data HashSet<CardModel> as { uid, uid_known, card_id } rows.
    private static List<Dictionary<string, object?>> ReadPowerCardSetRows(PowerModel power, string field, string warnPath)
    {
        var set = ReadPowerDataField<HashSet<CardModel>>(power, field);
        var rows = new List<Dictionary<string, object?>>(set.Count);
        foreach (var card in set)
            rows.Add(BuildRegisteredCardRow(card, warnPath));
        rows.Sort((a, b) => CompareRegisteredCardRows(a, b, null));
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
