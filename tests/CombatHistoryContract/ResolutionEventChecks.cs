using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static List<Dictionary<string, object?>> Stream(Dictionary<string, object?> h)
        => (List<Dictionary<string, object?>>)h["card_resolution_events"]!;

    private static string Row(Dictionary<string, object?> e) => (e["kind"] as string) switch
    {
        "draw" => $"draw:{e["uid"]}:{((bool)e["from_hand_draw"]! ? "hand" : "inner")}",
        "generated" => $"gen:{e["card_id"]}:{e["uid"]}:{e["creator"] ?? "null"}",
        "play" => $"{e["phase"]}{(e["is_auto_play"] is true ? " auto" : "")}:{e["uid"]}",
        _ => throw new Exception($"unknown kind {e["kind"]}")
    };

    // player.combat_history.card_resolution_events (sts2#1088): the owner's draw / play /
    // generated entries of the whole combat, in History order, across turn boundaries.
    // No turn filter, no reordering, dedup or reconstruction; one unreadable row nulls the array.
    private static void CheckCardResolutionEvents()
    {
        const string key = "card_resolution_events";
        var combat = new CombatState();
        var owner = new Player { Creature = new Creature { CombatState = combat } };
        var other = new Player { Creature = new Creature { CombatState = combat } };
        var history = new CombatHistory();
        CombatManager.Instance.Value = history;
        bool thisTurn = false; // flips at the new player turn; the stream must not care
        T Add<T>(T entry) where T : CombatHistoryEntry
        {
            bool current = thisTurn;
            entry.ThisTurn = state => ReferenceEquals(state, combat)
                ? current : throw new Exception("所有者の CombatState を渡すこと");
            history.Items.Add(entry);
            return entry;
        }

        BeginStateWarnings();
        RefreshCardUidRegistry(combat);
        var empty = BuildCombatHistoryState(owner);
        Check(Stream(empty).Count == 0 && !Reasons(empty).ContainsKey(key), "空履歴は既知の空配列");

        var inklet = new Creature { CombatId = 21, Enemy = true, Monster = new MonsterModel("INKLET") };
        combat.EnemyList.Add(inklet);
        var strike1 = new CardModel(CardType.Attack) { Id = new("STRIKE_IRONCLAD") };
        var strike2 = new CardModel(CardType.Attack) { Id = new("STRIKE_IRONCLAD") };
        var defend = new CardModel(CardType.Skill) { Id = new("DEFEND_IRONCLAD") };
        var burnA = new CardModel(CardType.Status) { Id = new("BURN") };
        var burnB = new CardModel(CardType.Status) { Id = new("BURN") };
        var shiv = new CardModel(CardType.Attack) { Id = new("SHIV") };
        var gift = new CardModel(CardType.Skill) { Id = new("GIFT") };
        var othersCard = new CardModel(CardType.Skill) { Id = new("OTHER") };

        // Old turn's end: an inner draw (Dark Embrace) whose Strike is auto-played (Hellraiser).
        Add(new CardDrawnEntry(strike1, false, owner.Creature));
        var autoOld = new CardPlay(strike1, owner) { AutoPlay = true, Target = inklet };
        Add(new CardPlayStartedEntry(autoOld));
        Add(new CardExhaustedEntry { Actor = owner.Creature }); // not a draw/play/generated entry
        Add(new CardPlayFinishedEntry(autoOld));
        // Enemy turn: Burns generated into the owner's discard with no player creator.
        Add(new CardGeneratedEntry(burnA, null, owner.Creature));
        Add(new CardGeneratedEntry(burnB, null, owner.Creature));
        Add(new CardGeneratedEntry(othersCard, null, other.Creature)); // another player's pile
        // New turn: hand draws; the same Strike is drawn again after a shuffle and auto-played.
        thisTurn = true;
        Add(new CardDrawnEntry(defend, true, owner.Creature));
        Add(new CardDrawnEntry(strike1, true, owner.Creature));
        var autoNew = new CardPlay(strike1, owner) { AutoPlay = true, Target = inklet };
        Add(new CardPlayStartedEntry(autoNew));
        Add(new CardPlayFinishedEntry(autoNew));
        Add(new CardDrawnEntry(othersCard, true, other.Creature)); // another player's draw
        Add(new CardPlayStartedEntry(new CardPlay(othersCard, other))); // another player's play
        var snapshot1 = BuildCombatHistoryState(owner);
        var first = Stream(snapshot1);

        Add(new CardDrawnEntry(strike2, true, owner.Creature));
        Add(new CardGeneratedEntry(shiv, owner, owner.Creature));
        Add(new CardGeneratedEntry(gift, other, owner.Creature)); // given to the owner by another player
        var manual = new CardPlay(strike2, owner) { Target = inklet };
        Add(new CardPlayStartedEntry(manual)); // started, not finished yet
        var entries = history.Items.ToArray();

        var state = BuildCombatHistoryState(owner);
        var events = Stream(state);
        string s1 = GetStableCardUid(strike1), s2 = GetStableCardUid(strike2), d = GetStableCardUid(defend);
        string bA = GetStableCardUid(burnA), bB = GetStableCardUid(burnB), sh = GetStableCardUid(shiv), g = GetStableCardUid(gift);
        Check(events.Select(Row).SequenceEqual(new[]
        {
            $"draw:{s1}:inner", $"started auto:{s1}", $"finished auto:{s1}",
            $"gen:BURN:{bA}:null", $"gen:BURN:{bB}:null",
            $"draw:{d}:hand", $"draw:{s1}:hand", $"started auto:{s1}", $"finished auto:{s1}",
            $"draw:{s2}:hand", $"gen:SHIV:{sh}:owner", $"gen:GIFT:{g}:other_player", $"started:{s2}"
        }), "戦闘全体の draw・play・generated を履歴順のまま、手番の境界をまたいで出し、他 Player・他の entry を除く");
        Check(bA != bB && s1 != s2, "同じ ID の別個体は別の uid");
        Check(events.Count(e => Equals(e["uid"], s1) && Equals(e["kind"], "draw")) == 2, "同じ個体の再ドローは 2 行（重複を除かない）");
        Check(first.Count == 9 && events.Take(first.Count).Select(Row).SequenceEqual(first.Select(Row)),
            "後の snapshot の列は前の snapshot の列で始まる（接頭部が変わらない）");
        Check(Events(state).Select(e => $"{e["phase"]}:{e["uid"]}").SequenceEqual(new[]
            { $"started:{s1}", $"finished:{s1}", $"started:{s2}" }), "既存の card_play_events_this_turn は今の手番だけのまま");
        var play = events[1];
        Check(play.Keys.SequenceEqual(new[] { "kind", "phase", "uid", "is_auto_play", "target", "play_index", "play_count", "reservation" }),
            "play の行は card_play_events_this_turn の要素に kind と reservation を足した形");
        Check(play["reservation"] == null, "予約から出ていないプレイの reservation は null");
        var target = (Dictionary<string, object?>)play["target"]!;
        Check(Equals(target["combat_id"], 21u) && Equals(target["entity_id"], "INKLET_0"), "対象は combat_id と安定 entity_id");
        Check(events[0].Keys.SequenceEqual(new[] { "kind", "uid", "from_hand_draw" })
            && events[3].Keys.SequenceEqual(new[] { "kind", "card_id", "uid", "creator" }), "draw・generated の行のキー");
        Check(history.Items.SequenceEqual(entries) && Reasons(state).Count == 0 && _stateWarnings.Count == 0, "履歴を変えず、警告なし");

        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions)))
        {
            var wire = json.RootElement.GetProperty(key);
            Check(wire.GetArrayLength() == 13, "wire の件数");
            Check(wire[0].GetProperty("from_hand_draw").ValueKind == JsonValueKind.False
                && wire[5].GetProperty("from_hand_draw").ValueKind == JsonValueKind.True, "wire の from_hand_draw は bool");
            Check(wire[3].GetProperty("creator").ValueKind == JsonValueKind.Null, "wire でも creator の null が残る");
            Check(wire[1].GetProperty("target").GetProperty("combat_id").GetUInt32() == 21, "wire の対象");
        }
        Check(Markdown(state).Contains("resolution events=13 (draw:4, play:5, generated:4)"), "Markdown は件数だけを種類ごとに出す");
        Check(!Markdown(empty).Contains("resolution events"), "空の列は Markdown に出さない");

        // One unreadable row makes the whole stream unknown, never a shortened stream.
        var brokenDraw = Add(new CardDrawnEntry(defend, true, owner.Creature) { ThrowFromHandDraw = true });
        var failed = BuildCombatHistoryState(owner);
        Check(failed[key] == null && Reasons(failed)[key].Contains("FromHandDraw"), "FromHandDraw の取得失敗は配列全体 null と理由");
        Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{key}:")), "項目パス付きの warning");
        Check(Events(failed).Count == 3, "他の項目は保持");
        Check(Markdown(failed).Contains($"{key}=null ("), "Markdown に失敗を出す");
        history.Items.Remove(brokenDraw);
        BeginStateWarnings();
        var unknownTarget = Add(new CardPlayFinishedEntry(new CardPlay(strike2, owner) { Target = new Creature { Enemy = true } }));
        Check(BuildCombatHistoryState(owner)[key] == null, "対象の CombatId 欠落も配列全体 null");
        history.Items.Remove(unknownTarget);
        BeginStateWarnings();
        owner.Creature.CombatState = null;
        var noCombat = BuildCombatHistoryState(owner);
        Check(noCombat[key] == null && Reasons(noCombat).ContainsKey(key), "combat state が無ければ空列でなく null");
        owner.Creature.CombatState = combat;
        BeginStateWarnings();
        Check(Stream(BuildCombatHistoryState(owner)).Count == 13 && Stream(state).Count == 13, "復旧後に再読取し、前の snapshot は独立");

        history.Items.Clear();
        Check(Stream(BuildCombatHistoryState(owner)).Count == 0, "履歴のリセット（次の戦闘）で空配列");
        BeginStateWarnings();
    }
}
