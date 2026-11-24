using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int _checks;
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static Dictionary<string, string> Reasons(Dictionary<string, object?> state)
        => (Dictionary<string, string>)state["unavailable_reasons"]!;

    public static void Main()
    {
        CheckStarX();
        CheckArrows();
        CheckWiring();
        Console.WriteLine($"ThumbscrewStateContract: {_checks} checks passed");
    }

    private static void CheckWiring()
    {
        static string Source(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", $"McpMod.{name}.cs"));
        string builder = Source("StateBuilder");
        int player = builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal);
        int hand = builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal);
        string playerBuilder = builder[player..hand];
        int guard = playerBuilder.IndexOf("if (creature != null && combatState != null && CombatManager.Instance.IsInProgress)", StringComparison.Ordinal);
        int arrows = playerBuilder.IndexOf("state[\"arrow_state\"] = BuildArrowState(player, creature.CombatState)", StringComparison.Ordinal);
        int end = playerBuilder.IndexOf("\n        }\n\n        state[\"gold\"]", StringComparison.Ordinal);
        Check(guard >= 0 && arrows > guard && arrows < end &&
            !playerBuilder[guard..arrows].Contains("if (screen", StringComparison.Ordinal),
            "arrow and pile player state are combat-scoped, including selection inputs");
        Check(builder.Contains("result[\"player\"] = BuildPlayerState(_player,") &&
            Source("MultiplayerState").Contains("result[\"player\"] = BuildPlayerState(localPlayer,"),
            "singleplayer and multiplayer use the same player builder on all screens");
        int pile = builder.IndexOf("private static List<Dictionary<string, object?>> BuildPileCardList", StringComparison.Ordinal);
        const string attach = "AttachCombatStarXValues((Dictionary<string, object?>)state[\"star_cost_state\"]!, card, ";
        Check(builder[hand..pile].Contains(attach + "PileType.Hand);", StringComparison.Ordinal) &&
            builder[pile..].Contains(attach + "pile);", StringComparison.Ordinal) &&
            builder.Split(attach, StringSplitOptions.None).Length == 3,
            "the X fields attach to the real hand and pile rows only");
        foreach (string name in new[] { "Draw", "Discard", "Exhaust", "Play" })
            Check(playerBuilder.Contains($"BuildPileCardList(combatState.{name}Pile.Cards, PileType.{name})"), $"shared {name} pile reader");
        foreach (string name in new[] { "ChooseCardSelection", "CombatUpgradePreview", "SelectionCandidate" })
            Check(Source(name).Contains("BuildCardStarCostState(") && !Source(name).Contains("AttachCombatStarXValues("),
                $"{name} retains the existing cost state for its display rows");
    }
}
