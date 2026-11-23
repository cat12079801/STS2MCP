// Boundary doubles: tests configure HappenedThisTurn for each entry and check
// that the production builder uses the current combat.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState
    {
        IReadOnlyList<Entities.Players.Player> Players { get; }
        IReadOnlyList<Entities.Creatures.Creature> Enemies { get; }
    }
    public class CombatState : ICombatState
    {
        public IReadOnlyList<Entities.Players.Player> Players => Array.Empty<Entities.Players.Player>();
        public List<Entities.Creatures.Creature> EnemyList = new();
        public IReadOnlyList<Entities.Creatures.Creature> Enemies => EnemyList;
    }
    public class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        public History.CombatHistory? Value = new();
        public bool ThrowHistory;
        public History.CombatHistory History => ThrowHistory ? throw new MissingMemberException("History renamed") : Value!;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public Combat.ICombatState? CombatState;
        public uint? CombatId;
        public Models.MonsterModel? Monster;
        public Players.Player? Player;
        public bool Enemy;
        public bool IsAlive = true;
        public bool IsEnemy => Enemy;
    }
    public class DamageResult(int damage) { public int UnblockedDamage = damage; }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class Player
    {
        public ulong NetId { get; set; }
        public List<Models.PotionModel?> PotionSlots { get; } = [];
        public Cards.CardPile Deck = new();
        private Creatures.Creature _creature;
        public Player() => _creature = new() { Player = this };
        public Creatures.Creature Creature
        {
            get => _creature;
            set { _creature = value; value.Player = this; }
        }
        public PlayerCombatState? PlayerCombatState = new();
        public Creatures.Creature? Osty;
    }
    public class PlayerCombatState
    {
        public int Number = 1;
        public bool ThrowTurn;
        public int TurnNumber => ThrowTurn ? throw new MissingMemberException("TurnNumber renamed") : Number;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }
    public enum CardTag { Wah, Cho, Ki }
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
    public class CardPile { public List<Models.CardModel> Cards = new(); }
    public class CardPlay(Models.CardModel card, Players.Player player)
    {
        public Models.CardModel Card = card;
        public Players.Player Player = player;
        public bool AutoPlay;
        public bool ThrowAutoPlay;
        public Creatures.Creature? Target;
        public int PlayIndex;
        public int PlayCount = 1;
        public ResourceInfo Paid = new();
        public bool ThrowResources;
        public ResourceInfo Resources => ThrowResources ? throw new MissingMemberException("Resources renamed") : Paid;
        public bool IsAutoPlay => ThrowAutoPlay ? throw new MissingMemberException("IsAutoPlay renamed") : AutoPlay;
    }
    public class ResourceInfo { public int EnergySpent; }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class ModelId(string entry) { public string Entry = entry; }
    public class CardModel(Entities.Cards.CardType type)
    {
        public AfflictionModel? Affliction;
        public ModelId Id = new("TEST");
        public CardModel? DeckVersion;
        public int Rarity => 0;
        public Entities.Cards.CardType Value = type;
        public bool ThrowType;
        public bool Ethereal;
        public HashSet<Entities.Cards.CardTag> Tags = [];
        public Entities.Cards.CardType Type => ThrowType ? throw new MissingMemberException("Type renamed") : Value;
    }
    public class AfflictionModel { }
    public class PotionModel(string id) { public ModelId Id { get; } = new(id); }
    public class MonsterModel(string id) { public ModelId Id = new(id); }
}
namespace MegaCrit.Sts2.Core.Models.Afflictions
{
    public sealed class Bound : AfflictionModel { }
}
namespace MegaCrit.Sts2.Core.ValueProps
{
    [Flags]
    public enum ValueProp { Unblockable = 2, Unpowered = 4, Move = 8, SkipHurtAnim = 16 }
    public static class ValuePropExtensions
    {
        public static bool IsCardOrMonsterMove(this ValueProp props) => props.HasFlag(ValueProp.Move);
    }
}
namespace MegaCrit.Sts2.Core.Combat.History
{
    public abstract class CombatHistoryEntry
    {
        public Entities.Creatures.Creature? Actor;
        public Func<ICombatState?, bool> ThisTurn = _ => false;
        public Func<Entities.Players.Player, bool> LastTurn = _ => false;
        public bool HappenedThisTurn(ICombatState? state) => ThisTurn(state);
        public bool HappenedLastPlayerTurn(Entities.Players.Player player) => LastTurn(player);
    }
    public class CombatHistory
    {
        public List<CombatHistoryEntry> Items = [];
        public bool ThrowEntries;
        public bool ThrowStarted;
        public bool ThrowFinished;
        public IEnumerable<CombatHistoryEntry> Entries => ThrowEntries ? throw new MissingMemberException("Entries renamed") : Items;
        public IEnumerable<Entries.CardPlayStartedEntry> CardPlaysStarted => ThrowStarted ? throw new MissingMemberException("CardPlaysStarted renamed") : Entries.OfType<Entries.CardPlayStartedEntry>();
        public IEnumerable<Entries.CardPlayFinishedEntry> CardPlaysFinished => ThrowFinished ? throw new MissingMemberException("CardPlaysFinished renamed") : Entries.OfType<Entries.CardPlayFinishedEntry>();
    }
}
namespace MegaCrit.Sts2.Core.Combat.History.Entries
{
    public class CardPlayStartedEntry : CombatHistoryEntry
    {
        public Entities.Cards.CardPlay CardPlay;
        public CardPlayStartedEntry(Entities.Cards.CardPlay play)
        {
            CardPlay = play;
            Actor = play.Player.Creature;
        }
    }
    public class CardPlayFinishedEntry : CombatHistoryEntry
    {
        public Entities.Cards.CardPlay CardPlay;
        public bool WasEthereal { get; }
        public CardPlayFinishedEntry(Entities.Cards.CardPlay play)
        {
            CardPlay = play;
            Actor = play.Player.Creature;
            WasEthereal = play.Card.Ethereal;
        }
    }
    public class EnergySpentEntry : CombatHistoryEntry
    {
        public int Amount { get; }
        public EnergySpentEntry(Entities.Players.Player owner, int amount) => (Actor, Amount) = (owner.Creature, amount);
    }
    public class StarsModifiedEntry : CombatHistoryEntry
    {
        public int Amount { get; }
        public StarsModifiedEntry(Entities.Creatures.Creature actor, int amount) => (Actor, Amount) = (actor, amount);
    }
    public class CreatureAttackedEntry : CombatHistoryEntry
    {
        public IReadOnlyList<Entities.Creatures.DamageResult> DamageResults { get; }
        public CreatureAttackedEntry(Entities.Creatures.Creature actor, params Entities.Creatures.DamageResult[] results)
            => (Actor, DamageResults) = (actor, results);
    }
    public class CardExhaustedEntry : CombatHistoryEntry { }
    public class PotionUsedEntry : CombatHistoryEntry
    {
        public Models.PotionModel Potion { get; }
        public Entities.Creatures.Creature? Target { get; }
        public PotionUsedEntry(Models.PotionModel potion, Entities.Creatures.Creature actor, Entities.Creatures.Creature? target)
        {
            Potion = potion;
            Target = target;
            Actor = actor;
        }
    }
    // Actor is Card.Owner.Creature in the real constructors (CardDrawnEntry / CardGeneratedEntry).
    public class CardDrawnEntry : CombatHistoryEntry
    {
        public Models.CardModel Card;
        public bool FromHand;
        public bool ThrowFromHandDraw;
        public bool FromHandDraw => ThrowFromHandDraw ? throw new MissingMemberException("FromHandDraw renamed") : FromHand;
        public CardDrawnEntry(Models.CardModel card, bool fromHandDraw, Entities.Creatures.Creature actor)
        {
            Card = card;
            FromHand = fromHandDraw;
            Actor = actor;
        }
    }
    public class CardGeneratedEntry : CombatHistoryEntry
    {
        public Models.CardModel Card;
        public Entities.Players.Player? Creator;
        public CardGeneratedEntry(Models.CardModel card, Entities.Players.Player? creator, Entities.Creatures.Creature actor)
        {
            Card = card;
            Creator = creator;
            Actor = actor;
        }
    }
    public class CardAfflictedEntry(Models.CardModel card, Models.AfflictionModel affliction) : CombatHistoryEntry
    {
        public Models.CardModel Card = card;
        public Models.AfflictionModel Affliction = affliction;
    }
    public class DamageReceivedEntry(Entities.Creatures.DamageResult result) : CombatHistoryEntry
    {
        public Entities.Creatures.Creature? Receiver => Actor;
        public Entities.Creatures.DamageResult Result = result;
    }
    public class BlockGainedEntry(Entities.Cards.CardPlay? play, ValueProps.ValueProp props) : CombatHistoryEntry
    {
        public Entities.Cards.CardPlay? CardPlay = play;
        public ValueProps.ValueProp Props = props;
    }
}
namespace MegaCrit.Sts2.Core.Commands
{
    using Combat;
    using Entities.Cards;
    using Entities.Players;

    public static class MelodyCmd
    {
        public static bool ThrowCho;
        public static bool PlayedWahThisTurn(Player owner) => PlayedThisTurn(owner, CardTag.Wah);
        public static bool PlayedChoThisTurn(Player owner) => ThrowCho
            ? throw new MissingMemberException("PlayedChoThisTurn renamed") : PlayedThisTurn(owner, CardTag.Cho);
        public static bool PlayedKiThisTurn(Player owner) => PlayedThisTurn(owner, CardTag.Ki);
        private static bool PlayedThisTurn(Player owner, CardTag tag) => CombatManager.Instance.History.CardPlaysFinished
            .Any(e => e.HappenedThisTurn(owner.Creature.CombatState) && e.CardPlay.Card.Tags.Contains(tag)
                && e.CardPlay.Player == owner);
    }
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
        private static string? SafeGetText(Func<string?> read) => read();
    }
}
