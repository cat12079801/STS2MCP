// Boundary doubles: fields with the v0.113.1 (sts2-a671e9ef) names and types only; no game logic.
// The generated allowlist resolves types by full name in this assembly (typeof(CardModel).Assembly).
// The private fields are read only through reflection, so the compiler sees them as unused.
#pragma warning disable CS0169, CS0414
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { IReadOnlyList<Entities.Players.Player> Players { get; } }
    public class CombatState : ICombatState
    {
        public IReadOnlyList<Entities.Players.Player> Players { get; set; } = [];
        private List<Entities.Creatures.Creature> _allies = [];
        private List<Entities.Creatures.Creature> _escapedCreatures = [];
        public IReadOnlyList<Entities.Creatures.Creature> Allies => _allies;
        public void SetAllies(params Entities.Creatures.Creature[] c) => _allies = [.. c];
        public void SetEscaped(params Entities.Creatures.Creature[] c) => _escapedCreatures = [.. c];
    }
    public class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        public bool IsInProgress { get; set; }
        public CombatState? State { get; set; }
        public CombatState? DebugOnlyGetState() => State;
    }
    // The game's other ICombatState: no allowlist entry targets it.
    public class NullCombatState : ICombatState { public IReadOnlyList<Entities.Players.Player> Players { get; } = []; }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { None, Draw, Hand, Discard, Exhaust, Play, Deck }
    // v0.113.1 CardKeyword (the whole enum, same order).
    public enum CardKeyword { None, Exhaust, Ethereal, Innate, Unplayable, Retain, Sly, Eternal }
    public class CardPile { public List<Models.CardModel> Cards = []; }
    public class CardPlay { public required Models.CardModel Card { get; init; } }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public uint? CombatId { get; set; }
        public Combat.ICombatState? CombatState { get; set; }
        public Players.Player? Player { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Entities.Relics
{
    public enum RelicStatus { Normal, Active, Disabled }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class PlayerCombatState
    {
        public List<Models.CardModel> Hand = [];
        public IEnumerable<Models.CardModel> AllCards => Hand;
    }
    public class Player
    {
        public List<Models.RelicModel> Relics { get; } = [];
        public ulong NetId { get; set; }
        public Cards.CardPile Deck = new();
        public PlayerCombatState? PlayerCombatState { get; set; }
        public Creatures.Creature Creature { get; }
        public ExtraPlayerFields ExtraFields { get; set; } = new();
        public Player(Creatures.Creature? creature = null) => Creature = creature!;
    }
    // v0.113.1: auto-properties { get; set; }.
    public class ExtraPlayerFields
    {
        public int CardShopRemovalsUsed { get; set; }
        public int WongoPoints { get; set; }
        // 手書きの getter（backing field が無い）。自動実装プロパティとして読もうとすると null と理由になる。
        public int Computed => CardShopRemovalsUsed + 1;
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public interface IRunState { string CardMultiplayerConstraint { get; } }
    public class RunState : IRunState
    {
        public int CurrentActIndex { get; set; }
        public int TotalFloor { get; set; }
        public int AscensionLevel { get; set; }
        public string CardMultiplayerConstraint => "SingleplayerOnly";
        public List<Entities.Players.Player> Players { get; } = [];
        public IReadOnlyList<Models.ModifierModel>? Modifiers { get; set; } = [];
    }
    public class RunManager
    {
        public static RunManager Instance { get; } = new();
        public RunState? State { get; set; }
        public bool IsInProgress => State != null;
        public RunState? DebugOnlyGetState() => State;
    }
}
namespace MegaCrit.Sts2.Core.Saves.Runs
{
    public class SerializableEnchantment { public Models.ModelId? Id { get; set; } }
    public class SerializableCard
    {
        public Models.ModelId? Id { get; set; }
        public int CurrentUpgradeLevel { get; set; }
        public SerializableEnchantment? Enchantment { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public record ModelId(string Entry);
    public class ModifierModel(string id) { public ModelId Id { get; } = new(id); }
    public class CardModel(string id) : IComparable<CardModel>
    {
        public ModelId Id { get; } = new(id);
        // v0.113.1: DeckVersion is a property over the private _deckVersion (CardModel.cs:124, :1010). The allowlist
        // reads the field; CardIdentity reads the property.
        private CardModel? _deckVersion;
        public CardModel? DeckVersion { get => _deckVersion; set => _deckVersion = value; }
        public int Rarity { get; set; }
        public Entities.Players.Player? Owner { get; set; }
        public int CompareTo(CardModel? other) => StringComparer.Ordinal.Compare(Id.Entry, other?.Id.Entry);
        // v0.113.1 CardModel private fields read by the allowlist.
        private CardModel? _cloneOf;
        private int _currentPlayIndex;
        private Entities.Creatures.Creature? _currentTarget;
        private int? _floorAddedToDeck;
        private bool _isDupe;
        private HashSet<Entities.Cards.CardKeyword> _keywordsAddedFromOutside = [];
        private HashSet<Entities.Cards.CardKeyword> _keywordsRemovedFromOutside = [];
        private CardModel? _transformIntoAfterPlay;
        public void Set(CardModel? cloneOf = null, int playIndex = 0, Entities.Creatures.Creature? target = null,
            int? floor = null, bool dupe = false, CardModel? transform = null)
        {
            _cloneOf = cloneOf; _currentPlayIndex = playIndex; _currentTarget = target;
            _floorAddedToDeck = floor; _isDupe = dupe; _transformIntoAfterPlay = transform;
        }
        public HashSet<Entities.Cards.CardKeyword> Added => _keywordsAddedFromOutside;
        public HashSet<Entities.Cards.CardKeyword> Removed => _keywordsRemovedFromOutside;
    }
    public class PowerModel(string id)
    {
        public ModelId Id { get; } = new(id);
        public Entities.Creatures.Creature Owner { get; set; } = new();
        private object? _internalData;
        private int _amountOnTurnStart;
        // v0.113.1 PowerModel._applier / _target (Creature?). The public getters exist in the game; the allowlist
        // reads the fields, so the double has no getter and a getter call could not compile.
        private Entities.Creatures.Creature? _applier;
        private Entities.Creatures.Creature? _target;
        public void SetData(object? data) => _internalData = data;
        public void SetAmountOnTurnStart(int value) => _amountOnTurnStart = value;
        public void SetApplier(Entities.Creatures.Creature? value) => _applier = value;
        public void SetTarget(Entities.Creatures.Creature? value) => _target = value;
    }
    public class RelicModel
    {
        public bool IsMelted { get; set; }
        private Entities.Relics.RelicStatus _status;
        private bool _isWax;
        private int _floorAddedToDeck;
        public void SetBase(Entities.Relics.RelicStatus status, bool wax, int floor)
        { _status = status; _isWax = wax; _floorAddedToDeck = floor; }
    }
}
namespace MegaCrit.Sts2.Core.Models.Cards
{
    public sealed class Rampage() : CardModel("RAMPAGE") { private decimal _extraDamageFromPlays; public void SetExtra(decimal v) => _extraDamageFromPlays = v; }
    public sealed class Thrash() : CardModel("THRASH") { private decimal _extraDamage; }
    public sealed class TheBall() : CardModel("THE_BALL") { private decimal _extraDamageFromPlays; }
    public sealed class Maul() : CardModel("MAUL") { private decimal _extraDamageFromMaulPlays; }
    public sealed class SpoilsMap() : CardModel("SPOILS_MAP") { private int _spoilsActIndex = -1; }
    public sealed class Regret() : CardModel("REGRET") { private int _cardsInHand; public void SetCardsInHand(int v) => _cardsInHand = v; }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    public sealed class InterceptPower() : PowerModel("INTERCEPT_POWER")
    {
        private class Data { public readonly List<Entities.Creatures.Creature> coveredCreatures = []; }
        public object NewData(params Entities.Creatures.Creature[] covered)
        {
            var data = new Data();
            data.coveredCreatures.AddRange(covered);
            return data;
        }
    }
    public sealed class StrengthPower() : PowerModel("STRENGTH_POWER") { }
    public sealed class HexPower() : PowerModel("HEX_POWER") { }
    public sealed class ThieveryPower() : PowerModel("THIEVERY_POWER") { }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class GoldenCompass : RelicModel { private int _goldenPathAct = -1; public void SetAct(int v) => _goldenPathAct = v; }
    public sealed class HappyFlower : RelicModel { private bool _grantEnergyThisTurn; public void Schedule() => _grantEnergyThisTurn = true; }
    public sealed class FakeHappyFlower : RelicModel { private bool _grantEnergyThisTurn; public void Schedule() => _grantEnergyThisTurn = true; }
    public sealed class MusicBox : RelicModel { private CardModel? _cardBeingPlayed; }
    public sealed class PaelsLegion : RelicModel { private Entities.Cards.CardPlay? _affectedCardPlay; public void Set(Entities.Cards.CardPlay? p) => _affectedCardPlay = p; }
    public sealed class PaelsTooth : RelicModel
    {
        private List<Saves.Runs.SerializableCard> _serializableCards = [];
        public List<Saves.Runs.SerializableCard> Cards => _serializableCards;
        public void SetUnreadable() => _serializableCards = null!;
    }
    public sealed class PenNib : RelicModel { private CardModel? _attackToDouble; public void Set(CardModel? c) => _attackToDouble = c; }
    public sealed class UnsettlingLamp : RelicModel
    {
        private CardModel? _triggeringCard;
        private List<PowerModel>? _doubledPowers;
        public void Set(CardModel? card, List<PowerModel>? powers) { _triggeringCard = card; _doubledPowers = powers; }
    }
    public sealed class Vambrace : RelicModel { private CardModel? _triggeringCard; }
    // A relic with no allowlist entry of its own (only the RelicModel base entries apply).
    public sealed class Anchor : RelicModel { }
    // Same simple name as a relic in another namespace must not match.
    public sealed class Byrdpip : RelicModel { }
}
namespace MegaCrit.Sts2.Core.Models.Monsters
{
    // The monster PaelsLegion shares the relic's simple name; the allowlist resolves by full name.
    public sealed class PaelsLegion { private Entities.Cards.CardPlay? _affectedCardPlay; }
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static string? SafeGetText(Func<string?> read) { try { return read(); } catch { return null; } }
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    }
}
