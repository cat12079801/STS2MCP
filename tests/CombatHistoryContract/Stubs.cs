// Boundary doubles: turn predicates are configured answers, not reconstructed game
// turn logic. Tests verify that the production builder delegates to these methods.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { IReadOnlyList<Entities.Players.Player> Players { get; } }
    public class CombatState : ICombatState
    {
        public IReadOnlyList<Entities.Players.Player> Players => Array.Empty<Entities.Players.Player>();
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
    }
    public class DamageResult(int damage) { public int UnblockedDamage = damage; }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class Player
    {
        public ulong NetId => 0;
        public Cards.CardPile Deck = new();
        public Creatures.Creature Creature = new();
        public PlayerCombatState? PlayerCombatState = new();
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
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
    public class CardPile { public List<Models.CardModel> Cards = new(); }
    public class CardPlay(Models.CardModel card, Players.Player player)
    {
        public Models.CardModel Card = card;
        public Players.Player Player = player;
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class ModelId(string entry) { public string Entry = entry; }
    public class CardModel(Entities.Cards.CardType type)
    {
        public ModelId Id = new("TEST");
        public CardModel? DeckVersion;
        public int Rarity => 0;
        public Entities.Cards.CardType Value = type;
        public bool ThrowType;
        public Entities.Cards.CardType Type => ThrowType ? throw new MissingMemberException("Type renamed") : Value;
    }
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
    public class CardPlayStartedEntry(Entities.Cards.CardPlay play) : CombatHistoryEntry { public Entities.Cards.CardPlay CardPlay = play; }
    public class CardPlayFinishedEntry(Entities.Cards.CardPlay play) : CombatHistoryEntry { public Entities.Cards.CardPlay CardPlay = play; }
    public class CardExhaustedEntry : CombatHistoryEntry { }
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
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
        private static string? SafeGetText(Func<string?> read) => read();
    }
}
