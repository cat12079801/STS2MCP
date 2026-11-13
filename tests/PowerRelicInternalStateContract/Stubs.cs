// Boundary doubles: fields/getters only; no simulated game calculations.
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature { public MegaCrit.Sts2.Core.Models.Player? Player { get; set; } }
}
namespace MegaCrit.Sts2.Core.Models
{
    public record ModelId(string Entry);
    public class PowerModel
    {
        private object? _internalData;
        public void SetData(object? data) => _internalData = data;
        public object? PeekData() => _internalData;
        public bool Skip;
        public bool ThrowSkip;
        public bool SkipNextDurationTick => ThrowSkip ? throw new InvalidOperationException("skip failed") : Skip;
    }
    public class RelicModel
    {
        public ModelId Id => new(GetType().Name);
        public Player Owner { get; set; } = new();
    }
    public class Player { public ulong NetId { get; set; } public RunState RunState { get; set; } = new(); }
    public class RunState { public MapPoint? CurrentMapPoint { get; set; } public int CurrentActIndex { get; set; } }
    public class MapPoint { public Coord coord; }
    public struct Coord { public int col; public int row; }
    public class DynamicVar
    {
        public decimal Value;
        public bool ThrowBase;
        public decimal BaseValue => ThrowBase ? throw new InvalidOperationException("base failed") : Value;
    }
    public class DynamicVars { public DynamicVar Block { get; set; } = new(); }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    using MegaCrit.Sts2.Core.Entities.Creatures;
    public class ToricToughnessPower : PowerModel { public DynamicVars DynamicVars { get; } = new(); }
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
        private class Data { public int attacksPlayedThisTurn = 0; }
        public JugglingPower() => SetData(new Data());
        public void Set(int value) => ((Data)PeekData()!).attacksPlayedThisTurn = value;
    }
    public class MissingDataPower : JugglingPower { }
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
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    }
}
