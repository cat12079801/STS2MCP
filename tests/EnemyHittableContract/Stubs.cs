// Boundary double: IsHittable is a configured answer (the game's IsDead + Hook.ShouldAllowHitting
// is not re-implemented here). HP/alive are deliberately independent so the test can prove the
// builder never derives hittable from them.
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public int CurrentHp = 10;
        public bool IsAlive => CurrentHp > 0;
        public Func<bool> Hittable = () => true;
        public int Reads;
        public bool IsHittable { get { Reads++; return Hittable(); } }
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
