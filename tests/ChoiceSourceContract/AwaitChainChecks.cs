using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace STS2_MCP
{
    public static partial class McpMod
    {
        // Real async methods shaped like the game's: a listener awaiting the selection, a Hook-like stage walking a
        // listener list through two nested iterators, a shuffle, and a draw loop that shuffles before its second draw.
        private static async Task<int> SimSelect(TaskCompletionSource<int> choice) => await choice.Task;

        private static async Task SimListenerAfterShuffle(TaskCompletionSource<int> choice) { await SimSelect(choice); }

        private static IEnumerable<object> SimIterateHookListeners(List<object> list)
        {
            foreach (var item in list) yield return item;
        }

        private static IEnumerable<object> SimIterateCombatHookListeners(List<object> list)
        {
            foreach (var item in SimIterateHookListeners(list)) yield return item;
        }

        private static async Task SimHookAfterShuffle(List<object> list, Func<object, Task> run)
        {
            // Like Hook: the listener is used after the await (InvokeExecutionFinished, PopModel), so it is hoisted.
            foreach (var model in SimIterateCombatHookListeners(list))
            {
                await run(model);
                GC.KeepAlive(model);
            }
        }

        private static async Task SimShuffle(Func<Task> hook) { await hook(); }

        private static async Task SimDraw(int drawsRequested, Func<Task> shuffle)
        {
            for (int i = 0; i < drawsRequested; i++)
                if (i == 1) await shuffle();
        }

        private sealed class InlineSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => d(state);
        }

        private static void CheckAwaitChain()
        {
            // Inside out: SimSelect's Task <- SimListenerAfterShuffle <- SimHookAfterShuffle <- SimShuffle <- SimDraw.
            var choice = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            object before = new(), target = new(), after = new();
            var list = new List<object> { before, target, after };
            Task? selection = null;
            Task Run(object model)
            {
                if (!ReferenceEquals(model, target)) return Task.CompletedTask;
                // The listener is started here; the selection is its first await.
                var listener = SimListenerAfterShuffle(choice);
                return listener;
            }
            var draw = SimDraw(3, () => SimShuffle(() => SimHookAfterShuffle(list, Run)));
            Check(!draw.IsCompleted, "the simulated draw waits on the selection");

            // The selection is SimSelect's own Task (the box awaiting the TaskCompletionSource).
            var fromSource = AsyncChain.Walk(choice.Task).Frames;
            selection = GetAwaitedBy(choice.Task);
            var (frames, end, inconsistent) = AsyncChain.Walk(selection);
            Check(!inconsistent, "正常な終わりは inconsistent でない");
            Check(frames.Select(f => f.Method).SequenceEqual(new[] { "SimListenerAfterShuffle", "SimHookAfterShuffle", "SimShuffle", "SimDraw" }),
                "外側へ順に、選択を待つ async メソッドをたどる: " + string.Join(" < ", frames.Select(f => f.Method)));
            Check(frames.All(f => f.DeclaringType == typeof(McpMod)), "宣言した型を出す");
            Check(end.StartsWith("no async method awaits", StringComparison.Ordinal), "誰も await していない Task で止まり、理由を出す: " + end);
            Check(ReferenceEquals(frames[^1].Task, draw), "一番外は draw の Task");
            Check(frames.Zip(frames.Skip(1)).All(p => ReferenceEquals(AsyncChain.AwaitedTask(p.Second.StateMachine), p.First.Task)),
                "各フレームの awaiter が内側のフレームの Task を指す");
            var drawFrame = frames[^1];
            Check(drawFrame.State == 0 && AsyncChain.Hoisted(drawFrame.StateMachine, "i") is 1 && AsyncChain.Field(drawFrame.StateMachine, "drawsRequested") is 3,
                "ドローのループの添字（hoisted の <i>5__N）と引数を読む");
            Check(fromSource.Count == frames.Count + 1 && fromSource[0].Method == "SimSelect", "選択メソッド自身から始めると 1 段多い");

            // The Hook-like stage: its enumerator chain ends in List<T>.Enumerator at the running listener.
            var hook = frames[1];
            var outer = AsyncChain.Field(hook.StateMachine, "<>7__wrap1")!;
            Check(outer.GetType().Name.StartsWith("<SimIterateCombatHookListeners>d__", StringComparison.Ordinal), "段の列挙子（外側の iterator）");
            var inner = AsyncChain.Field(outer, "<>7__wrap1")!;
            Check(inner.GetType().Name.StartsWith("<SimIterateHookListeners>d__", StringComparison.Ordinal), "内側の iterator");
            var (walked, next) = AsyncChain.ListEnumerator(AsyncChain.Field(inner, "<>7__wrap1")!);
            Check(ReferenceEquals(walked, list) && next == 2 && ReferenceEquals(walked[next - 1], target)
                && ReferenceEquals(AsyncChain.Field(inner, "<>2__current"), target) && ReferenceEquals(AsyncChain.Hoisted(hook.StateMachine, "model"), target),
                "段が取った列そのものと、走っている listener の位置（_index - 1）を読む");
            Check(!draw.IsCompleted, "読むだけで何も進めない");

            choice.SetResult(1);
            draw.Wait(TimeSpan.FromSeconds(5));
            Check(draw.IsCompletedSuccessfully, "選択を終えれば元どおり最後まで進む");
            Check(AsyncChain.Walk(selection).End == "completed", "終わった Task はたどらない");

            // A SynchronizationContext captured by the await wraps the box in an AwaitTaskContinuation.
            var saved = SynchronizationContext.Current;
            var choice2 = new TaskCompletionSource<int>();
            Task outerTask;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
                outerTask = SimShuffle(() => SimListenerAfterShuffle(choice2));
            }
            finally { SynchronizationContext.SetSynchronizationContext(saved); }
            var (ctxFrames, _, _) = AsyncChain.Walk(GetAwaitedBy(choice2.Task));
            Check(ctxFrames.Select(f => f.Method).SequenceEqual(new[] { "SimListenerAfterShuffle", "SimShuffle" }),
                "SynchronizationContext を捕まえた await（AwaitTaskContinuation.m_action）でもたどる");

            // ContinueWith on the selection (the MOD closes its record that way) is not an async method.
            var selection2 = GetAwaitedBy(choice2.Task);
            selection2.ContinueWith(_ => { }, TaskScheduler.Default);
            var (boxes, others) = AsyncChain.Awaiters(selection2);
            Check(boxes.Count == 1 && others.Count == 1, "ContinueWith の継続は box でなく others に入る");
            Check(AsyncChain.Walk(selection2).Frames.Count == 2, "ContinueWith があっても同じ鎖をたどる");
            choice2.SetResult(0);
            outerTask.Wait(TimeSpan.FromSeconds(5));

            // Two async methods awaiting the same task: the chain stops instead of picking one.
            var shared = new TaskCompletionSource<int>();
            var first = SimShuffle(() => shared.Task);
            var second = SimShuffle(() => shared.Task);
            var (twoFrames, twoEnd, twoInconsistent) = AsyncChain.Walk(shared.Task);
            Check(twoFrames.Count == 0 && twoEnd.StartsWith("2 async methods await", StringComparison.Ordinal) && !twoInconsistent,
                "複数が待つなら選ばずに止める（正常な打ち切り）: " + twoEnd);
            shared.SetResult(0);
            Task.WaitAll(first, second);

            var lone = new TaskCompletionSource<int>();
            Check(AsyncChain.Walk(lone.Task).End == "no async method awaits the selection" && AsyncChain.FrameOf(lone.Task) == null,
                "async メソッドでない Task は frame を持たない");
            var deep = GetDeep(20, lone.Task);
            Check(AsyncChain.Walk(lone.Task, 4) is { Frames.Count: 4, End: "depth limit 4", Inconsistent: false }, "深さの上限で止める（正常な打ち切り）");
            lone.SetResult(0);
            deep.Wait(TimeSpan.FromSeconds(5));

            Check(Throws(() => AsyncChain.Hoisted(new { a = 1 }, "missing")), "無い hoisted は例外（推測しない）");
            Check(Throws(() => AsyncChain.Field(new object(), "missing")), "無いフィールドは例外");
            Check(Throws(() => AsyncChain.ListEnumerator(new object())), "List<T>.Enumerator でなければ例外");

            CheckHookListSnapshots();
            CheckAwaitChainMarkdown();
        }

        private static Task GetDeep(int depth, Task inner) => depth == 0 ? inner : SimShuffle(() => GetDeep(depth - 1, inner));

        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (Exception) { return true; }
        }

        // The box awaiting a TaskCompletionSource's task (SimSelect's own Task).
        private static Task GetAwaitedBy(Task task)
        {
            var (boxes, _) = AsyncChain.Awaiters(task);
            if (boxes.Count != 1) throw new Exception($"expected one awaiting box, found {boxes.Count}");
            return boxes[0];
        }

        private static void CheckHookListSnapshots()
        {
            HookListSnapshots.Clear();
            object combat = new(), other = new();
            Check(!HookListSnapshots.AnyOpen && !HookListSnapshots.IsActiveFor(combat), "開いた Hook が無ければ記録しない");
            var run = HookListSnapshots.Begin(combat);
            Check(HookListSnapshots.AnyOpen && HookListSnapshots.IsActiveFor(combat) && !HookListSnapshots.IsActiveFor(other), "その戦闘の Hook の間だけ記録する");
            object iterator = new(), drawCard = new(), handCard = new();
            HookListSnapshots.Record(iterator, new[] { drawCard });
            Check(HookListSnapshots.DrawPileCardsAtStart(iterator) is { } set && set.Contains(drawCard) && !set.Contains(handCard), "列を作った時点の山札の札を iterator ごとに保つ");
            Check(HookListSnapshots.DrawPileCardsAtStart(new object()) == null, "記録の無い iterator は null（推測しない）");
            HookListSnapshots.End(run);
            Check(!HookListSnapshots.AnyOpen, "Hook の Task が終われば閉じる");
            HookListSnapshots.Begin(combat);
            HookListSnapshots.Begin(other);
            HookListSnapshots.DropOtherCombats(combat);
            Check(HookListSnapshots.OpenCount == 1 && HookListSnapshots.IsActiveFor(combat), "終わった戦闘の Hook を捨てる");
            HookListSnapshots.DropOtherCombats(null);
            Check(HookListSnapshots.OpenCount == 0, "戦闘が無ければ全部捨てる");
            for (int i = 0; i < HookListSnapshots.Capacity + 3; i++) HookListSnapshots.Begin(combat);
            Check(HookListSnapshots.OpenCount == HookListSnapshots.Capacity, "上限を超えた古いものは捨てる");
            HookListSnapshots.Clear();

            // OrderRemaining: rest = [power, drawA, drawA's enchantment, drawB, relic, handCard, removedRelic, plainPower].
            object power = "power", drawA = "drawA", enchA = "enchA", drawB = "drawB", relic = "relic", hand = "hand", removed = "removed", plain = "plain";
            var rest = new List<object> { power, drawA, enchA, drawB, relic, hand, removed, plain };
            var cardOf = new Dictionary<object, object> { [drawA] = drawA, [enchA] = drawA, [drawB] = drawB, [hand] = hand };
            var drawAtStart = new HashSet<object> { drawA, drawB };
            // Public rank puts drawB before drawA (the draw pile's display order), and a card before its enchantment.
            var rank = new Dictionary<object, (int, int)> { [drawB] = (0, 0), [drawA] = (1, 0), [enchA] = (1, 1) };
            var ordered = HookListSnapshots.OrderRemaining(rest, m => !ReferenceEquals(m, plain), m => !ReferenceEquals(m, removed),
                m => cardOf.GetValueOrDefault(m), drawAtStart, m => rank[m]);
            Check(ordered.Select(o => o.Model).SequenceEqual(new[] { power, drawB, drawA, enchA, relic, hand }),
                "山札にあった札の run は公開の順に並べ替え、他は列の順のまま: " + string.Join(",", ordered.Select(o => o.Model)));
            Check(ordered[0].Order == 0 && ordered[4].Order == 4 && ordered[5].Order == 5, "山札以外の order は次を 0 とする列の位置");
            Check(ordered.Skip(1).Take(3).All(o => o.Order == null && o.GroupStart == 1), "山札にあった札は order=null と run の始まり");
            Check(!ordered.Any(o => ReferenceEquals(o.Model, removed) || ReferenceEquals(o.Model, plain)), "いま走らない listener と override していない listener は出さない");
            var moved = HookListSnapshots.OrderRemaining(new List<object> { drawA, relic, drawB }, _ => true, _ => true,
                m => cardOf.GetValueOrDefault(m), drawAtStart, m => rank[m]);
            Check(moved[0].GroupStart == 0 && moved[2].GroupStart == 2 && moved[1].Order == 1, "離れた run はそれぞれの始まりを持つ");
        }

        private static void CheckAwaitChainMarkdown()
        {
            string Render(Dictionary<string, object?> chain)
            {
                var sb = new StringBuilder();
                AppendChoiceSourceMarkdown(sb, new Dictionary<string, object?>
                {
                    ["choice_source"] = new Dictionary<string, object?>
                    {
                        ["known"] = true,
                        ["selector"] = "FromCombatPile",
                        ["models"] = new List<Dictionary<string, object?>> { new() { ["model_id"] = "STRATAGEM_POWER" } },
                        ["await_chain"] = chain
                    }
                });
                return sb.ToString();
            }
            var stratagem = new Dictionary<string, object?>
            {
                ["known"] = true,
                ["frames"] = new List<Dictionary<string, object?>>
                {
                    new() { ["kind"] = null, ["type"] = "StratagemPower", ["method"] = "AfterShuffle" },
                    new() { ["kind"] = "hook", ["awaiting"] = "AfterShuffle", ["listener"] = new Dictionary<string, object?> { ["remaining_listeners"] = new List<Dictionary<string, object?>> { new() } } },
                    new() { ["kind"] = "shuffle" },
                    new() { ["kind"] = "shuffle_if_necessary" },
                    new() { ["kind"] = "draw", ["remaining_draws"] = 2, ["from_hand_draw"] = true },
                    new() { ["kind"] = "setup_player_turn" },
                }
            };
            Check(Render(stratagem).Contains("awaited by hook AfterShuffle (1 listeners left) < shuffle < shuffle_if_necessary < draw (2 left, hand draw) < setup_player_turn"),
                "計略の await の鎖の 1 行: " + Render(stratagem));
            var splash = new Dictionary<string, object?>
            {
                ["known"] = true,
                ["frames"] = new List<Dictionary<string, object?>> { new() { ["kind"] = null, ["type"] = "Splash", ["method"] = "OnPlay" } }
            };
            Check(!Render(splash).Contains("awaited by"), "説明する段が無ければ（スプラッシュの本文など）何も足さない");
            var unknown = new Dictionary<string, object?> { ["known"] = false, ["unavailable_reasons"] = new List<string> { "the selection's Task was not recorded" } };
            Check(Render(unknown).Contains("await chain unknown (the selection's Task was not recorded)"), "分からないときは理由");
        }

        private static void CheckAwaitChainSources(Func<string, string> read)
        {
            string chain = read("McpMod.ChoiceAwaitChain.cs");
            string asyncChain = read("McpMod.AsyncChain.cs");
            string choice = read("McpMod.ChoiceSource.cs");
            Check(!Regex.IsMatch(chain + asyncChain, @"\.Rng|NextItem|TakeRandom|StableShuffle|\.Invoke\(\)|MoveNext\(\)"),
                "乱数・抽選に触らず、列挙子を進めない");
            Check(!Regex.IsMatch(chain, @"static bool Snapshot") && !Regex.IsMatch(chain, @"ref Task[^ ]* __result"), "patch は本体を飛ばさず、戻り値を差し替えない");
            Check(chain.Contains("private static void SnapshotHookListBuilding(object __instance)")
                && chain.Contains("if (_hookListIteratorState?.GetValue(__instance) is not 0) return;"), "列を作る最初の MoveNext の前だけ記録する");
            Check(chain.Contains("if (!HookListSnapshots.AnyOpen) return;"), "対象の Hook が無いときは何もしない（毎回の列挙の負担を避ける）");
            int stateFirst = chain.IndexOf("if (_hookListIteratorState?.GetValue(__instance) is not 0) return;", StringComparison.Ordinal);
            int lockAfter = chain.IndexOf("!HookListSnapshots.IsActiveFor(combat)", StringComparison.Ordinal);
            Check(stateFirst >= 0 && lockAfter > stateFirst, "最初の MoveNext でなければ lock に入る前に戻る");
            Check(chain.Contains("if (inconsistent) reasons.Add($\"await chain inconsistent: {end}\");"),
                "鎖が食い違って切れたときは known=false（正常な打ち切りは end だけ）");
            Check(chain.Contains("contains.Invoke(combat, new[] { m }) is true"), "残りは本体の CombatState.Contains で、いま走るものだけ");
            Check(chain.Contains("drawAtStart.Contains(own) || DrawPileOf(model) != null"), "山札にあった listener の位置は出さない");
            Check(choice.Contains("result[\"await_chain\"] = BuildChoiceAwaitChain(record, contextObjects, modelObjects);")
                && choice.Contains("record.SelectionTask = task;") && choice.Contains("InstallHookListSnapshotTracking();"), "choice_source に配線する");
            foreach (var steps in new[]
            {
                "[(typeof(Hook), \"AfterPlayerTurnStart\")] = new[] { \"AfterPlayerTurnStartEarly\", \"AfterPlayerTurnStart\", \"AfterPlayerTurnStartLate\" }",
                "[(typeof(CardPileCmd), \"DrawInternal\")] = new[] { \"AfterPreventingDraw\", \"ShuffleIfNecessary\", \"AddToHand\", \"AfterCardDrawn\" }",
                "[(typeof(CardPileCmd), \"AutoPlayFromDrawPile\")] = new[] { \"ShuffleIfNecessary\", \"AddToPlay\", \"AutoPlay\" }",
                "[(typeof(CombatManager), \"RunAutoPrePlayPhase\")] = new[] { \"SetupPlayerTurn\", \"CheckForEmptyHand\", \"AfterAutoPrePlayPhaseEntered\" }",
            })
                Check(chain.Contains(steps, StringComparison.Ordinal), "await の順の表（IL の <>1__state と同じ）: " + steps);
        }
    }
}
