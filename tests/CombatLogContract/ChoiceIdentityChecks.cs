namespace STS2_MCP;

public static partial class McpMod
{
    private static void CheckChoiceIdentity()
    {
        CombatLogStore.Clear();
        OpenCardSelections.Clear();
        object combat = new(), chooser = new(), context = new();
        var a = new CombatAttempt(combat, 41, "P");
        // Even reuse of the CombatState reference cannot hide a new CombatTurnState.Id.
        var b = new CombatAttempt(combat, 42, "P");
        object first = new(), second = new();
        object?[] shown = { first, second };
        const string selector = OpenCardSelection.ChooseSelector;

        OpenCardSelection Open(CombatAttempt attempt, Task task, IReadOnlyList<object?> offered)
        {
            var record = OpenCardSelections.Add(selector, context, chooser, combat, candidates: offered,
                attempt: attempt, chooserPlayerId: "17");
            record.OpenedAtLogSeq = CombatLogStore.Bind(attempt);
            record.SelectionTask = task;
            return record;
        }

        var pending = new TaskCompletionSource<object?>();
        var opened = Open(a, pending.Task, shown);
        var snapshot = CombatLogStore.Read(a, null, 100);
        Check(Equals(CombatAttempt.Identity(a, "")["attempt_id"], "P/41") && snapshot.Rows.Count == 0 && snapshot.CombatId == null,
            "初行より前の live battle でも試行を識別し、従来の combat_id の null を保つ");
        var opener = opened.Opener; // It can be unavailable independently of the identity.
        var read = OpenCardSelections.MatchChoose(new CombatAttempt(combat, 41, "P"), chooser, shown);
        Check(ReferenceEquals(read.Record, opened) && OpenCardSelections.ObserveChoose(opened), "画面で実際の呼出しを照合できる");
        var id = CombatAttempt.ChoiceIdentity(a, read.Record, read.Reason);
        var again = OpenCardSelections.MatchChoose(a, chooser, shown);
        Check(id.SequenceEqual(CombatAttempt.ChoiceIdentity(a, again.Record, again.Reason))
            && id["known"] is true && opener == null && OpenCardSelections.Count == 1
            && CombatLogStore.Read(a, null, 100).NextSeq == 0, "再読の ID は安定し、opener の欠測や採番と独立");

        void Unobserved(CombatAttempt? attempt, OpenCardSelection? record, string reason)
        {
            var missing = CombatAttempt.ChoiceIdentity(attempt, record, reason);
            Check(missing["known"] is false && missing["selection_id"] == null
                && Equals(missing["attempt_id"], attempt?.Id)
                && missing["unavailable_reasons"] is List<string> reasons && reasons.Any(r => r.Length > 0),
                "対応が読めない選択には理由と null を出す");
        }
        foreach (var (attempt, player, holders) in new[]
        {
            (a, new object(), shown),
            (b, chooser, shown),
            (new CombatAttempt(new object(), 41, "P"), chooser, shown),
            (a, chooser, new object?[] { second, first }),
            (a, chooser, new object?[] { first, new object() }),
            (a, chooser, new object?[] { first, null, second })
        })
        {
            var mismatch = OpenCardSelections.MatchChoose(attempt, player, holders);
            Unobserved(attempt, mismatch.Record, mismatch.Reason);
        }
        Unobserved(null, null, "current combat ID unavailable");
        Unobserved(a, null, "choice source tracking unavailable");

        var duplicate = new TaskCompletionSource<object?>();
        var overlap = Open(a, duplicate.Task, shown);
        var overlapId = CombatAttempt.ChoiceIdentity(a, overlap, "");
        Check(opened.OpenedAtLogSeq == overlap.OpenedAtLogSeq
            && !Equals(id["selection_id"], overlapId["selection_id"]) && Equals(id["attempt_id"], overlapId["attempt_id"]),
            "候補・選択者・開始位置が同じでも別呼出しの選択 ID は異なる");
        var ambiguous = OpenCardSelections.MatchChoose(a, chooser, shown);
        Unobserved(a, ambiguous.Record, ambiguous.Reason);
        Check(!opened.HasInner, "同じ context で既に待っている呼出しは転送元として扱わない");
        duplicate.SetCanceled();
        Check(OpenCardSelections.Complete(overlap, duplicate.Task) == OpenCardSelections.Completion.Ignored, "取消は行にしない");

        string? Uid(object card) => ReferenceEquals(card, first) ? "CARD#g1" : ReferenceEquals(card, second) ? "CARD#g2" : null;
        Dictionary<string, object?> CardRef(object _) => new() { ["card_id"] = "CARD", ["pile"] = null };
        Check(OpenCardSelections.Complete(opened, duplicate.Task) == OpenCardSelections.Completion.Ignored
            && ReferenceEquals(OpenCardSelections.MatchChoose(a, chooser, shown).Record, opened), "別 Task の完了は対象の記録を消さない");
        pending.SetResult(second);
        Check(OpenCardSelections.Complete(opened, pending.Task) == OpenCardSelections.Completion.Ready, "元の Task の成功だけを消費する");
        var logRow = Row("selection");
        opened.WriteChooseResult(logRow, a, pending.Task.Result, CardRef, Uid);
        logRow["opened_at_seq"] = opened.OpenedAtLogSeq;
        var offered = (List<Dictionary<string, object?>?>)logRow["offered"]!;
        var chosen = (Dictionary<string, object?>)((List<object?>)logRow["chosen"]!).Single()!;
        Check(CombatLogStore.TryAppend(a, logRow), "元の試行の store に確定を追記する");
        var immediateBattle = CombatAttempt.Identity(a, "");
        var confirmed = CombatLogStore.Read(a, 0, 100);
        var logId = (Dictionary<string, object?>)confirmed.Rows.Single()["choice_identity"]!;
        Check(id.SequenceEqual(logId) && Equals(immediateBattle["attempt_id"], logId["attempt_id"]) && confirmed.CombatId == "41"
            && Equals(logRow["opened_at_seq"], 0L) && Equals(logRow["seq"], 0L) && Equals(logRow["chooser_player_id"], "17")
            && logRow["screen_observed"] is true && offered.Select(c => c!["uid"]).SequenceEqual(new[] { "CARD#g1", "CARD#g2" })
            && Equals(chosen["uid"], offered[1]!["uid"]) && !logRow.ContainsKey("unavailable_reasons"),
            "画面と確定行、直後の battle が同じ試行に結合し、同名の提示個体を UID で区別できる");
        var unexpected = Row("selection");
        opened.WriteChooseResult(unexpected, a, new object(), CardRef, _ => "unrelated");
        var unexpectedCard = (Dictionary<string, object?>)((List<object?>)unexpected["chosen"]!).Single()!;
        Check(unexpectedCard["uid"] == null && unexpected["unavailable_reasons"] is List<string> { Count: 1 },
            "提示外の戻り値の UID は結び付けない");
        Check(OpenCardSelections.Complete(opened, pending.Task) == OpenCardSelections.Completion.Ignored
            && !OpenCardSelections.ObserveChoose(opened), "二重通知と閉じた画面を退ける");

        var skip = Task.FromResult<object?>(null);
        var nextChoice = Open(a, skip, shown);
        var nextId = CombatAttempt.ChoiceIdentity(a, nextChoice, "");
        var otherProcess = new CombatAttempt(combat, 41, "Q");
        var collision = new OpenCardSelection { Selector = selector, Context = context, Player = chooser,
            Attempt = otherProcess, Sequence = nextChoice.Sequence };
        var collisionId = CombatAttempt.ChoiceIdentity(otherProcess, collision, "");
        Check(!Equals(nextId["attempt_id"], collisionId["attempt_id"]) && !Equals(nextId["selection_id"], collisionId["selection_id"]),
            "数値が衝突する別プロセスの試行と選択を区別する");
        Check(OpenCardSelections.Complete(nextChoice, skip) == OpenCardSelections.Completion.Ready && skip.Result == null
            && OpenCardSelections.Complete(nextChoice, skip) == OpenCardSelections.Completion.Ignored,
            "0 枚スキップの即時成功を 1 回だけ消費する");
        var skipRow = Row("selection");
        nextChoice.WriteChooseResult(skipRow, a, skip.Result, CardRef, Uid);
        Check(skipRow["screen_observed"] is false && skipRow["skipped"] is true && skipRow["chosen"] is List<object?> { Count: 0 }
            && ((List<Dictionary<string, object?>?>)skipRow["offered"]!).All(card => card!["uid"] == null)
            && skipRow["unavailable_reasons"] is List<string> { Count: > 0 },
            "空の確定結果を空列で出し、即時成功を画面表示や UID の観測とみなさない");
        Check(OpenCardSelections.Complete(Open(a, skip, Array.Empty<object?>()), skip) == OpenCardSelections.Completion.Ignored,
            "0 候補の早期 return は選択の行にしない");
        var fault = Task.FromException<object?>(new InvalidOperationException("test failure"));
        Check(OpenCardSelections.Complete(Open(a, fault, shown), fault) == OpenCardSelections.Completion.Ignored, "失敗は行にしない");
        _ = fault.Exception;

        var late = new TaskCompletionSource<object?>();
        var lateCall = Open(a, late.Task, shown);
        late.SetResult(first);
        Check(OpenCardSelections.Complete(lateCall, late.Task) == OpenCardSelections.Completion.Ready,
            "A の完了を消費した時点で止め、追記の直前に切り替える");
        Check(!a.IsLive(false, combat) && !a.IsLive(true, new object()), "終了後 Reset 前の同じ ID・参照と、別参照を拒む");
        CombatLogStore.Bind(b);
        Check(!Equals(CombatAttempt.Identity(a, "")["attempt_id"], CombatAttempt.Identity(b, "")["attempt_id"]),
            "同じ遭遇を再試行した直後の battle に別の試行 ID が付く");
        Check(!CombatLogStore.TryAppend(a, Row("selection")) && !CombatLogStore.TryMarkBroken(a, "old completion")
            && CombatLogStore.Read(b, null, 100) is { NextSeq: 0, Reasons.Count: 0 },
            "B の初行前に A の追記・欠測理由を隔離する");
        CombatLogStore.TryAppend(b, Row("draw"));
        var bPending = new TaskCompletionSource<object?>();
        var bCall = Open(b, bPending.Task, shown);
        Check(!CombatLogStore.TryAppend(a, Row("selection")) && !CombatLogStore.TryMarkBroken(a, "old completion")
            && CombatLogStore.Read(b, null, 100) is { NextSeq: 1, Reasons.Count: 0, Rows.Count: 1 }
            && CombatLogStore.Read(a, null, 100).Rows.Count == 0 && ReferenceEquals(OpenCardSelections.MatchChoose(b, chooser, shown).Record, bCall),
            "B の初行後も A の遅延通知は B のログと open record に影響しない");
        Check(confirmed.CombatId == "41" && ReferenceEquals(confirmed.Rows.Single(), logRow)
            && Equals(logId["attempt_id"], a.Id) && Equals(logRow["seq"], 0L), "先に保存した A の終了ログの ID と seq が残る");

        // An unfinished A record is removed even when B uses the same CombatState reference.
        OpenCardSelections.Clear();
        var dropped = new TaskCompletionSource<object?>();
        var droppedCall = OpenCardSelections.Add(selector, context, chooser, combat, candidates: shown, attempt: a);
        droppedCall.SelectionTask = dropped.Task;
        OpenCardSelections.DropOtherCombats(combat, b);
        dropped.SetResult(first);
        Check(OpenCardSelections.Complete(droppedCall, dropped.Task) == OpenCardSelections.Completion.Ignored,
            "別試行に切り替えて捨てた選択の通知を無視する");

        var evicted = new TaskCompletionSource<object?>();
        var evictedCall = Open(b, evicted.Task, shown);
        for (int i = 0; i < OpenCardSelections.Capacity; i++)
            OpenCardSelections.Add("FromHand", new object(), chooser, combat, attempt: b);
        evicted.SetResult(first);
        Check(OpenCardSelections.Complete(evictedCall, evicted.Task) == OpenCardSelections.Completion.Evicted
            && OpenCardSelections.Complete(evictedCall, evicted.Task) == OpenCardSelections.Completion.Ignored,
            "同じ試行の上限退避で失った確定行は欠測と判別でき、二重処理しない");
        CombatLogStore.TryMarkBroken(b, "selection: pending call evicted before completion");
        Check(CombatLogStore.Read(b, 0, 100) is { Rows.Count: 1, Reasons.Count: 1 }, "欠測理由から確定行を復元しない");
        OpenCardSelections.Clear();
        CombatLogStore.Clear();
    }
}
