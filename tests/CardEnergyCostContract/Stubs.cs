using MegaCrit.Sts2.Core.Entities.Cards;

// Boundary doubles: calculations are configured results, not a reimplementation
// of game cost rules. The contract checks which public calculation is requested.
namespace MegaCrit.Sts2.Core.Entities.Cards
{
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
    }
    public class CardEnergyCost
    {
        // object allows an incompatible/null list in a future game to be tested.
        private object? _localModifiers = new List<LocalCostModifier>();
        public bool X;
        public bool ThrowCostsX;
        public int Printed = 2;
        public int Captured;
        public bool ThrowCanonical;
        public bool ThrowCaptured;
        public int CapturedReads;
        public int Canonical => ThrowCanonical ? throw new InvalidOperationException("canonical failed") : Printed;
        public int CapturedXValue
        {
            get
            {
                CapturedReads++;
                if (!X || ThrowCaptured) throw new InvalidOperationException("captured X failed");
                return Captured;
            }
        }
        public bool CostsX => ThrowCostsX ? throw new InvalidOperationException("cost kind failed") : X;
        public Dictionary<CostModifiers, int> Values = new() { [CostModifiers.None] = 2, [CostModifiers.Local] = 2, [CostModifiers.All] = 2 };
        public HashSet<CostModifiers> ThrowOn = [];
        public List<CostModifiers> Calls = [];
        public int GetWithModifiers(CostModifiers modifiers)
        {
            Calls.Add(modifiers);
            if (ThrowOn.Contains(modifiers)) throw new InvalidOperationException($"calculation failed: {modifiers}");
            return Values[modifiers];
        }
        public void SetModifiers(object? modifiers) => _localModifiers = modifiers;
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class CardModel
    {
        public bool ThrowEnergyCost;
        public CardEnergyCost Cost = new();
        public CardEnergyCost EnergyCost => ThrowEnergyCost ? throw new InvalidOperationException("energy cost failed") : Cost;
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
