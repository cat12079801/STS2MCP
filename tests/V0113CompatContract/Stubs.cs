namespace MegaCrit.Sts2.Core.Debug
{
    public sealed record ReleaseInfo(string Version, string Commit);
    public sealed class ReleaseInfoManager
    {
        private static readonly ReleaseInfoManager Singleton = new();
        public static bool FailInstance { get; set; }
        public static bool FailReleaseInfo { get; set; }
        public static ReleaseInfoManager Instance => FailInstance
            ? throw new InvalidOperationException("Godot initialization failed") : Singleton;
        private ReleaseInfo? _releaseInfo;
        public ReleaseInfo? ReleaseInfo
        {
            get => FailReleaseInfo ? throw new InvalidOperationException("release info unavailable") : _releaseInfo;
            set => _releaseInfo = value;
        }
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
