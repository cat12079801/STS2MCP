using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }
    public enum TargetType { None, Self, AnyEnemy, AllEnemies, RandomEnemy, AnyPlayer, AnyAlly, AllAllies, TargetedNoCreature, Osty }
    public enum UnplayableReason { None, BlockedByCardLogic }
}

// Boundary doubles for game-owned getters. No game calculations or previews.
namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);
    public class AbstractModel
    {
        public bool ThrowId;
        public ModelId ModelId = new("TEST");
        public ModelId Id => ThrowId ? throw new InvalidOperationException("id failed") : ModelId;
    }
    public class AfflictionModel : AbstractModel
    {
        public int Value;
        public bool ThrowAmount;
        public int Amount => ThrowAmount ? throw new InvalidOperationException("amount failed") : Value;
    }
    public class EnchantmentModel : AbstractModel
    {
        public int Value;
        public bool ThrowAmount;
        public int Amount => ThrowAmount ? throw new InvalidOperationException("amount failed") : Value;
    }
    public class CardModel
    {
        public HashSet<string> ThrowOn = [];
        public CardType CardType = CardType.Attack;
        public TargetType Target = TargetType.AnyEnemy;
        public AfflictionModel? Afflicted;
        public EnchantmentModel? Enchanted;
        public IEnumerable<KeyValuePair<string, DynamicVar>> Vars = new Dictionary<string, DynamicVar>();
        public UnplayableReason PlayReason;
        public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer)
        {
            _ = TargetType;
            reason = Read("can_play", PlayReason);
            preventer = null;
            return reason == UnplayableReason.None;
        }
        private T Read<T>(string field, T value)
            => ThrowOn.Contains(field) ? throw new InvalidOperationException($"{field} failed") : value;
        public CardType Type => Read("type", CardType);
        public TargetType TargetType => Read("target_type", Target);
        public AfflictionModel? Affliction => Read("affliction", Afflicted);
        public EnchantmentModel? Enchantment => Read("enchantment", Enchanted);
        public IEnumerable<KeyValuePair<string, DynamicVar>> DynamicVars => Read("dynamic_vars", Vars);
    }
}
namespace MegaCrit.Sts2.Core.Localization.DynamicVars
{
    public class DynamicVar(decimal value)
    {
        public decimal Value = value;
        public decimal PreviewValue = 999m;
        public decimal EnchantedValue = 888m;
        public bool ThrowBase;
        public decimal BaseValue => ThrowBase ? throw new InvalidOperationException("base failed") : Value;
    }
    public class StringVar() : DynamicVar(0m);
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    }
}
