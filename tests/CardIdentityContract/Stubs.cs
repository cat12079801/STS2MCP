// Game boundary only: DisplayRank is a configured answer from CardModel.CompareTo,
// not a second implementation of the game's upgrade/enchantment/cost comparison.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { IReadOnlyList<Entities.Players.Player> Players { get; } }
    public sealed class CombatState : ICombatState
    {
        public IReadOnlyList<Entities.Players.Player> Players { get; set; } = [];
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed class Player
    {
        public ulong NetId;
        public Cards.CardPile Deck = new();
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
    public sealed class CardPile { public List<Models.CardModel> Cards = []; }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class AbstractModel : IComparable<AbstractModel>
    {
        public virtual int CompareTo(AbstractModel? other) => other == null ? 1 : 0;
    }
    public sealed class ModelId(string entry) { public string Entry = entry; }
    public sealed class CardModel(string id, int rarity = 0, int displayRank = 0) : AbstractModel
    {
        public ModelId Id = new(id);
        public CardModel? DeckVersion;
        public int Rarity = rarity;
        public int DisplayRank = displayRank;
        public int CompareCalls;
        public bool ThrowCompare;
        public override int CompareTo(AbstractModel? other)
        {
            CompareCalls++;
            if (ThrowCompare || other is CardModel { ThrowCompare: true })
                throw new InvalidOperationException("comparison hook unavailable");
            if (other is not CardModel card) return base.CompareTo(other);
            int idOrder = StringComparer.Ordinal.Compare(Id.Entry, card.Id.Entry);
            return idOrder != 0 ? idOrder : DisplayRank.CompareTo(card.DisplayRank);
        }
    }
}
