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
    private static List<Dictionary<string, object?>> Events(Dictionary<string, object?> h)
        => (List<Dictionary<string, object?>>)h["card_play_events_this_turn"]!;

    // player.combat_history.card_play_events_this_turn (sts2 #937): the engine's own
    // CardPlayStarted/FinishedEntry list, in recorded order, with CardPlay's auto flag,
    // target, PlayIndex and PlayCount. No reordering, dedup or reconstruction.
    private static void CheckCardPlayEvents()
    {
        const string key = "card_play_events_this_turn";
        var combat = new CombatState();
        var owner = new Player { Creature = new Creature { CombatState = combat } };
        var other = new Player { Creature = new Creature { CombatState = combat } };
        var history = new CombatHistory();
        CombatManager.Instance.Value = history;
        T Current<T>(T entry, bool current = true) where T : CombatHistoryEntry
        {
            entry.ThisTurn = state => ReferenceEquals(state, combat)
                ? current : throw new Exception("所有者の CombatState を渡すこと");
            history.Items.Add(entry);
            return entry;
        }

        BeginStateWarnings();
        RefreshCardUidRegistry(combat);
        var empty = BuildCombatHistoryState(owner);
        Check(Events(empty).Count == 0 && !Reasons(empty).ContainsKey(key), "空履歴は既知の空配列");

        // Enemies: two of the same monster, ids by first sight. The second dies and leaves
        // combat before the snapshot; its id must stay WRIGGLER_1, not be re-derived.
        var wrigglerA = new Creature { CombatId = 11, Enemy = true, Monster = new MonsterModel("WRIGGLER") };
        var wrigglerB = new Creature { CombatId = 12, Enemy = true, Monster = new MonsterModel("WRIGGLER") };
        combat.EnemyList.AddRange([wrigglerA, wrigglerB]);
        RefreshEntityIdRegistry(combat);
        combat.EnemyList.Remove(wrigglerB);
        var self = new Creature { CombatId = 1 };

        var havoc = new CardModel(CardType.Skill) { Id = new("HAVOC") };
        var taunt = new CardModel(CardType.Skill) { Id = new("TAUNT") };
        var strike = new CardModel(CardType.Attack) { Id = new("STRIKE") };
        var parent = new CardPlay(havoc, owner);
        var child = new CardPlay(taunt, owner) { AutoPlay = true, Target = wrigglerB };
        var replay0 = new CardPlay(strike, owner) { Target = wrigglerA, PlayIndex = 0, PlayCount = 2 };
        var replay1 = new CardPlay(strike, owner) { Target = wrigglerA, PlayIndex = 1, PlayCount = 2 };
        var selfTarget = new CardPlay(havoc, owner) { Target = self };
        Current(new CardPlayStartedEntry(parent), false); // previous turn
        Current(new CardPlayStartedEntry(parent));
        Current(new CardPlayStartedEntry(child));
        Current(new CardExhaustedEntry { Actor = owner.Creature }); // non-play entries are skipped
        Current(new CardPlayStartedEntry(new CardPlay(strike, other))); // other player's play
        Current(new CardPlayFinishedEntry(child));
        Current(new CardPlayFinishedEntry(parent));
        Current(new CardPlayStartedEntry(replay0));
        Current(new CardPlayFinishedEntry(replay0));
        Current(new CardPlayStartedEntry(replay1));
        Current(new CardPlayFinishedEntry(replay1));
        Current(new CardPlayStartedEntry(selfTarget)); // started, not finished yet
        var entries = history.Items.ToArray();

        var state = BuildCombatHistoryState(owner);
        var events = Events(state);
        string havocUid = GetStableCardUid(havoc), tauntUid = GetStableCardUid(taunt), strikeUid = GetStableCardUid(strike);
        Check(events.Select(e => $"{e["phase"]}:{e["uid"]}").SequenceEqual(new[]
        {
            $"started:{havocUid}", $"started:{tauntUid}", $"finished:{tauntUid}", $"finished:{havocUid}",
            $"started:{strikeUid}", $"finished:{strikeUid}", $"started:{strikeUid}", $"finished:{strikeUid}",
            $"started:{havocUid}"
        }), "履歴順のまま、他 Player・別手番・プレイ以外を除き、親の完了は子の後、リプレイの同 UID を残す");
        Check(events[0]["is_auto_play"] is false && events[1]["is_auto_play"] is true && events[2]["is_auto_play"] is true, "自動プレイの印は CardPlay.IsAutoPlay");
        Check(events[0]["target"] == null && events[0].ContainsKey("target"), "対象なしは target=null（キーは残す）");
        var deadTarget = (Dictionary<string, object?>)events[1]["target"]!;
        Check(Equals(deadTarget["combat_id"], 12u) && Equals(deadTarget["entity_id"], "WRIGGLER_1"), "戦闘から消えた敵も最初の割当ての entity_id と combat_id");
        Check(Equals(((Dictionary<string, object?>)events[4]["target"]!)["entity_id"], "WRIGGLER_0"), "生存の敵の entity_id");
        Check(Equals(events[4]["play_index"], 0) && Equals(events[6]["play_index"], 1) && Equals(events[6]["play_count"], 2), "PlayIndex と PlayCount をそのまま出す");
        var selfRef = (Dictionary<string, object?>)events[8]["target"]!;
        Check(Equals(selfRef["combat_id"], 1u) && selfRef.ContainsKey("entity_id") && selfRef["entity_id"] == null, "敵でない対象は entity_id=null・combat_id は保つ");
        Check(history.Items.SequenceEqual(entries) && Reasons(state).Count == 0 && _stateWarnings.Count == 0, "履歴を変えず、警告なし");
        // Existing summaries stay consistent with the new array.
        Check(Equals(Plays(state, "started")["total"], 5) && Equals(Plays(state, "finished")["total"], 4), "開始・完了の既存集計は変わらない");

        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions)))
        {
            var wire = json.RootElement.GetProperty(key);
            Check(wire.GetArrayLength() == 9 && wire[0].GetProperty("target").ValueKind == JsonValueKind.Null, "wire でも target の null が残る");
            Check(wire[1].GetProperty("is_auto_play").ValueKind == JsonValueKind.True && wire[1].GetProperty("target").GetProperty("combat_id").GetUInt32() == 12, "wire の bool と数値");
            Check(wire[8].GetProperty("target").GetProperty("entity_id").ValueKind == JsonValueKind.Null, "wire で非敵の entity_id は null");
        }
        string md = Markdown(state);
        Check(md.Contains($"play events=[start {havocUid}, start auto {tauntUid} -> WRIGGLER_1, finish auto {tauntUid} -> WRIGGLER_1, finish {havocUid}")
            && md.Contains($"start {strikeUid} -> WRIGGLER_0 (2/2)") && md.Contains($"start {havocUid} -> combat_id 1]"), "Markdown は順序・auto・対象・リプレイ回を表示する");

        // A target that cannot be identified makes the whole array unknown (never a null target).
        var unknownTarget = Current(new CardPlayStartedEntry(new CardPlay(strike, owner) { Target = new Creature { Enemy = true } }));
        var failed = BuildCombatHistoryState(owner);
        Check(failed[key] == null && Reasons(failed)[key].Contains("CombatId"), "対象の CombatId 欠落は配列全体 null と理由");
        Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{key}:")), "項目パス付きの warning");
        Check(Equals(Plays(failed, "started")["total"], 6), "他の項目は保持");
        history.Items.Remove(unknownTarget);

        BeginStateWarnings();
        var brokenAuto = Current(new CardPlayFinishedEntry(new CardPlay(strike, owner) { ThrowAutoPlay = true }));
        Check(BuildCombatHistoryState(owner)[key] == null, "IsAutoPlay の取得失敗も配列全体 null");
        history.Items.Remove(brokenAuto);
        var brokenTurn = Current(new CardPlayStartedEntry(parent));
        brokenTurn.ThisTurn = _ => throw new MissingMethodException("HappenedThisTurn renamed");
        Check(BuildCombatHistoryState(owner)[key] == null, "途中の手番判定の失敗で部分配列を返さない");
        history.Items.Remove(brokenTurn);
        Check(Events(BuildCombatHistoryState(owner)).Count == 9 && Events(state).Count == 9, "復旧後に再読取し、前の snapshot は独立");

        history.Items.Clear();
        Check(Events(BuildCombatHistoryState(owner)).Count == 0, "履歴リセットで空配列");
        BeginStateWarnings();
    }
}
