using System.Text.Json;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;

STS2_MCP.McpMod.RunContractChecks();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        public static void RunContractChecks()
        {
            int checks = 0;
            void Check(bool condition, string label)
            {
                if (!condition) throw new Exception(label);
                checks++;
            }
            void Configure(string json)
            {
                using var config = JsonDocument.Parse(json);
                ConfigureDevConsole(config.RootElement);
            }
            var console = new DevConsole();
            NDevConsole.Current = new NDevConsole(console);
            Dictionary<string, object?> Start(string? command) => StartDevConsole(command, out _);
            void Rejected(string? command)
            {
                int calls = console.Calls;
                var result = Start(command);
                Check(result["status"] as string == "error" && result["success"] is false
                    && Equals(result["message"], result["error"]) && Equals(result["command"], command), "error shape");
                Check(console.Calls == calls, $"must not execute: {command}");
            }

            // Explicit opt-in only, including upgrades from an old config.
            Rejected("energy 10");
            foreach (var config in new[] { "{}", "{\"dev_console_enabled\":false}",
                "{\"dev_console_enabled\":\"true\"}", "{\"dev_console_enabled\":1}",
                "{\"dev_console_enabled\":null}" })
            {
                Configure(config);
                Rejected("energy 10");
            }
            Configure("{\"dev_console_enabled\":true}");
            foreach (var command in new string?[] { null, "", " \t\n ", "unlock", "UNLOCK all",
                "cloud", "achievement", "leaderboard", "multiplayer", "sentry", "getlogs", "open",
                "art", "trailer", "bestiary", "ancient", "instant", "help", "clear", "exit", "remove",
                "unknown", "energy\t10", "energy\n10", "energy;unlock", "energy\u00a010" })
                Rejected(command);

            // All intended names work; only the game's spelling remove_card is accepted.
            foreach (var name in "card draw energy block relic potion power damage heal kill fight gold upgrade enchant afflict remove_card stars godmode room event act travel win die dump log".Split(' '))
            {
                console.Register(name);
                Check(Start(name)["success"] is true, $"allowed: {name}");
            }
            string original = " \tCARD  BURNING_PACT Draw \n";
            var success = Start(original);
            Check(console.LastCommand == original && Equals(success["command"], original), "forward original input verbatim");
            Check(success["message"] as string == "game result" && !success.ContainsKey("error"), "success shape");

            Multiplayer = true; Rejected("energy 10"); Multiplayer = false;
            NDevConsole.Current = null; Rejected("energy 10");
            NDevConsole.Current = new(null); Rejected("energy 10");
            NDevConsole.Current = new(console) { Ready = false }; Rejected("energy 10");
            NDevConsole.Current = new(console) { InsideTree = false }; Rejected("energy 10");
            NDevConsole.Current = new(console) { Valid = false }; Rejected("energy 10");
            NDevConsole.Current = new(new DevConsole());
            Check((Start("energy 10")["message"] as string)!.Contains("debug commands may be disabled"), "missing registration");
            NDevConsole.Current = new(console);
            console.Execute = _ => new(false, "game rejected");
            Check(Start("energy 10")["error"] as string == "game rejected", "preserve game failure");
            console.Execute = _ => throw new InvalidOperationException("game exception");
            Check((Start("energy 10")["error"] as string)!.Contains("game exception"), "sync exception");

            // The actual production completion helper must wait and surface async failure/cancellation.
            var pending = new TaskCompletionSource();
            console.Execute = _ => new(true, "pending", pending.Task);
            var result = StartDevConsole("energy 10", out var task);
            Check(ReferenceEquals(task, pending.Task), "retain exact CmdResult task");
            using var entered = new ManualResetEventSlim();
            var worker = Task.Run(() => { entered.Set(); CompleteDevConsole(result, task); });
            entered.Wait();
            Check(!worker.Wait(50), "do not return while game task is pending");
            pending.SetResult(); worker.GetAwaiter().GetResult();
            Check(result["success"] is true, "successful completion");
            CompleteDevConsole(result, Task.FromException(new InvalidOperationException("async exception")));
            Check(result["success"] is false && result["status"] as string == "error"
                && (result["error"] as string)!.Contains("async exception"), "async exception");
            result = DevConsoleResult("card", true, "accepted");
            CompleteDevConsole(result, Task.FromCanceled(new CancellationToken(true)));
            Check(result["success"] is false, "cancellation");
            Console.WriteLine($"PASS: {checks} dev_console contract checks (boundary doubles; no live game).");
        }
    }
}
