// Boundary doubles only: no game DLL. Hook methods mirror the v0.113.1 AbstractModel
// signatures so override detection is exercised by real reflection; no hook is invoked
// (every double override throws if called).
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { None, Draw, Hand, Discard, Exhaust, Play, Deck }
    public class CardPile(PileType type)
    {
        public PileType Type = type;
        public bool IsCombatPile => Type is PileType.Draw or PileType.Hand or PileType.Discard or PileType.Exhaust or PileType.Play;
        public List<Models.CardModel> Cards = new();
    }
    public class TemporaryCardCost(int cost, bool turn, bool played)
    {
        public int Cost { get; } = cost;
        public bool ClearsWhenTurnEnds { get; } = turn;
        public bool ClearsWhenCardIsPlayed { get; } = played;
    }
}
namespace MegaCrit.Sts2.Core.GameActions.Multiplayer { public class PlayerChoiceContext { } }
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public uint? CombatId;
        public Players.Player? Player;
        public bool IsEnemy;
        public string MonsterId = "MONSTER";
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class Player
    {
        public ulong NetId = 1;
        public List<Models.PotionModel?> Slots = new();
        public IReadOnlyList<Models.PotionModel?> PotionSlots => Slots;
        public Cards.CardPile Deck = new(Cards.PileType.Deck);
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    using MegaCrit.Sts2.Core.Entities.Cards;
    using MegaCrit.Sts2.Core.Entities.Creatures;
    using MegaCrit.Sts2.Core.Entities.Players;
    using MegaCrit.Sts2.Core.GameActions.Multiplayer;

    public class ModelId(string entry) { public string Entry = entry; }
    public abstract class AbstractModel(string id)
    {
        public ModelId Id { get; } = new(id);
        public virtual Task AfterCardEnteredCombat(CardModel card) => Task.CompletedTask;
        public virtual Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => Task.CompletedTask;
        public virtual Task AfterHandEmptied(PlayerChoiceContext choiceContext, Player player) => Task.CompletedTask;
        public virtual Task AfterPotionUsed(PotionModel potion, Creature? target) => Task.CompletedTask;
        // An unrelated overload: detection must match the exact parameter list.
        public virtual Task AfterPotionUsed(PotionModel potion) => Task.CompletedTask;
    }
    public class PowerModel(string id, Creature owner) : AbstractModel(id) { public Creature Owner => owner; }
    public class RelicModel(string id, Player owner) : AbstractModel(id) { public Player Owner => owner; }
    public class PotionModel(string id, Player owner) : AbstractModel(id) { public Player Owner => owner; }
    public class OrbModel(string id, Player owner) : AbstractModel(id) { public Player Owner => owner; }
    public class MonsterModel(string id, Creature creature) : AbstractModel(id) { public Creature Creature => creature; }
    public class ModifierModel(string id) : AbstractModel(id);
    public class BadgeModel(string id) : AbstractModel(id);
    public class RunModel(string id) : AbstractModel(id);
    public class AchievementModel(string id) : AbstractModel(id);
    public class AfflictionModel(string id, CardModel card) : AbstractModel(id) { public CardModel Card => card; }
    public class EnchantmentModel(string id, CardModel card) : AbstractModel(id) { public CardModel Card => card; }
    public class CardModel(string id, Player owner) : AbstractModel(id)
    {
        public Player Owner => owner;
        public CardPile? Pile;
        public AfflictionModel? Affliction;
        public EnchantmentModel? Enchantment;
        public bool ExhaustOnNextPlay;
        public bool HasStarCostX;
        public int CanonicalStarCost = -1;
        public int BaseStarCost => CanonicalStarCost;
        private List<TemporaryCardCost> _temporaryStarCosts = new();
        public List<TemporaryCardCost> TemporaryStarCostsForTest => _temporaryStarCosts;
        public int CurrentStarCost => _temporaryStarCosts.LastOrDefault() is { } t && !(t.Cost == 0 && BaseStarCost < 0) ? t.Cost : BaseStarCost;
        public bool ThrowStar;
        public void BreakStarField() => _temporaryStarCosts = null!;
    }
}
namespace MegaCrit.Sts2.Core.Models.Dread { public class DominionModel(string id) : AbstractModel(id); }
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { IEnumerable<Models.AbstractModel> IterateHookListeners(); }
    public class CombatState : ICombatState
    {
        public List<Models.AbstractModel> Listeners = new();
        public int Iterations;
        public IEnumerable<Models.AbstractModel> IterateHookListeners() { Iterations++; return Listeners; }
    }
    public class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        public bool IsInProgress;
        public CombatState? State;
        public bool ShouldSkipCombatEffect;
        public CombatState? DebugOnlyGetState() => State;
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public interface IRunState { IEnumerable<Models.AbstractModel> IterateHookListeners(Combat.ICombatState? combat); }
    public class RunState : IRunState
    {
        public List<Models.AbstractModel> DeckListeners = new();
        public IEnumerable<Models.AbstractModel> IterateHookListeners(Combat.ICombatState? combat)
            => DeckListeners.Concat(combat?.IterateHookListeners() ?? []);
    }
}
namespace MegaCrit.Sts2.Core.Modding
{
    public static class ModHelper
    {
        private static readonly List<object> _runHookSubscribers = new();
        private static readonly List<object> _combatHookSubscribers = new();
        public static List<object> RunSubscribersForTest => _runHookSubscribers;
    }
}
