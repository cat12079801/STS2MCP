using System.Reflection;
using HarmonyLib;

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

Install("InstallChoiceSourceTracking");
Install("InstallAutoPlayReservationTracking");
Install("InstallCombatLogTracking");
Install("InstallDecisionContextTracking");

Check(Field(mod, "_choiceSourceTrackingError") == null, $"choice source: {Field(mod, "_choiceSourceTrackingError")}");
Check(Field(mod, "_hookListSnapshotError") == null, $"hook list snapshot: {Field(mod, "_hookListSnapshotError")}");
Check(Field(Internal("AutoPlayReservations"), "TrackingError") == null, $"auto-play reservation: {Field(Internal("AutoPlayReservations"), "TrackingError")}");
Check(Field(Internal("EndedCombatCaptures"), "LossCaptureError") == null, $"ended combat loss: {Field(Internal("EndedCombatCaptures"), "LossCaptureError")}");
Check(Field(mod, "_decisionContextTrackingError") == null, $"decision context: {Field(mod, "_decisionContextTrackingError")}");
var installErrors = (IEnumerable<string>)Field(Internal("CombatLogStore"), "InstallErrors")!;
Check(!installErrors.Any(), "combat_log: " + string.Join("; ", installErrors));

int Patched(string owner) => Harmony.GetAllPatchedMethods().Count(m =>
{
    var info = Harmony.GetPatchInfo(m);
    return info != null && info.Owners.Contains(owner);
});
int combatLog = Patched("com.sts2mcp.combatlog");
int decision = Patched("com.sts2mcp.decisioncontext");
int choice = Patched("com.sts2mcp.choicesource");
// combat_log: history 1, hooks 53 (one overload each in v0.113.1), modify 5, piles 6, cards 3, creatures 10, energy 1,
// potions 3, powers 5, flash 2, phases 12.
Check(combatLog == 101, $"combat_log patched {combatLog} methods, expected 101");
Check(decision == 9, $"decision_context patched {decision} methods, expected 9");
Check(choice == 8, $"choice_source patched {choice} methods, expected 8");

// The phases resolve to the private overloads that take the turn state (not the forwarding parameterless ones).
foreach (var name in new[] { "EndCombatInternal", "EndPlayerTurnPhaseOneInternal", "EndPlayerTurnPhaseTwoInternal" })
{
    var patched = Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType?.Name == "CombatManager" && m.Name == name).ToList();
    Check(patched.Count == 1 && patched[0].GetParameters().Length == 1 && ((MethodInfo)patched[0]).IsPrivate,
        $"{name}: patch only the private overload that takes CombatTurnState");
}
Console.WriteLine($"PatchTargetsContract: {checks} checks passed (combat_log {combatLog}, decision_context {decision}, choice_source {choice})");

static class GdConsole
{
    public static bool Write(object[] __args)
    {
        Console.Error.WriteLine("[GD] " + string.Join(" ", __args.SelectMany(a => a is object[] many ? many : new[] { a })));
        return false;
    }
}
