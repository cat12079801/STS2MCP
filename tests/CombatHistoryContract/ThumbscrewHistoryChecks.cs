using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CheckThumbscrewHistory()
    {
        const string energy = "energy_spent_this_turn";
        const string unfinished = "unfinished_energy_spent_by_uid_this_turn";
        const string stars = "stars_gained_this_turn";
        const string ethereal = "ethereal_plays_finished_this_combat";
        const string attacks = "osty_attacks_this_turn";
        const string wah = "played_wah_this_turn";
        const string cho = "played_cho_this_turn";
        const string ki = "played_ki_this_turn";
        var owner = new Player { NetId = 1, Osty = new Creature() };
        var other = new Player { NetId = 2, Osty = new Creature() };
        var combat = new CombatState();
        owner.Creature.CombatState = other.Creature.CombatState = combat;
        var history = new CombatHistory();
        CombatManager.Instance.Value = history;
        int round = 1;
        bool playerSide = true;

        void Record(CombatHistoryEntry entry)
        {
            var period = (round, playerSide, owner.PlayerCombatState!.Number, other.PlayerCombatState!.Number);
            entry.ThisTurn = state =>
            {
                Check(ReferenceEquals(state, combat), "turn predicates receive the current combat");
                return period == (round, playerSide, owner.PlayerCombatState.Number, other.PlayerCombatState.Number);
            };
            history.Items.Add(entry);
        }

        static CardPlay Play(CardModel card, Player player, int cost) =>
            new(card, player) { Paid = new ResourceInfo { EnergySpent = cost } };
        static Dictionary<string, int> Pending(Dictionary<string, object?> state) =>
            (Dictionary<string, int>)state[unfinished]!;

        BeginStateWarnings();
        RefreshCardUidRegistry(combat);
        var empty = BuildCombatHistoryState(owner);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(empty, WireOptions)))
        {
            Check(new[] { energy, stars, ethereal, attacks }.All(key => json.RootElement.GetProperty(key).GetInt32() == 0),
                "all four successful zero aggregates survive JSON");
            Check(new[] { wah, cho, ki }.All(key => !json.RootElement.GetProperty(key).GetBoolean()),
                "each unplayed melody is false");
            Check(!json.RootElement.GetProperty(unfinished).EnumerateObject().Any(), "known no unfinished plays is an empty object");
        }

        var a = new CardModel(CardType.Attack) { Ethereal = true, Tags = [CardTag.Wah, CardTag.Cho] };
        var b = new CardModel(CardType.Attack) { Tags = [CardTag.Ki] };
        var c = new CardModel(CardType.Attack);
        var otherCard = new CardModel(CardType.Skill) { Ethereal = true, Tags = [CardTag.Ki] };
        var oldA = Play(a, owner, 2);
        var lastA = Play(a, owner, 3);
        var zeroB = Play(b, owner, 0);
        var lastC = Play(c, owner, 4);
        var otherPlay = Play(otherCard, other, 7);
        var borrowed = Play(otherCard, other, 0);
        Record(new CardPlayStartedEntry(oldA));
        Record(new CardPlayStartedEntry(lastA));
        Record(new CardPlayFinishedEntry(lastA));
        var replayA = Play(a, owner, 0);
        Record(new CardPlayStartedEntry(replayA));
        Record(new CardPlayFinishedEntry(replayA));
        a.Ethereal = false;
        Record(new CardPlayStartedEntry(zeroB));
        Record(new CardPlayStartedEntry(lastC));
        // Someone else starting this same CardModel must not replace the owner's last start.
        Record(new CardPlayStartedEntry(Play(c, other, 11)));
        Record(new CardPlayStartedEntry(borrowed));
        // Same CardPlay reference, but the player recorded as Actor is different.
        Record(new CardPlayFinishedEntry(lastC) { Actor = other.Creature });
        Record(new CardPlayFinishedEntry(otherPlay));
        Record(new EnergySpentEntry(owner, 3));
        Record(new EnergySpentEntry(owner, 4));
        Record(new EnergySpentEntry(other, 7));
        Record(new StarsModifiedEntry(owner.Creature, 6));
        Record(new StarsModifiedEntry(owner.Creature, -5));
        Record(new StarsModifiedEntry(owner.Creature, 2));
        Record(new StarsModifiedEntry(other.Creature, 99));
        Record(new CreatureAttackedEntry(owner.Osty!, new DamageResult(7), new DamageResult(7)));
        Record(new CreatureAttackedEntry(other.Osty!, new DamageResult(2)));
        owner.Osty!.IsAlive = false;

        var snapshot = BuildCombatHistoryState(owner);
        string aUid = GetStableCardUid(a);
        string bUid = GetStableCardUid(b);
        string cUid = GetStableCardUid(c);
        Check(a.Id.Entry == b.Id.Entry && aUid != bUid && bUid != cUid, "same model IDs get independent UIDs");
        Check(Equals(snapshot[energy], 7), "Helix sum is before subtracting the current play");
        Check(Pending(snapshot).Count == 2 && !Pending(snapshot).ContainsKey(aUid) &&
            Pending(snapshot)[bUid] == 0 && Pending(snapshot)[cUid] == 4,
            "last finished masks older pending play, zero is present and finish must match the actor");
        Check(Equals(snapshot[stars], 8) && Equals(snapshot[ethereal], 2) && Equals(snapshot[attacks], 1),
            "positive stars, two Ethereal finishes from one UID and one multi-hit attack by current dead Osty");
        Check(snapshot[wah] is true && snapshot[cho] is true && snapshot[ki] is false,
            "multi-tag and repeated completions set independent melody booleans; started Ki alone is false");
        var otherSnapshot = BuildCombatHistoryState(other);
        Check(Equals(otherSnapshot[energy], 7) && Equals(otherSnapshot[stars], 99) &&
            Equals(otherSnapshot[ethereal], 1) && Equals(otherSnapshot[attacks], 1) &&
            otherSnapshot[wah] is false && otherSnapshot[cho] is false && otherSnapshot[ki] is true,
            "history is owner-scoped");
        Check(Pending(otherSnapshot)[cUid] == 11 && Pending(otherSnapshot)[GetStableCardUid(otherCard)] == 0,
            "pending starts are owner-scoped and only finish with the same CardPlay reference");
        Check(JsonSerializer.Serialize(snapshot, WireOptions) ==
            JsonSerializer.Serialize(BuildCombatHistoryState(owner), WireOptions), "same input returns the same aggregates and UID table");

        lastC.ThrowResources = true;
        MelodyCmd.ThrowCho = true;
        var broken = BuildCombatHistoryState(owner);
        Check(broken[unfinished] == null && Reasons(broken)[unfinished].Contains("Resources renamed") &&
            broken[cho] == null && Reasons(broken).ContainsKey(cho) && broken[wah] is true && broken[ki] is false &&
            Equals(broken[energy], 7), "one unreadable payment invalidates the whole map, a melody failure is independent");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(broken, WireOptions)))
            Check(json.RootElement.GetProperty(unfinished).ValueKind == JsonValueKind.Null &&
                json.RootElement.GetProperty(cho).ValueKind == JsonValueKind.Null, "missing map and melody are explicit nulls");
        Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{unfinished}:")) &&
            _stateWarnings.Any(w => w.StartsWith($"player.combat_history.{cho}:")), "each failure reports a top-level warning");
        lastC.ThrowResources = false;
        MelodyCmd.ThrowCho = false;

        // Same round, an extra player turn changes only the other player's turn number.
        other.PlayerCombatState!.Number++;
        var extraTurn = BuildCombatHistoryState(owner);
        Check(Equals(extraTurn[energy], 0) && Equals(extraTurn[stars], 0) && Equals(extraTurn[attacks], 0) &&
            Equals(extraTurn[ethereal], 2) && Pending(extraTurn).Count == 0 && extraTurn[cho] is false,
            "the game's period predicate applies even when only another player's turn number changes");
        Record(new EnergySpentEntry(owner, 1));
        playerSide = false;
        Record(new StarsModifiedEntry(owner.Creature, 3));
        var enemyTurn = BuildCombatHistoryState(owner);
        Check(Equals(enemyTurn[energy], 0) && Equals(enemyTurn[stars], 3) && Equals(enemyTurn[ethereal], 2),
            "enemy side in the same round has independent current history");
        round++;
        playerSide = true;
        owner.PlayerCombatState!.Number++;
        owner.Osty = new Creature();
        var newCard = new CardModel(CardType.Attack);
        Record(new CardGeneratedEntry(newCard, owner, owner.Creature));
        Record(new CardPlayStartedEntry(Play(newCard, owner, 1)));
        Record(new EnergySpentEntry(owner, 1));
        Record(new CreatureAttackedEntry(other.Osty!, new DamageResult(5)));
        var next = BuildCombatHistoryState(owner);
        Check(Equals(next[energy], 1) && Equals(next[stars], 0) && Equals(next[ethereal], 2) &&
            Equals(next[attacks], 0) && Pending(next).Single().Key == GetStableCardUid(newCard),
            "one snapshot during next-turn play includes newly generated UID and current Osty only");

        // CombatManager.Reset / EndCombatInternal clear the game history.
        owner.Creature.CombatState = new CombatState();
        owner.Osty = null;
        history.Items.Clear();
        var reset = BuildCombatHistoryState(owner);
        Check(Equals(reset[ethereal], 0) && Equals(reset[energy], 0) && Equals(reset[attacks], 0) &&
            Pending(reset).Count == 0, "a separate combat has no previous entries or pet");
    }
}
