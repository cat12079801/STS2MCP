namespace STS2_MCP;

public static partial class McpMod
{
    private sealed class DrawListener(TaskCompletionSource<int>? normal = null, TaskCompletionSource<int>? late = null)
    {
        internal async Task BeforeHandDraw()
        {
            if (normal != null) await SimSelect(normal);
        }

        internal async Task BeforeHandDrawLate()
        {
            if (late != null) await SimSelect(late);
        }

        internal async Task OnPlay(TaskCompletionSource<int> selection)
        {
            await SimSelect(selection);
        }
    }

    private sealed class DrawCombat(params DrawListener[] initial)
    {
        internal List<DrawListener> Live { get; } = new(initial);
        private IEnumerable<DrawListener> Subscribers { get; } = Array.Empty<DrawListener>();

        internal IEnumerable<DrawListener> IterateHookListeners()
        {
            var snapshot = Live.ToList();
            foreach (var listener in snapshot)
                if (Live.Contains(listener)) yield return listener;
            foreach (var listener in Subscribers) yield return listener;
        }
    }

    private static class DrawHook
    {
        private static IEnumerable<DrawListener> IterateCombatHookListeners(DrawCombat combat)
        {
            foreach (var listener in combat.IterateHookListeners()) yield return listener;
        }

        internal static async Task BeforeHandDraw(DrawCombat combat, object player, object playerChoiceContext)
        {
            foreach (var model in IterateCombatHookListeners(combat))
            {
                await model.BeforeHandDraw();
                GC.KeepAlive(model);
            }
            foreach (var model in IterateCombatHookListeners(combat))
            {
                await model.BeforeHandDrawLate();
                GC.KeepAlive(model);
            }
            GC.KeepAlive(player);
            GC.KeepAlive(playerChoiceContext);
        }
    }

    private sealed record DrawTurn(DrawCombat Combat);

    private static class DrawManager
    {
        internal static async Task SetupPlayerTurn(DrawTurn turnState, object player, object playerChoiceContext)
        {
            var state = turnState.Combat;
            await Task.CompletedTask;
            await DrawHook.BeforeHandDraw(state, player, playerChoiceContext);
            GC.KeepAlive(state);
        }
    }

    private static void CheckBeforeHandDraw()
    {
        var pending = new TaskCompletionSource<int>();
        var late = new TaskCompletionSource<int>();
        DrawListener first = new(), current = new(normal: pending, late: late), gone = new(), stillThere = new(), added = new();
        var combat = new DrawCombat(first, current, gone, stillThere);
        object player = new(), context = new();
        Task setup = DrawManager.SetupPlayerTurn(new DrawTurn(combat), player, context);
        var (normalFrames, _, _) = AsyncChain.Walk(GetAwaitedBy(pending.Task));
        var hook = normalFrames[1];
        string normalStage = AsyncChain.BeforeHandDrawStages[hook.State];
        Check(normalStage == "BeforeHandDraw" && AsyncChain.BeforeHandDrawStages.Skip(hook.State + 1).SequenceEqual(new[] { "BeforeHandDrawLate" }),
            "通常段で停止し、Late は続く段としてだけ分かる");
        AsyncChain.ValidateBeforeHandDraw(normalFrames, 1, typeof(DrawManager), normalStage, typeof(DrawListener), current, player, context, combat);
        Check(Throws(() => AsyncChain.ValidateBeforeHandDraw(normalFrames.Take(2).ToList(), 1, typeof(DrawManager), normalStage,
            typeof(DrawListener), current, player, context, combat)), "SetupPlayerTurn フレームの欠測");
        Check(Throws(() => AsyncChain.ValidateBeforeHandDraw(normalFrames, 1, typeof(DrawManager), normalStage,
            typeof(DrawListener), current, player, new object(), combat)), "別 context のフレームを拒む");

        combat.Live.Remove(gone);
        combat.Live.Add(added);
        var (original, next, originalIterator) = HookListSnapshots.ReadCursor(hook, current, typeof(DrawHook), typeof(DrawCombat));
        Check(next - 1 == 1 && original.Count == 4 && ReferenceEquals(original[2], gone) && ReferenceEquals(original[3], stillThere),
            "通常段が開始時に取った列と現在位置を読む");
        Check(Throws(() => HookListSnapshots.ReadCursor(hook, gone, typeof(DrawHook), typeof(DrawCombat)))
            && Throws(() => HookListSnapshots.ReadCursor(normalFrames[0], current, typeof(DrawHook), typeof(DrawCombat)))
            && HookListSnapshots.DrawPileCardsAtStart(originalIterator) == null, "別モデル・列挙子・山札スナップショットの欠測を検出");
        var remaining = HookListSnapshots.OrderRemaining(original.Cast<object>().Skip(next).ToList(), _ => true,
            model => combat.Live.Contains((DrawListener)model), _ => null, new HashSet<object>(), _ => 0);
        Check(remaining.Count == 1 && ReferenceEquals(remaining[0].Model, stillThere) && remaining[0].Order == 1,
            "読み直した列にいる追加モデルや、開始時の列から除去されたモデルを残りに出さない");
        Check(!setup.IsCompleted, "列の読み取りで選択は進まない");

        pending.SetResult(1);
        var (lateFrames, _, _) = AsyncChain.Walk(GetAwaitedBy(late.Task));
        var lateHook = lateFrames[1];
        string lateStage = AsyncChain.BeforeHandDrawStages[lateHook.State];
        Check(lateStage == "BeforeHandDrawLate" && !AsyncChain.BeforeHandDrawStages.Skip(lateHook.State + 1).Any(), "Late 自身の待機位置を読む");
        AsyncChain.ValidateBeforeHandDraw(lateFrames, 1, typeof(DrawManager), lateStage, typeof(DrawListener), current, player, context, combat);
        var (lateList, lateNext, lateIterator) = HookListSnapshots.ReadCursor(lateHook, current, typeof(DrawHook), typeof(DrawCombat));
        Check(!ReferenceEquals(originalIterator, lateIterator) && lateList.Cast<object>().Contains(added), "Late が開始時に新しく取った列を使う");
        combat.Live.Remove(stillThere);
        combat.Live.Remove(added);
        remaining = HookListSnapshots.OrderRemaining(lateList.Cast<object>().Skip(lateNext).ToList(), _ => true,
            model => combat.Live.Contains((DrawListener)model), _ => null, new HashSet<object>(), _ => 0);
        Check(remaining.Count == 0, "残りに有効なものがなければ空列");
        late.SetResult(2);
        Check(setup.IsCompletedSuccessfully, "Late から復帰して手番の準備が進む");

        var discovery = new TaskCompletionSource<int>();
        var cardPlay = stillThere.OnPlay(discovery);
        var (playFrames, _, _) = AsyncChain.Walk(GetAwaitedBy(discovery.Task));
        Check(playFrames.Count == 1 && playFrames[0].Method == "OnPlay" && !playFrames.Any(frame => frame.DeclaringType == typeof(DrawHook)),
            "同じ戦闘で別の choose を開いてもドロー前の段が現れない");
        discovery.SetResult(3);
        Check(cardPlay.IsCompletedSuccessfully, "別の選択も独立して完了する");
    }
}
