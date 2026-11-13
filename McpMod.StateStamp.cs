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
        var release = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo;
        return new Dictionary<string, object?>
        {
            ["state_type"] = stateType,
            ["mod_version"] = Version,
            ["mod_commit"] = BuildCommit,
            ["game_version"] = release?.Version,
            ["game_commit"] = release?.Commit,
            ["schema_version"] = StateSchemaVersion
        };
    }
}
