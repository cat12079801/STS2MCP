using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;

namespace MegaCrit.Sts2.Core.Entities.Enchantments
{
    public enum EnchantmentStatus { Normal, Disabled }
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }
    public enum TargetType { None, Self, AnyEnemy, AllEnemies, RandomEnemy, AnyPlayer, AnyAlly, AllAllies, TargetedNoCreature, Osty }
    // Mirrors v0.113.1 MegaCrit.Sts2.Core.Entities.Cards.UnplayableReason / CardTag.
    [Flags]
    public enum UnplayableReason
    {
        None = 0, HasUnplayableKeyword = 2, BlockedByHook = 4, BlockedByCardLogic = 8,
        EnergyCostTooHigh = 0x10, StarCostTooHigh = 0x20, NoLivingAllies = 0x40
    }
    public enum CardTag { None, Strike, Defend, Minion, OstyAttack, Shiv, Wah, Cho, Ki, Shoot }
    // Mirrors v0.113.1 KeywordSources ([Flags]: Local = 2, Global = 4, All = -1).
    [Flags]
    public enum KeywordSources { None = 0, Local = 2, Global = 4, All = -1 }
    public enum CardKeyword { None, Exhaust, Ethereal, Innate, Unplayable, Retain, Sly, Eternal }
}

// Boundary doubles for game-owned getters. No game calculations or previews.
namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);
    public class AbstractModel
    {
        public bool ThrowId;
        public ModelId ModelId = new("TEST");
        public ModelId Id => ThrowId ? throw new InvalidOperationException("id failed") : ModelId;
    }
    public class AfflictionModel : AbstractModel
    {
        public int Value;
        public bool ThrowAmount;
        public int Amount => ThrowAmount ? throw new InvalidOperationException("amount failed") : Value;
    }
    public class EnchantmentModel : AbstractModel
    {
        public int Value;
        public bool ThrowAmount;
        public int Amount => ThrowAmount ? throw new InvalidOperationException("amount failed") : Value;
        public EnchantmentStatus StatusValue;
        public bool ThrowStatus;
        public EnchantmentStatus Status => ThrowStatus ? throw new InvalidOperationException("status failed") : StatusValue;
        // v0.113.1 EnchantmentModel._dynamicVars: set by DeepCloneFields on every mutable
        // enchantment; the public getter builds it when null. Tests set it directly.
        private DynamicVarSet? _dynamicVars = new();
        public void SetVars(DynamicVarSet? vars) => _dynamicVars = vars;
        public bool VarsInitialized => _dynamicVars != null;
        public int GetterReads;
        public DynamicVarSet DynamicVars { get { GetterReads++; return _dynamicVars ??= new DynamicVarSet(); } }
        public int PlayCountCalls;
        public virtual int EnchantPlayCount(int originalPlayCount) { PlayCountCalls++; return originalPlayCount; }
    }
    public class CardModel
    {
        public HashSet<string> ThrowOn = [];
        public CardType CardType = CardType.Attack;
        public TargetType Target = TargetType.AnyEnemy;
        public AfflictionModel? Afflicted;
        public EnchantmentModel? Enchanted;
        public IEnumerable<KeyValuePair<string, DynamicVar>> Vars = new Dictionary<string, DynamicVar>();
        public UnplayableReason PlayReason;
        public int UpgradeLevel;
        public int MaxLevel = 1;
        public IEnumerable<CardTag> TagSet = new HashSet<CardTag>();
        public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer)
        {
            _ = TargetType;
            reason = Read("can_play", PlayReason);
            preventer = null;
            return reason == UnplayableReason.None;
        }
        private T Read<T>(string field, T value)
            => ThrowOn.Contains(field) ? throw new InvalidOperationException($"{field} failed") : value;
        public CardType Type => Read("type", CardType);
        public TargetType TargetType => Read("target_type", Target);
        public AfflictionModel? Affliction => Read("affliction", Afflicted);
        public EnchantmentModel? Enchantment => Read("enchantment", Enchanted);
        public IEnumerable<KeyValuePair<string, DynamicVar>> DynamicVars => Read("dynamic_vars", Vars);
        public bool IsUpgraded => Read("is_upgraded", UpgradeLevel > 0);
        public bool IsUpgradable => Read("is_upgradable", UpgradeLevel < MaxLevel);
        public IEnumerable<CardTag> Tags => Read("tags", TagSet);
        // v0.113.1 CardModel: ShouldRetainThisTurn / IsSlyThisTurn = keyword || private flag;
        // BaseReplayCount / ExtraReplayThisTurn are plain getters; GetEnchantedReplayCount =
        // Enchantment?.EnchantPlayCount(BaseReplayCount + ExtraReplayThisTurn) ?? the sum.
        private bool _hasSingleTurnRetain;
        private bool _hasSingleTurnSly;
        public void GiveSingleTurnRetain() => _hasSingleTurnRetain = true;
        public void GiveSingleTurnSly() => _hasSingleTurnSly = true;
        public (bool Retain, bool Sly) PeekSingleTurn() => (_hasSingleTurnRetain, _hasSingleTurnSly);
        public void EndOfTurnCleanup() { _hasSingleTurnRetain = false; _hasSingleTurnSly = false; Extra = 0; }
        public bool ShouldRetainThisTurn => Read("retain_this_turn", LocalKeywordSet.Contains(CardKeyword.Retain) || GlobalKeywordSet.Contains(CardKeyword.Retain) || _hasSingleTurnRetain);
        public bool IsSlyThisTurn => Read("sly_this_turn", LocalKeywordSet.Contains(CardKeyword.Sly) || GlobalKeywordSet.Contains(CardKeyword.Sly) || _hasSingleTurnSly);
        public int BaseReplay;
        public int Extra;
        public int BaseReplayCount => Read("base_replay_count", BaseReplay);
        public int ExtraReplayThisTurn => Read("extra_replay_this_turn", Extra);
        public int GetEnchantedReplayCount()
        {
            Read("enchanted_replay_count", 0);
            return Enchantment?.EnchantPlayCount(BaseReplayCount + ExtraReplayThisTurn) ?? BaseReplayCount + ExtraReplayThisTurn;
        }
        // Boundary double of CardModel.GetKeywordsWithSources: Local is the card's own set,
        // Global the keywords other models add in combat (HexPower's Ethereal on Hexed).
        public HashSet<CardKeyword> LocalKeywordSet = [];
        public HashSet<CardKeyword> GlobalKeywordSet = [];
        public List<KeywordSources> KeywordReads = [];
        public IReadOnlySet<CardKeyword> GetKeywordsWithSources(KeywordSources sources)
        {
            KeywordReads.Add(sources);
            Read("local_ethereal", 0);
            var result = new HashSet<CardKeyword>();
            if (sources.HasFlag(KeywordSources.Local)) result.UnionWith(LocalKeywordSet);
            if (sources.HasFlag(KeywordSources.Global)) result.UnionWith(GlobalKeywordSet);
            return result;
        }
    }
}
namespace MegaCrit.Sts2.Core.Models.Afflictions
{
    public sealed class Hexed : AfflictionModel { }
}
// v0.113.1 MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect and
// MegaCrit.Sts2.Core.Models.Cards.MadScience.TinkerTimeRider (saved per instance).
namespace MegaCrit.Sts2.Core.Models.Events
{
    public sealed class TinkerTime
    {
        public enum RiderEffect { None, Sapping, Violence, Choking, Energized, Wisdom, Chaos, Expertise, Curious, Improvement }
    }
}
namespace MegaCrit.Sts2.Core.Models.Cards
{
    public sealed class MadScience : CardModel
    {
        public Events.TinkerTime.RiderEffect Rider;
        public bool ThrowRider;
        public Events.TinkerTime.RiderEffect TinkerTimeRider => ThrowRider ? throw new InvalidOperationException("rider failed") : Rider;
    }
}
// Mirrors v0.113.1 MegaCrit.Sts2.Core.Models.Enchantments.Momentum: a private int _extraDamage
// that OnPlay grows by Amount. The real class is sealed; the subclasses model a moved or retyped field.
namespace MegaCrit.Sts2.Core.Models.Enchantments
{
    // v0.113.1 Glam.EnchantPlayCount: original + Times while Status is not Disabled
    // (Glam.AfterCardPlayed disables it after the card's first play). Spiral: original + Times.
    public sealed class Glam : EnchantmentModel
    {
        public Glam() { ModelId = new("GLAM"); SetVars(new DynamicVarSet(new DynamicVar(1m) { Name = "Times" })); }
        public override int EnchantPlayCount(int originalPlayCount)
        {
            base.EnchantPlayCount(originalPlayCount);
            return Status == EnchantmentStatus.Disabled ? originalPlayCount : originalPlayCount + (int)DynamicVars["Times"].BaseValue;
        }
    }
    public sealed class Spiral : EnchantmentModel
    {
        public Spiral() { ModelId = new("SPIRAL"); SetVars(new DynamicVarSet(new DynamicVar(1m) { Name = "Times" })); }
        public override int EnchantPlayCount(int originalPlayCount)
        {
            base.EnchantPlayCount(originalPlayCount);
            return originalPlayCount + (int)DynamicVars["Times"].BaseValue;
        }
    }
    public class Momentum : EnchantmentModel
    {
        private int _extraDamage;
        public void SimulateOnPlay() => _extraDamage += Amount;
        public int PeekExtraDamage() => _extraDamage;
    }
    public class MissingMomentumField : Momentum { }
    public class WrongMomentumField : Momentum
    {
#pragma warning disable CS0169
        private long _extraDamage;
#pragma warning restore CS0169
    }
}
namespace MegaCrit.Sts2.Core.Localization.DynamicVars
{
    public class DynamicVar(decimal value)
    {
        public string Name = "Var";
        public decimal Value = value;
        public decimal PreviewValue = 999m;
        public decimal EnchantedValue = 888m;
        public bool ThrowBase;
        public decimal BaseValue => ThrowBase ? throw new InvalidOperationException("base failed") : Value;
    }
    public class StringVar() : DynamicVar(0m);
    public class DynamicVarSet(params DynamicVar[] vars) : IEnumerable<KeyValuePair<string, DynamicVar>>
    {
        private readonly Dictionary<string, DynamicVar> _vars = vars.ToDictionary(v => v.Name);
        public bool ThrowEnumeration;
        public DynamicVar this[string key] => _vars[key];
        public void Add(DynamicVar var) => _vars[var.Name] = var;
        public IEnumerator<KeyValuePair<string, DynamicVar>> GetEnumerator()
            => ThrowEnumeration ? throw new InvalidOperationException("vars failed") : _vars.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    }
}
