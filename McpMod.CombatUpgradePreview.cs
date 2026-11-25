using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

/// <summary>
/// Combat rows' upgrade state (sts2#1003, schema 5): every row of the hand and the four
/// combat piles carries its upgrade level, its keywords and the result of upgrading that
/// very card once, read off a detached clone of it.
///
/// Sources (v0.114.0, sts2-ae309f85): CardCmd.Upgrade(IEnumerable&lt;CardModel&gt;, ...) runs
/// CardModel.UpgradeInternal then FinalizeUpgradeInternal on the live card; this applies the
/// same two calls to a clone with the Upgraded event suppressed. AbstractModel.MutableClone = MemberwiseClone -&gt; DeepCloneFields
/// -&gt; AfterCloned. CardModel.DeepCloneFields re-attaches the enchantment / affliction through
/// EnchantInternal / AfflictInternal, which invoke EnchantmentChanged / AfflictionChanged while
/// the clone still holds the original's shallow-copied delegates (CardModel.AfterCloned clears
/// them only afterwards). Those delegates belong to CombatStateTracker.Subscribe and NCard, so
/// a plain MutableClone of an enchanted or afflicted live card notifies the real card's
/// listeners. The clone is therefore made from an unregistered shallow copy whose event fields
/// are cleared first. Nothing here registers a uid, enters a pile, records history or draws
/// from an Rng; CardCmd.Upgrade and ICardScope.CloneCard (which AddCards) are not used.
/// </summary>
public static partial class McpMod
{
    internal const string UpgradePreviewReady = "ready";
    internal const string UpgradePreviewNotUpgradable = "not_upgradable";
    internal const string UpgradePreviewUnavailable = "unavailable";

    /// <summary>Preview fields that are display only: a failure there does not make the
    /// machine-readable preview unavailable.</summary>
    private static readonly HashSet<string> UpgradePreviewDisplayOnlyFields = new(StringComparer.Ordinal)
    {
        "cost", "star_cost"
    };

    /// <summary>
    /// Adds the combat-only upgrade fields to a hand or pile row: the live card's level, cap,
    /// keyword sets, and "upgrade_preview". Call after AttachCardDetails,
    /// which creates the row's unavailable_reasons.
    /// </summary>
    private static void AttachCombatUpgradeState(Dictionary<string, object?> row, CardModel card, PileType pile)
    {
        if (row.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
            row["unavailable_reasons"] = reasons = new Dictionary<string, string>();
        // CardModel.CurrentUpgradeLevel / MaxUpgradeLevel.
        ReadCardDetailValue(row, reasons, "upgrade_level", () => card.CurrentUpgradeLevel);
        ReadCardDetailValue(row, reasons, "max_upgrade_level", () => card.MaxUpgradeLevel);
        // CardModel.GetKeywordsWithSources(Local) / Keywords (= All, Hook.ModifyKeywordsInCombat).
        ReadCardDetailValue(row, reasons, "local_keyword_ids",
            () => KeywordIds(card.GetKeywordsWithSources(KeywordSources.Local)));
        ReadCardDetailValue(row, reasons, "keyword_ids", () => KeywordIds(card.Keywords));
        // retain_this_turn / single_turn_retain, the Sly flags and the replay counts come with
        // AttachCardDetails (AttachCardTurnState), so the ready clone carries them too.
        row["upgrade_preview"] = BuildCombatUpgradePreview(card, pile);
    }

    /// <summary>CardKeyword enum names, ordered by enum value: the game holds HashSets.</summary>
    private static List<string> KeywordIds(IEnumerable<CardKeyword> keywords)
        => keywords.Distinct().OrderBy(keyword => (int)keyword).Select(keyword => keyword.ToString()).ToList();

    /// <summary>
    /// The "upgrade_preview" of one live combat card. Never throws: a failure is reported as
    /// status "unavailable" with its field paths, and the rest of the state is unaffected.
    /// </summary>
    internal static Dictionary<string, object?> BuildCombatUpgradePreview(CardModel card, PileType pile)
    {
        long started = Stopwatch.GetTimestamp();
        _upgradePreviewStats.Rows++;
        var reasons = new Dictionary<string, string>();
        Dictionary<string, object?> result;
        try { result = BuildCombatUpgradePreviewCore(card, pile, reasons); }
        catch (Exception ex)
        {
            reasons["upgrade_preview"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("card.upgrade_preview", ex);
            result = UnavailablePreview(null, reasons);
        }
        switch (result["status"])
        {
            case UpgradePreviewReady: _upgradePreviewStats.Ready++; break;
            case UpgradePreviewNotUpgradable: _upgradePreviewStats.NotUpgradable++; break;
            default: _upgradePreviewStats.Unavailable++; break;
        }
        _upgradePreviewStats.Ticks += Stopwatch.GetTimestamp() - started;
        return result;
    }

    private static Dictionary<string, object?> BuildCombatUpgradePreviewCore(CardModel card, PileType pile,
        Dictionary<string, string> reasons)
    {
        var parent = new Dictionary<string, object?>();
        ReadPreviewValue(parent, reasons, "id", "parent.id", () => card.Id.Entry);
        ReadPreviewValue(parent, reasons, "level", "parent.upgrade_level", () => card.CurrentUpgradeLevel);
        ReadPreviewValue(parent, reasons, "max", "parent.max_upgrade_level", () => card.MaxUpgradeLevel);
        ReadPreviewValue(parent, reasons, "upgradable", "parent.is_upgradable", () => card.IsUpgradable);
        int? fromLevel = parent["level"] as int?;
        if (reasons.Count > 0) return UnavailablePreview(fromLevel, reasons);

        int level = (int)parent["level"]!;
        int max = (int)parent["max"]!;
        if (parent["upgradable"] is false)
        {
            // CardModel.IsUpgradable is CurrentUpgradeLevel < MaxUpgradeLevel, so false means the
            // cap (0 included: Wound / Dazed / AscendersBane.MaxUpgradeLevel). No clone is made.
            if (level >= max)
                return new Dictionary<string, object?>
                {
                    ["status"] = UpgradePreviewNotUpgradable,
                    ["from_level"] = level,
                    ["unavailable_reasons"] = reasons
                };
            reasons["parent.is_upgradable"] = $"inconsistent: false at level {level} below max {max}";
            return UnavailablePreview(level, reasons);
        }

        CardModel clone;
        try
        {
            clone = CreateIsolatedMutableClone(card);
            _upgradePreviewStats.Clones++;
            // Combat context for reads that need the owner's combat (CardModel.CombatState,
            // GetKeywordsWithSources, UpdateDynamicVarPreview), as NUpgradePreview.Reload does.
            // The clone has no Pile, so pile-only hooks (FreeAttackPower etc.) do not see it.
            clone.UpgradePreviewType = CardUpgradePreviewType.Combat;
            clone.UpgradeInternal(silent: true);
            clone.FinalizeUpgradeInternal();
        }
        catch (Exception ex)
        {
            reasons["clone"] = $"{ex.GetType().Name}: {ex.Message}";
            Warn("card.upgrade_preview.clone", ex);
            return UnavailablePreview(level, reasons);
        }

        var preview = new Dictionary<string, object?>
        {
            ["status"] = UpgradePreviewReady,
            ["from_level"] = level
        };
        ReadPreviewValue(preview, reasons, "id", "id", () => clone.Id.Entry);
        ReadPreviewValue(preview, reasons, "name", "name", () => clone.Title);
        // The plain text at the clone's real level for the parent's pile: no "before -> after"
        // upgrade-preview rendering and no fallback text (SafeGetCardDescription has one).
        ReadPreviewValue(preview, reasons, "description", "description",
            () => StripRichTextTags(clone.GetDescriptionForPile(pile)).Replace("\n", " "));
        // type, target_type, is_upgraded, is_upgradable, tags, affliction, enchantment (with its
        // dynamic_vars), dynamic_vars (BaseValue, StringVar excluded), retain_this_turn,
        // single_turn_retain, sly_this_turn, single_turn_sly, base_replay_count,
        // extra_replay_this_turn, enchanted_replay_count (and tinker_time_rider on Mad Science):
        // the live rows' reader, on the clone. MemberwiseClone copies the private single-turn
        // flags and replay counts and DeepCloneFields keeps them, so an upgrade changes them
        // only where OnUpgrade does. Any failure among them makes the preview unavailable.
        var details = new Dictionary<string, object?>();
        AttachCardDetails(details, clone);
        var detailReasons = (Dictionary<string, string>)details["unavailable_reasons"]!;
        details.Remove("unavailable_reasons");
        foreach (var (key, value) in details) preview[key] = value;
        foreach (var (path, reason) in detailReasons) reasons[path] = reason;
        ReadPreviewValue(preview, reasons, "upgrade_level", "upgrade_level", () => clone.CurrentUpgradeLevel);
        ReadPreviewValue(preview, reasons, "max_upgrade_level", "max_upgrade_level", () => clone.MaxUpgradeLevel);
        ReadPreviewValue(preview, reasons, "local_keyword_ids", "local_keyword_ids",
            () => KeywordIds(clone.GetKeywordsWithSources(KeywordSources.Local)));
        ReadPreviewValue(preview, reasons, "keyword_ids", "keyword_ids", () => KeywordIds(clone.Keywords));

        var energy = BuildCardEnergyCostState(clone, detachedPreview: true);
        preview["energy_cost"] = energy;
        foreach (string path in RequiredEnergyCostFailures(energy))
            reasons[$"energy_cost.{path}"] = ((Dictionary<string, string>)energy["unavailable_reasons"]!)[path];
        var star = BuildCardStarCostState(clone);
        preview["star_cost_state"] = star;
        foreach (var (path, reason) in (Dictionary<string, string>)star["unavailable_reasons"]!)
            reasons[$"star_cost_state.{path}"] = reason;

        // Display fields, as the game's own upgrade preview card would show them (detached:
        // no pile). Hover tips are not duplicated: keyword_ids name the upgraded keywords.
        ReadPreviewValue(preview, reasons, "cost", "cost", () => GetCostDisplay(clone));
        ReadPreviewValue(preview, reasons, "star_cost", "star_cost", () => GetStarCostDisplay(clone));

        // The single upgrade CardCmd.Upgrade would apply: exactly one level, same card and cap.
        if (preview["id"] is string id && !string.Equals(id, (string)parent["id"]!, StringComparison.Ordinal))
            reasons["id"] = $"inconsistent: {id} is not {parent["id"]}";
        if (preview["upgrade_level"] is int upgraded && upgraded != level + 1)
            reasons["upgrade_level"] = $"inconsistent: {upgraded} is not {level + 1}";
        if (preview["max_upgrade_level"] is int upgradedMax && upgradedMax != max)
            reasons["max_upgrade_level"] = $"inconsistent: {upgradedMax} is not {max}";

        preview["unavailable_reasons"] = reasons;
        if (reasons.Keys.Any(path => !UpgradePreviewDisplayOnlyFields.Contains(path)))
            return UnavailablePreview(level, reasons);
        return preview;
    }

    /// <summary>
    /// Energy cost keys whose absence means the value was not observed. Spec-level empties are
    /// normal: X cost (no base), negative base (no energy cost), the X capture on a non-X card,
    /// and with_all_modifiers, which a pileless clone cannot know (detached_preview_context).
    /// </summary>
    private static IEnumerable<string> RequiredEnergyCostFailures(Dictionary<string, object?> energy)
    {
        var reasons = (Dictionary<string, string>)energy["unavailable_reasons"]!;
        foreach (string key in new[] { "costs_x", "base_raw", "canonical", "local_modifiers" })
            if (reasons.ContainsKey(key)) yield return key;
        if (energy["costs_x"] is true && reasons.ContainsKey("captured_x_value"))
            yield return "captured_x_value";
        if (energy["costs_x"] is false && energy["base_raw"] is int baseCost && baseCost >= 0
            && reasons.ContainsKey("with_local_modifiers"))
            yield return "with_local_modifiers";
    }

    private static Dictionary<string, object?> UnavailablePreview(int? fromLevel, Dictionary<string, string> reasons)
        => new()
        {
            ["status"] = UpgradePreviewUnavailable,
            ["from_level"] = fromLevel,
            ["unavailable_reasons"] = reasons
        };

    private static void ReadPreviewValue(Dictionary<string, object?> state, Dictionary<string, string> reasons,
        string key, string path, Func<object?> read)
    {
        try
        {
            object? value = read();
            if (value == null && !UpgradePreviewDisplayOnlyFields.Contains(key))
                throw new InvalidOperationException("null");
            state[key] = value;
        }
        catch (Exception ex)
        {
            state[key] = null;
            reasons[path] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"card.upgrade_preview.{path}", ex);
        }
    }

    private static readonly MethodInfo ShallowCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Per card type: the event fields to clear, or why the type cannot be isolated.
    /// A refusal is cached too, so an unsupported type costs one scan, not one per row and read.</summary>
    private static readonly ConcurrentDictionary<Type, (FieldInfo[]? Fields, string? Error)> _modelEventFields = new();

    /// <summary>
    /// A mutable clone of a live (mutable) card that notifies nobody: the real card's event
    /// delegates are cleared on an unregistered shallow copy, and the game's own MutableClone
    /// is run from that copy. Neither the live card nor anything it shares is written to: the
    /// only writes are the copy's own delegate fields, and DeepCloneFields replaces every
    /// shared collection (keywords, DynamicVars, energy cost and its modifiers, temporary star
    /// costs, enchantment, affliction) on the result before UpgradeInternal touches them.
    /// </summary>
    internal static CardModel CreateIsolatedMutableClone(CardModel card)
    {
        if (!card.IsMutable)
            throw new InvalidOperationException("Expected a mutable (live) card");
        var (fields, error) = ModelEventFields(card.GetType());
        if (fields == null)
            throw new InvalidOperationException(error);
        var shallow = (CardModel)ShallowCloneMethod.Invoke(card, null)!;
        foreach (var field in fields)
            field.SetValue(shallow, null);
        return (CardModel)shallow.MutableClone();
    }

    /// <summary>
    /// The backing fields of every event declared on the card's type chain. An event without a
    /// same-named delegate field (custom add/remove) cannot be cleared, so the clone is refused.
    /// </summary>
    private static (FieldInfo[]? Fields, string? Error) ModelEventFields(Type type) => _modelEventFields.GetOrAdd(type, static type =>
    {
        var fields = new List<FieldInfo>();
        try
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly;
                foreach (var evt in current.GetEvents(declared))
                {
                    var field = current.GetField(evt.Name, declared);
                    if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
                        return (null, $"Cannot isolate event {current.Name}.{evt.Name}");
                    fields.Add(field);
                }
            }
        }
        catch (Exception ex)
        {
            return (null, $"Cannot isolate events of {type.Name}: {ex.GetType().Name}: {ex.Message}");
        }
        return (fields.ToArray(), null);
    });

    /// <summary>Per-read counters for ?perf=1. Main thread only, like the warnings.</summary>
    private struct UpgradePreviewStats
    {
        public int Rows, Clones, Ready, NotUpgradable, Unavailable;
        public long Ticks;
    }

    private static UpgradePreviewStats _upgradePreviewStats;

    /// <summary>
    /// Builds a state; with perf, also reports how long it took and what the combat upgrade
    /// previews cost (rows, clones, statuses, time). Off by default so the state is unchanged
    /// and deterministic. The raw JSON size is measured by the client on a normal read.
    /// </summary>
    internal static Dictionary<string, object?> BuildStateMeasured(Func<Dictionary<string, object?>> build, bool perf)
    {
        if (!perf) return build();
        _upgradePreviewStats = default;
        long started = Stopwatch.GetTimestamp();
        var state = build();
        long elapsed = Stopwatch.GetTimestamp() - started;
        var stats = _upgradePreviewStats;
        state["state_build_perf"] = new Dictionary<string, object?>
        {
            ["build_ms"] = TicksToMs(elapsed),
            ["upgrade_preview"] = new Dictionary<string, object?>
            {
                ["rows"] = stats.Rows,
                ["clones"] = stats.Clones,
                ["ready"] = stats.Ready,
                ["not_upgradable"] = stats.NotUpgradable,
                ["unavailable"] = stats.Unavailable,
                ["elapsed_ms"] = TicksToMs(stats.Ticks)
            }
        };
        return state;
    }

    private static double TicksToMs(long ticks) => Math.Round(ticks * 1000.0 / Stopwatch.Frequency, 3);
}
