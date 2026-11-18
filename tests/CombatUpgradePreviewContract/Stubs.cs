using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

// Boundary doubles that copy the v0.113.1 (sts2-a671e9ef) clone/upgrade mechanics the
// production code depends on: AbstractModel.MutableClone, CardModel.DeepCloneFields /
// AfterCloned / EnchantInternal / AfflictInternal / UpgradeInternal / FinalizeUpgradeInternal /
// GetKeywordsWithSources / UpgradeStarCostBy, CardEnergyCost.GetWithModifiers / UpgradeBy /
// Clone, DynamicVar.Clone / UpgradeValueBy, EnchantmentModel.DeepCloneFields / ApplyInternal,
// AfflictionModel.AfterCloned / Amount. Card-specific OnUpgrade bodies are the ones named in
// each test double. No hooks other than the configured ones run.
namespace MegaCrit.Sts2.Core.Entities.Enchantments
{
    public enum EnchantmentStatus { Normal, Disabled }
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }
    public enum TargetType { None, Self, AnyEnemy, AllEnemies, RandomEnemy, AnyPlayer, AnyAlly, AllAllies, TargetedNoCreature, Osty }
    [Flags]
    public enum UnplayableReason
    {
        None = 0, HasUnplayableKeyword = 2, BlockedByHook = 4, BlockedByCardLogic = 8,
        EnergyCostTooHigh = 0x10, StarCostTooHigh = 0x20, NoLivingAllies = 0x40
    }
    public enum CardTag { None, Strike, Defend, Minion, OstyAttack, Shiv, Wah, Cho, Ki, Shoot }
    public enum CardKeyword { None, Exhaust, Ethereal, Innate, Unplayable, Retain, Sly, Eternal }
    [Flags]
    public enum KeywordSources { None = 0, Local = 2, Global = 4, All = -1 }
    public enum PileType { None, Draw, Hand, Discard, Exhaust, Play, Deck }
    public enum CardUpgradePreviewType { None, Deck, Combat }
    [Flags]
    public enum CostModifiers { None = 0, Local = 2, Global = 4, All = -1 }
    public enum LocalCostType { None, Absolute, Relative }
    [Flags]
    public enum LocalCostModifierExpiration { EndOfCombat = 0, EndOfTurn = 2, WhenPlayed = 4 }

    public class LocalCostModifier(int amount, LocalCostType type, LocalCostModifierExpiration expiration, bool reduceOnly)
    {
        public int Amount { get; set; } = amount;
        public LocalCostType Type { get; } = type;
        public LocalCostModifierExpiration Expiration { get; } = expiration;
        public bool IsReduceOnly { get; } = reduceOnly;
        public int Modify(int cost)
        {
            int result = Type == LocalCostType.Absolute ? Amount : cost + Amount;
            return IsReduceOnly ? Math.Min(cost, result) : result;
        }
        public LocalCostModifier Clone() => new(Amount, Type, Expiration, IsReduceOnly);
    }

    public sealed class TemporaryCardCost(int cost, bool clearsWhenTurnEnds, bool clearsWhenCardIsPlayed)
    {
        public int Cost { get; } = cost;
        public bool ClearsWhenTurnEnds { get; } = clearsWhenTurnEnds;
        public bool ClearsWhenCardIsPlayed { get; } = clearsWhenCardIsPlayed;
    }

    public sealed class CardEnergyCost
    {
        private readonly Models.CardModel _card;
        private int _base;
        private int _capturedXValue;
        private List<LocalCostModifier> _localModifiers = new();
        public int Canonical { get; }
        public bool CostsX { get; }
        public bool WasJustUpgraded { get; private set; }
        public bool ThrowCanonicalOnPreview;
        public bool ThrowAllOnPreview;

        public CardEnergyCost(Models.CardModel card, int canonicalCost, bool costsX)
        {
            _card = card;
            CostsX = costsX;
            Canonical = !CostsX ? canonicalCost : 0;
            _base = Canonical;
        }

        public int CanonicalChecked => Canonical;
        public int CapturedXValue
        {
            get => !CostsX ? throw new InvalidOperationException("Only X-cost cards have a captured value.") : _capturedXValue;
            set => _capturedXValue = value;
        }
        public List<LocalCostModifier> Modifiers => _localModifiers;

        public int GetWithModifiers(CostModifiers modifiers)
        {
            if (ThrowCanonicalOnPreview && _card.IsPreviewClone && modifiers == CostModifiers.None)
                throw new InvalidOperationException("base failed on clone");
            if (ThrowAllOnPreview && _card.IsPreviewClone && modifiers == CostModifiers.All)
                throw new InvalidOperationException("display cost failed on clone");
            int num = _base;
            if (_base < 0 || CostsX) return num;
            if (modifiers.HasFlag(CostModifiers.Local))
                foreach (var modifier in _localModifiers) num = modifier.Modify(num);
            if (modifiers.HasFlag(CostModifiers.Global) && _card.CombatState != null)
                num = _card.OwnerPlayer!.ModifyEnergyCostInCombat(_card, num);
            return Math.Max(0, num);
        }

        public void UpgradeBy(int addend)
        {
            _card.AssertMutable();
            if (CostsX || addend == 0) return;
            int num = _base;
            int num2 = Math.Max(_base + addend, 0);
            WasJustUpgraded = true;
            if (num2 < num)
                foreach (var modifier in _localModifiers)
                    if (modifier.Type == LocalCostType.Absolute && modifier.Amount > num2) modifier.Amount = num2;
            _base = num2;
            _card.InvokeEnergyCostChanged();
        }

        public void FinalizeUpgrade() { _card.AssertMutable(); WasJustUpgraded = false; }

        public CardEnergyCost Clone(Models.CardModel newCard) => new(newCard, newCard.EnergyCost.Canonical, newCard.EnergyCost.CostsX)
        {
            _base = _base,
            _capturedXValue = _capturedXValue,
            WasJustUpgraded = WasJustUpgraded,
            _localModifiers = _localModifiers.Select(m => m.Clone()).ToList(),
            ThrowCanonicalOnPreview = ThrowCanonicalOnPreview,
            ThrowAllOnPreview = ThrowAllOnPreview
        };
    }

    public sealed class CardPile(PileType type)
    {
        public PileType Type { get; } = type;
        public List<Models.CardModel> Cards { get; } = new();
        public bool IsCombatPile => Type is PileType.Draw or PileType.Hand or PileType.Discard or PileType.Exhaust or PileType.Play;
    }

    public sealed class Player
    {
        public List<CardPile> Piles { get; } = new();
        public bool InCombat = true;
        public int RecalculateCardValuesCalls;
        public int EnergyHookCalls;
        public int EnergyHookCallsOnPilelessCards;
        public int KeywordHookCalls;
        /// <summary>FreeAttackPower.TryModifyEnergyCostInCombatLate: free Attacks in Hand/Play only.</summary>
        public bool FreeAttack;
        /// <summary>HexPower.TryModifyKeywordsInCombat: Ethereal on this player's Hexed cards.</summary>
        public bool Hex;
        public CardPile Pile(PileType type)
        {
            var pile = Piles.FirstOrDefault(p => p.Type == type);
            if (pile == null) Piles.Add(pile = new CardPile(type));
            return pile;
        }
        public int ModifyEnergyCostInCombat(Models.CardModel card, int cost)
        {
            EnergyHookCalls++;
            if (card.Pile == null) EnergyHookCallsOnPilelessCards++;
            return FreeAttack && card.Type == CardType.Attack && card.Pile?.Type is PileType.Hand or PileType.Play ? 0 : cost;
        }
        public void ModifyKeywordsInCombat(Models.CardModel card, ISet<CardKeyword> keywords)
        {
            KeywordHookCalls++;
            if (Hex && card.Affliction is Models.Afflictions.Hexed) keywords.Add(CardKeyword.Ethereal);
        }
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);

    public class AbstractModel
    {
        public ModelId ModelId = new("TEST");
        public ModelId Id => ModelId;
        public bool IsMutable { get; private set; } = true;
        public event Action<AbstractModel>? ExecutionFinished;
        public void AssertMutable()
        {
            if (!IsMutable) throw new InvalidOperationException("canonical model");
        }
        public AbstractModel ClonePreservingMutability() => !IsMutable ? this : MutableClone();
        public AbstractModel MutableClone()
        {
            var model = (AbstractModel)MemberwiseClone();
            model.IsMutable = true;
            model.DeepCloneFields();
            model.AfterCloned();
            return model;
        }
        protected virtual void DeepCloneFields() => AssertMutable();
        protected virtual void AfterCloned() => ExecutionFinished = null;
        public void MakeCanonical() => IsMutable = false;
        public void InvokeExecutionFinished() => ExecutionFinished?.Invoke(this);
    }

    public class AfflictionModel : AbstractModel
    {
        private CardModel? _card;
        private int _amount;
        public event Action<int, int>? AmountChanged;
        public CardModel Card
        {
            get { AssertMutable(); return _card!; }
            set
            {
                AssertMutable();
                if (_card != null) throw new InvalidOperationException("moved");
                _card = value;
            }
        }
        public CardModel? AttachedCard => _card;
        public int Amount
        {
            get => _amount;
            set
            {
                AssertMutable();
                if (_amount == value) return;
                int old = _amount;
                _amount = value;
                if (_card != null) _card.OwnerPlayer!.RecalculateCardValuesCalls++;
                AmountChanged?.Invoke(old, _amount);
            }
        }
        protected override void AfterCloned()
        {
            base.AfterCloned();
            AmountChanged = null;
            _card = null;
        }
    }

    public class EnchantmentModel : AbstractModel
    {
        private CardModel? _card;
        private int _amount;
        public EnchantmentStatus StatusValue;
        public EnchantmentStatus Status => StatusValue;
        public event Action? StatusChanged;
        public CardModel? AttachedCard => _card;
        public int Amount { get => _amount; set { AssertMutable(); _amount = value; } }
        public CardModel? Card
        {
            get { AssertMutable(); return _card; }
            set
            {
                AssertMutable();
                if (_card != null) throw new InvalidOperationException("Enchantments cannot be moved from one card to another.");
                _card = value;
            }
        }
        protected override void DeepCloneFields()
        {
            _card = null;
            StatusChanged = null;
        }
        public void ApplyInternal(CardModel card, decimal amount)
        {
            if (Card != null) throw new InvalidOperationException("already applied");
            AssertMutable();
            card.AssertMutable();
            Amount = (int)amount;
            Card = card;
        }
        public void FireStatusChanged() => StatusChanged?.Invoke();
    }

    public class CardModel : AbstractModel
    {
        // Configuration of the double (not game members).
        public string Name = "Card";
        public CardType CardType = CardType.Attack;
        public TargetType Target = TargetType.AnyEnemy;
        public int MaxLevel = 1;
        public int CanonicalCost = 1;
        public bool CostsXValue;
        public int CanonicalStars = -1;
        public bool StarX;
        public HashSet<CardTag> TagSet = new();
        public IEnumerable<CardKeyword> Canonical = Array.Empty<CardKeyword>();
        public Dictionary<string, decimal> CanonicalVarValues = new() { ["Damage"] = 6m };
        /// <summary>Getter names that throw on the upgrade-preview clone only.</summary>
        public HashSet<string> PreviewThrowOn = new();
        public HashSet<string> ThrowOn = new();
        public Player? OwnerPlayer;

        // Game state, copied by MemberwiseClone like the real fields.
        private int _currentUpgradeLevel;
        private HashSet<CardKeyword>? _keywords;
        private HashSet<CardKeyword> _keywordsAddedFromOutside = new();
        private HashSet<CardKeyword> _keywordsRemovedFromOutside = new();
        private DynamicVarSet? _dynamicVars;
        private CardEnergyCost? _energyCost;
        private List<TemporaryCardCost> _temporaryStarCosts = new();
        private int _baseStarCost;
        private bool _starCostSet;
        private bool _hasSingleTurnRetain;
        private bool _isUpgrading;
        private CardUpgradePreviewType _upgradePreviewType;
        public bool ExhaustOnNextPlay;

        public event Action? AfflictionChanged;
        public event Action? EnchantmentChanged;
        public event Action? EnergyCostChanged;
        public event Action? KeywordsChanged;
        public event Action? ReplayCountChanged;
        public event Action? Played;
        public event Action? Drawn;
        public event Action? StarCostChanged;
        public event Action? Upgraded;
        public event Action? Forged;

        public EnchantmentModel? Enchantment { get; private set; }
        public AfflictionModel? Affliction { get; private set; }

        public bool IsPreviewClone => _upgradePreviewType == CardUpgradePreviewType.Combat;
        private T Read<T>(string field, T value)
            => ThrowOn.Contains(field) || IsPreviewClone && PreviewThrowOn.Contains(field)
                ? throw new InvalidOperationException($"{field} failed") : value;

        public CardPile? Pile => OwnerPlayer?.Piles.FirstOrDefault(p => p.Cards.Contains(this));
        public object? CombatState => OwnerPlayer is { InCombat: true }
            && (Pile is { IsCombatPile: true } || _upgradePreviewType == CardUpgradePreviewType.Combat) ? OwnerPlayer : null;

        public virtual string Title => Read("title", IsUpgraded ? Name + "+" : Name);
        public CardType Type => Read("type", CardType);
        public TargetType TargetType => Read("target_type", Target);
        public IEnumerable<CardTag> Tags => Read("tags", TagSet);
        public virtual int MaxUpgradeLevel => Read("max_upgrade_level", MaxLevel);
        public int CurrentUpgradeLevel
        {
            get => Read("upgrade_level", _currentUpgradeLevel);
            private set
            {
                AssertMutable();
                if (value > MaxUpgradeLevel) throw new InvalidOperationException("cannot be upgraded past its MaxUpgradeLevel.");
                _currentUpgradeLevel = value;
            }
        }
        public bool IsUpgraded => Read("is_upgraded", CurrentUpgradeLevel > 0);
        public bool IsUpgradable => Read("is_upgradable", CurrentUpgradeLevel < MaxUpgradeLevel);
        public CardUpgradePreviewType UpgradePreviewType
        {
            get => _upgradePreviewType;
            set { AssertMutable(); _upgradePreviewType = value; }
        }

        public DynamicVarSet DynamicVars => Read("dynamic_vars", _dynamicVars ??= new DynamicVarSet(
            CanonicalVarValues.Select(entry => entry.Key == "Text" ? new StringVar(entry.Key) : new DynamicVar(entry.Key, entry.Value))));
        public CardEnergyCost EnergyCost => _energyCost ??= new CardEnergyCost(this, CanonicalCost, CostsXValue);

        private HashSet<CardKeyword> LocalKeywords => _keywords ??= new HashSet<CardKeyword>(Canonical);
        public IReadOnlySet<CardKeyword> GetKeywordsWithSources(KeywordSources sources)
        {
            bool local = sources.HasFlag(KeywordSources.Local);
            if (!sources.HasFlag(KeywordSources.Global) || !IsMutable || CombatState == null)
                return local ? LocalKeywords : new HashSet<CardKeyword>();
            var set = local ? new HashSet<CardKeyword>(LocalKeywords) : new HashSet<CardKeyword>();
            OwnerPlayer!.ModifyKeywordsInCombat(this, set);
            return set;
        }
        public IReadOnlySet<CardKeyword> Keywords => GetKeywordsWithSources(KeywordSources.All);
        public bool ShouldRetainThisTurn => Keywords.Contains(CardKeyword.Retain) || _hasSingleTurnRetain;
        public void GiveSingleTurnRetain() => _hasSingleTurnRetain = true;

        public int CanonicalStarCost => CanonicalStars;
        public bool HasStarCostX => StarX;
        public int BaseStarCost
        {
            get
            {
                if (!IsMutable) return CanonicalStarCost;
                if (!_starCostSet) { _baseStarCost = CanonicalStarCost; _starCostSet = true; }
                return _baseStarCost;
            }
            private set
            {
                AssertMutable();
                if (!HasStarCostX) { _baseStarCost = value; _starCostSet = true; }
                StarCostChanged?.Invoke();
            }
        }
        public int CurrentStarCost
        {
            get
            {
                int? cost = _temporaryStarCosts.LastOrDefault()?.Cost;
                if (cost.HasValue) return cost == 0 && BaseStarCost < 0 ? BaseStarCost : cost.Value;
                return BaseStarCost;
            }
        }
        public void AddTemporaryStarCost(TemporaryCardCost cost) => _temporaryStarCosts.Add(cost);
        public IReadOnlyList<TemporaryCardCost> TemporaryStarCosts => _temporaryStarCosts;
        protected void UpgradeStarCostBy(int addend)
        {
            if (HasStarCostX) throw new InvalidOperationException("star X");
            if (addend == 0) return;
            int old = BaseStarCost;
            BaseStarCost += addend;
            if (BaseStarCost < old) _temporaryStarCosts.RemoveAll(c => c.Cost > BaseStarCost);
        }

        public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer)
        {
            reason = UnplayableReason.None;
            preventer = null;
            return true;
        }

        public string GetDescriptionForPile(PileType pileType)
            => Read("description", $"{Title} Damage {(DynamicVars.TryGet("Damage", out var damage) ? damage.BaseValue : 0)} at {pileType}");

        public void AddKeyword(CardKeyword keyword)
        {
            AssertMutable();
            LocalKeywords.Add(keyword);
            if (!_isUpgrading) { _keywordsAddedFromOutside.Add(keyword); _keywordsRemovedFromOutside.Remove(keyword); }
            KeywordsChanged?.Invoke();
        }
        public void RemoveKeyword(CardKeyword keyword)
        {
            AssertMutable();
            LocalKeywords.Remove(keyword);
            if (!_isUpgrading) { _keywordsRemovedFromOutside.Add(keyword); _keywordsAddedFromOutside.Remove(keyword); }
            KeywordsChanged?.Invoke();
        }
        public void InvokeEnergyCostChanged() => EnergyCostChanged?.Invoke();

        public void EnchantInternal(EnchantmentModel enchantment, decimal amount)
        {
            AssertMutable();
            enchantment.AssertMutable();
            Enchantment = enchantment;
            Enchantment.ApplyInternal(this, amount);
            EnchantmentChanged?.Invoke();
        }
        public void AfflictInternal(AfflictionModel affliction, decimal amount)
        {
            AssertMutable();
            affliction.AssertMutable();
            if (Affliction != null) throw new InvalidOperationException("already afflicted");
            Affliction = affliction;
            Affliction.Card = this;
            Affliction.Amount = (int)amount;
            AfflictionChanged?.Invoke();
        }

        protected override void DeepCloneFields()
        {
            _keywords = new HashSet<CardKeyword>(GetKeywordsWithSources(KeywordSources.Local));
            _keywordsAddedFromOutside = new HashSet<CardKeyword>(_keywordsAddedFromOutside);
            _keywordsRemovedFromOutside = new HashSet<CardKeyword>(_keywordsRemovedFromOutside);
            _dynamicVars = DynamicVars.Clone();
            _energyCost = _energyCost?.Clone(this);
            _temporaryStarCosts = _temporaryStarCosts.ToList();
            if (Enchantment != null)
            {
                var enchantment = (EnchantmentModel)Enchantment.ClonePreservingMutability();
                Enchantment = null;
                EnchantInternal(enchantment, enchantment.Amount);
            }
            if (Affliction != null)
            {
                var affliction = (AfflictionModel)Affliction.ClonePreservingMutability();
                Affliction = null;
                AfflictInternal(affliction, affliction.Amount);
            }
        }

        protected override void AfterCloned()
        {
            base.AfterCloned();
            _isUpgrading = false;
            AfflictionChanged = null;
            Drawn = null;
            EnchantmentChanged = null;
            EnergyCostChanged = null;
            Forged = null;
            KeywordsChanged = null;
            Played = null;
            ReplayCountChanged = null;
            StarCostChanged = null;
            Upgraded = null;
        }

        protected virtual void OnUpgrade() => DynamicVars["Damage"].UpgradeValueBy(3m);

        public void UpgradeInternal()
        {
            AssertMutable();
            CurrentUpgradeLevel++;
            _isUpgrading = true;
            try { OnUpgrade(); }
            finally { _isUpgrading = false; }
            Upgraded?.Invoke();
        }

        public void FinalizeUpgradeInternal()
        {
            DynamicVars.FinalizeUpgrade();
            EnergyCost.FinalizeUpgrade();
        }

        public CardModel ToMutable() => (CardModel)MutableClone();

        public void Enchant(EnchantmentModel enchantment, int amount) => EnchantInternal(enchantment, amount);
        public void Afflict(AfflictionModel affliction, int amount) => AfflictInternal(affliction, amount);
        public void SubscribeAll(Action count)
        {
            AfflictionChanged += count; EnchantmentChanged += count; EnergyCostChanged += count;
            KeywordsChanged += count; ReplayCountChanged += count; Played += count; Drawn += count;
            StarCostChanged += count; Upgraded += count; Forged += count;
            ExecutionFinished += _ => count();
        }
        public void FireAll()
        {
            AfflictionChanged?.Invoke(); EnchantmentChanged?.Invoke(); EnergyCostChanged?.Invoke();
            KeywordsChanged?.Invoke(); ReplayCountChanged?.Invoke(); Played?.Invoke(); Drawn?.Invoke();
            StarCostChanged?.Invoke(); Upgraded?.Invoke(); Forged?.Invoke(); InvokeExecutionFinished();
        }
        public int UpgradeLevelField => _currentUpgradeLevel;
    }

    /// <summary>DingyDagger: MaxUpgradeLevel 999999999, OnUpgrade Damage += 3 + new level.</summary>
    public class DingyDaggerDouble : CardModel
    {
        public DingyDaggerDouble() { Name = "Dingy Dagger"; MaxLevel = 999999999; CanonicalCost = 0; CanonicalVarValues = new() { ["Damage"] = 7m }; }
        public override string Title => CurrentUpgradeLevel > 0 ? $"{Name}+{CurrentUpgradeLevel}" : Name;
        protected override void OnUpgrade() => DynamicVars["Damage"].UpgradeValueBy(3 + CurrentUpgradeLevel);
    }

    /// <summary>A card whose OnUpgrade lowers the energy cost (CardEnergyCost.UpgradeBy).</summary>
    public class CostUpgradeDouble : CardModel
    {
        public int Addend = -1;
        protected override void OnUpgrade() => EnergyCost.UpgradeBy(Addend);
    }

    /// <summary>Apparition.OnUpgrade: RemoveKeyword(Ethereal).</summary>
    public class ApparitionDouble : CardModel
    {
        public ApparitionDouble() { Name = "Apparition"; CardType = CardType.Skill; Canonical = new[] { CardKeyword.Ethereal, CardKeyword.Exhaust }; CanonicalVarValues = new(); }
        protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Ethereal);
    }

    /// <summary>A star-cost card whose OnUpgrade calls UpgradeStarCostBy(-1).</summary>
    public class StarUpgradeDouble : CardModel
    {
        public StarUpgradeDouble() { Name = "Star"; CanonicalStars = 2; }
        protected override void OnUpgrade() => UpgradeStarCostBy(-1);
    }

    /// <summary>An OnUpgrade that changes the cap (not in the game): exercises the consistency check.</summary>
    public class CapChangingDouble : CardModel
    {
        public bool Grown;
        public override int MaxUpgradeLevel => Grown ? 3 : 1;
        protected override void OnUpgrade() => Grown = true;
    }

    /// <summary>An event with custom accessors has no same-named delegate field to clear.</summary>
    public class CustomEventDouble : CardModel
    {
        public event Action? Custom { add { } remove { } }
    }
}

// Hexed (MegaCrit.Sts2.Core.Models.Afflictions): what HexPower.TryModifyKeywordsInCombat checks.
namespace MegaCrit.Sts2.Core.Models.Afflictions
{
    public class Hexed : Models.AfflictionModel
    {
        public Hexed() => ModelId = new("HEXED");
    }
}

namespace MegaCrit.Sts2.Core.Models.Enchantments
{
    public class Momentum : Models.EnchantmentModel
    {
        private int _extraDamage;
        public void SimulateOnPlay() => _extraDamage += Amount;
        public int PeekExtraDamage() => _extraDamage;
    }
}

namespace MegaCrit.Sts2.Core.Localization.DynamicVars
{
    public class DynamicVar(string name, decimal baseValue)
    {
        private decimal _baseValue = baseValue;
        public string Name { get; } = name;
        public decimal PreviewValue { get; set; } = baseValue;
        public bool WasJustUpgraded { get; private set; }
        public decimal BaseValue
        {
            get => _baseValue;
            set { _baseValue = Math.Min(value, 999999999m); PreviewValue = _baseValue; }
        }
        public void UpgradeValueBy(decimal addend) { BaseValue += addend; WasJustUpgraded = true; }
        public void FinalizeUpgrade() => WasJustUpgraded = false;
        public DynamicVar Clone()
        {
            var clone = (DynamicVar)MemberwiseClone();
            clone.PreviewValue = clone.BaseValue;
            return clone;
        }
    }
    public class StringVar(string name) : DynamicVar(name, 0m);

    public class DynamicVarSet(IEnumerable<DynamicVar> vars) : IEnumerable<KeyValuePair<string, DynamicVar>>
    {
        private readonly Dictionary<string, DynamicVar> _vars = vars.ToDictionary(v => v.Name);
        public DynamicVar this[string name] => _vars[name];
        public bool TryGet(string name, out DynamicVar value) => _vars.TryGetValue(name, out value!);
        public DynamicVarSet Clone() => new(_vars.Values.Select(v => v.Clone()));
        public void FinalizeUpgrade() { foreach (var v in _vars.Values) v.FinalizeUpgrade(); }
        public IEnumerator<KeyValuePair<string, DynamicVar>> GetEnumerator() => _vars.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
        internal static string StripRichTextTags(string text) => text;
        // StateBuilder's display helpers: CostsX ? "X" : GetAmountToSpend (GetWithModifiers(All)).
        private static string GetCostDisplay(MegaCrit.Sts2.Core.Models.CardModel card)
            => card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetWithModifiers(CostModifiers.All).ToString();
        private static string? GetStarCostDisplay(MegaCrit.Sts2.Core.Models.CardModel card)
            => card.HasStarCostX ? "X" : card.CurrentStarCost >= 0 ? card.CurrentStarCost.ToString() : null;
    }
}
