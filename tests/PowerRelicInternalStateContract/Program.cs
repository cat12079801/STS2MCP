using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }
    private static Dictionary<string, object?> Power(PowerModel power, bool visible = true)
    {
        var row = new Dictionary<string, object?> { ["id"] = power.GetType().Name, ["amount"] = 1 };
        AttachPowerInternalState(row, power, visible);
        return row;
    }
    private static Dictionary<string, string> Reasons(Dictionary<string, object?> row)
        => (Dictionary<string, string>)row["unavailable_reasons"]!;
    private static List<Dictionary<string, object?>> Stolen(Dictionary<string, object?> row, string key)
        => (List<Dictionary<string, object?>>)row[key]!;
    private static string RelicMarkdown(Dictionary<string, object?> state)
        => FormatRelicCombatState(new() { ["combat_state"] = state });
    private static void Unknown(Dictionary<string, object?> state, string key, string context)
    {
        Check(state.ContainsKey(key) && state[key] == null, $"{key} is explicit null");
        Check(Reasons(state).ContainsKey(key) && Reasons(state)[key].Length > 0, $"{key} has reason");
        Check(_stateWarnings.Any(s => s.StartsWith(context + "." + key + ":")), $"{key} has path warning");
    }
    public static void Main()
    {
        BeginStateWarnings();
        var plain = new PowerModel();
        Check(Power(plain)["skip_next_duration_tick"] is false, "real false is retained");
        Check(FormatPowerInternalState(Power(plain)) == "", "ordinary false skip adds no markdown");
        plain.Skip = true;
        Check(Power(plain)["skip_next_duration_tick"] is true, "true skip read");
        Check(FormatPowerInternalState(Power(plain)).Contains("skip next duration tick: yes"), "true skip markdown");
        Check(Power(plain, false).Count == 2, "hidden or visibility-unknown powers have no new details");
        plain.ThrowSkip = true;
        var failedSkip = Power(plain);
        Unknown(failedSkip, "skip_next_duration_tick", "status.PowerModel");
        Check(FormatPowerInternalState(failedSkip).Contains("null (InvalidOperationException: skip failed)"), "null markdown includes reason");
        Check(Equals(failedSkip["amount"], 1), "basic row survives failure");
        var toric = new ToricToughnessPower();
        foreach (decimal value in new[] { 0m, 12.75m, -1.25m })
        {
            toric.DynamicVars.Block.Value = value;
            Check(Equals(Power(toric)["block_base_value"], value), "block BaseValue exact decimal, zero, negative");
        }
        toric.DynamicVars.Block.ThrowBase = true;
        var failedBlock = Power(toric);
        Unknown(failedBlock, "block_base_value", "status.ToricToughnessPower");
        Check(failedBlock["skip_next_duration_tick"] is false, "block failure keeps independent field");
        var surrounded = new SurroundedPower();
        foreach (var direction in Enum.GetValues<SurroundedPower.Direction>())
        {
            surrounded.Value = direction;
            Check(Equals(Power(surrounded)["facing"], direction.ToString()), "facing follows game enum");
        }
        surrounded.ThrowFacing = true;
        Unknown(Power(surrounded), "facing", "status.SurroundedPower");
        var skittish = new SkittishPower();
        foreach (bool value in new[] { false, true })
        {
            skittish.Set(value);
            Check(Equals(Power(skittish)["has_gained_block_this_turn"], value), "skittish reads private Data bool");
        }
        var juggling = new JugglingPower();
        foreach (int value in new[] { 0, 2, 3, 4 })
        {
            juggling.Set(value);
            Check(Equals(Power(juggling)["attacks_played_this_turn"], value), "juggling count is not capped");
        }
        var snapshot = Power(juggling);
        juggling.Set(0);
        Check(Equals(snapshot["attacks_played_this_turn"], 4) && Equals(Power(juggling)["attacks_played_this_turn"], 0), "new state observes game reset; old snapshot independent");
        foreach (object? invalidData in new object?[] { null, new object() })
        {
            juggling.SetData(invalidData);
            Unknown(Power(juggling), "attacks_played_this_turn", "status.JugglingPower");
            Check(ReferenceEquals(juggling.PeekData(), invalidData), "failed read never initializes or changes InternalData");
        }
        Unknown(Power(new MissingDataPower()), "attacks_played_this_turn", "status.MissingDataPower");
        var strength = new PossessStrengthPower();
        var speed = new PossessSpeedPower();
        Check(Stolen(Power(strength), "stolen_strength").Count == 0 && Stolen(Power(speed), "stolen_dexterity").Count == 0, "known no theft is empty, not zero or unknown");
        Check(FormatPowerInternalState(Power(strength)) == "", "empty theft list adds no markdown");
        var first = new Creature { Player = new Player { NetId = 17 } };
        var second = new Creature { Player = new Player { NetId = 23 } };
        var source = new Dictionary<Creature, decimal> { [first] = -2.75m, [second] = -1m };
        strength.Set(source);
        speed.Set(source);
        foreach (var (model, key) in new (PowerModel, string)[] { (strength, "stolen_strength"), (speed, "stolen_dexterity") })
        {
            var row = Power(model);
            var amounts = Stolen(row, key);
            Check(amounts.Count == 2 && Equals(amounts[0]["player_id"], "17") && Equals(amounts[1]["player_id"], "23"), "thefts retain multiplayer recipients as existing string player IDs");
            Check(Equals(amounts[0]["amount"], -2.75m) && Equals(amounts[1]["amount"], -1m), "theft signed decimal retained");
            Check(FormatPowerInternalState(row).Contains("17: -2.75, 23: -1"), "theft markdown concise with recipient");
        }
        var theftSnapshot = Power(strength);
        source[first] = -9m;
        Check(Equals(Stolen(theftSnapshot, "stolen_strength")[0]["amount"], -2.75m), "theft snapshot is a copy");
        source[new Creature()] = -1m;
        Unknown(Power(strength), "stolen_strength", "status.PossessStrengthPower");
        strength.SetData(null);
        Unknown(Power(strength), "stolen_strength", "status.PossessStrengthPower");

        var nib = new PenNib();
        foreach (int count in new[] { 0, 9 })
        {
            nib.Set(count);
            Check(Equals(BuildRelicCombatState(nib)["attacks_played"], count) && nib.Peek() == count, "PenNib raw counter read without mutation");
        }
        var paper = new JossPaper();
        paper.Set(4, 2);
        var paperState = BuildRelicCombatState(paper);
        Check(Equals(paperState["cards_exhausted"], 4) && Equals(paperState["ethereal_count"], 2) && paper.Peek() == 6, "JossPaper separates regular count and deferred ethereal");
        paper.Set(1, 0);
        Check(Equals(paperState["ethereal_count"], 2) && Equals(BuildRelicCombatState(paper)["ethereal_count"], 0), "relic snapshots independent across game reset");
        Check(RelicMarkdown(paperState).Contains("cards exhausted: 4, ethereal count: 2"), "relic markdown");
        foreach (var bad in new PenNib[] { new MissingPenNibField(), new WrongPenNibField() })
        {
            var failed = BuildRelicCombatState(bad);
            Unknown(failed, "attacks_played", $"player.relics.{bad.Id.Entry}.combat_state");
            Check(RelicMarkdown(failed).Contains("attacks played: null (") && !RelicMarkdown(failed).Contains("unavailable reasons"), "relic markdown reports null and excludes metadata");
        }
        var partialPaper = BuildRelicCombatState(new PartialJossPaper());
        Unknown(partialPaper, "ethereal_count", "player.relics.PartialJossPaper.combat_state");
        Check(Equals(partialPaper["cards_exhausted"], 4), "one missing relic field preserves its successful neighbor");
        var coat = new FurCoat();
        coat.Owner.RunState.CurrentMapPoint = new MapPoint { coord = new Coord { col = 2, row = 5 } };
        Check(BuildRelicCombatState(coat)["affects_current_room"] is false, "unset coat coordinates mean false as game hook");
        coat.Set([2, 3], [5, 6]);
        Check(BuildRelicCombatState(coat)["affects_current_room"] is true, "marked current room");
        coat.Owner.RunState.CurrentActIndex = 2;
        coat.FurCoatActIndex = 0;
        Check(BuildRelicCombatState(coat)["affects_current_room"] is true, "actual v0.113 combat hook has no act guard");
        coat.Owner.RunState.CurrentMapPoint.coord.col = 3;
        Check(BuildRelicCombatState(coat)["affects_current_room"] is false, "col and row must match same coordinate pair");
        coat.Set([], []);
        Check(BuildRelicCombatState(coat)["affects_current_room"] is false, "empty marked coords known false");
        coat.Set([2], []);
        Unknown(BuildRelicCombatState(coat), "affects_current_room", "player.relics.FurCoat.combat_state");
        coat.Set(null!, []);
        Unknown(BuildRelicCombatState(coat), "affects_current_room", "player.relics.FurCoat.combat_state");
        coat.Set([2], [5]);
        coat.Owner.RunState.CurrentMapPoint = null;
        Unknown(BuildRelicCombatState(coat), "affects_current_room", "player.relics.FurCoat.combat_state");
        Check(BuildRelicCombatState(new RelicModel()).Count == 0, "nonallowlisted relic stays empty");
        Check(FormatRelicCombatState(new()) == "" && RelicMarkdown(new()) == "", "missing/empty relic state has no markdown");
        var isolated = new Dictionary<string, object?> { ["kept"] = 2 };
        ReadInternalStateValue(isolated, "broken", () => throw new MissingFieldException("removed"), "test");
        ReadInternalStateValue(isolated, "zero", () => 0, "test");
        Check(Equals(isolated["kept"], 2) && Equals(isolated["zero"], 0), "failed private read keeps other fields");
        var wireOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(isolated, wireOptions)))
            Check(json.RootElement.GetProperty("broken").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").GetProperty("broken").GetString()!.Contains("removed"), "null and reason survive production wire settings");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(theftSnapshot, wireOptions)))
            Check(json.RootElement.GetProperty("stolen_strength")[0].GetProperty("amount").GetDecimal() == -2.75m, "wire numeric decimal theft");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failedSkip, wireOptions)))
            Check(json.RootElement.GetProperty("skip_next_duration_tick").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("skip_next_duration_tick", out _), "power null and reason survive wire");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(partialPaper, wireOptions)))
            Check(json.RootElement.GetProperty("cards_exhausted").GetInt32() == 4 && json.RootElement.GetProperty("ethereal_count").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("ethereal_count", out _), "relic partial values and null reason survive wire");
        var response = new Dictionary<string, object?>();
        EndStateWarnings(response);
        Check(response.ContainsKey("warnings"), "warnings attached to surviving response");
        BeginStateWarnings();
        Check(Power(new JugglingPower())["skip_next_duration_tick"] is false && _stateWarnings.Count == 0, "later read recovers without stale warnings");

        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));
        var builder = Source("McpMod.StateBuilder.cs");
        Check(builder.Contains("AttachPowerInternalState(entry, power, visibilityKnown && isVisible);") && builder.Split("AttachPowerInternalState(entry, power, visibilityKnown && isVisible);").Length == 3, "full and minimal power rows attach details conservatively");
        Check(builder.Contains("[\"status\"] = BuildPowersState(creature)") && builder.Contains("[\"status\"] = BuildPowersState(osty)") && builder.Contains("state[\"status\"] = BuildPowersState(creature);"), "player, enemy and pet share builder");
        Check(builder.Contains("if (inCombat)") && builder.Contains("BuildRelicCombatState(relic)"), "relic details wired under combat guard");
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildPlayerState("), "multiplayer local player shares path");
        Check(Source("McpMod.Formatting.cs").Split("{FormatPowerInternalState(p)}").Length == 4, "player, enemy, pet markdown wired");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 4;"), "additive schema remains 4");
        Console.WriteLine($"PASS: {Checks} power/relic internal-state contract checks");
    }
}
