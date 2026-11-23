namespace MegaCrit.Sts2.Core.Combat
{
    using Entities.Creatures;

    public interface ICombatState
    {
        IReadOnlyList<Creature> Enemies { get; }
        IReadOnlyList<Creature> Creatures { get; }
    }

    public class CombatState : ICombatState
    {
        public List<Creature> EnemyList = [];
        public List<Creature> Allies = [];
        public IReadOnlyList<Creature> Enemies => EnemyList;
        public IReadOnlyList<Creature> Creatures => EnemyList.Concat(Allies).ToList();
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public uint? CombatId;
        public Models.MonsterModel? Monster;
        public bool IsEnemy => Monster != null;
        public List<Models.PowerModel> Powers = [];
    }
}

namespace MegaCrit.Sts2.Core.Entities.Players
{
    using Cards;
    using FoolArrows;
    using Models;

    public class Player
    {
        public Creatures.Creature Creature = new();
        public PlayerCombatState? PlayerCombatState = new();
    }

    public class PlayerCombatState
    {
        public int Stars;
        public int Energy;
        public List<CardModel> PileCards = [];
        public List<int> History = [];
        public List<ArrowSlot> Slots = [];
        public bool ThrowArrows;
        public bool ThrowCount;
        public int? CountOverride;
        public IReadOnlyList<ArrowSlot> Arrows => ThrowArrows ? throw new MissingMemberException("Arrows") : Slots.ToList();
        public int AvailableArrowCount => ThrowCount
            ? throw new MissingMemberException("AvailableArrowCount")
            : CountOverride ?? Slots.Count(slot => slot.IsAvailable);
    }
}

namespace MegaCrit.Sts2.Core.Entities.FoolArrows
{
    public enum ArrowStatus { Retrieved, Away, Destroyed }
    public class ArrowSlot(Players.Player player)
    {
        public readonly Players.Player owner = player;
        public ArrowStatus Status;
        public bool DestroyAfterImpaled;
        public bool IsAvailable => Status == ArrowStatus.Retrieved;
    }
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
    public class TemporaryCardCost
    {
        public int Cost { get; init; }
        public bool ClearsWhenTurnEnds { get; init; }
        public bool ClearsWhenCardIsPlayed { get; init; }
    }
    public class CardEnergyCost
    {
        public int Amount;
        public int GetAmountToSpend() => Amount;
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    using Combat;
    using Entities.Cards;
    using Entities.Creatures;
    using Entities.Players;
    using Hooks;

    public class ModelId(string entry) { public string Entry { get; } = entry; }
    public class MonsterModel(string id) { public ModelId Id { get; } = new(id); }
    public class PowerModel { }
    public class CardModel
    {
        private readonly List<TemporaryCardCost> _temporaryStarCosts = [];
        public string Id = "STARDUST";
        public Player Owner = new();
        public ICombatState? CombatState = new CombatState();
        public bool StarX = true;
        public bool ThrowCostsX;
        public bool ThrowLastStarsSpent;
        public bool ThrowStarCost;
        public int Captured;
        public int? StarCostOverride;
        public bool HasStarCostX => ThrowCostsX ? throw new MissingMemberException("HasStarCostX") : StarX;
        public int BaseStarCost => CanonicalStarCost;
        public int CanonicalStarCost => StarX ? 0 : -1;
        public int CurrentStarCost => _temporaryStarCosts.LastOrDefault()?.Cost ?? BaseStarCost;
        public int LastStarsSpent => ThrowLastStarsSpent ? throw new MissingMemberException("LastStarsSpent") : Captured;
        public CardEnergyCost EnergyCost = new();
        public int ResolveStarXValue() => Hook.ModifyXValue(CombatState!, this, LastStarsSpent);
        public int GetStarCostWithModifiers() => ThrowStarCost
            ? throw new MissingMemberException("GetStarCostWithModifiers")
            : StarCostOverride ?? Owner.PlayerCombatState!.Stars;
        public void AddTemporaryCost(TemporaryCardCost cost) => _temporaryStarCosts.Add(cost);
    }
}

namespace MegaCrit.Sts2.Core.Models.Powers
{
    using Entities.Creatures;
    using Entities.FoolArrows;

    public class ImpaledPower : PowerModel
    {
        public Creature? Applier;
        public int HitCounter = 3;
        public List<ArrowSlot> Sources = [];
        public bool ThrowArrows;
        public IReadOnlyList<ArrowSlot> Arrows => ThrowArrows ? throw new MissingMemberException("ImpaledPower.Arrows") : Sources.ToList();
    }
}

namespace MegaCrit.Sts2.Core.Hooks
{
    using Combat;
    using Entities.Players;
    using Models;

    public static class Hook
    {
        public static List<(ICombatState Combat, CardModel Card, int Value)> XCalls = [];
        public static Func<CardModel, int, int> X = (_, value) => value;
        public static Func<Player, bool> PayWithStars = _ => false;
        public static int ModifyXValue(ICombatState combat, CardModel card, int originalValue)
        {
            XCalls.Add((combat, card, originalValue));
            return X(card, originalValue);
        }
        public static bool ShouldPayExcessEnergyCostWithStars(ICombatState combat, Player player) => PayWithStars(player);
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
