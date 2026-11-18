// Boundary doubles: fields/getters only; no simulated game calculations.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { IReadOnlyList<Models.Player> Players { get; } }
    public class CombatState : ICombatState { public IReadOnlyList<Models.Player> Players { get; set; } = []; }
}
namespace MegaCrit.Sts2.Core.Commands.Builders
{
    // v0.113.1: Attacker is an auto-property { get; private set; }. The throwing getter
    // only exercises the reader's failure path.
    public class AttackCommand
    {
        public bool ThrowAttacker;
        private Entities.Creatures.Creature? _attacker;
        public Entities.Creatures.Creature? Attacker
        {
            get => ThrowAttacker ? throw new InvalidOperationException("attacker failed") : _attacker;
            set => _attacker = value;
        }
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
    public class CardPile { public List<Models.CardModel> Cards = []; }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    // v0.113.1: CombatId is an auto-property { get; set; } (uint?), the same value as
    // enemies[].combat_id.
    public class Creature
    {
        public MegaCrit.Sts2.Core.Models.Player? Player { get; set; }
        public uint? CombatId { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public record ModelId(string Entry);
    public class CardModel(string id) : IComparable<CardModel>
    {
        public ModelId Id { get; } = new(id);
        public CardModel? DeckVersion;
        public int Rarity { get; set; }
        public int CompareTo(CardModel? other) => StringComparer.Ordinal.Compare(Id.Entry, other?.Id.Entry);
    }
    public class PowerModel
    {
        private object? _internalData;
        // v0.113.1 PowerModel.DynamicVars: built by DeepCloneFields on every live power.
        public DynamicVars DynamicVars { get; } = new();
        public void SetData(object? data) => _internalData = data;
        public object? PeekData() => _internalData;
        public bool Skip;
        public bool ThrowSkip;
        public bool SkipNextDurationTick => ThrowSkip ? throw new InvalidOperationException("skip failed") : Skip;
        public Entities.Creatures.Creature? Target { get; set; }
        // Real getter: returns _applier only (AssertMutable is on the setter); the throw path is defensive.
        public bool ThrowApplier;
        public int ApplierReads;
        private Entities.Creatures.Creature? _applier;
        public Entities.Creatures.Creature? Applier
        {
            get { ApplierReads++; return ThrowApplier ? throw new InvalidOperationException("applier failed") : _applier; }
            set => _applier = value;
        }
        public Entities.Creatures.Creature? PeekApplier() => _applier;
        // Real getter: AssertMutable() then _owner (throws only on a canonical model).
        public bool ThrowOwner;
        private Entities.Creatures.Creature? _owner;
        public Entities.Creatures.Creature? Owner
        {
            get => ThrowOwner ? throw new InvalidOperationException("canonical owner") : _owner;
            set => _owner = value;
        }
    }
    public class RelicModel
    {
        public ModelId Id => new(GetType().Name);
        public Player Owner { get; set; } = new();
    }
    public class Player
    {
        public ulong NetId { get; set; }
        public RunState RunState { get; set; } = new();
        public Entities.Cards.CardPile Deck = new();
    }
    public class RunState { public MapPoint? CurrentMapPoint { get; set; } public int CurrentActIndex { get; set; } }
    public class MapPoint { public Coord coord; }
    public struct Coord { public int col; public int row; }
    public class DynamicVar
    {
        public decimal Value;
        public bool ThrowBase;
        public decimal BaseValue => ThrowBase ? throw new InvalidOperationException("base failed") : Value;
        public int IntValue => (int)BaseValue;
    }
    public class DynamicVars
    {
        public DynamicVar Block { get; set; } = new();
        public DynamicVar Gold { get; set; } = new();
        // DynamicVarSet.Damage is _vars["Damage"]; the indexer throws KeyNotFoundException.
        public Dictionary<string, DynamicVar> Vars = new();
        public DynamicVar Damage => Vars["Damage"];
        public DynamicVar this[string key] => Vars[key];
    }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    using MegaCrit.Sts2.Core.Entities.Creatures;
    public class ToricToughnessPower : PowerModel { }
    // v0.113.1: Instanced, per-target. Gold starts at GoldVar(0); Steal adds the real loss.
    public class ThieveryPower : PowerModel { }
    public class HeistPower : PowerModel { }
    // v0.113.1 (sts2#1019): TheBombPower (Instanced) keeps SetDamage's value in DynamicVars.Damage;
    // InfernoPower / CrimsonMantlePower keep DynamicVars["SelfDamage"]; PanachePower (Instanced)
    // keeps DynamicVars["CardsLeft"] and a private Data { bool alreadyApplied }.
    public class TheBombPower : PowerModel
    {
        public TheBombPower() => DynamicVars.Vars["Damage"] = new() { Value = 40m };
        public void SetDamage(decimal damage) => DynamicVars.Vars["Damage"].Value = damage;
    }
    public class InfernoPower : PowerModel
    {
        public InfernoPower() => DynamicVars.Vars["SelfDamage"] = new() { Value = 0m };
        public void IncrementSelfDamage() => DynamicVars.Vars["SelfDamage"].Value++;
    }
    public class CrimsonMantlePower : PowerModel
    {
        public CrimsonMantlePower() => DynamicVars.Vars["SelfDamage"] = new() { Value = 0m };
        public void IncrementSelfDamage() => DynamicVars.Vars["SelfDamage"].Value++;
    }
    public class PanachePower : PowerModel
    {
        private class Data { public bool alreadyApplied; }
        public PanachePower() { SetData(new Data()); DynamicVars.Vars["CardsLeft"] = new() { Value = 5m }; }
        public void Applied() => ((Data)PeekData()!).alreadyApplied = true;
        public bool PeekApplied() => ((Data)PeekData()!).alreadyApplied;
    }
    public class MissingPanacheDataPower : PanachePower { public MissingPanacheDataPower() => SetData(null); }
    // v0.113.1: sealed, Single; AfterDeath removes it when the dying creature is Applier.
    public class HexPower : PowerModel { }
    // v0.113.1 RitualPower: private bool _wasJustAppliedByEnemy（AfterApplied で true、AfterSideTurnEnd で false）。
    public class RitualPower : PowerModel
    {
        private bool _wasJustAppliedByEnemy;
        public void Set(bool value) => _wasJustAppliedByEnemy = value;
        public bool Peek() => _wasJustAppliedByEnemy;
    }
    public class SurroundedPower : PowerModel
    {
        public enum Direction { Right, Left }
        public Direction Value;
        public bool ThrowFacing;
        public Direction Facing => ThrowFacing ? throw new InvalidOperationException("facing failed") : Value;
    }
    public class PossessStrengthPower : PowerModel
    {
        private class Data { public Dictionary<Creature, decimal> stolenStrength = new(); }
        public PossessStrengthPower() => SetData(new Data());
        public void Set(Dictionary<Creature, decimal> value) => ((Data)PeekData()!).stolenStrength = value;
    }
    public class PossessSpeedPower : PowerModel
    {
        private class Data { public Dictionary<Creature, decimal> stolenDexterity = new(); }
        public PossessSpeedPower() => SetData(new Data());
        public void Set(Dictionary<Creature, decimal> value) => ((Data)PeekData()!).stolenDexterity = value;
    }
    public class SkittishPower : PowerModel
    {
        private class Data { public bool hasGainedBlockThisTurn = false; }
        public SkittishPower() => SetData(new Data());
        public void Set(bool value) => ((Data)PeekData()!).hasGainedBlockThisTurn = value;
    }
    public class JugglingPower : PowerModel
    {
        private class Data { public int attacksPlayedThisTurn = 0; public CardModel? cardToClone; }
        public JugglingPower() => SetData(new Data());
        public void Set(int value) => ((Data)PeekData()!).attacksPlayedThisTurn = value;
        public void SetPending(CardModel? card) => ((Data)PeekData()!).cardToClone = card;
        public CardModel? PeekPending() => ((Data)PeekData()!).cardToClone;
    }
    public class ChainsOfBindingPower : PowerModel
    {
        private class Data { public bool boundCardPlayed = false; }
        public ChainsOfBindingPower() => SetData(new Data());
        public void Set(bool value) => ((Data)PeekData()!).boundCardPlayed = value;
        public bool Peek() => ((Data)PeekData()!).boundCardPlayed;
    }
    public class MissingBoundDataPower : ChainsOfBindingPower { }
    public class MissingBoundFieldPower : ChainsOfBindingPower
    {
        private class Data { }
        public MissingBoundFieldPower() => SetData(new Data());
    }
    public class WrongBoundFieldPower : ChainsOfBindingPower
    {
        private class Data { public int boundCardPlayed = 1; }
        public WrongBoundFieldPower() => SetData(new Data());
    }
    public class CurlUpPower : PowerModel
    {
        private class Data { public CardModel? playedCard; }
        public CurlUpPower() => SetData(new Data());
        public void SetPlayed(CardModel? card) => ((Data)PeekData()!).playedCard = card;
        public CardModel? PeekPlayed() => ((Data)PeekData()!).playedCard;
    }
    public class MissingCurlUpDataPower : CurlUpPower { }
    public class MissingPlayedCardFieldPower : CurlUpPower
    {
        private class Data { }
        public MissingPlayedCardFieldPower() => SetData(new Data());
    }
    public class WrongPlayedCardFieldPower : CurlUpPower
    {
        private class Data { public string playedCard = "wrong"; }
        public WrongPlayedCardFieldPower() => SetData(new Data());
    }
    // v0.113.1 VigorPower.Data: AttackCommand? commandToModify; int amountWhenAttackStarted.
    public class VigorPower : PowerModel
    {
        private class Data { public Commands.Builders.AttackCommand? commandToModify; public int amountWhenAttackStarted; }
        public VigorPower() => SetData(new Data());
        public void Bind(Commands.Builders.AttackCommand? command, int started)
        {
            var data = (Data)PeekData()!;
            data.commandToModify = command;
            data.amountWhenAttackStarted = started;
        }
        public (Commands.Builders.AttackCommand?, int) Peek()
        {
            var data = (Data)PeekData()!;
            return (data.commandToModify, data.amountWhenAttackStarted);
        }
    }
    public class MissingVigorDataPower : VigorPower { }
    public class MissingVigorCommandFieldPower : VigorPower
    {
        private class Data { public int amountWhenAttackStarted = 4; }
        public MissingVigorCommandFieldPower() => SetData(new Data());
    }
    public class WrongVigorCommandFieldPower : VigorPower
    {
        private class Data { public object? commandToModify = new(); public int amountWhenAttackStarted = 4; }
        public WrongVigorCommandFieldPower() => SetData(new Data());
    }
    public class WrongVigorAmountFieldPower : VigorPower
    {
        private class Data { public Commands.Builders.AttackCommand? commandToModify = null; public long amountWhenAttackStarted = 4; }
        public WrongVigorAmountFieldPower() => SetData(new Data());
    }
    public class MissingDataPower : JugglingPower { }
    public class MissingCloneFieldPower : JugglingPower
    {
        private class Data { public int attacksPlayedThisTurn = 3; }
        public MissingCloneFieldPower() => SetData(new Data());
    }
    public class WrongCloneFieldPower : JugglingPower
    {
        private class Data { public int attacksPlayedThisTurn = 3; public string cardToClone = "wrong"; }
        public WrongCloneFieldPower() => SetData(new Data());
    }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public class CentennialPuzzle : RelicModel { public bool UsedThisCombat { get; set; } }
    public class LavaLamp : RelicModel { public bool TookDamageThisCombat { get; set; } }
    public class PenNib : RelicModel
    {
        private int _observedAttacks;
        public bool ThrowAttacks;
        public int AttacksPlayed => ThrowAttacks ? throw new InvalidOperationException("attacks getter failed") : _observedAttacks;
        public int Peek() => _observedAttacks;
        public void Set(int n) => _observedAttacks = n;
    }
    public class BrilliantScarf : RelicModel
    {
        private int _cardsPlayedThisTurn;
        public bool ShowCounter => _cardsPlayedThisTurn < 6;
        public void Set(int value) => _cardsPlayedThisTurn = value;
        public int Peek() => _cardsPlayedThisTurn;
    }
    public class MissingScarfField : BrilliantScarf { }
    public class WrongScarfField : BrilliantScarf
    {
        private string _cardsPlayedThisTurn = "wrong";
        public string PeekWrong() => _cardsPlayedThisTurn;
    }
    public class JossPaper : RelicModel
    {
        private int _observedCards;
        private int _etherealCount;
        public bool ThrowCards;
        public int CardsExhausted => ThrowCards ? throw new InvalidOperationException("cards getter failed") : _observedCards;
        public void Set(int cards, int ethereal) { _observedCards = cards; _etherealCount = ethereal; }
        public int Peek() => _observedCards + _etherealCount;
    }
    public class PartialJossPaper : JossPaper
    {
        public PartialJossPaper() => Set(4, 0);
    }
    public class WrongJossPaperField : JossPaper
    {
        private string _etherealCount = "wrong";
        public string PeekWrong() => _etherealCount;
    }
    public class FurCoat : RelicModel
    {
        // Handwritten properties deliberately have no compiler-generated backing fields.
        private int[] _cols = [];
        private int[] _rows = [];
        private bool _ready;
        public HashSet<string> ThrowProperties = [];
        private T Read<T>(string name, T value) => ThrowProperties.Contains(name)
            ? throw new InvalidOperationException($"{name} getter failed") : value;
        private int[] FurCoatCoordCols => Read(nameof(FurCoatCoordCols), _cols);
        private int[] FurCoatCoordRows => Read(nameof(FurCoatCoordRows), _rows);
        private bool FurCoatCoordsSet => Read(nameof(FurCoatCoordsSet), _ready);
        public int FurCoatActIndex { get; set; }
        public void Set(int[] cols, int[] rows, bool ready = true)
        { _cols = cols; _rows = rows; _ready = ready; }
    }
    public class RainbowRing : RelicModel { }
    public class Permafrost : RelicModel { }
    public class ArtOfWar : RelicModel { }
    public class PaelsTears : RelicModel { }
    public class Vambrace : RelicModel { }
    public class DemonTongue : RelicModel { }
    public class Pocketwatch : RelicModel { }
    public class RedSkull : RelicModel { }
    public class BeltBuckle : RelicModel { }
    public class BurningSticks : RelicModel { }
    public class MusicBox : RelicModel { }
    public class PaelsEye : RelicModel { }
    public class ThrowingAxe : RelicModel { }
    public class PaelsLegion : RelicModel { }
    public class BeatingRemnant : RelicModel { }
    public class MiniRegent : RelicModel { }
    public class Regalite : RelicModel { }
    public class RuinedHelmet : RelicModel { }
    public class UnsettlingLamp : RelicModel { }
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static string? SafeGetText(Func<string?> read) => read();
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    }
}
