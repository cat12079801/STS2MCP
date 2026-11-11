// Offline contract doubles: no game, Godot, REST API, or RNG is loaded.
namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState;
    public sealed class CombatState : ICombatState;
}

namespace MegaCrit.Sts2.Core.Unlocks
{
    public sealed class UnlockState;
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardMultiplayerConstraint { SingleplayerOnly, MultiplayerOnly }
}

namespace MegaCrit.Sts2.Core.Runs
{
    public sealed class RunState
    {
        public Entities.Cards.CardMultiplayerConstraint CardMultiplayerConstraint { get; set; }
    }
}

namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed class Player
    {
        public Unlocks.UnlockState UnlockState { get; } = new();
        public Runs.RunState RunState { get; } = new();
        public Models.CharacterModel Character { get; } = new();
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);
    public sealed record CardModel(ModelId Id);
    public sealed record PotionModel(ModelId Id);

    public sealed class CharacterModel
    {
        public CardPoolModel CardPool { get; } = new();
        public PotionPoolModel PotionPool { get; } = new();
    }

    public class CardPoolModel
    {
        public Func<Unlocks.UnlockState, Entities.Cards.CardMultiplayerConstraint, IEnumerable<CardModel>> Read
            { get; set; } = (_, _) => [];
        public IEnumerable<CardModel> GetUnlockedCards(Unlocks.UnlockState unlocks,
            Entities.Cards.CardMultiplayerConstraint constraint) => Read(unlocks, constraint);
    }

    public class PotionPoolModel
    {
        public Func<Unlocks.UnlockState, IEnumerable<PotionModel>> Read { get; set; } = _ => [];
        public IEnumerable<PotionModel> GetUnlockedPotions(Unlocks.UnlockState unlocks) => Read(unlocks);
    }

    public static class ModelDb
    {
        public static CardPools.ColorlessCardPool Colorless { get; } = new();
        public static PotionPools.SharedPotionPool Shared { get; } = new();
        public static T CardPool<T>() where T : CardPoolModel => (T)(CardPoolModel)Colorless;
        public static T PotionPool<T>() where T : PotionPoolModel => (T)(PotionPoolModel)Shared;
    }
}

namespace MegaCrit.Sts2.Core.Models.CardPools
{
    public sealed class ColorlessCardPool : CardPoolModel;
}

namespace MegaCrit.Sts2.Core.Models.PotionPools
{
    public sealed class SharedPotionPool : PotionPoolModel;
}
