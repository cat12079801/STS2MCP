using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
        }
        return state;
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

    private static string FormatRelicCombatState(Dictionary<string, object?> relic)
    {
        if (relic.GetValueOrDefault("combat_state") is not Dictionary<string, object?> state || state.Count == 0)
            return "";

        return " (" + string.Join(", ", state.Select(entry =>
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
                : Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "?";
            return $"{label}: {value}";
        })) + ")";
    }
}
