// Minimal boundary doubles. No game, Godot native library, HTTP listener or network is used.
namespace Godot
{
    public class GodotObject
    {
        public bool Valid = true;
        public static bool IsInstanceValid(GodotObject value) => value.Valid;
    }
    public static class GD { public static void Print(string value) { } }
}
namespace MegaCrit.Sts2.Core.DevConsole.ConsoleCommands
{
    public class AbstractConsoleCmd { }
}
namespace MegaCrit.Sts2.Core.DevConsole
{
    public readonly record struct CmdResult(bool success, string msg, Task? task = null);
    public class DevConsole
    {
        private readonly Dictionary<string, ConsoleCommands.AbstractConsoleCmd> _commands = new();
        public int Calls;
        public string? LastCommand;
        public Func<string, CmdResult> Execute = _ => new(true, "game result");
        public void Register(string name) => _commands[name] = new();
        public void Override(string name, ConsoleCommands.AbstractConsoleCmd command) => _commands[name] = command;
        public CmdResult ProcessCommand(string command)
        {
            Calls++;
            LastCommand = command;
            return Execute(command);
        }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Debug
{
    public class NDevConsole : Godot.GodotObject
    {
        public static NDevConsole? Current;
        public static NDevConsole Instance => Current ?? throw new InvalidOperationException();
        private readonly DevConsole.DevConsole? _devConsole;
        public NDevConsole(DevConsole.DevConsole? console) { _devConsole = console; }
        public DevConsole.DevConsole? ConsoleForTest => _devConsole;
        public bool Ready = true;
        public bool InsideTree = true;
        public bool IsNodeReady() => Ready;
        public bool IsInsideTree() => InsideTree;
    }
}
namespace STS2_MCP.Recorder
{
    public static class CombatRecorder
    {
        public static Func<T> InHandler<T>(long id, Func<T> action) => action;
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public class RunManager
    {
        public static bool Unreadable;
        private static readonly RunManager Current = new();
        public static RunManager Instance => Unreadable ? throw new InvalidOperationException() : Current;
        public bool IsInProgress { get; set; } = true;
        public bool IsSingleplayerOrFakeMultiplayer { get; set; } = true;
    }
}
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private const int MainThreadTimeoutMs = 10_000;
        private const int SettleDefaultTimeoutMs = 10_000;
        private const int SettleMinTimeoutMs = 500;
        private const int SettleMaxTimeoutMs = 30_000;
        private const int SettlePollIntervalMs = 60;
        private const int SettleConsecutivePolls = 3;
        private const int SettleNoChangeGiveUpMs = 3_000;
        private static int DispatchCalls;
        private static int StateReads;
        private static readonly Queue<int> DispatchDelays = new();
        private static readonly List<int> DispatchTimeouts = new();
        private static Func<Dictionary<string, object?>> ReadState = () => new() { ["phase"] = "before", ["settled"] = true };
        private static T RunOnMainThreadBlocking<T>(Func<T> action, int timeoutMs = MainThreadTimeoutMs)
        {
            DispatchCalls++;
            DispatchTimeouts.Add(timeoutMs);
            int delay = DispatchDelays.Count == 0 ? 0 : DispatchDelays.Dequeue();
            Thread.Sleep(Math.Min(delay, timeoutMs));
            if (delay > timeoutMs) throw new MainThreadUnavailableException("simulated stalled frames");
            return action();
        }
        private static Dictionary<string, object?> BuildGameState() { StateReads++; return ReadState(); }
        private static string StateFingerprint(Dictionary<string, object?> state) => (string)state["phase"]!;
        private static bool IsSettledState(Dictionary<string, object?> state) => state["settled"] is true;
        private static void AppendRewardActionOutcome(Dictionary<string, object?> result) { }
        private static Dictionary<string, object?> EnsureStatus(Dictionary<string, object?> result)
        {
            if (!result.ContainsKey("status")) result["status"] = result.ContainsKey("error") ? "error" : "ok";
            return result;
        }
        // Only parameter parsing is doubled; production dispatch/wait/settle code is linked.
        private static bool ReadFlagParam(Dictionary<string, System.Text.Json.JsonElement> data,
            System.Net.HttpListenerRequest request, string key, bool fallback)
            => data.TryGetValue(key, out var value) ? value.GetBoolean() : fallback;
        private static int? ReadOptionalInt(Dictionary<string, System.Text.Json.JsonElement> data, string key)
            => data.TryGetValue(key, out var value) ? value.GetInt32() : null;
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        private static void SendJson(System.Net.HttpListenerResponse response, object result, bool pretty) { }
    }
    internal sealed class MainThreadUnavailableException(string message) : Exception(message) { }
}
