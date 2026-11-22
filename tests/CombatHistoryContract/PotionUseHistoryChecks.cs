using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CheckPotionUseHistory()
    {
        const string key = "potion_uses";
        var combat = new CombatState();
        var owner = new Player { NetId = 71, Creature = new Creature { CombatState = combat } };
        owner.Creature.Player = owner;
        var ally = new Player { NetId = 72, Creature = new Creature { CombatState = combat } };
        ally.Creature.Player = ally;
        var history = CombatManager.Instance.Value = new CombatHistory();
        List<Dictionary<string, object?>> Uses(Player player)
            => (List<Dictionary<string, object?>>)BuildCombatHistoryState(player)[key]!;
        BeginStateWarnings();
        Check(!BuildCombatHistoryState(owner).ContainsKey(key), "non-Brew combat omits potion use history");
        var brew = new PotionModel("ENTROPIC_BREW");
        owner.PotionSlots.Add(brew);
        Check(Uses(owner).Count == 0, "before use, a held Brew gives a known empty history");
        owner.PotionSlots[0] = new PotionModel("ENTROPIC_BREW");
        history.Items.Add(new PotionUsedEntry(brew, owner.Creature, owner.Creature));
        history.Items.Add(new PotionUsedEntry(brew, ally.Creature, owner.Creature));
        var first = Uses(owner).Single();
        Check(Equals(first["potion_id"], owner.PotionSlots[0]!.Id.Entry)
            && Equals(first["target_entity_id"], "player:71"), "same slot and ID refill still records one self use");
        Check(Equals(Uses(ally).Single()["target_entity_id"], "player:71"), "owner switch partitions the actor, not the target");

        var secondBrew = owner.PotionSlots[0]!;
        var enemy = new Creature { Enemy = true, Monster = new MonsterModel("TEST_ENEMY"), CombatId = 5 };
        owner.PotionSlots[0] = null;
        history.Items.Add(new PotionUsedEntry(secondBrew, owner.Creature, ally.Creature));
        history.Items.Add(new PotionUsedEntry(new PotionModel("FIRE_POTION"), owner.Creature, enemy));
        history.Items.Add(new PotionUsedEntry(new PotionModel("BLOCK_POTION"), owner.Creature, null));
        var rows = Uses(owner);
        Check(rows.Count == 4 && first.SequenceEqual(rows[0]) && Equals(rows[1]["target_entity_id"], "player:72")
            && Equals(rows[2]["target_entity_id"], "TEST_ENEMY_0") && rows[3]["target_entity_id"] == null,
            "post-Brew history keeps order and target IDs after Brew leaves the belt");
        Check(rows.Zip(Uses(owner)).All(pair => pair.First.SequenceEqual(pair.Second))
            && history.Items.Count == 5 && owner.PotionSlots[0] == null, "repeated reads do not mutate belt or history");

        enemy.CombatId = null;
        var badTarget = BuildCombatHistoryState(owner);
        Check(badTarget[key] == null && Reasons(badTarget)[key].Contains("no CombatId"), "an unreadable target fails the whole array");
        history.ThrowEntries = true;
        var unreadable = BuildCombatHistoryState(owner);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(unreadable, WireOptions));
        Check(json.RootElement.GetProperty(key).ValueKind == JsonValueKind.Null && Reasons(unreadable).ContainsKey(key)
            && _stateWarnings.Any(w => w.StartsWith("player.combat_history.potion_uses:")), "unreadable history stays null with reason and warning");
    }
}
