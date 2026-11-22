using System;
using System.Collections.Generic;
using System.Globalization;

namespace STS2_MCP;

// CurrentCombatId and DebugOnlyGetState are read together on the game thread. Reference equality
// also protects the bindings to the live card holders and the combat log.
internal sealed class CombatAttempt(object combat, int number, string session)
{
    internal object Combat { get; } = combat;
    internal int Number { get; } = number;
    internal string CombatId { get; } = number.ToString(CultureInfo.InvariantCulture);
    internal string Id => $"{sessionId}/{CombatId}";
    private readonly string sessionId = session;

    internal bool Matches(CombatAttempt? other) =>
        other != null && ReferenceEquals(Combat, other.Combat) && Number == other.Number && sessionId == other.sessionId;

    internal bool IsLive(bool idIsLive, object? currentCombat) => idIsLive && ReferenceEquals(Combat, currentCombat);

    internal static Dictionary<string, object?> Identity(CombatAttempt? attempt, string reason)
    {
        var row = new Dictionary<string, object?> { ["known"] = attempt != null, ["attempt_id"] = attempt?.Id };
        if (attempt == null) row["unavailable_reasons"] = new List<string> { reason };
        return row;
    }

    internal static Dictionary<string, object?> ChoiceIdentity(CombatAttempt? attempt, OpenCardSelection? record, string reason)
    {
        bool known = attempt?.Matches(record?.Attempt) == true;
        var row = new Dictionary<string, object?>
        {
            ["known"] = known,
            ["attempt_id"] = attempt?.Id,
            ["selection_id"] = known ? $"{attempt!.Id}/{record!.Sequence.ToString(CultureInfo.InvariantCulture)}" : null
        };
        if (!known) row["unavailable_reasons"] = new List<string> { reason };
        return row;
    }
}
