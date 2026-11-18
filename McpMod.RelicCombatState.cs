using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace STS2_MCP;

public static partial class McpMod
{
    // 許可リスト。sts2-9cb4f1ad の各クラスのフックで意味・リセットを確認した値だけを読む。
    // 公開 getter があるものは直接利用し、private 値は指定名・指定型のフィールドに限定する。
    // 演出フラグやカード解決途中の参照は含めない。出典・選定理由は docs/relic-combat-state.md。
    private static Dictionary<string, object?> BuildRelicCombatState(RelicModel relic)
    {
        var state = new Dictionary<string, object?>();
        switch (relic)
        {
            case CentennialPuzzle puzzle:
                state["used_this_combat"] = puzzle.UsedThisCombat;
                break;
            case RainbowRing:
                AddRelicCombatField<int>(relic, state, "attacks_played_this_turn", "_attacksPlayedThisTurn");
                AddRelicCombatField<int>(relic, state, "skills_played_this_turn", "_skillsPlayedThisTurn");
                AddRelicCombatField<int>(relic, state, "powers_played_this_turn", "_powersPlayedThisTurn");
                AddRelicCombatField<int>(relic, state, "activation_count_this_turn", "_activationCountThisTurn");
                break;
            case Permafrost:
                AddRelicCombatField<bool>(relic, state, "activated_this_combat", "_activatedThisCombat");
                break;
            case ArtOfWar:
                AddRelicCombatField<bool>(relic, state, "any_attacks_played_this_turn", "_anyAttacksPlayedThisTurn");
                AddRelicCombatField<bool>(relic, state, "any_attacks_played_last_turn", "_anyAttacksPlayedLastTurn");
                break;
            case PaelsTears:
                AddRelicCombatField<bool>(relic, state, "had_leftover_energy", "_hadLeftoverEnergy");
                break;
            case Vambrace:
                AddRelicCombatField<bool>(relic, state, "block_gained_this_combat", "_blockGainedThisCombat");
                break;
            case DemonTongue:
                AddRelicCombatField<bool>(relic, state, "triggered_this_turn", "_triggeredThisTurn");
                break;
            case Pocketwatch:
                AddRelicCombatField<int>(relic, state, "cards_played_last_turn", "_cardsPlayedLastTurn");
                // sts2#1020: AfterCardPlayed counts the owner's plays in combat; BeforeSideTurnStart moves
                // it to _cardsPlayedLastTurn (ModifyHandDraw's next-turn test) and clears it.
                AddRelicInternalField<int>(relic, state, "cards_played_this_turn", "_cardsPlayedThisTurn");
                break;
            case RedSkull:
                AddRelicCombatField<bool>(relic, state, "strength_applied", "_strengthApplied");
                break;
            case BeltBuckle:
                AddRelicCombatField<bool>(relic, state, "dexterity_applied", "_dexterityApplied");
                break;
            case BurningSticks:
                AddRelicCombatField<bool>(relic, state, "used_this_combat", "_wasUsedThisCombat");
                break;
            case MusicBox:
                AddRelicCombatField<bool>(relic, state, "used_this_turn", "_wasUsedThisTurn");
                break;
            case PaelsEye:
                AddRelicCombatField<bool>(relic, state, "used_this_combat", "_usedThisCombat");
                AddRelicCombatField<bool>(relic, state, "was_owner_part_of_last_player_turn", "_wasOwnerPartOfLastPlayerTurn");
                break;
            case ThrowingAxe:
                AddRelicCombatField<bool>(relic, state, "used_this_combat", "_usedThisCombat");
                break;
            case PaelsLegion:
                AddRelicCombatField<int>(relic, state, "cooldown", "_cooldown");
                break;
            case BeatingRemnant:
                AddRelicCombatField<decimal>(relic, state, "damage_received_this_turn", "_damageReceivedThisTurn");
                break;
            case MiniRegent:
                AddRelicCombatField<bool>(relic, state, "used_this_turn", "_usedThisTurn");
                break;
            case Regalite:
                AddRelicCombatField<bool>(relic, state, "used_this_turn", "_usedThisTurn");
                break;
            case RuinedHelmet:
                AddRelicCombatField<bool>(relic, state, "used_this_combat", "_usedThisCombat");
                break;
            case UnsettlingLamp:
                AddRelicCombatField<bool>(relic, state, "used_this_combat", "_isFinishedTriggering");
                break;
            case LavaLamp lamp:
                state["took_damage_this_combat"] = lamp.TookDamageThisCombat;
                break;
            case PenNib nib:
                ReadInternalStateValue(state, "attacks_played", () => nib.AttacksPlayed,
                    $"player.relics.{relic.Id.Entry}.combat_state");
                break;
            case BrilliantScarf:
                AddRelicInternalField<int>(relic, state, "cards_played_this_turn", "_cardsPlayedThisTurn");
                break;
            case JossPaper paper:
                ReadInternalStateValue(state, "cards_exhausted", () => paper.CardsExhausted,
                    $"player.relics.{relic.Id.Entry}.combat_state");
                AddRelicInternalField<int>(relic, state, "ethereal_count", "_etherealCount");
                break;
            // sts2#1020, v0.113.1 (sts2-a671e9ef): per-turn counters. AfterCardPlayed counts the owner's
            // Attacks (Skills for Letter Opener) while a combat is in progress and fires on every multiple
            // of DynamicVars.Cards (relics[].vars); BeforeSideTurnStart (owner participating) and
            // AfterCombatEnd set them to 0. counter is a display value (count % Cards, or Cards while the
            // private _isActivating animation flag is set), so the count itself is read here.
            case Kunai:
            case Shuriken:
            case OrnamentalFan:
            case Kusarigama:
                AddRelicInternalField<int>(relic, state, "attacks_played_this_turn", "_attacksPlayedThisTurn");
                break;
            case LetterOpener:
                AddRelicInternalField<int>(relic, state, "skills_played_this_turn", "_skillsPlayedThisTurn");
                break;
            // BeforeSideTurnEndVeryEarly sets it when the owner ends the turn without Block;
            // BeforeSideTurnEnd consumes it (gains DynamicVars.Block) and BeforeSideTurnStart clears it,
            // so it is false while the player acts and true only between those two end-of-turn hooks.
            case Orichalcum:
            case FakeOrichalcum:
                AddRelicInternalField<bool>(relic, state, "should_trigger", "_shouldTrigger");
                break;
            case FurCoat coat:
                ReadInternalStateValue(state, "affects_current_room", () => ReadFurCoatCurrentRoom(coat),
                    $"player.relics.{relic.Id.Entry}.combat_state");
                break;
        }
        return state;
    }

    private static void AddRelicInternalField<T>(RelicModel relic,
        Dictionary<string, object?> state, string key, string field, string section = "combat_state") where T : struct
        => ReadInternalStateValue(state, key, () => ReadDeclaredStateField<T>(relic, relic.GetType(), field),
            $"player.relics.{relic.Id.Entry}.{section}");

    /// <summary>
    /// sts2#1020: [SavedProperty] counters that live across combats (v0.113.1, sts2-a671e9ef). They are
    /// emitted in and out of combat, as relics[].run_state, because their next trigger can lie in the
    /// next combat, a rest site or a reward screen. Each is the private backing field of the saved
    /// property, read by declared name and type (no getter, setter, hook or RNG). A failed read is null
    /// with run_state.unavailable_reasons.&lt;key&gt; and a warning at player.relics.&lt;ID&gt;.run_state.&lt;key&gt;.
    /// </summary>
    private static Dictionary<string, object?> BuildRelicRunState(RelicModel relic)
    {
        var state = new Dictionary<string, object?>();
        void Int(string key, string field) => AddRelicInternalField<int>(relic, state, key, field, "run_state");
        void Bool(string key, string field) => AddRelicInternalField<bool>(relic, state, key, field, "run_state");
        switch (relic)
        {
            // AfterCardPlayed counts every owner Attack (Nunchaku: energy on each multiple of Cards while
            // in combat) / Skill (Tuning Fork: Block at Cards, then subtracts Cards) / card (Iron Club:
            // draw on each multiple of Cards in combat). Never reset at combat end.
            case Nunchaku:
                Int("attacks_played", "_attacksPlayed");
                break;
            case TuningFork:
                Int("skills_played", "_skillsPlayed");
                break;
            case IronClub:
                Int("cards_played", "_cardsPlayed");
                break;
            // Happy Flower: AfterSideTurnStart sets (TurnsSeen + 1) % Turns and gains energy at 0.
            // Pendulum: BeforeHandDraw does the same and ModifyHandDraw adds Cards at 0. Pollinous Core:
            // BeforeHandDraw adds 1, ModifyHandDraw adds Cards once it reaches Turns and
            // AfterModifyingHandDraw sets 0. None resets at combat end.
            case HappyFlower:
            case FakeHappyFlower:
            case Pendulum:
            case PollinousCore:
                Int("turns_seen", "_turnsSeen");
                break;
            // TimesLifted: each Lift at a rest site; AfterRoomEntered applies that much Strength in
            // combat rooms, and the Lift option is offered while it is below 3.
            case Girya:
                Int("times_lifted", "_timesLifted");
                break;
            // BeforeCombatRewardOffered adds 1 when the rewards hold a card reward; the extra card is
            // offered while it is odd (IsInTriggeringCombat).
            case LastingCandy:
                Int("combat_rewards_seen", "_combatRewardsSeen");
                break;
            // AfterCardChangedPiles adds 1 per card put into the deck and heals on each multiple of Cards.
            case BookOfFiveRings:
                Int("cards_added", "_cardsAdded");
                break;
            // AfterRoomEntered (rest site) sets it; AfterEnergyReset gains Energy and clears it.
            case VenerableTeaSet:
            case FakeVenerableTeaSet:
                Bool("gain_energy_in_next_combat", "_gainEnergyInNextCombat");
                break;
            // ModifyMaxEnergy adds Energy while it is above 0; AfterCombatEnd lowers it by 1.
            case PumpkinCandle:
                Int("kindle_count", "_kindleCount");
                break;
            // Out-of-combat triggers whose counter shows only a derived value (a remainder, the uses
            // left) or nothing: elites defeated (Sword of Stone), flights used (Winged Boots), uses and
            // treasure rooms (Silver Crucible), combats seen (Fishing Rod, Toy Box), combats finished
            // (Wongo's Mystery Ticket), rewards sacrificed (Pael's Wing), the act-1 boss relic reward
            // already added (Lava Rock).
            case SwordOfStone:
                Int("elites_defeated", "_elitesDefeated");
                break;
            case WingedBoots:
                Int("times_used", "_timesUsed");
                break;
            case SilverCrucible:
                Int("times_used", "_timesUsed");
                Int("treasure_rooms_entered", "_treasureRoomsEntered");
                break;
            case FishingRod:
            case ToyBox:
                Int("combats_seen", "_combatsSeen");
                break;
            case WongosMysteryTicket:
                Int("combats_finished", "_combatsFinished");
                break;
            case PaelsWing:
                Int("rewards_sacrificed", "_rewardsSacrificed");
                break;
            case LavaRock:
                Bool("has_triggered", "_hasTriggered");
                break;
        }
        return state;
    }

    /// <summary>
    /// relics[].vars (sts2#1020): RelicModel._dynamicVars as { name -> BaseValue }, StringVar left out,
    /// read from the private field (never the getter, which builds the set when it is null). A null or
    /// unreadable set is vars=null with unavailable_reasons.vars and a warning at player.relics.&lt;ID&gt;.vars;
    /// a single unreadable var is null with unavailable_reasons["vars.&lt;name&gt;"]; the others are kept.
    /// </summary>
    private static void AttachRelicDynamicVars(Dictionary<string, object?> relicState, RelicModel relic)
    {
        string context = $"player.relics.{SafeGetText(() => relic.Id.Entry) ?? "unknown"}";
        ReadInternalStateValue(relicState, "vars", () =>
        {
            var values = new Dictionary<string, object?>();
            foreach (var (name, dynamicVar) in ReadDeclaredStateField<DynamicVarSet>(relic, typeof(RelicModel), "_dynamicVars"))
            {
                if (dynamicVar is StringVar) continue;
                try { values[name] = dynamicVar.BaseValue; }
                catch (Exception ex)
                {
                    values[name] = null;
                    RecordInternalStateFailure(relicState, "vars." + name, ex, context);
                }
            }
            return values;
        }, context);
    }

    private static bool ReadFurCoatCurrentRoom(FurCoat coat)
    {
        // These saved properties have no public getters. Read the private properties,
        // not compiler-generated backing fields. GetMarkedCoords projects the arrays
        // without validating their lengths; retain our explicit consistency check.
        // No map generation, RNG, or GetMarkedCoords.
        // v0.113.0 combat hooks check coordinates WITHOUT checking FurCoatActIndex.
        // The act index only gates AddMarkedRooms; adding that gate here would disagree
        // with BeforeCombatStart / AfterCreatureAddedToCombat on later acts.
        bool coordsSet = ReadDeclaredStateProperty<bool>(coat, typeof(FurCoat), "FurCoatCoordsSet");
        if (!coordsSet) return false;
        var cols = ReadDeclaredStateProperty<int[]>(coat, typeof(FurCoat), "FurCoatCoordCols");
        var rows = ReadDeclaredStateProperty<int[]>(coat, typeof(FurCoat), "FurCoatCoordRows");
        if (cols.Length != rows.Length) throw new InvalidOperationException("FurCoat coordinate lengths differ");
        var point = coat.Owner.RunState.CurrentMapPoint
            ?? throw new InvalidOperationException("Current map point unavailable");
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] == point.coord.col && rows[i] == point.coord.row) return true;
        return false;
    }

    private static void AddRelicCombatField<T>(
        RelicModel relic, Dictionary<string, object?> state, string key, string fieldName)
        where T : struct
    {
        try
        {
            // DeclaredOnly: 更新で型の構造が変わっても、別クラスの同名値を誤読しない。
            var field = relic.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(relic.GetType().Name, fieldName);
            if (field.FieldType != typeof(T) || field.GetValue(relic) is not T value)
                throw new InvalidOperationException($"Expected {typeof(T).Name} field {fieldName}");
            state[key] = value;
        }
        catch (Exception ex)
        {
            // false / 0 は本当の値。不明な場合に代入せず、既存の warnings 契約に従う。
            Warn($"player.relics.{relic.Id.Entry}.combat_state.{key}", ex);
        }
    }

    // The relic row's markdown suffix: combat_state, then run_state (sts2#1020) in the same
    // "(key: value, ...)" shape, then a vars read failure (successful vars are JSON-only).
    private static string FormatRelicCombatState(Dictionary<string, object?> relic)
    {
        string text = FormatRelicStateSection(relic.GetValueOrDefault("combat_state") as Dictionary<string, object?>)
            + FormatRelicStateSection(relic.GetValueOrDefault("run_state") as Dictionary<string, object?>);
        var rowReasons = relic.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        if (relic.TryGetValue("vars", out var vars))
        {
            if (vars is Dictionary<string, object?> values)
            {
                var failed = values.Where(pair => pair.Value == null).Select(pair =>
                    $"{pair.Key}=null ({rowReasons?.GetValueOrDefault("vars." + pair.Key) ?? "unavailable"})").ToList();
                if (failed.Count > 0) text += " (vars: " + string.Join(", ", failed) + ")";
            }
            else text += $" (vars: null ({rowReasons?.GetValueOrDefault("vars") ?? "unavailable"}))";
        }
        return text;
    }

    private static string FormatRelicStateSection(Dictionary<string, object?>? state)
    {
        if (state == null || state.Count == 0)
            return "";

        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        return " (" + string.Join(", ", state.Where(entry => entry.Key != "unavailable_reasons").Select(entry =>
        {
            string label = entry.Key switch
            {
                "attacks_played_this_turn" => "attacks this turn",
                "skills_played_this_turn" => "skills this turn",
                "powers_played_this_turn" => "powers this turn",
                "activation_count_this_turn" => "activations this turn",
                "any_attacks_played_this_turn" => "attacked this turn",
                "any_attacks_played_last_turn" => "attacked last turn",
                "was_owner_part_of_last_player_turn" => "participated last player turn",
                "block_gained_this_combat" => "block doubled this combat",
                _ => entry.Key.Replace('_', ' ')
            };
            string value = entry.Value is bool flag ? (flag ? "yes" : "no")
                : entry.Value == null ? $"null ({reasons?.GetValueOrDefault(entry.Key) ?? "unavailable"})"
                : Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "?";
            return $"{label}: {value}";
        })) + ")";
    }
}
