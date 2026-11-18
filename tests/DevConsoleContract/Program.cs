using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
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
            Dictionary<string, JsonElement> Args(bool wait, int timeout = 500, bool includeState = true)
                => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(new
                    { wait, wait_timeout_ms = timeout, include_state = includeState }))!;
            Dictionary<string, object?> Start(string? command)
            {
                var rejected = ValidateDevConsole(command, out var name);
                return rejected ?? StartDevConsole(command!, name, out _);
            }
            void Rejected(string? command)
            {
                int calls = console.Calls;
                int dispatches = DispatchCalls;
                int reads = StateReads;
                var result = ExecuteDevConsole(null!, Args(true), command, 0);
                Check(result["status"] as string == "error" && result["success"] is false
                    && Equals(result["message"], result["error"]) && Equals(result["command"], command), "error shape");
                Check(console.Calls == calls, $"must not execute: {command}");
                if (ValidateDevConsole(command, out _) != null)
                    Check(DispatchCalls == dispatches && StateReads == reads, "pure rejection skips dispatch and snapshot");
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

            RunManager.Instance.IsSingleplayerOrFakeMultiplayer = false;
            Rejected("energy 10");
            RunManager.Instance.IsInProgress = false;
            Check(Start("energy 10")["success"] is true, "menu is not a multiplayer run");
            RunManager.Instance.IsInProgress = true;
            RunManager.Instance.IsSingleplayerOrFakeMultiplayer = true;
            RunManager.Unreadable = true;
            Rejected("energy 10");
            Check((Start("energy 10")["error"] as string)!.Contains("Cannot verify"), "run mode fails closed");
            RunManager.Unreadable = false;

            var foreignAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OtherMod"), AssemblyBuilderAccess.Run);
            var foreignType = foreignAssembly.DefineDynamicModule("Commands")
                .DefineType("OtherEnergy", TypeAttributes.Public, typeof(AbstractConsoleCmd)).CreateType()!;
            console.Override("energy", (AbstractConsoleCmd)Activator.CreateInstance(foreignType)!);
            Rejected("energy 10");
            Check((Start("energy 10")["error"] as string)!.Contains("game's assembly"), "foreign assembly refused");
            console.Register("energy");
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
            var result = StartDevConsole("energy 10", "energy", out var task);
            Check(ReferenceEquals(task, pending.Task), "retain exact CmdResult task");
            using var entered = new ManualResetEventSlim();
            var worker = Task.Run(() => { entered.Set(); CompleteDevConsole(result, task, new ActionDeadline(1000)); });
            entered.Wait();
            Check(!worker.Wait(50), "do not return while game task is pending");
            pending.SetResult(); worker.GetAwaiter().GetResult();
            Check(result["success"] is true, "successful completion");
            CompleteDevConsole(result, Task.FromException(new InvalidOperationException("async exception")), new ActionDeadline(500));
            Check(result["success"] is false && result["status"] as string == "error"
                && (result["error"] as string)!.Contains("async exception"), "async exception");
            result = DevConsoleResult("card", true, "accepted");
            CompleteDevConsole(result, Task.FromCanceled(new CancellationToken(true)), new ActionDeadline(500));
            Check(result["success"] is false, "cancellation");
            Check(result["task_completed"] is true, "faulted/cancelled tasks have finished");

            // Production pipeline: an uncompleted Task returns within one deadline, never retries.
            pending = new TaskCompletionSource();
            console.Execute = _ => new(true, "accepted", pending.Task);
            var clock = Stopwatch.StartNew();
            result = ExecuteDevConsole(null!, Args(true), "energy 10", 0);
            Check(clock.ElapsedMilliseconds >= 450 && clock.ElapsedMilliseconds < 850, "pending task respects 500 ms deadline");
            Check(result["task_completed"] is false && result["status"] as string == "error"
                && result["success"] is true && result["message"] as string == "accepted", "timeout preserves CmdResult");
            Check(!result.ContainsKey("retry") && !result.ContainsKey("settled")
                && !pending.Task.IsCompleted, "timeout neither retries, settles nor cancels game task");
            Check((result["error"] as string)!.Contains("dispatched") && (result["error"] as string)!.Contains("do not resend, re-read state"), "timeout recovery message");
            Check((int)result["waited_ms"]! >= 450, "waited_ms includes task wait");
            pending.SetResult(); // The game remains free to complete after the response.

            console.Execute = _ => new(false, "rejected with task", new TaskCompletionSource().Task);
            result = ExecuteDevConsole(null!, Args(true), "energy 10", 0);
            Check(result["task_completed"] is false && result["success"] is false && !result.ContainsKey("retry"), "unsuccessful CmdResult task is also bounded");

            // Spend most of the budget on the Task; settle must use only what remains.
            ReadState = () => new() { ["phase"] = console.Calls.ToString(), ["settled"] = false };
            console.Execute = _ => new(true, "accepted", Task.Delay(350));
            clock.Restart();
            result = ExecuteDevConsole(null!, Args(true), "energy 10", 0);
            Check(clock.ElapsedMilliseconds >= 450 && clock.ElapsedMilliseconds < 750, "Task and settle share 500 ms, not 350 + 500");
            Check(result["task_completed"] is true && result["settled"] is false && result["changed"] is true, "completed Task still polls unsettled state");
            Check((int)result["polls"]! > 0 && result.ContainsKey("state"), "settle captures final observed state");

            // A frozen settle read must not add the separate 10 s main-thread timeout.
            console.Execute = _ => new(true, "accepted", Task.Delay(350));
            DispatchDelays.Enqueue(0); DispatchDelays.Enqueue(0); DispatchDelays.Enqueue(5000);
            clock.Restart();
            result = ExecuteDevConsole(null!, Args(true), "energy 10", 0);
            Check(clock.ElapsedMilliseconds < 750 && DispatchTimeouts[^1] <= 180, "settle main-thread read uses remaining deadline");
            Check(result.ContainsKey("wait_error") && !result.ContainsKey("retry"), "post-dispatch read failure never requests retry");

            // Snapshot and dispatch also consume the same deadline.
            console.Execute = _ => new(true, "accepted", Task.Delay(400));
            DispatchDelays.Enqueue(180); DispatchDelays.Enqueue(120);
            clock.Restart();
            result = ExecuteDevConsole(null!, Args(true), "energy 10", 0);
            Check(clock.ElapsedMilliseconds < 750 && result["task_completed"] is false, "snapshot/dispatch time is subtracted from Task budget");

            int callsBefore = console.Calls;
            DispatchDelays.Enqueue(5000);
            clock.Restart();
            try { ExecuteDevConsole(null!, Args(true), "energy 10", 0); throw new Exception("expected dispatch failure"); }
            catch (MainThreadUnavailableException) { }
            Check(clock.ElapsedMilliseconds < 750 && console.Calls == callsBefore, "stalled pre-state returns within deadline without command");

            // wait:false has its own finite 10 s budget, independent of wait_timeout_ms.
            console.Execute = _ => new(true, "accepted", Task.Delay(80));
            int stateReadsBefore = StateReads;
            clock.Restart();
            result = ExecuteDevConsole(null!, Args(false, 1), "energy 10", 0);
            Check(clock.ElapsedMilliseconds >= 60 && result["task_completed"] is true, "wait:false still awaits a Task");
            Check(StateReads == stateReadsBefore && !result.ContainsKey("state") && !result.ContainsKey("settled"), "wait:false skips snapshots and settle");
            Check(DispatchTimeouts[^1] > 9000, "wait:false uses dedicated 10 s cap");
            console.Execute = _ => new(true, "accepted", new TaskCompletionSource().Task);
            clock.Restart();
            result = ExecuteDevConsole(null!, Args(false, 30000), "energy 10", 0);
            Check(clock.ElapsedMilliseconds >= 9500 && clock.ElapsedMilliseconds < 11000
                && result["task_completed"] is false && !result.ContainsKey("retry"), "wait:false pending Task returns at its dedicated deadline");

            result = DevConsoleResult("energy", true, "accepted");
            clock.Restart();
            CompleteDevConsole(result, new TaskCompletionSource().Task, new ActionDeadline(80));
            Check(clock.ElapsedMilliseconds < 300 && result["task_completed"] is false, "completion helper bounds never-finishing Tasks");

            ReadState = () => new() { ["phase"] = console.Calls.ToString(), ["settled"] = true };
            console.Execute = _ => new(true, "accepted", Task.CompletedTask);
            result = ExecuteDevConsole(null!, Args(true, 500, false), "energy 10", 0);
            Check(result["settled"] is true && !result.ContainsKey("state"), "successful settle honors include_state:false");
            console.Execute = _ => new(true, "no task");
            result = ExecuteDevConsole(null!, Args(false), "energy 10", 0);
            Check(!result.ContainsKey("task_completed"), "no Task has no task_completed field");
            var nullResult = ExecuteDevConsole(null!, Args(true), null, 0);
            Check(JsonSerializer.Serialize(nullResult, _jsonOptions).Contains("\"command\":null"), "dictionary retains command:null");

            // Clamp the public wait timeout before creating the shared deadline.
            int observedTimeout = 0;
            ExecuteWithOptionalWait(null!, Args(true, 1), () => DevConsoleResult("energy", false, "rejected"),
                (_, deadline) => observedTimeout = deadline.TimeoutMs);
            Check(observedTimeout == 500, "wait timeout clamps to 500 ms minimum");
            ExecuteWithOptionalWait(null!, Args(true, 99999), () => DevConsoleResult("energy", false, "rejected"),
                (_, deadline) => observedTimeout = deadline.TimeoutMs);
            Check(observedTimeout == 30000, "wait timeout clamps to 30000 ms maximum");

            // scenario_setup_guard (sts2#1037): parsed on the HTTP worker, re-checked on the main
            // thread in the frame that runs the command; a refusal executes nothing.
            ReadState = () => new() { ["phase"] = console.Calls.ToString(), ["settled"] = true };
            console.Execute = _ => new(true, "game result");
            Dictionary<string, JsonElement> Guarded(string guardJson, bool wait = false)
            {
                var args = Args(wait);
                args["scenario_setup_guard"] = JsonDocument.Parse(guardJson).RootElement.Clone();
                return args;
            }
            Dictionary<string, object?> GuardState(bool allowed, string screen, string? reason = null) => new()
            {
                ["version"] = 1, ["screen"] = screen, ["room"] = "MapRoom", ["combat_starting"] = false,
                ["combat_in_progress"] = false, ["run_over"] = false, ["allowed"] = allowed, ["reason"] = reason
            };
            const string MapGuard = "{\"version\":1,\"expected_screen\":\"map\"}";
            GuardNow = () => GuardState(true, "map");
            int callsBeforeGuard = console.Calls;
            result = ExecuteDevConsole(null!, Guarded(MapGuard), "energy 10", 0);
            Check(result["success"] is true && console.Calls == callsBeforeGuard + 1, "allowed guard on the expected screen runs the command");
            Check(GuardReads > 0 && GuardReadsOutsideDispatch == 0, "guard is read on the main thread, inside the dispatch that runs the command");
            Check(result.GetValueOrDefault("scenario_setup_guard") == null, "a run command carries no guard refusal");

            // Moved between the client's GET and this POST: the guard read now shows another screen.
            GuardNow = () => GuardState(true, "event");
            callsBeforeGuard = console.Calls;
            result = ExecuteDevConsole(null!, Guarded(MapGuard, wait: true), "energy 10", 0);
            Check(result["success"] is false && result["status"] as string == "error" && console.Calls == callsBeforeGuard,
                "screen moved since the GET: refused, nothing executed");
            Check((result["message"] as string)!.Contains("screen moved") && (result["message"] as string)!.Contains("not executed")
                && result["guard_refused"] is true && result["scenario_setup_guard"] is Dictionary<string, object?> moved
                && Equals(moved["screen"], "event"), "refusal names the move and returns the guard it read");
            foreach (var (blocked, reason) in new[] { (GuardState(false, "map", "combat_starting"), "combat_starting"),
                (GuardState(false, "map", "run_over"), "run_over"), (GuardState(false, "map", "resolving"), "resolving") })
            {
                GuardNow = () => blocked;
                callsBeforeGuard = console.Calls;
                result = ExecuteDevConsole(null!, Guarded(MapGuard), "card STRIKE_IRONCLAD Deck", 0);
                Check(result["success"] is false && console.Calls == callsBeforeGuard
                    && (result["message"] as string)!.Contains(reason), $"guard not allowed ({reason}) refuses");
            }
            GuardNow = () => throw new InvalidOperationException("guard boom");
            callsBeforeGuard = console.Calls;
            result = ExecuteDevConsole(null!, Guarded(MapGuard), "energy 10", 0);
            Check(result["success"] is false && console.Calls == callsBeforeGuard && (result["message"] as string)!.Contains("guard boom"),
                "an unreadable guard refuses");

            // Malformed or unknown guards are refused on the HTTP worker: no dispatch, no state read.
            GuardNow = () => GuardState(true, "map");
            foreach (var bad in new[] { "null", "[]", "{}", "{\"version\":2,\"expected_screen\":\"map\"}",
                "{\"version\":\"1\",\"expected_screen\":\"map\"}", "{\"version\":1}", "{\"version\":1,\"expected_screen\":\"shop\"}",
                "{\"version\":1,\"expected_screen\":\"map\",\"extra\":true}", "{\"version\":1,\"expected_screen\":1}" })
            {
                int dispatches = DispatchCalls, reads = GuardReads;
                callsBeforeGuard = console.Calls;
                result = ExecuteDevConsole(null!, Guarded(bad), "energy 10", 0);
                Check(result["success"] is false && result["guard_refused"] is true && console.Calls == callsBeforeGuard
                    && DispatchCalls == dispatches && GuardReads == reads, $"malformed guard refused before dispatch: {bad}");
            }

            // Empty command: never runs. With a guard it checks the guard, then keeps the exact empty rejection.
            const string EmptyRejection = "dev_console requires a non-empty string 'command'.";
            int readsBefore = GuardReads;
            result = ExecuteDevConsole(null!, Guarded(MapGuard), "", 0);
            Check(result["message"] as string == EmptyRejection && result["success"] is false && GuardReads == readsBefore + 1
                && GuardReadsOutsideDispatch == 0, "guarded empty probe passes the guard and returns the unchanged rejection");
            GuardNow = () => GuardState(false, "map", "combat_in_progress");
            result = ExecuteDevConsole(null!, Guarded(MapGuard), "", 0);
            Check(result["success"] is false && (result["message"] as string)!.Contains("combat_in_progress") && result["guard_refused"] is true,
                "guarded empty probe in a fight gets the guard refusal, not the empty rejection");
            readsBefore = GuardReads;
            result = ExecuteDevConsole(null!, Args(true), "", 0);
            Check(result["message"] as string == EmptyRejection && GuardReads == readsBefore, "unguarded empty command is unchanged");
            result = ExecuteDevConsole(null!, Guarded(MapGuard), "unlock all", 0);
            Check(result["success"] is false && GuardReads == readsBefore && !result.ContainsKey("guard_refused"),
                "a command outside the allow-list is refused as before, without reading the guard");
            Configure("{\"dev_console_enabled\":false}");
            result = ExecuteDevConsole(null!, Guarded("{}"), "energy 10", 0);
            Check((result["message"] as string)!.Contains("disabled") && GuardReads == readsBefore, "disabled console wins over the guard");
            Configure("{\"dev_console_enabled\":true}");
            GuardNow = () => GuardState(true, "map");

            // The optional deadline path must leave existing ordinary actions intact.
            result = ExecuteWithOptionalWait(null!, Args(true), () => new() { ["status"] = "ok" });
            Check(result["settled"] is true && !result.ContainsKey("task_completed"), "ordinary action uses existing settle contract");
            Console.WriteLine($"PASS: {checks} dev_console contract checks (boundary doubles; no live game).");
        }
    }
}
