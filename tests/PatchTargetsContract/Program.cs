using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;

// Calls the MOD's own install methods (the same code Initialize runs) against the real sts2.dll / 0Harmony.dll and
// checks that every group bound and patched. Nothing is invoked in the game: patching only rewrites the methods.
var mod = typeof(STS2_MCP.McpMod);
const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Install(string method) => (mod.GetMethod(method, Static) ?? throw new MissingMethodException(mod.Name, method)).Invoke(null, null);
object? Field(Type type, string name) => type.GetField(name, Static) is { } f ? f.GetValue(null)
    : (type.GetProperty(name, Static) ?? throw new MissingMemberException(type.Name, name)).GetValue(null);
Type Internal(string name) => mod.Assembly.GetType("STS2_MCP." + name) ?? throw new TypeLoadException(name);

// Outside the engine GD.Print* reaches native Godot and crashes the process: route the MOD's failure messages to the
// console (test-only patch) so a failing group is reported instead of a segmentation fault.
var console = new Harmony("test.gdconsole");
foreach (var method in typeof(Godot.GD).GetMethods(BindingFlags.Public | BindingFlags.Static)
             .Where(m => m.Name is "Print" or "PrintErr" or "PushError" or "PushWarning"))
    console.Patch(method, prefix: new HarmonyMethod(typeof(GdConsole), nameof(GdConsole.Write)));

// Initialize's order. After each step, note how many patches each method carries: the step that last changed a method
// is when Harmony compiled its replacement. The last step is the combat recorder's patches
// (Recorder.CombatRecorder.StartIfConfigured → Install("startup") → RecorderPatches.Apply, after LoadPort): applied here
// directly, since StartIfConfigured reads the config file and opens the record directory.
var recorderApply = mod.Assembly.GetType("STS2_MCP.Recorder.RecorderPatches")?.GetMethod("Apply", Static)
    ?? throw new MissingMethodException("RecorderPatches", "Apply");
var steps = new[] { "TryApplyHarmonyPatches", "InstallRewardTracking", "InstallChoiceSourceTracking",
    "InstallAutoPlayReservationTracking", "InstallCombatLogTracking", "InstallDecisionContextTracking", "RecorderPatches.Apply" };
void Step(string name)
{
    if (name == "RecorderPatches.Apply") recorderApply.Invoke(null, null);
    else Install(name);
}
var lastChanged = new Dictionary<MethodBase, int>();
var firstPatched = new Dictionary<MethodBase, int>();
var patchCount = new Dictionary<MethodBase, int>();
for (int step = 0; step < steps.Length; step++)
{
    Step(steps[step]);
    foreach (var m in Harmony.GetAllPatchedMethods())
    {
        var info = Harmony.GetPatchInfo(m);
        if (info == null || info.Owners.All(o => o.StartsWith("test.", StringComparison.Ordinal))) continue;
        int n = info.Prefixes.Count + info.Postfixes.Count + info.Transpilers.Count + info.Finalizers.Count;
        if (!patchCount.TryGetValue(m, out int before) || before != n) lastChanged[m] = step;
        firstPatched.TryAdd(m, step);
        patchCount[m] = n;
    }
}

Check(Field(mod, "_choiceSourceTrackingError") == null, $"choice source: {Field(mod, "_choiceSourceTrackingError")}");
Check(Field(mod, "_hookListSnapshotError") == null, $"hook list snapshot: {Field(mod, "_hookListSnapshotError")}");
var beforeHandDraw = AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.BeforeHandDraw),
    new[] { typeof(ICombatState), typeof(Player), typeof(PlayerChoiceContext) })!;
var beforeDrawPatches = Harmony.GetPatchInfo(beforeHandDraw);
Check(beforeDrawPatches.Prefixes.Any(p => p.owner == "com.sts2mcp.hooklistsnapshot")
    && beforeDrawPatches.Postfixes.Any(p => p.owner == "com.sts2mcp.hooklistsnapshot"),
    "BeforeHandDraw records the draw-pile membership at the start of each actual stage list");
const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
var hookFrame = beforeHandDraw.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
Check(new[] { "combatState", "player", "playerChoiceContext", "<>7__wrap1", "<model>5__3" }
    .All(name => hookFrame.GetField(name, Instance) != null), "BeforeHandDraw's two loops share the active iterator and model fields");
var setupFrame = AccessTools.DeclaredMethod(typeof(CombatManager), "SetupPlayerTurn")!
    .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
Check(new[] { "player", "playerChoiceContext", "<state>5__2" }
    .All(name => setupFrame.GetField(name, Instance) != null), "SetupPlayerTurn holds the same player, context and combat across BeforeHandDraw");
Check(Field(Internal("AutoPlayReservations"), "TrackingError") == null, $"auto-play reservation: {Field(Internal("AutoPlayReservations"), "TrackingError")}");
Check(Field(Internal("EndedCombatCaptures"), "LossCaptureError") == null, $"ended combat loss: {Field(Internal("EndedCombatCaptures"), "LossCaptureError")}");
Check(Field(mod, "_decisionContextTrackingError") == null, $"decision context: {Field(mod, "_decisionContextTrackingError")}");
var installErrors = (IEnumerable<string>)Field(Internal("CombatLogStore"), "InstallErrors")!;
Check(!installErrors.Any(), "combat_log: " + string.Join("; ", installErrors));

// The inlining hazard: a method Harmony compiled (patched) before one of its callees got a patch may have that callee
// inlined into its replacement, so the callee's patch never runs from there (CardPlayStarted / the private Add, live
// check of b692360). Walk each patched method's IL, through unpatched game methods up to 3 calls deep. Only direct calls
// (call / callvirt / newobj) can be inlined: a method only referenced (ldftn for a delegate) is not. The game runs on
// .NET 9.0.7 (sts2.runtimeconfig.json), whose JIT never inlines a method with exception handling: a callee with try /
// catch / finally is compiled on its own (after every patch is on), so neither it nor anything it calls is a hazard.
var gameAssembly = typeof(MegaCrit.Sts2.Core.Combat.History.CombatHistory).Assembly;
var hazards = new List<string>();
static bool HasExceptionHandling(MethodBase m)
{
    try { return m.GetMethodBody()?.ExceptionHandlingClauses.Count > 0; }
    catch { return false; }
}
foreach (var (method, compiledAt) in lastChanged)
{
    var seen = new HashSet<MethodBase>();
    void Walk(MethodBase current, int depth, string path)
    {
        if (depth > 3 || !seen.Add(current)) return;
        List<CodeInstruction> body;
        try { body = PatchProcessor.GetOriginalInstructions(current); }
        catch { return; }
        foreach (var callee in body.Where(i => i.opcode == System.Reflection.Emit.OpCodes.Call || i.opcode == System.Reflection.Emit.OpCodes.Callvirt
                     || i.opcode == System.Reflection.Emit.OpCodes.Newobj).Select(i => i.operand).OfType<MethodBase>())
        {
            if (callee.DeclaringType?.Assembly != gameAssembly || HasExceptionHandling(callee)) continue;
            if (firstPatched.TryGetValue(callee, out int patchedAt))
            {
                if (patchedAt > compiledAt) hazards.Add($"{path} -> {callee.DeclaringType!.Name}.{callee.Name} (patched at {steps[patchedAt]}, caller compiled at {steps[compiledAt]})");
                continue;
            }
            // Virtual and abstract callees are not followed: the JIT inlines them only after devirtualizing the call
            // (a sealed type or exact receiver type), or by guarded devirtualization, which needs profile data that a
            // Harmony replacement (a DynamicMethod, never tiered up) does not collect. Theoretical, so not walked.
            if (callee.IsAbstract || callee.IsVirtual) continue;
            Walk(callee, depth + 1, $"{path} -> {callee.DeclaringType!.Name}.{callee.Name}");
        }
    }
    Walk(method, 0, $"{method.DeclaringType?.Name}.{method.Name}");
}
Check(hazards.Count == 0, "patched callee may be inlined into an earlier-compiled replacement: " + string.Join("; ", hazards));

int Patched(string owner) => Harmony.GetAllPatchedMethods().Count(m =>
{
    var info = Harmony.GetPatchInfo(m);
    return info != null && info.Owners.Contains(owner);
});
int combatLog = Patched("com.sts2mcp.combatlog");
int decision = Patched("com.sts2mcp.decisioncontext");
int choice = Patched("com.sts2mcp.choicesource");
// combat_log: history 18 (CombatHistory's public recording methods), hooks 53, modify 5 + 1 dry-run guard,
// piles 6, cards 3, creatures 10, energy 1, potions 3, powers 5, flash 2, phases 12.
Check(combatLog == 119, $"combat_log patched {combatLog} methods, expected 119");

// Every history entry has an emitting path that cannot be inlined away: each public recording method of CombatHistory
// (each makes one *Entry) carries the combat_log prefix and postfix itself.
var historyType = typeof(MegaCrit.Sts2.Core.Combat.History.CombatHistory);
var recorders = historyType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
    .Where(m => m.ReturnType == typeof(void) && !m.IsSpecialName && m.Name != "Clear").ToList();
var entryTypes = historyType.Assembly.GetTypes()
    .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry))).ToList();
Check(recorders.Count == 18 && entryTypes.Count == recorders.Count, $"{recorders.Count} recording methods for {entryTypes.Count} entry types");
foreach (var recorder in recorders)
{
    var info = Harmony.GetPatchInfo(recorder);
    Check(info != null && info.Postfixes.Any(p => p.owner == "com.sts2mcp.combatlog") && info.Prefixes.Any(p => p.owner == "com.sts2mcp.combatlog"),
        $"CombatHistory.{recorder.Name} carries the combat_log history patches");
    var built = PatchProcessor.GetOriginalInstructions(recorder).Select(i => i.operand).OfType<ConstructorInfo>()
        .Where(c => entryTypes.Contains(c.DeclaringType!)).Select(c => c.DeclaringType!).ToList();
    Check(built.Count == 1, $"CombatHistory.{recorder.Name} builds exactly one entry ({built.Count})");
    entryTypes.Remove(built[0]);
}
Check(entryTypes.Count == 0, "every *Entry type is built by a patched recording method: " + string.Join(", ", entryTypes.Select(t => t.Name)));

Check(decision == 9, $"decision_context patched {decision} methods, expected 9");
Check(choice == 8, $"choice_source patched {choice} methods, expected 8");

// The phases resolve to the private overloads that take the turn state (not the forwarding parameterless ones).
foreach (var name in new[] { "EndCombatInternal", "EndPlayerTurnPhaseOneInternal", "EndPlayerTurnPhaseTwoInternal" })
{
    var patched = Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType?.Name == "CombatManager" && m.Name == name).ToList();
    Check(patched.Count == 1 && patched[0].GetParameters().Length == 1 && ((MethodInfo)patched[0]).IsPrivate,
        $"{name}: patch only the private overload that takes CombatTurnState");
}
EntropicBrewChecks.Run(mod, Check);
Console.WriteLine($"PatchTargetsContract: {checks} checks passed (combat_log {combatLog}, decision_context {decision}, choice_source {choice}, {lastChanged.Count} patched methods scanned for inlining)");

static class GdConsole
{
    public static bool Write(object[] __args)
    {
        Console.Error.WriteLine("[GD] " + string.Join(" ", __args.SelectMany(a => a is object[] many ? many : new[] { a })));
        return false;
    }
}
