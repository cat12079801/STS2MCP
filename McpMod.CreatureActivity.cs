using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    // BuildEnemyList has already refreshed the enemy ID registry for this combat.
    // Keep the full roster here: CreatureCmd.KillWithoutCheckingWinCondition can defer removal
    // while AttackCommand.Execute or MonsterModel.PerformMove is still on the stack.
    private static void AttachCreatureActivity(Dictionary<string, object?> battle, ICombatState? combatState)
    {
        ReadInternalStateValue(battle, "creature_activity", () =>
        {
            var creatures = (combatState?.Creatures
                ?? throw new InvalidOperationException("ICombatState.Creatures を取得できない"))
                .Distinct<Creature>(ReferenceEqualityComparer.Instance).ToList();
            if (creatures.Any(creature => creature == null))
                throw new InvalidOperationException("ICombatState.Creatures に null の個体がある");

            var rows = new List<Dictionary<string, object?>>(creatures.Count);
            var idCounts = new Dictionary<uint, int>();
            for (int i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                var row = new Dictionary<string, object?>();
                string context = $"battle.creature_activity.{i}";
                ReadInternalStateValue(row, "combat_id", () => RequireCombatId(creature), context);
                if (row["combat_id"] is uint id)
                    idCounts[id] = idCounts.GetValueOrDefault(id) + 1;

                ReadInternalStateValue(row, CreatureIsAttackingKey,
                    () => ReadDeclaredStateProperty<bool>(creature, typeof(Creature), nameof(Creature.IsAttacking)), context);
                try
                {
                    if (creature.Monster is { } monster)
                        row[MonsterIsPerformingMoveKey] = ReadDeclaredStateProperty<bool>(
                            monster, typeof(MonsterModel), nameof(MonsterModel.IsPerformingMove));
                }
                catch (Exception ex)
                {
                    row[MonsterIsPerformingMoveKey] = null;
                    RecordInternalStateFailure(row, MonsterIsPerformingMoveKey, ex, context);
                }
                rows.Add(row);
            }

            for (int i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                var row = rows[i];
                string context = $"battle.creature_activity.{i}";
                if (row["combat_id"] is uint id && idCounts[id] > 1)
                {
                    row["combat_id"] = null;
                    RecordInternalStateFailure(row, "combat_id",
                        new InvalidOperationException($"Creature.CombatId {id} が重複している"), context);
                }

                ReadInternalStateValue(row, "kind", () => creature.Player != null ? "player"
                    : creature.IsEnemy ? "enemy" : creature.PetOwner != null ? "pet" : "ally", context);
                switch (row["kind"])
                {
                    case "player":
                        ReadInternalStateValue(row, "player_id", () => creature.Player!.NetId.ToString(), context);
                        break;
                    case "enemy":
                        ReadInternalStateValue(row, "entity_id", () => row["combat_id"] is uint
                            ? GetStableEntityId(creature)
                            : throw new InvalidOperationException("有効な CombatId がない"), context);
                        break;
                    case "pet":
                        ReadInternalStateValue(row, "owner_player_id", () => creature.PetOwner!.NetId.ToString(), context);
                        break;
                }
            }
            return rows;
        }, "battle");
    }
}
