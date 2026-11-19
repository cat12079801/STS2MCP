// Boundary doubles: the game's own predicates (IsAllowedInRun, FilterForCombat,
// GetUnlockedCards) are configured answers that record their calls. The tests verify
// that the production builder delegates to them, in the game's order, and never
// re-implements their conditions.
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }
    public enum CardMultiplayerConstraint { None, SingleplayerOnly, MultiplayerOnly }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public class RunState
    {
        public List<Entities.Players.Player> Players = new();
        public Entities.Cards.CardMultiplayerConstraint CardMultiplayerConstraint = Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly;
        public bool Blitz;
    }
}
namespace MegaCrit.Sts2.Core.Unlocks
{
    public class UnlockState
    {
        // v0.113.1 UnlockState.CharacterCardPools: Characters.Select(c => c.CardPool).
        public List<Models.CardPoolModel> Pools = new();
        public IEnumerable<Models.CardPoolModel> CharacterCardPools => Pools;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class Player
    {
        public ulong NetId = 7;
        public Runs.RunState RunState = new();
        public Unlocks.UnlockState UnlockState = new();
        public Models.CharacterModel Character = new();
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class ModelId(string entry) { public string Entry = entry; }
    public class CardModel(string id, Entities.Cards.CardType type)
    {
        public ModelId Id = new(id);
        public Entities.Cards.CardType Type = type;
        public bool BlitzForbidden;
        public bool GeneratedInCombat = true;
        public Entities.Cards.CardMultiplayerConstraint MultiplayerConstraint;
        public static readonly List<(CardModel Card, Runs.RunState Run)> AllowedCalls = new();
        // Same answer as v0.113.1 CardModel.IsAllowedInRun, recorded so the test can
        // assert the builder passes the owner's real run state.
        public bool IsAllowedInRun(Runs.RunState run)
        {
            AllowedCalls.Add((this, run));
            if (run.Players.Count > 1 ? MultiplayerConstraint == Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly
                : MultiplayerConstraint == Entities.Cards.CardMultiplayerConstraint.MultiplayerOnly) return false;
            return !(run.Blitz && BlitzForbidden);
        }
    }
    public class CardPoolModel
    {
        public Func<Unlocks.UnlockState, Entities.Cards.CardMultiplayerConstraint, IEnumerable<CardModel>> Read = (_, _) => [];
        public IEnumerable<CardModel> GetUnlockedCards(Unlocks.UnlockState unlocks, Entities.Cards.CardMultiplayerConstraint constraint) => Read(unlocks, constraint);
    }
    public class CharacterModel
    {
        public ModelId Id = new("IRONCLAD");
        public CardPoolModel CardPool = new();
    }
    public static class ModelDb
    {
        public static readonly CardPools.ColorlessCardPool Colorless = new();
        public static T CardPool<T>() where T : CardPoolModel => (T)(CardPoolModel)Colorless;
    }
}
namespace MegaCrit.Sts2.Core.Models.CardPools { public class ColorlessCardPool : CardPoolModel { } }
namespace MegaCrit.Sts2.Core.Models.Cards
{
    public sealed class JackOfAllTrades() : CardModel("JACK_OF_ALL_TRADES", Entities.Cards.CardType.Skill);
}
namespace MegaCrit.Sts2.Core.Factories
{
    public static class CardFactory
    {
        public static int FilterCalls;
        // v0.113.1 CardFactory.FilterForCombat: CanBeGeneratedInCombat and rarity, then Distinct.
        public static IEnumerable<Models.CardModel> FilterForCombat(IEnumerable<Models.CardModel> cards)
        {
            FilterCalls++;
            return cards.Where(c => c.GeneratedInCombat).Distinct();
        }
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
