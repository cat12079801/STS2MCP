// Boundary doubles. HpDisplay is the game's enum (same member order as sts2-a671e9ef); IsInfinite is a configured
// answer that counts its calls (the game's switch is not re-implemented here), so the test can prove the builder
// passes on whatever the game's extension says instead of comparing the enum itself. HP is deliberately
// independent so the test can prove nothing is derived from it.
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public enum HpDisplay
    {
        Normal,
        InfiniteWithNumbers,
        InfiniteWithoutNumbers
    }

    public static class HpDisplayExtensions
    {
        public static Func<HpDisplay, bool> Answer = _ => throw new InvalidOperationException("answer not configured");
        public static List<HpDisplay> Calls = new();
        public static bool IsInfinite(this HpDisplay display)
        {
            Calls.Add(display);
            return Answer(display);
        }
    }

    public class Creature
    {
        public int CurrentHp = 10;
        public int MaxHp = 10;
        public bool IsAlive => CurrentHp > 0;
        public Func<HpDisplay> Display = () => HpDisplay.Normal;
        public int Reads;
        public HpDisplay HpDisplay { get { Reads++; return Display(); } }
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
