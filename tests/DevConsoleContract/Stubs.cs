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
        public void Register(string name) => _commands.Add(name, new());
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
namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static bool Multiplayer;
        private static bool IsMultiplayerRun() => Multiplayer;
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new();
        private static Dictionary<string, object?> ExecuteWithOptionalWait(
            System.Net.HttpListenerRequest request, Dictionary<string, System.Text.Json.JsonElement> data,
            Func<Dictionary<string, object?>> action, Action<Dictionary<string, object?>>? complete)
            => throw new NotSupportedException("HTTP integration requires a live game");
        private static void SendJson(System.Net.HttpListenerResponse response, object result, bool pretty) { }
    }
    internal sealed class MainThreadUnavailableException : Exception { }
}
