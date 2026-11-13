namespace MegaCrit.Sts2.Core.Debug
{
    public sealed record ReleaseInfo(string Version, string Commit);
    public sealed class ReleaseInfoManager
    {
        public static ReleaseInfoManager Instance { get; } = new();
        public ReleaseInfo? ReleaseInfo { get; set; }
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public sealed class Creature
    {
        public List<Power> Powers { get; } = new();
    }
    public class Power(int amount)
    {
        public int Amount => amount;
        public string Title => GetType().Name;
        public (string Entry, int Dummy) Id => (GetType().Name, 0);
    }
    public sealed class SlipperyPower(int amount) : Power(amount);
    public sealed class IntangiblePower() : Power(1);
    public sealed class HardToKillPower(int amount) : Power(amount);
    public sealed class HardenedShellPower(int amount) : Power(amount);
}

namespace Godot
{
    public static class GD
    {
        public static List<string> Warnings { get; } = new();
        public static List<string> Errors { get; } = new();
        public static void PushWarning(string message) => Warnings.Add(message);
        public static void PushError(string message) => Errors.Add(message);
    }
}
