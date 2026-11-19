using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    // 棚卸し（docs/state-inventory.md）で public と分類し、分類表に section を付けた内部状態を、
    // 各行の internal_state に出す。読むのは宣言フィールド（DeclaredOnly）と、自動実装プロパティの backing field だけで、
    // getter・ゲームのフック・遅延初期化・乱数は呼ばない。読めない値は null と internal_state.unavailable_reasons
    // （と warnings）にし、ほかの項目は残す。行のモデルに当てはまる項目が無ければ internal_state を付けない。
    private static readonly Dictionary<string, Type?> _allowlistTypes = new(StringComparer.Ordinal);

    private static Type? ResolveAllowlistType(string fullName)
    {
        if (_allowlistTypes.TryGetValue(fullName, out var cached)) return cached;
        var type = typeof(CardModel).Assembly.GetType(fullName, throwOnError: false);
        _allowlistTypes[fullName] = type;
        return type;
    }

    private static void AttachAllowlistedState(Dictionary<string, object?> row, string section, object? model,
        string context)
    {
        if (model == null) return;
        Dictionary<string, object?>? values = null;
        foreach (var entry in StateAllowlist)
        {
            if (entry.Section != section) continue;
            var modelType = ResolveAllowlistType(entry.ModelType);
            if (modelType == null)
            {
                // 型が無い版では、その型の行は作られない。黙って落とさず warnings に残す。
                Warn($"{context}.internal_state.{entry.Key}", $"型 {entry.ModelType} が見つからない");
                continue;
            }
            if (!modelType.IsInstanceOfType(model)) continue;
            values ??= new Dictionary<string, object?>();
            ReadInternalStateValue(values, entry.Key, () => ReadAllowlistedValue(entry, model), $"{context}.internal_state");
        }
        if (values != null) row["internal_state"] = values;
    }

    private static object? ReadAllowlistedValue(StateAllowlistEntry entry, object model)
    {
        var declaring = ResolveAllowlistType(entry.DeclaringType)
            ?? throw new TypeLoadException($"型 {entry.DeclaringType} が見つからない");
        object target = model;
        if (entry.Kind == StateMemberKind.DataField)
        {
            // PowerModel._internalData をそのまま読む（InitInternalData・GetInternalData は呼ばない）。
            if (model is not PowerModel power)
                throw new InvalidOperationException($"{entry.DeclaringType} の Data は PowerModel にだけある");
            target = ReadDeclaredStateField<object>(power, typeof(PowerModel), "_internalData");
            if (target.GetType() != declaring)
                throw new InvalidOperationException($"Expected {entry.DeclaringType}, got {target.GetType().FullName}");
        }

        // 自動実装プロパティは getter を呼ばず、コンパイラが作る backing field（<Name>k__BackingField）を読む。
        // 版が上がって getter に処理が入った（backing field が無くなった）ら、読まずに null と理由にする。
        string fieldName = entry.Kind == StateMemberKind.AutoProperty ? $"<{entry.Member}>k__BackingField" : entry.Member;
        var field = declaring.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException(declaring.Name, fieldName);
        Type memberType = field.FieldType;
        object? raw = field.GetValue(target);
        RequireAllowlistMemberType(entry, memberType);
        return ConvertAllowlistedValue(entry, raw);
    }

    // 宣言の型が変換の前提と違えば読まない（版が上がって型が変わったときに誤った値を出さない）。
    private static void RequireAllowlistMemberType(StateAllowlistEntry entry, Type memberType)
    {
        bool ok = entry.Converter switch
        {
            StateValueConverter.Bool => memberType == typeof(bool),
            StateValueConverter.Int => memberType == typeof(int),
            StateValueConverter.NullableInt => memberType == typeof(int?),
            StateValueConverter.Decimal => memberType == typeof(decimal),
            StateValueConverter.EnumName => memberType.IsEnum,
            StateValueConverter.CardUid or StateValueConverter.CardModelId => typeof(CardModel).IsAssignableFrom(memberType),
            StateValueConverter.CardPlayCardUid => memberType == typeof(CardPlay),
            StateValueConverter.CreatureCombatId => memberType == typeof(Creature),
            StateValueConverter.CreatureCombatIds => typeof(IEnumerable<Creature>).IsAssignableFrom(memberType),
            StateValueConverter.KeywordNames => typeof(IEnumerable<CardKeyword>).IsAssignableFrom(memberType),
            StateValueConverter.PowerRefs => typeof(IEnumerable<PowerModel>).IsAssignableFrom(memberType),
            StateValueConverter.SerializableCards => typeof(IEnumerable<SerializableCard>).IsAssignableFrom(memberType),
            _ => false
        };
        if (!ok)
            throw new InvalidOperationException($"{entry.DeclaringType}.{entry.Member} の型 {memberType.Name} は {entry.Converter} で読めない");
    }

    private static object? ConvertAllowlistedValue(StateAllowlistEntry entry, object? raw)
    {
        switch (entry.Converter)
        {
            case StateValueConverter.Bool:
            case StateValueConverter.Int:
            case StateValueConverter.Decimal:
                return raw ?? throw new InvalidOperationException($"{entry.Member} が null");
            case StateValueConverter.NullableInt:
                return raw;
            case StateValueConverter.EnumName:
                return raw?.ToString() ?? throw new InvalidOperationException($"{entry.Member} が null");
            case StateValueConverter.CardUid:
                return raw is CardModel card ? ResolveAllowlistCardUid(card) : null;
            case StateValueConverter.CardModelId:
                return raw is CardModel model
                    ? model.Id.Entry ?? throw new InvalidOperationException("Id.Entry が null")
                    : null;
            case StateValueConverter.CardPlayCardUid:
                return raw is CardPlay play ? ResolveAllowlistCardUid(play.Card) : null;
            case StateValueConverter.CreatureCombatId:
                return raw is Creature creature ? RequireCombatId(creature) : null;
            case StateValueConverter.CreatureCombatIds:
                return raw is IEnumerable<Creature> creatures
                    ? creatures.Select(RequireCombatId).OrderBy(id => id).ToList()
                    : new List<uint>();
            case StateValueConverter.KeywordNames:
                // HashSet の順番に意味は無い。列挙の値の順にそろえる。
                return raw is IEnumerable<CardKeyword> keywords
                    ? keywords.Distinct().OrderBy(k => (int)k).Select(k => k.ToString()).ToList()
                    : new List<string>();
            case StateValueConverter.PowerRefs:
                // 遅延で作るリスト（UnsettlingLamp.DoubledPowers）がまだ null なら空。getter は呼ばない。
                if (raw is not IEnumerable<PowerModel> powers) return new List<Dictionary<string, object?>>();
                return powers.Select(power => new Dictionary<string, object?>
                    {
                        ["id"] = power.Id.Entry,
                        ["owner_combat_id"] = power.Owner is { } owner ? RequireCombatId(owner) : null
                    })
                    .OrderBy(row => (string?)row["id"], StringComparer.Ordinal)
                    .ThenBy(row => row["owner_combat_id"] as uint?)
                    .ToList();
            case StateValueConverter.SerializableCards:
                // 預けたカード（PaelsTooth）。返すのは乱数で 1 枚なので、並びは意味を持たないよう id・強化段階の順にする。
                if (raw is not IEnumerable<SerializableCard> cards) return new List<Dictionary<string, object?>>();
                return cards.Select(saved => new Dictionary<string, object?>
                    {
                        ["id"] = saved.Id?.Entry ?? throw new InvalidOperationException("SerializableCard.Id が null"),
                        ["upgrade_level"] = saved.CurrentUpgradeLevel,
                        ["enchantment_id"] = saved.Enchantment?.Id?.Entry
                    })
                    .OrderBy(row => (string)row["id"]!, StringComparer.Ordinal)
                    .ThenBy(row => (int)row["upgrade_level"]!)
                    .ThenBy(row => row["enchantment_id"] as string, StringComparer.Ordinal)
                    .ToList();
            default:
                throw new InvalidOperationException($"未対応の変換 {entry.Converter}");
        }
    }

    private static uint RequireCombatId(Creature creature)
        => creature.CombatId ?? throw new InvalidOperationException("Creature に CombatId がない");

    /// <summary>
    /// 参照先のカードの uid。この読み取りで採番済みならそれを使う。まだなら、持ち主の戦闘の山（手札・山札・
    /// 捨て札・廃棄・プレイ中）にあるカード（同じ読み取りで山の行に出るもの）に限って採番する。
    /// 山に無いカードには新たに振らず、null と理由にする。
    /// </summary>
    private static string ResolveAllowlistCardUid(CardModel card)
    {
        if (TryGetRegisteredCardUid(card, out string uid)) return uid;
        var combat = card.Owner?.PlayerCombatState;
        if (combat != null && combat.AllCards.Contains(card)) return GetStableCardUid(card);
        string id = SafeGetText(() => card.Id.Entry) ?? "unknown";
        throw new InvalidOperationException($"{id} は戦闘の山に無い（uid を新たに振らない）");
    }
}
