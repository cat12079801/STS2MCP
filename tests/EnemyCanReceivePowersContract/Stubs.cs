// Boundary double: CanReceivePowers and IsHittable are configured answers (the game's
// CombatState check + Hook.ShouldAllowHitting is not re-implemented here). HP/alive and IsHittable are
// deliberately independent so the test can prove the builder never derives can_receive_powers from them.
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public int CurrentHp = 10;
        public bool IsAlive => CurrentHp > 0;
        public Func<bool> Receives = () => true;
        public Func<bool> Hittable = () => true;
        public int Reads;
        public int HittableReads;
        public bool CanReceivePowers { get { Reads++; return Receives(); } }
        public bool IsHittable { get { HittableReads++; return Hittable(); } }
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
