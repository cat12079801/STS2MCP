using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CheckArrows()
    {
        const string slots = "slots";
        const string count = "available_count";
        const string impalements = "impalements";
        static List<Dictionary<string, object?>> Rows(Dictionary<string, object?> state, string key)
            => (List<Dictionary<string, object?>>)state[key]!;
        static int? NextShot(Dictionary<string, object?> state)
            => Rows(state, slots).LastOrDefault(row => row["is_available"] is true)?["slot"] as int?;

        BeginStateWarnings();
        var owner = new Player();
        var other = new Player();
        var combat = new CombatState();
        var empty = BuildArrowState(owner, combat);
        Check(Equals(empty[count], 0) && Rows(empty, slots).Count == 0 && Rows(empty, impalements).Count == 0 &&
            Reasons(empty).Count == 0, "combat with zero slots has known empty arrays and zero count");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(empty, WireOptions)))
            Check(json.RootElement.GetProperty(slots).GetArrayLength() == 0 &&
                json.RootElement.GetProperty(count).GetInt32() == 0, "empty arrow state survives wire serialization");

        var state = owner.PlayerCombatState!;
        var a = new ArrowSlot(owner);
        var b = new ArrowSlot(owner);
        var c = new ArrowSlot(owner) { Status = ArrowStatus.Away };
        state.Slots.AddRange([a, b, c]);
        var foreign = new ArrowSlot(other) { Status = ArrowStatus.Away };
        other.PlayerCombatState!.Slots.Add(foreign);
        var firstEnemy = new Creature { CombatId = 10, Monster = new MonsterModel("ENEMY") };
        var enemy = new Creature { CombatId = 11, Monster = new MonsterModel("ENEMY") };
        combat.EnemyList.AddRange([firstEnemy, enemy]);
        var ownPower = new ImpaledPower { Applier = owner.Creature, Sources = [c], HitCounter = 2 };
        var foreignPower = new ImpaledPower { Applier = other.Creature, Sources = [foreign], HitCounter = 1 };
        enemy.Powers.AddRange([foreignPower, ownPower]);
        var initial = BuildArrowState(owner, combat);
        Check(Equals(initial[count], 2) && NextShot(initial) == 1 &&
            Rows(initial, slots).Select(row => (string)row["status"]!).SequenceEqual(new[] { "Retrieved", "Retrieved", "Away" }),
            "last available slot wins while trailing Away is skipped");
        var impaled = Rows(initial, impalements).Single();
        Check(Equals(impaled["target_combat_id"], 11u) && Equals(impaled["target_entity_id"], "ENEMY_1") &&
            Equals(impaled["hit_counter"], 2) && ((List<int>)impaled["slot_indices"]!).SequenceEqual(new[] { 2 }),
            "combat ID, stable enemy ID, hit counter and owner slots are independently read");
        Check(((List<int>)Rows(BuildArrowState(other, combat), impalements).Single()["slot_indices"]!).SequenceEqual(new[] { 0 }),
            "same target exposes only each player's own power and slot numbering");
        Check(JsonSerializer.Serialize(initial, WireOptions) == JsonSerializer.Serialize(BuildArrowState(owner, combat), WireOptions),
            "repeated reads do not consume arrows or change slot or impalement order");

        b.Status = ArrowStatus.Away;
        b.DestroyAfterImpaled = true;
        var beforeImpale = BuildArrowState(owner, combat);
        Check(Equals(beforeImpale[count], 1) && NextShot(beforeImpale) == 0 &&
            Rows(beforeImpale, slots)[1]["destroy_after_impaled"] is true &&
            ((List<int>)Rows(beforeImpale, impalements).Single()["slot_indices"]!).SequenceEqual(new[] { 2 }),
            "a shot can be Away during its hook, before it reaches the impaled list");
        ownPower.Sources.Add(b);
        a.Status = ArrowStatus.Away;
        combat.EnemyList.Remove(firstEnemy);
        var exhausted = BuildArrowState(owner, combat);
        Check(Equals(exhausted[count], 0) && NextShot(exhausted) == null, "no available slot after the next shot");
        impaled = Rows(exhausted, impalements).Single();
        Check(Equals(impaled["target_entity_id"], "ENEMY_1") &&
            ((List<int>)impaled["slot_indices"]!).SequenceEqual(new[] { 2, 1 }),
            "stable target ID survives removal of another enemy and source order is not sorted by slot");

        ownPower.Sources.RemoveAt(0);
        ownPower.HitCounter = 3;
        c.Status = ArrowStatus.Retrieved;
        var retrieved = BuildArrowState(owner, combat);
        Check(Equals(retrieved[count], 1) && NextShot(retrieved) == 2 &&
            Equals(Rows(retrieved, impalements).Single()["hit_counter"], 3), "first impalement was retrieved and can be shot again");
        ownPower.Sources.RemoveAt(0);
        enemy.Powers.Remove(ownPower);
        b.Status = ArrowStatus.Destroyed;
        var destroyed = BuildArrowState(owner, combat);
        Check(Equals(destroyed[count], 1) && NextShot(destroyed) == 2 &&
            Equals(Rows(destroyed, slots)[1]["status"], "Destroyed") && Rows(destroyed, slots)[1]["is_available"] is false &&
            Rows(destroyed, impalements).Count == 0, "destroyed arrow never becomes a candidate");

        var ally = new Creature { CombatId = 12 };
        combat.Allies.Add(ally);
        ownPower.Sources.Add(a);
        ally.Powers.Add(ownPower);
        var allyRow = Rows(BuildArrowState(owner, combat), impalements).Single();
        Check(Equals(allyRow["target_combat_id"], 12u) && !allyRow.ContainsKey("target_entity_id"),
            "non-enemy target uses only its combat ID");
        foreach (var stateType in new[] { "monster", "card_select", "hand_select" })
        {
            var response = new
            {
                state_type = stateType,
                player = new Dictionary<string, object?> { ["arrow_state"] = BuildArrowState(owner, combat), ["energy"] = 2 }
            };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
            var player = json.RootElement.GetProperty("player");
            var arrows = player.GetProperty("arrow_state");
            Check(player.GetProperty("energy").GetInt32() == 2 && arrows.GetProperty(count).GetInt32() == 1 &&
                arrows.GetProperty(slots)[2].GetProperty("is_available").GetBoolean() &&
                arrows.GetProperty(impalements)[0].GetProperty("slot_indices")[0].GetInt32() == 0,
                $"{stateType} JSON can determine the next shot from a single snapshot");
        }

        ownPower.Sources.Add(foreign);
        var invalidRef = BuildArrowState(owner, combat);
        Check(invalidRef[impalements] == null && Reasons(invalidRef).ContainsKey(impalements) && NextShot(invalidRef) == 2,
            "foreign reference invalidates impalements only");
        ownPower.Sources.Remove(foreign);
        ownPower.Sources.Add(new ArrowSlot(owner));
        Check(BuildArrowState(owner, combat)[impalements] == null, "unregistered arrow with the correct owner is unreadable");
        ownPower.Sources.RemoveAt(1);
        ownPower.ThrowArrows = true;
        Check(BuildArrowState(owner, combat)[impalements] == null, "ImpaledPower.Arrows getter failure is retained");
        ownPower.ThrowArrows = false;
        ally.CombatId = null;
        Check(BuildArrowState(owner, combat)[impalements] == null, "missing target combat ID is unreadable");
        ally.CombatId = 12;
        a.Status = (ArrowStatus)99;
        var unknownStatus = BuildArrowState(owner, combat);
        Check(unknownStatus[slots] == null && Reasons(unknownStatus)[slots].Contains("Unknown ArrowStatus") &&
            Equals(unknownStatus[count], 1) && Rows(unknownStatus, impalements).Count == 1,
            "unknown enum leaves independently readable count and impaled references");
        a.Status = ArrowStatus.Away;

        state.ThrowCount = true;
        var missingCount = BuildArrowState(owner, combat);
        Check(missingCount[count] == null && NextShot(missingCount) == 2 && Reasons(missingCount).ContainsKey(count),
            "count getter failure preserves slot reads");
        state.ThrowCount = false;
        state.ThrowArrows = true;
        var missingSlots = BuildArrowState(owner, combat);
        Check(missingSlots[slots] == null && Equals(missingSlots[count], 1) && missingSlots[impalements] == null &&
            Reasons(missingSlots).ContainsKey(impalements), "failed slot getter makes active reference indices unknown");
        state.ThrowArrows = false;
        state.Slots.Add(foreign);
        Check(BuildArrowState(owner, combat)[slots] == null, "foreign slot in the player's list is rejected");
        state.Slots.Remove(foreign);

        state.CountOverride = 0;
        var contradictory = BuildArrowState(owner, combat);
        Check(contradictory[slots] == null && contradictory[count] == null &&
            Reasons(contradictory).Keys.Order().SequenceEqual(new[] { count, slots }) &&
            Rows(contradictory, impalements).Count == 1, "contradictory availability makes both shot inputs unknown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(contradictory, WireOptions)))
            Check(json.RootElement.GetProperty(slots).ValueKind == JsonValueKind.Null &&
                json.RootElement.GetProperty(count).ValueKind == JsonValueKind.Null, "failed reads retain null values on the wire");
        Check(new[] { slots, count, impalements }.All(key => _stateWarnings.Any(w => w.StartsWith($"player.arrow_state.{key}:"))),
            "arrow failures report field warnings");
    }
}
