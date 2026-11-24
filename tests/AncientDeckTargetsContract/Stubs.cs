using MegaCrit.Sts2.Core.Models;

namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed record Player(CardPile? Deck);
    public sealed record CardPile(IReadOnlyList<CardModel> Cards);
}

namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);
    public abstract class RelicModel;
    public enum CardType { Attack, Skill, Curse, Quest, Status }
    public enum CardTag { Strike, Defend }

    public sealed class CardModel(string id, CardType type)
    {
        public ModelId Id { get; } = new(id);
        public CardType Type { get; } = type;
        public CardTag[] CanonicalTags { get; init; } = [];
        public bool IsBasic { get; init; }
        public int UpgradeLevel { get; init; }
        public bool Eternal { get; init; }
        public bool Unplayable { get; init; }
        public EnchantmentModel? Attached { get; set; }
        public bool FailEnchantmentRead { get; set; }
        public bool FailTagsRead { get; set; }

        public IEnumerable<CardTag> Tags => FailTagsRead
            ? throw new InvalidOperationException("Tags read failed.") : CanonicalTags;
        public EnchantmentModel? Enchantment => FailEnchantmentRead
            ? throw new InvalidOperationException("Enchantment read failed.") : Attached;
        public bool IsBasicStrikeOrDefend => IsBasic && Tags.Any(tag => tag is CardTag.Strike or CardTag.Defend);
        public bool IsRemovable => !Eternal;
        public bool IsTransformable => IsRemovable;
    }

    public class EnchantmentModel(string id)
    {
        public ModelId Id { get; } = new(id);
        public int Calls { get; private set; }

        // Only the deck case of v0.113.1 EnchantmentModel.CanEnchant is needed here.
        public bool CanEnchant(CardModel card)
        {
            Calls++;
            return card.Type is not (CardType.Curse or CardType.Quest or CardType.Status)
                && CanEnchantCardType(card.Type) && !card.Unplayable && card.Enchantment is null;
        }

        protected virtual bool CanEnchantCardType(CardType type) => true;
    }

    public static class ModelDb
    {
        public static bool FailLookup { get; set; }
        public static T Enchantment<T>() where T : EnchantmentModel, new()
            => FailLookup ? throw new InvalidOperationException("Enchantment lookup failed.") : Canonical<T>.Model;

        private static class Canonical<T> where T : EnchantmentModel, new()
        {
            public static readonly T Model = new();
        }
    }
}

namespace MegaCrit.Sts2.Core.Models.Enchantments
{
    public sealed class Swift() : EnchantmentModel("SWIFT");
    public sealed class Imbued() : EnchantmentModel("IMBUED")
    {
        protected override bool CanEnchantCardType(CardType type) => type == CardType.Skill;
    }
}

namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class BeautifulBracelet : RelicModel;
    public sealed class ElectricShrymp : RelicModel;
    public sealed class NeowsTalisman : RelicModel;
    public sealed class LeafyPoultice : RelicModel;
    public sealed class Astrolabe : RelicModel;
    public sealed class PandorasBox : RelicModel;
    public sealed class OtherRelic : RelicModel;
}

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => throw new NotSupportedException();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => throw new NotSupportedException();

        // The generated file also contains entries for unrelated sections.
        internal sealed record StateAllowlistEntry(string Section, string Model, string DeclaringType, string Member,
            StateMemberKind Kind, string Key, StateValueConverter Converter, StateMarkdown Markdown, string Note);
        internal enum StateMemberKind { Field, AutoProperty, DataField }
        internal enum StateMarkdown { Always, Never, NonDefault, DiffersFromAmount }
        internal enum StateValueConverter
        {
            Bool, Int, NullableInt, Decimal, EnumName, CardUid, CardModelId, CardPlayCardUid, CreatureCombatId,
            CreatureCombatIds, KeywordNames, PowerRefs, SerializableCards, ModifierIds
        }
    }
}
