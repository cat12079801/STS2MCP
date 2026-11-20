using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace STS2_MCP;

/// <summary>
/// One async method that is suspended at an <c>await</c>: its Task (the runtime's
/// <c>AsyncStateMachineBox</c>), a copy of its state machine read now, and the method it runs.
/// </summary>
internal sealed class AsyncFrame
{
    public required Task Task { get; init; }
    public required object StateMachine { get; init; }
    public required Type StateMachineType { get; init; }
    /// <summary>The async method's name ("DrawInternal" for <c>&lt;DrawInternal&gt;d__21</c>), or the state machine's type name.</summary>
    public required string Method { get; init; }
    public required Type? DeclaringType { get; init; }
    /// <summary><c>&lt;&gt;1__state</c>: the index of the await it is suspended at (the compiler numbers them in source order).</summary>
    public required int State { get; init; }
}

/// <summary>
/// Reads the chain of async methods that wait on a Task, outward, without running anything: a Task's continuation
/// list names the state machine boxes awaiting it (System.Private.CoreLib, .NET 9: <c>Task.m_continuationObject</c>,
/// <c>AwaitTaskContinuation.m_action</c> when the await captured a SynchronizationContext, the box's public
/// <c>StateMachine</c> field), and each state machine's live awaiter (<c>TaskAwaiter.m_task</c>) confirms what it awaits.
/// Free of game types so the contract test drives it with real async methods. Every read is reflection on fields;
/// nothing is invoked, and nothing changes the tasks.
/// </summary>
internal static class AsyncChain
{
    internal const int MaxDepth = 16;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Regex AsyncMethodName = new(@"^<([^>]+)>d__\d+$", RegexOptions.Compiled);
    private static readonly Dictionary<Type, bool> BoxTypes = new();
    private static readonly object Gate = new();

    internal static bool IsStateMachineBox(object? value)
    {
        if (value is not Task) return false;
        var type = value.GetType();
        lock (Gate)
        {
            if (BoxTypes.TryGetValue(type, out bool known)) return known;
            bool box = type.GetInterfaces().Any(i => i.FullName == "System.Runtime.CompilerServices.IAsyncStateMachineBox")
                && type.GetField("StateMachine", BindingFlags.Instance | BindingFlags.Public) != null;
            BoxTypes[type] = box;
            return box;
        }
    }

    /// <summary>The suspended async method behind <paramref name="task"/>, or null when the task is not an async method's box.</summary>
    internal static AsyncFrame? FrameOf(Task task)
    {
        if (!IsStateMachineBox(task)) return null;
        var stateMachine = task.GetType().GetField("StateMachine", BindingFlags.Instance | BindingFlags.Public)!.GetValue(task)
            ?? throw new InvalidOperationException($"{task.GetType().Name}.StateMachine is null");
        var type = stateMachine.GetType();
        var match = AsyncMethodName.Match(type.Name);
        return new AsyncFrame
        {
            Task = task,
            StateMachine = stateMachine,
            StateMachineType = type,
            Method = match.Success ? match.Groups[1].Value : type.Name,
            DeclaringType = type.DeclaringType,
            State = Field(stateMachine, "<>1__state") is int state ? state : throw new MissingFieldException(type.Name, "<>1__state")
        };
    }

    /// <summary>
    /// The Task the state machine is suspended on: the one awaiter field (<c>&lt;&gt;u__N</c>, TaskAwaiter or
    /// ConfiguredTaskAwaiter) holding a task. The compiler clears an awaiter after it resumes, so at most one holds one.
    /// Null when none does; an exception when several do or an awaiter of another kind (ValueTask) is set.
    /// </summary>
    internal static Task? AwaitedTask(object stateMachine)
    {
        var found = new List<Task>();
        foreach (var field in stateMachine.GetType().GetFields(InstanceFields))
        {
            if (!field.Name.StartsWith("<>u__", StringComparison.Ordinal)) continue;
            var awaiter = field.GetValue(stateMachine);
            if (awaiter == null) continue;
            var taskField = awaiter.GetType().GetField("m_task", InstanceFields);
            if (taskField == null)
            {
                // ValueTaskAwaiter and the like: their content is not a Task we can follow.
                if (!IsDefault(awaiter)) throw new InvalidOperationException($"{stateMachine.GetType().Name}.{field.Name} is a {awaiter.GetType().Name}");
                continue;
            }
            if (taskField.GetValue(awaiter) is Task task) found.Add(task);
        }
        return found.Count switch
        {
            0 => null,
            1 => found[0],
            _ => throw new InvalidOperationException($"{stateMachine.GetType().Name} holds {found.Count} awaiters")
        };
    }

    private static bool IsDefault(object boxedStruct)
    {
        var type = boxedStruct.GetType();
        return type.IsValueType && Equals(boxedStruct, Activator.CreateInstance(type));
    }

    /// <summary>
    /// The async methods awaiting <paramref name="task"/> (their boxes) and the type names of the other continuations
    /// (ContinueWith, WhenAny and the like). A completed task has none.
    /// </summary>
    internal static (List<Task> Boxes, List<string> Others) Awaiters(Task task)
    {
        var boxes = new List<Task>();
        var others = new List<string>();
        if (task.IsCompleted) return (boxes, others);
        var field = typeof(Task).GetField("m_continuationObject", InstanceFields)
            ?? throw new MissingFieldException(nameof(Task), "m_continuationObject");
        var value = field.GetValue(task);
        IEnumerable<object?> continuations = value switch
        {
            null => Array.Empty<object?>(),
            IList list when value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(List<>) => list.Cast<object?>().ToList(),
            _ => new[] { value }
        };
        foreach (var continuation in continuations)
        {
            if (continuation == null) continue;
            var box = BoxOf(continuation, 0);
            if (box != null)
            {
                if (!boxes.Contains(box)) boxes.Add(box);
            }
            else others.Add(continuation.GetType().Name);
        }
        return (boxes, others);
    }

    // A continuation is the box itself, an AwaitTaskContinuation (SynchronizationContext / TaskScheduler captured
    // by the await) whose m_action is box.MoveNextAction, or that Action; a tracing ContinuationWrapper keeps the
    // original Action in _continuation.
    private static Task? BoxOf(object continuation, int depth)
    {
        if (depth > 4) return null;
        if (IsStateMachineBox(continuation)) return (Task)continuation;
        Delegate? action = continuation as Delegate;
        if (action == null && continuation.GetType().GetField("m_action", InstanceFields) is { } actionField)
            action = actionField.GetValue(continuation) as Delegate;
        if (action?.Target is not { } target) return null;
        if (IsStateMachineBox(target)) return (Task)target;
        if (target.GetType().GetField("_continuation", InstanceFields)?.GetValue(target) is Delegate inner)
            return BoxOf(inner, depth + 1);
        return null;
    }

    /// <summary>
    /// The async methods waiting on <paramref name="start"/>, innermost first: each step takes the only box awaiting
    /// the previous task and confirms that its suspended awaiter holds that task. Stops with <c>End</c> naming why.
    /// </summary>
    internal static (List<AsyncFrame> Frames, string End) Walk(Task start, int maxDepth = MaxDepth)
    {
        var frames = new List<AsyncFrame>();
        var current = start;
        while (true)
        {
            if (frames.Count >= maxDepth) return (frames, $"depth limit {maxDepth}");
            if (current.IsCompleted) return (frames, "completed");
            var (boxes, others) = Awaiters(current);
            string what = frames.Count == 0 ? "the selection" : $"{frames[^1].DeclaringType?.Name}.{frames[^1].Method}";
            if (boxes.Count == 0)
                return (frames, others.Count == 0 ? $"no async method awaits {what}"
                    : $"no async method awaits {what} (other continuations: {string.Join(", ", others)})");
            if (boxes.Count > 1) return (frames, $"{boxes.Count} async methods await {what}");
            var frame = FrameOf(boxes[0]) ?? throw new InvalidOperationException("awaiting box without a state machine");
            if (!ReferenceEquals(AwaitedTask(frame.StateMachine), current))
                return (frames, $"{frame.DeclaringType?.Name}.{frame.Method} is not suspended on {what}");
            frames.Add(frame);
            current = frame.Task;
        }
    }

    /// <summary>An instance field by its exact name (null when it holds null); throws when the field does not exist.</summary>
    internal static object? Field(object target, string name)
    {
        var field = target.GetType().GetField(name, InstanceFields) ?? throw new MissingFieldException(target.GetType().Name, name);
        return field.GetValue(target);
    }

    /// <summary>
    /// A hoisted local (<c>&lt;name&gt;5__N</c>): the one instance field whose name starts with
    /// <c>&lt;name&gt;5__</c>; throws when there is none or several.
    /// </summary>
    internal static object? Hoisted(object target, string name)
    {
        string prefix = $"<{name}>5__";
        var fields = target.GetType().GetFields(InstanceFields).Where(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (fields.Count != 1) throw new MissingFieldException(target.GetType().Name, $"{prefix}* ({fields.Count} found)");
        return fields[0].GetValue(target);
    }

    /// <summary>
    /// <c>List&lt;T&gt;.Enumerator</c> (boxed): the list it walks and <c>_index</c>, the index after the element it
    /// returned last (List&lt;T&gt;.Enumerator.MoveNext stores the element, then increments).
    /// </summary>
    internal static (IList List, int Index) ListEnumerator(object boxedEnumerator)
    {
        var type = boxedEnumerator.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>.Enumerator))
            throw new InvalidOperationException($"{type.Name} is not a List<T>.Enumerator");
        var list = Field(boxedEnumerator, "_list") as IList ?? throw new InvalidOperationException("List<T>.Enumerator._list is null");
        return (list, Field(boxedEnumerator, "_index") is int index ? index : throw new MissingFieldException(type.Name, "_index"));
    }
}

/// <summary>
/// The listener list a running Hook stage took, and which cards were in a draw pile when it took it. The game builds
/// the list once per stage (<c>CombatState.IterateHookListeners</c>: powers, relics, potions, orbs, then each pile's
/// cards in order), so a draw-pile card's place in it is its hidden place in the draw order. The draw-pile cards are
/// recorded right before that list is built, keyed by the iterator, only while a Hook call the state reads is open.
/// Free of game types for the contract test.
/// </summary>
internal static class HookListSnapshots
{
    internal const int Capacity = 64;

    internal sealed class Run
    {
        public required object Combat { get; init; }
    }

    private static readonly object Gate = new();
    private static readonly List<Run> Open = new();
    private static readonly ConditionalWeakTable<object, HashSet<object>> DrawPileAtStart = new();
    private static volatile int _openCount;

    /// <summary>A Hook call the state reads (AfterPlayerTurnStart, AfterShuffle) began in <paramref name="combat"/>.</summary>
    internal static Run Begin(object combat)
    {
        lock (Gate)
        {
            var run = new Run { Combat = combat };
            Open.Add(run);
            while (Open.Count > Capacity) Open.RemoveAt(0);
            _openCount = Open.Count;
            return run;
        }
    }

    internal static void End(Run run)
    {
        lock (Gate)
        {
            Open.Remove(run);
            _openCount = Open.Count;
        }
    }

    /// <summary>Whether any Hook call the state reads is open (a volatile read; no lock).</summary>
    internal static bool AnyOpen => _openCount != 0;

    /// <summary>Whether a list built now in <paramref name="combat"/> may belong to an open Hook call (cheap when none is open).</summary>
    internal static bool IsActiveFor(object combat)
    {
        if (_openCount == 0) return false;
        lock (Gate) return Open.Any(r => ReferenceEquals(r.Combat, combat));
    }

    internal static void Record(object iterator, IEnumerable<object> drawPileCards)
    {
        var set = new HashSet<object>(drawPileCards, ReferenceEqualityComparer.Instance);
        lock (Gate) DrawPileAtStart.AddOrUpdate(iterator, set);
    }

    internal static IReadOnlySet<object>? DrawPileCardsAtStart(object iterator)
    {
        lock (Gate) return DrawPileAtStart.TryGetValue(iterator, out var set) ? set : null;
    }

    /// <summary>Drops open calls of a fight that is gone (save and quit mid-hook never completes their Task).</summary>
    internal static void DropOtherCombats(object? liveCombat)
    {
        lock (Gate)
        {
            Open.RemoveAll(r => !ReferenceEquals(r.Combat, liveCombat));
            _openCount = Open.Count;
        }
    }

    internal static int OpenCount
    {
        get { lock (Gate) return Open.Count; }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Open.Clear();
            DrawPileAtStart.Clear();
            _openCount = 0;
        }
    }

    /// <summary>
    /// Orders the listeners after the running one (<paramref name="rest"/>, in the list's order) for publication:
    /// a listener is kept when it overrides the stage and the game would still run it now (<paramref name="runsNow"/>:
    /// <c>CombatState.Contains</c>, checked again when the game reaches it). A listener tied to a card that was in a
    /// draw pile when the list was built gets <c>Order = null</c> and the start of its contiguous run; each run is
    /// re-sorted by <paramref name="publicRank"/> so neither the numbers nor the row order reveal the draw order. The
    /// others keep their place (<c>Order</c> 0 = the next listener), which depends only on public counts.
    /// </summary>
    internal static List<(object Model, int? Order, int? GroupStart)> OrderRemaining(IReadOnlyList<object> rest,
        Func<object, bool> overrides, Func<object, bool> runsNow, Func<object, object?> cardOf,
        IReadOnlySet<object> drawPileAtStart, Func<object, IComparable> publicRank)
    {
        var rows = new List<(object Model, int? Order, int? GroupStart, IComparable? Rank)>();
        int runStart = -1;
        for (int i = 0; i < rest.Count; i++)
        {
            var model = rest[i];
            var card = cardOf(model);
            bool hidden = card != null && drawPileAtStart.Contains(card);
            if (!hidden) runStart = -1;
            else if (runStart < 0) runStart = i;
            if (!overrides(model) || !runsNow(model)) continue;
            rows.Add(hidden ? (model, null, runStart, publicRank(model)) : (model, i, null, null));
        }
        var result = new List<(object Model, int? Order, int? GroupStart)>(rows.Count);
        for (int start = 0; start < rows.Count;)
        {
            int end = start + 1;
            if (rows[start].GroupStart is int group)
                while (end < rows.Count && rows[end].GroupStart == group) end++;
            result.AddRange(rows.Skip(start).Take(end - start)
                .OrderBy(r => r.Rank, Comparer<IComparable?>.Default).Select(r => (r.Model, r.Order, r.GroupStart)));
            start = end;
        }
        return result;
    }
}
