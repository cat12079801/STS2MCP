using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static bool _devConsoleEnabled;

    // Exact CmdName values, not class names. RemoveCardConsoleCmd uses remove_card.
    private static readonly HashSet<string> DevConsoleAllowedCommands = new(StringComparer.Ordinal)
    {
        "card", "draw", "energy", "block", "relic", "potion", "power", "damage",
        "heal", "kill", "fight", "gold", "upgrade", "enchant", "afflict", "remove_card",
        "stars", "godmode", "room", "event", "act", "travel", "win", "die", "dump", "log"
    };

    private static void ConfigureDevConsole(JsonElement config)
    {
        // Missing keys and every value other than the JSON boolean true fail closed.
        _devConsoleEnabled = config.TryGetProperty("dev_console_enabled", out var enabled)
            && enabled.ValueKind == JsonValueKind.True;
    }

    private static Dictionary<string, object?> DevConsoleResult(string? command, bool success, string message)
    {
        var result = new Dictionary<string, object?>
        {
            ["command"] = command,
            ["success"] = success,
            ["message"] = message,
            ["status"] = success ? "ok" : "error"
        };
        if (!success) result["error"] = message;
        return result;
    }

    // Pure request checks run on the HTTP worker, before snapshots or main-thread dispatch.
    private static Dictionary<string, object?>? ValidateDevConsole(string? command, out string name)
    {
        name = "";
        if (!_devConsoleEnabled)
            return DevConsoleResult(command, false,
                "dev_console is disabled. Set dev_console_enabled to true in STS2_MCP.conf and restart the game.");
        if (string.IsNullOrWhiteSpace(command))
            return DevConsoleResult(command, false, "dev_console requires a non-empty string 'command'.");

        // Match DevConsole.ProcessCommand exactly. Do not rewrite the dispatched string.
        name = command.Trim().Split(' ')[0].ToLowerInvariant();
        return DevConsoleAllowedCommands.Contains(name) ? null
            : DevConsoleResult(command, false, $"Console command '{name}' is not allowed by dev_console.");
    }

    // Called only by the main-thread dispatcher after ValidateDevConsole. Never create a
    // second console or register commands ourselves: the initialized registry is authoritative.
    private static Dictionary<string, object?> StartDevConsole(string command, string name, out Task? task)
    {
        task = null;
        try
        {
            var run = RunManager.Instance;
            if (run.IsInProgress && !run.IsSingleplayerOrFakeMultiplayer)
                return DevConsoleResult(command, false, "dev_console is only supported by the singleplayer endpoint outside multiplayer runs.");
        }
        catch (Exception)
        {
            return DevConsoleResult(command, false, "Cannot verify singleplayer run mode; dev_console execution refused.");
        }

        try
        {
            NDevConsole node;
            try { node = NDevConsole.Instance; }
            catch (InvalidOperationException)
            {
                return DevConsoleResult(command, false, "The game's dev console has not been created.");
            }
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.IsNodeReady())
                return DevConsoleResult(command, false, "The game's dev console is not ready or is no longer in the scene tree.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (typeof(NDevConsole).GetField("_devConsole", flags)?.GetValue(node) is not DevConsole console)
                return DevConsoleResult(command, false, "The game's dev console is not initialized or its _devConsole field is unavailable.");
            if (typeof(DevConsole).GetField("_commands", flags)?.GetValue(console)
                is not Dictionary<string, AbstractConsoleCmd> commands)
                return DevConsoleResult(command, false, "Cannot inspect the game's dev console command registry; execution refused.");
            if (!commands.TryGetValue(name, out var implementation))
                return DevConsoleResult(command, false,
                    $"Console command '{name}' is unavailable in the game's registry (debug commands may be disabled).");

            if (implementation == null || implementation.GetType().Assembly != typeof(DevConsole).Assembly)
                return DevConsoleResult(command, false,
                    $"Console command '{name}' is not implemented by the game's assembly (possibly overridden by another mod); execution refused.");

            CmdResult cmdResult = console.ProcessCommand(command);
            task = cmdResult.task;
            return DevConsoleResult(command, cmdResult.success, cmdResult.msg);
        }
        catch (Exception ex)
        {
            return DevConsoleResult(command, false, $"Console command failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CompleteDevConsole(Dictionary<string, object?> result, Task? task, ActionDeadline deadline)
    {
        // HTTP worker only; unsuccessful CmdResults can also carry a Task.
        if (task == null) return;
        try
        {
            bool completed;
            try { completed = task.Wait(deadline.RemainingMs); }
            catch (AggregateException)
            {
                // Preserve the original exception/cancellation rather than AggregateException.
                task.GetAwaiter().GetResult();
                throw;
            }
            result["task_completed"] = completed;
            if (!completed)
            {
                result["status"] = "error";
                result["error"] = $"Console command was dispatched to the game but its task did not finish within {deadline.TimeoutMs} ms; do not resend, re-read state.";
                // Keep CmdResult.success/message. The game still owns and observes the Task.
                return;
            }
        }
        catch (Exception ex)
        {
            string message = $"Console command task failed: {ex.GetType().Name}: {ex.Message}";
            result["task_completed"] = true;
            result["success"] = false;
            result["status"] = "error";
            result["message"] = message;
            result["error"] = message;
        }
    }

    private static Dictionary<string, object?> ExecuteDevConsole(
        HttpListenerRequest request, Dictionary<string, JsonElement> parsed, string? command, long apiRequest)
    {
        var rejected = ValidateDevConsole(command, out var name);
        if (rejected != null) return rejected;

        Task? commandTask = null;
        return ExecuteWithOptionalWait(request, parsed,
            Recorder.CombatRecorder.InHandler(apiRequest, () => StartDevConsole(command!, name, out commandTask)),
            (dispatched, deadline) => CompleteDevConsole(dispatched, commandTask, deadline));
    }

    private static string HandleDevConsole(
        HttpListenerRequest request, HttpListenerResponse response,
        Dictionary<string, JsonElement> parsed, long apiRequest, bool pretty)
    {
        string? command = parsed.TryGetValue("command", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        Dictionary<string, object?> result;
        try
        {
            result = ExecuteDevConsole(request, parsed, command, apiRequest);
        }
        catch (MainThreadUnavailableException ex)
        {
            response.StatusCode = 503;
            result = DevConsoleResult(command, false, ex.Message);
            result["retry"] = true;
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            result = DevConsoleResult(command, false, $"dev_console failed: {ex.GetType().Name}: {ex.Message}");
        }

        // JSON escaping keeps user-controlled newlines from forging extra log entries.
        GD.Print($"[STS2 MCP] dev_console {JsonSerializer.Serialize(new
        {
            command,
            success = result["success"],
            message = result["message"],
            error = result.GetValueOrDefault("error"),
            task_completed = result.GetValueOrDefault("task_completed")
        }, _jsonOptions)}");
        SendJson(response, result, pretty);
        return (string)result["status"]!;
    }
}
