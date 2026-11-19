using System.Collections.Generic;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// A top-level state dictionary already carrying the build stamp every payload must report.
    ///
    /// Seeded at creation rather than stamped on the way out because the state builders have
    /// ~20 return paths (menu, unknown, error, FTUE); anything added at the end is missed by the
    /// early ones. `state_type` is seeded first so it keeps its leading position when a caller
    /// overwrites it, and "unknown" is the right answer for a path that never sets one.
    /// </summary>
    internal static Dictionary<string, object?> NewStateResult(string stateType = "unknown")
    {
        // The game loads release_info.json (Contents/Resources on macOS) once.
        // Use the same source as its version UI, not the DLL/assembly version.
        MegaCrit.Sts2.Core.Debug.ReleaseInfo? release = null;
        try { release = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo; }
        catch { /* Version metadata is optional, including singleton/Godot initialization failure. */ }
        return new Dictionary<string, object?>
        {
            ["state_type"] = stateType,
            ["mod_version"] = Version,
            ["mod_commit"] = BuildCommit,
            ["game_version"] = release?.Version,
            ["game_commit"] = release?.Commit,
            ["schema"] = StateSchema,
            // Deprecated: frozen at 12, removed in schema 2.0.0.
            ["schema_version"] = StateSchemaVersion,
            // Build-level: which verification contracts this MOD implements (sts2#1037).
            ["scenario_capabilities"] = BuildScenarioCapabilities()
        };
    }

    /// <summary>
    /// The Markdown header line naming the build: `mod 0.4.0+&lt;commit&gt; · schema 1.3.0 (schema_version 12)`.
    /// The deprecated integer is shown only beside the semver, until it is removed in schema 2.0.0.
    /// </summary>
    internal static string FormatStampLine(Dictionary<string, object?> state)
    {
        string modVersion = state.TryGetValue("mod_version", out var mv) ? mv?.ToString() ?? Version : Version;
        string modCommit = state.TryGetValue("mod_commit", out var mc) ? mc?.ToString() ?? "" : "";
        string schema = state.TryGetValue("schema", out var sc) ? sc?.ToString() ?? "" : "";
        string schemaVersion = state.TryGetValue("schema_version", out var sv) ? sv?.ToString() ?? "" : "";
        string line = $"mod {modVersion}{(modCommit.Length > 0 ? "+" + modCommit : "")}";
        if (schema.Length > 0)
            line += $" · schema {schema}{(schemaVersion.Length > 0 ? " (schema_version " + schemaVersion + ")" : "")}";
        else if (schemaVersion.Length > 0)
            line += $" · schema_version {schemaVersion}";
        return line;
    }
}
