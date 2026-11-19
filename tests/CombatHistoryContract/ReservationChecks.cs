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
    private static string ReservationRow(Dictionary<string, object?> e)
    {
        switch (e["kind"] as string)
        {
            case "auto_play_reservation":
                var parent = e["parent"] as Dictionary<string, object?>;
                return $"call {e["call_id"]}<{e["parent_call_id"] ?? "-"}> parent={parent?["kind"] ?? "null"}:{parent?["model_id"] ?? "?"}:{parent?["uid"] ?? "?"}"
                    + $"@{e["parent_play_started_index"] ?? "-"} n={e["requested_count"]} {e["position"]}{(e["force_exhaust"] is true ? " exhaust" : "")}";
            case "auto_play_reserved":
                return $"reserved {e["call_id"]}.{e["reserve_index"]}:{e["uid"]}";
            case "auto_play_dispatch":
                return $"dispatch {e["call_id"]}.{e["reserve_index"]}:{e["uid"]}";
            case "play":
                var r = e["reservation"] as Dictionary<string, object?>;
                return $"{e["phase"]}{(e["is_auto_play"] is true ? " auto" : "")}:{e["uid"]}{(r == null ? "" : $" [{r["call_id"]}.{r["reserve_index"]}]")}";
            case "draw":
                return $"draw:{e["uid"]}";
            default:
                throw new Exception($"unknown kind {e["kind"]}");
        }
    }

    // CardPileCmd.AutoPlayFromDrawPile (Cascade X=4): reserve 4 cards into Play in order, then AutoPlay each.
    // The first child (Havoc) makes a nested call; the second is unplayable (no start row, its exhaust draws);
    // the third ends the fight, so the fourth stays reserved and is never dispatched.
    private static void CheckAutoPlayReservations()
    {
        const string key = "card_resolution_events";
        var combat = new CombatState();
        var owner = new Player { Creature = new Creature { CombatState = combat } };
        var other = new Player { Creature = new Creature { CombatState = combat } };
        var history = new CombatHistory();
        CombatManager.Instance.Value = history;
        AutoPlayReservations.Reset();
        T Add<T>(T entry) where T : CombatHistoryEntry
        {
            entry.ThisTurn = _ => true;
            history.Items.Add(entry);
            return entry;
        }
        int Pos() => history.Items.Count;
        CardPlay Start(CardModel card, bool auto, Player? player = null)
        {
            var play = new CardPlay(card, player ?? owner) { AutoPlay = auto };
            AutoPlayReservations.PlayStarting(play, auto); // CombatHistory.CardPlayStarted prefix
            Add(new CardPlayStartedEntry(play));
            return play;
        }

        BeginStateWarnings();
        RefreshCardUidRegistry(combat);
        var cascade = new CardModel(CardType.Skill) { Id = new("CASCADE") };
        var havoc = new CardModel(CardType.Skill) { Id = new("HAVOC") };
        var burn = new CardModel(CardType.Status) { Id = new("BURN") };
        var strike = new CardModel(CardType.Attack) { Id = new("STRIKE_IRONCLAD") };
        var bash = new CardModel(CardType.Attack) { Id = new("BASH") };
        var defend = new CardModel(CardType.Skill) { Id = new("DEFEND_IRONCLAD") };
        var drawn = new CardModel(CardType.Skill) { Id = new("SHRUG_IT_OFF") };
        var stray = new CardModel(CardType.Skill) { Id = new("STRAY") };
        object cascadeContext = new(), havocContext = new(), otherContext = new();

        var cascadePlay = Start(cascade, false);
        var call1 = AutoPlayReservations.Begin(Pos(), owner, cascadeContext, "card", "CASCADE", cascade, false, cascadePlay, 4, "top", false);
        foreach (var card in new[] { havoc, burn, strike, bash })
        {
            AutoPlayReservations.Selected(call1, card);
            Check(AutoPlayReservations.AnyPendingMove, "選んだ札が Play に移るまでは保留");
            AutoPlayReservations.EnteredPlayPile(Pos(), other, card); // another player's Play pile: not this move
            AutoPlayReservations.EnteredPlayPile(Pos(), owner, card);
        }
        Check(!AutoPlayReservations.AnyPendingMove, "全部が Play に移った");
        AutoPlayReservations.EnteredPlayPile(Pos(), owner, stray); // a card no call is moving: no row
        var midReserve = BuildCombatHistoryState(owner);

        // Havoc: dispatched, played, and its OnPlay reserves one card of its own (call 2).
        Check(AutoPlayReservations.AutoPlayBegan(Pos(), otherContext, havoc, false, true) == null, "別の context の AutoPlay は予約の消化でない");
        Check(AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, burn, false, true) == null, "予約の順でない札は消化でない");
        var havocDispatch = AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, havoc, false, true)!;
        var havocPlay = Start(havoc, true);
        var call2 = AutoPlayReservations.Begin(Pos(), owner, havocContext, "card", "HAVOC", havoc, false, havocPlay, 1, "top", true);
        AutoPlayReservations.Selected(call2, defend);
        AutoPlayReservations.EnteredPlayPile(Pos(), owner, defend);
        var defendDispatch = AutoPlayReservations.AutoPlayBegan(Pos(), havocContext, defend, false, true)!;
        var defendPlay = Start(defend, true);
        Add(new CardPlayFinishedEntry(defendPlay));
        AutoPlayReservations.AutoPlayEnded(defendDispatch);
        AutoPlayReservations.CallCompleted(call2);
        Add(new CardPlayFinishedEntry(havocPlay));
        AutoPlayReservations.AutoPlayEnded(havocDispatch);

        // Burn: unplayable, moved to the result pile without a start; the move's exhaust hook draws a card.
        var burnDispatch = AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, burn, false, true)!;
        Add(new CardDrawnEntry(drawn, false, owner.Creature));
        AutoPlayReservations.AutoPlayEnded(burnDispatch);

        // Strike: played (twice, a replay), ends the fight. Bash's AutoPlay returns at once: no row.
        var strikeDispatch = AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, strike, false, true)!;
        var strike1 = Start(strike, true);
        Add(new CardPlayFinishedEntry(strike1));
        var strike2 = Start(strike, true);
        strike2.PlayIndex = 1;
        Add(new CardPlayFinishedEntry(strike2));
        AutoPlayReservations.AutoPlayEnded(strikeDispatch);
        Check(AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, bash, false, proceeds: false) == null, "終戦後の AutoPlay は行を作らない");
        AutoPlayReservations.CallCompleted(call1);
        Add(new CardPlayFinishedEntry(cascadePlay));
        // Later auto-plays of a reserved card by something else carry no reservation.
        var later = Start(strike, true);
        Check(AutoPlayReservations.AutoPlayBegan(Pos(), cascadeContext, strike, true, true) == null, "対象を渡す AutoPlay は予約の消化でない");

        var state = BuildCombatHistoryState(owner);
        var events = Stream(state);
        string U(CardModel c) => GetStableCardUid(c);
        var expected = new[]
        {
            $"started:{U(cascade)}",
            $"call 1<-> parent=card:CASCADE:{U(cascade)}@0 n=4 top",
            $"reserved 1.0:{U(havoc)}", $"reserved 1.1:{U(burn)}", $"reserved 1.2:{U(strike)}", $"reserved 1.3:{U(bash)}",
            $"dispatch 1.0:{U(havoc)}",
            $"started auto:{U(havoc)} [1.0]",
            $"call 2<1> parent=card:HAVOC:{U(havoc)}@7 n=1 top exhaust",
            $"reserved 2.0:{U(defend)}",
            $"dispatch 2.0:{U(defend)}",
            $"started auto:{U(defend)} [2.0]", $"finished auto:{U(defend)} [2.0]",
            $"finished auto:{U(havoc)} [1.0]",
            $"dispatch 1.1:{U(burn)}",
            $"draw:{U(drawn)}",
            $"dispatch 1.2:{U(strike)}",
            $"started auto:{U(strike)} [1.2]", $"finished auto:{U(strike)} [1.2]",
            $"started auto:{U(strike)} [1.2]", $"finished auto:{U(strike)} [1.2]",
            $"finished:{U(cascade)}",
            $"started auto:{U(strike)}"
        };
        Check(events.Select(ReservationRow).SequenceEqual(expected),
            "予約・確保・消化の行を記録時の履歴の位置に差し込み、子のプレイに予約を付ける:\n" + string.Join("\n", events.Select(ReservationRow)));
        Check(Reasons(state).Count == 0 && _stateWarnings.Count == 0, "警告なし");
        Check(Equals(events[8]["parent_play_started_index"], 7) && Equals(events[7]["kind"], "play") && Equals(events[7]["phase"], "started"),
            "親プレイの開始位置はこの配列での開始行の添字");
        var midRows = Stream(midReserve);
        Check(midRows.Count == 6 && events.Take(midRows.Count).Select(ReservationRow).SequenceEqual(midRows.Select(ReservationRow)),
            "確保の途中の snapshot も後の列の接頭部");
        Check(events.Count(e => Equals(e["kind"], "auto_play_reserved")) == 5 && events.Count(e => Equals(e["kind"], "auto_play_dispatch")) == 4,
            "終戦で残った予約（BASH）は確保の行だけで消化の行が無い");
        Check(events[1].Keys.SequenceEqual(new[] { "kind", "call_id", "parent_call_id", "parent", "parent_play_started_index", "requested_count", "position", "force_exhaust" })
            && ((Dictionary<string, object?>)events[1]["parent"]!).Keys.SequenceEqual(new[] { "kind", "model_id", "uid", "hidden_reason" })
            && events[2].Keys.SequenceEqual(new[] { "kind", "call_id", "reserve_index", "uid" })
            && events[6].Keys.SequenceEqual(new[] { "kind", "call_id", "reserve_index", "uid" }), "予約の各行のキー");
        Check(Markdown(state).Contains("auto_play_reservation:2") && Markdown(state).Contains("auto_play_reserved:5")
            && Markdown(state).Contains("auto_play_dispatch:4"), "Markdown は種類ごとの件数");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions)))
        {
            var wire = json.RootElement.GetProperty(key);
            Check(wire[1].GetProperty("parent_call_id").ValueKind == JsonValueKind.Null
                && wire[1].GetProperty("parent").GetProperty("hidden_reason").ValueKind == JsonValueKind.Null
                && wire[8].GetProperty("parent_call_id").GetInt32() == 1
                && wire[8].GetProperty("force_exhaust").ValueKind == JsonValueKind.True
                && wire[0].GetProperty("reservation").ValueKind == JsonValueKind.Null
                && wire[7].GetProperty("reservation").GetProperty("reserve_index").GetInt32() == 0, "wire の null・bool・数値");
        }
        Check(Stream(BuildCombatHistoryState(other)).Count == 0, "他 Player の列には出ない");

        // A power's call (MayhemPower) and a card still in the draw pile (IAmInvincible's own hook).
        var mayhem = AutoPlayReservations.Begin(Pos(), owner, new object(), "power", "MAYHEM_POWER", null, false, null, 1, "top", false);
        var invincible = new CardModel(CardType.Skill) { Id = new("I_AM_INVINCIBLE") };
        AutoPlayReservations.Begin(Pos(), owner, new object(), "card", "I_AM_INVINCIBLE", invincible, true, null, 1, "top", false);
        AutoPlayReservations.Begin(Pos(), owner, new object(), null, null, null, false, null, 2, "top", false);
        AutoPlayReservations.CallCompleted(mayhem);
        var tail = Stream(BuildCombatHistoryState(owner)).TakeLast(3).ToList();
        Check(ReservationRow(tail[0]) == "call 3<-> parent=power:MAYHEM_POWER:?@- n=1 top", "パワーの呼出し: uid と開始位置は null");
        var hidden = (Dictionary<string, object?>)tail[1]["parent"]!;
        Check(hidden["kind"] == null && hidden["model_id"] == null && hidden["uid"] == null && Equals(hidden["hidden_reason"], "draw_pile"),
            "山札にある親札は種類も名前も uid も出さない（確保が失敗しても山札の先頭を推せない）");
        Check(tail[2]["parent"] == null, "モデルの無い context は parent null");

        // Failures make the whole array unknown, never a stream without its reservations.
        BeginStateWarnings();
        AutoPlayReservations.TrackingError = "MissingMethodException: AutoPlayFromDrawPile(6)";
        var untracked = BuildCombatHistoryState(owner);
        Check(untracked[key] == null && Reasons(untracked)[key].Contains("tracking unavailable"), "patch が無ければ null と理由");
        AutoPlayReservations.TrackingError = null;
        AutoPlayReservations.Broken = "CardCmd.AutoPlay: NullReferenceException";
        Check(BuildCombatHistoryState(owner)[key] == null, "記録の失敗はその戦闘の列を null にする");
        AutoPlayReservations.Broken = null;
        BeginStateWarnings();
        history.Items.Clear();
        Check(BuildCombatHistoryState(owner)[key] == null, "履歴より後ろの位置の行は矛盾として null");
        AutoPlayReservations.Reset(); // CombatHistory.Clear postfix
        BeginStateWarnings();
        var fresh = BuildCombatHistoryState(owner);
        Check(Stream(fresh).Count == 0 && Reasons(fresh).Count == 0, "履歴のリセットで予約も記録の失敗も消える");
        Check(Stream(state).Count == expected.Length, "前の snapshot は独立");
        BeginStateWarnings();
    }
}
