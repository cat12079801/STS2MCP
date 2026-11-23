using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace MegaCrit.Sts2.Core.Combat
{
    public interface ICombatState
    {
        IReadOnlyList<Creature> Allies { get; }
        bool ContainsCreature(Creature creature);
    }

    public sealed class CombatState : ICombatState
    {
        public List<Creature> AllyList { get; } = [];
        public IReadOnlyList<Creature> Allies => AllyList;
        public bool ContainsCreature(Creature creature) => Allies.Contains(creature);
    }

    public sealed class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        public bool IsInProgress { get; set; }
        public ICombatState? Combat { get; set; }
        public ICombatState? DebugOnlyGetState() => Combat;
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    public sealed record ModelId(string Entry);
    public class MonsterModel(string id, string title)
    {
        public ModelId Id { get; } = new(id);
        public string Title { get; } = title;
    }

    public sealed class PowerModel(string id, bool includedInStatus)
    {
        public bool ThrowId { get; set; }
        public ModelId Id => ThrowId ? throw new InvalidOperationException("Power ID unavailable") : new(id);
        public bool IncludedInStatus { get; } = includedInStatus;
    }

    public class RelicModel;
}

namespace MegaCrit.Sts2.Core.Models.Monsters
{
    public sealed class Osty() : MonsterModel("OSTY", "Osty");
    public sealed class Wuppy() : MonsterModel("WUPPY", "ワッピー");
}

namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class Wuppy : RelicModel
    {
        public bool IsMelted { get; set; }
        public Creature? CombatWuppy { get; set; }
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public sealed class Creature
    {
        public uint? CombatId { get; set; }
        public MonsterModel? Monster { get; init; }
        public Player? PetOwner { get; set; }
        public ICombatState? CombatState { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; init; }
        public int Block { get; init; }
        public bool IsAlive => CurrentHp > 0;
        public bool ThrowName { get; set; }
        public string Name => ThrowName ? throw new InvalidOperationException("Name unavailable") : Monster!.Title;
        public List<PowerModel> PowerList { get; } = [];
        public bool ThrowPowers { get; set; }
        public IReadOnlyList<PowerModel> Powers => ThrowPowers ? throw new InvalidOperationException("Powers unavailable") : PowerList;
    }
}

namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed class Player
    {
        public Creature Creature { get; } = new();
        public List<RelicModel> Relics { get; } = [];
        public PlayerCombatState? PlayerCombatState { get; set; } = new();
    }

    public sealed class PlayerCombatState
    {
        public List<Creature> PetList { get; } = [];
        public bool ThrowPets { get; set; }
        public IReadOnlyList<Creature> Pets => ThrowPets ? throw new InvalidOperationException("Pets unavailable") : PetList;
        public Creature? GetPet<T>() where T : MonsterModel => Pets.FirstOrDefault(pet => pet.Monster is T);
    }
}

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => throw new NotSupportedException();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => throw new NotSupportedException();
        private static string? SafeGetText(Func<object?> getter) => getter()?.ToString();
        private static string StripRichTextTags(string text) => text;

        // Status filtering is allowed to hide a power. The unfiltered ID read is tested through the real pets builder.
        private static List<Dictionary<string, object?>> BuildPowersState(Creature creature) => creature.Powers
            .Where(power => power.IncludedInStatus).Select(power => new Dictionary<string, object?> { ["id"] = power.Id.Entry }).ToList();
    }
}
