using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> BuildRunState(RunState runState)
    {
        var row = new Dictionary<string, object?>
        {
            ["act"] = runState.CurrentActIndex + 1,
            ["floor"] = runState.TotalFloor,
            ["player_count"] = runState.Players.Count,
            ["card_multiplayer_constraint"] = ((IRunState)runState).CardMultiplayerConstraint.ToString(),
            ["ascension"] = runState.AscensionLevel
        };
        // Melted and empty PaelsTooth instances still count as owned.
        if (RunHasPaelsTooth(runState))
            AttachAllowlistedState(row, "run", runState, "run");
        return row;
    }

    private static bool RunHasPaelsTooth(RunState runState)
        => runState.Players.Any(player => player.Relics.Any(relic => relic is PaelsTooth));

    // FTUE short-circuits screen detection before the common run row is attached.
    private static Dictionary<string, object?> WithRelevantFtueRunState(Dictionary<string, object?> ftueState)
    {
        if (RunManager.Instance.IsInProgress && RunManager.Instance.DebugOnlyGetState() is { } runState
            && RunHasPaelsTooth(runState))
            ftueState["run"] = BuildRunState(runState);
        return ftueState;
    }
}
