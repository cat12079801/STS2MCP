// Game boundary only: DisplayRank is a configured answer from CardModel.CompareTo,
// not a second implementation of the game's upgrade/enchantment/cost comparison.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState { }
    public sealed class CombatState : ICombatState { }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum PileType { Hand, Draw, Discard, Exhaust, Play }
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
        public int Rarity = rarity;
        public int DisplayRank = displayRank;
        public int CompareCalls;
        public override int CompareTo(AbstractModel? other)
        {
            CompareCalls++;
            if (other is not CardModel card) return base.CompareTo(other);
            int idOrder = StringComparer.Ordinal.Compare(Id.Entry, card.Id.Entry);
            return idOrder != 0 ? idOrder : DisplayRank.CompareTo(card.DisplayRank);
        }
    }
}
