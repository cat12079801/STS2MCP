using System;
using System.Collections.Generic;
using System.Linq;

namespace STS2_MCP.Rewards;

// UI-independent registry. Keys are object identities, never serialized or hashed into IDs.
internal sealed class RewardSession
{
    internal sealed class Entry(object key, string id, bool fresh)
    {
        internal object Key { get; } = key;
        internal string Id { get; } = id;
        internal string SelectionState { get; set; } = fresh ? "unopened" : "unknown";
        internal string? LastSelection { get; set; }
        internal string? LastOptionId { get; set; }
        internal bool Resolving { get; set; }
        internal Dictionary<string, object?> Describe(bool claimable) => new()
        {
            ["reward_id"] = Id,
            ["selection_state"] = SelectionState,
            ["claimable"] = claimable,
            ["skipped"] = SelectionState == "skipped",
            ["reward_consumed"] = SelectionState == "claimed" ? true : SelectionState == "skipped" ? false : null,
            ["last_selection"] = LastSelection,
            ["last_option_id"] = LastOptionId
        };
    }

    private readonly string _epoch = Guid.NewGuid().ToString("N");
    private long _serial;
    private long _revision;
    private object? _screen;
    private object? _run;
    private readonly Dictionary<object, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private List<Entry> _claimable = new();
    internal IEnumerable<Entry> Entries => _entries.Values;
    internal IReadOnlyList<Entry> Claimable => _claimable;
    internal string Revision => $"{_epoch}:{_revision}";
    internal bool IsScreen(object screen) => ReferenceEquals(_screen, screen);
    internal bool Contains(Entry entry) => _entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry);

    internal void CheckRun(object? run)
    {
        if (ReferenceEquals(_run, run)) return;
        Clear();
        _run = run;
    }

    internal void Clear()
    {
        _screen = null;
        _entries.Clear();
        _claimable.Clear();
        _revision++;
    }

    internal void Open(object screen, object? run)
    {
        CheckRun(run);
        if (IsScreen(screen)) return;
        Clear();
        _screen = screen;
    }

    internal Entry Register(object key, bool fresh = false)
    {
        if (_entries.TryGetValue(key, out var entry)) return entry;
        entry = new Entry(key, $"reward:{_epoch}:{++_serial}", fresh);
        _entries.Add(key, entry);
        _revision++;
        return entry;
    }

    internal Entry? Find(object key) => _entries.GetValueOrDefault(key);

    internal void Sync(IEnumerable<object> available)
    {
        var next = available.Select(key => Register(key)).Where(e => e.SelectionState != "claimed").ToList();
        if (_claimable.SequenceEqual(next)) return;
        _claimable = next;
        _revision++;
    }

    internal string? Resolve(string? id, int? index, string? revision, out Entry? entry)
    {
        entry = null;
        if (revision != null && revision != Revision)
            return "Stale screen_revision; re-read rewards state";
        if (id != null)
        {
            entry = _claimable.FirstOrDefault(e => e.Id == id);
            if (entry == null) return "reward_id is unknown, from another screen, claimed, or not currently claimable";
        }
        if (index.HasValue)
        {
            if (index < 0 || index >= _claimable.Count)
                return $"Reward index {index} out of range (screen has {_claimable.Count} claimable rewards)";
            var indexed = _claimable[index.Value];
            if (entry != null && !ReferenceEquals(entry, indexed))
                return "reward_id and index refer to different rewards; re-read rewards state";
            entry = indexed;
        }
        return entry == null ? "Provide reward_id (preferred), or index with screen_revision (index alone is legacy current-list position)" : null;
    }

    internal void Begin(Entry entry)
    {
        if (!Contains(entry)) return;
        entry.SelectionState = "opened";
        entry.LastSelection = null;
        entry.LastOptionId = null;
        entry.Resolving = true;
        _revision++;
    }

    internal void AwaitChoice(Entry entry)
    {
        if (!Contains(entry)) return;
        entry.SelectionState = "opened";
        entry.Resolving = false;
        _revision++;
    }

    internal void Choice(Entry entry, string kind, string? optionId = null)
    {
        if (!Contains(entry)) return;
        entry.LastSelection = kind;
        entry.LastOptionId = optionId;
        entry.Resolving = true;
        _revision++;
    }

    internal void Complete(Entry entry, bool consumed)
    {
        if (!Contains(entry)) return;
        entry.SelectionState = consumed ? "claimed" : entry.LastSelection == "skip" ? "skipped" : "unknown";
        entry.Resolving = false;
        if (consumed)
        {
            entry.LastSelection ??= "direct";
            _claimable.Remove(entry);
        }
        _revision++;
    }

    internal void Interrupt(Entry entry)
    {
        if (!Contains(entry)) return;
        entry.SelectionState = "unknown";
        entry.LastSelection = "interrupted";
        entry.LastOptionId = null;
        entry.Resolving = false;
        _revision++;
    }
}
