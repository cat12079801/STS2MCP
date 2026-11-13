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
        private int _attacksPlayed;
        public int Peek() => _attacksPlayed;
        public void Set(int n) => _attacksPlayed = n;
    }
    public class JossPaper : RelicModel
    {
        private int _cardsExhausted;
        private int _etherealCount;
        public void Set(int cards, int ethereal) { _cardsExhausted = cards; _etherealCount = ethereal; }
        public int Peek() => _cardsExhausted + _etherealCount;
    }
    public class MissingPenNibField : PenNib { }
    public class PartialJossPaper : JossPaper
    {
        private int _cardsExhausted = 4;
        public int PeekPartial() => _cardsExhausted;
    }
    public class WrongPenNibField : PenNib
    {
        private string _attacksPlayed = "wrong";
        public string PeekWrong() => _attacksPlayed;
    }
    public class FurCoat : RelicModel
    {
        private int[] FurCoatCoordCols { get; set; } = [];
        private int[] FurCoatCoordRows { get; set; } = [];
        private bool FurCoatCoordsSet { get; set; }
        public int FurCoatActIndex { get; set; }
        public void Set(int[] cols, int[] rows, bool ready = true)
        { FurCoatCoordCols = cols; FurCoatCoordRows = rows; FurCoatCoordsSet = ready; }
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
