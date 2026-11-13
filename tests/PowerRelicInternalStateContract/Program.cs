using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using MegaCrit.Sts2.Core.Combat;
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
        var noPending = Power(juggling);
        Check(noPending["card_to_clone_uid"] == null && noPending["card_to_clone_readable"] is true && !noPending.ContainsKey("unavailable_reasons"), "readable null means no pending clone even at count four");
        Check(FormatPowerInternalState(noPending).Contains("card to clone uid: none"), "known no pending clone has explicit markdown");
        Check(!FormatPowerInternalState(noPending).Contains("card to clone readable"), "known empty pending omits readable success from markdown");
        Check(!Power(juggling, false).ContainsKey("card_to_clone_uid") && !Power(juggling, false).ContainsKey("card_to_clone_readable"), "hidden power omits clone fields");
        var deckCard = new CardModel("STRIKE_R");
        var owner = new Player();
        owner.Deck.Cards.Add(deckCard);
        RefreshCardUidRegistry(new CombatState { Players = [owner] });
        var pendingCard = new CardModel("STRIKE_R") { DeckVersion = deckCard };
        juggling.SetPending(pendingCard);
        var pending = Power(juggling);
        string uid = GetStableCardUid(pendingCard);
        Check(Equals(pending["card_to_clone_uid"], "STRIKE_R#0") && Equals(pending["card_to_clone_uid"], uid) && pending["card_to_clone_readable"] is true, "pending reference uses real shared deck uid registry");
        Check(FormatPowerInternalState(pending).Contains("card to clone uid: " + uid), "pending uid markdown");
        Check(!FormatPowerInternalState(pending).Contains("card to clone readable") && pending["card_to_clone_readable"] is true, "pending uid omits readable success from markdown and retains JSON field");
        Check(ReferenceEquals(juggling.PeekPending(), pendingCard), "read keeps game pending reference unchanged");
        var generated = new CardModel("STRIKE_R");
        juggling.SetPending(generated);
        var generatedPending = Power(juggling);
        Check(Equals(generatedPending["card_to_clone_uid"], GetStableCardUid(generated)) && !Equals(generatedPending["card_to_clone_uid"], uid), "generated same-ID card gets its own shared uid");
        juggling.SetPending(null);
        Check(Power(juggling)["card_to_clone_uid"] == null && Equals(pending["card_to_clone_uid"], uid), "clear is observed and old pending snapshot detached");
        foreach (var invalidClone in new JugglingPower[] { new MissingCloneFieldPower(), new WrongCloneFieldPower() })
        {
            var invalidRow = Power(invalidClone);
            Unknown(invalidRow, "card_to_clone_uid", "status." + invalidClone.GetType().Name);
            Check(invalidRow["card_to_clone_readable"] is false && Equals(invalidRow["attacks_played_this_turn"], 3), "clone field failure keeps independently readable count");
        }
        var snapshot = Power(juggling);
        juggling.Set(0);
        Check(Equals(snapshot["attacks_played_this_turn"], 4) && Equals(Power(juggling)["attacks_played_this_turn"], 0), "new state observes game reset; old snapshot independent");
        foreach (object? invalidData in new object?[] { null, new object() })
        {
            juggling.SetData(invalidData);
            Unknown(Power(juggling), "attacks_played_this_turn", "status.JugglingPower");
            var failedClone = Power(juggling);
            Unknown(failedClone, "card_to_clone_uid", "status.JugglingPower");
            Check(failedClone["card_to_clone_readable"] is false && FormatPowerInternalState(failedClone).Contains("card to clone uid: null ("), "unreadable pending clone differs from known null");
            Check(FormatPowerInternalState(failedClone).Contains("card to clone readable: no"), "unreadable pending keeps readable failure in markdown");
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
        var scarf = new BrilliantScarf();
        foreach (int count in new[] { 0, 5, 6, 7 })
        {
            scarf.Set(count);
            var scarfState = BuildRelicCombatState(scarf);
            Check(Equals(scarfState["cards_played_this_turn"], count) && scarf.Peek() == count, "scarf raw count survives hidden counter and read is pure");
            Check(scarf.ShowCounter == (count < 6) && RelicMarkdown(scarfState).Contains("cards played this turn: " + count), "scarf 5 to 6 display boundary and markdown");
        }
        var scarfSnapshot = BuildRelicCombatState(scarf);
        scarf.Set(0);
        Check(Equals(scarfSnapshot["cards_played_this_turn"], 7) && Equals(BuildRelicCombatState(scarf)["cards_played_this_turn"], 0), "scarf reset keeps previous snapshot detached");
        foreach (var badScarf in new BrilliantScarf[] { new MissingScarfField(), new WrongScarfField() })
            Unknown(BuildRelicCombatState(badScarf), "cards_played_this_turn", "player.relics." + badScarf.GetType().Name + ".combat_state");
        foreach (int count in new[] { 0, 9 })
        {
            nib.Set(count);
            Check(Equals(BuildRelicCombatState(nib)["attacks_played"], count) && nib.Peek() == count, "PenNib public getter read without private field dependency or mutation");
        }
        var paper = new JossPaper();
        paper.Set(4, 2);
        var paperState = BuildRelicCombatState(paper);
        Check(Equals(paperState["cards_exhausted"], 4) && Equals(paperState["ethereal_count"], 2) && paper.Peek() == 6, "JossPaper separates regular count and deferred ethereal");
        paper.Set(1, 0);
        Check(Equals(paperState["ethereal_count"], 2) && Equals(BuildRelicCombatState(paper)["ethereal_count"], 0), "relic snapshots independent across game reset");
        Check(RelicMarkdown(paperState).Contains("cards exhausted: 4, ethereal count: 2"), "relic markdown");
        nib.ThrowAttacks = true;
        var failedNib = BuildRelicCombatState(nib);
        Unknown(failedNib, "attacks_played", "player.relics.PenNib.combat_state");
        Check(RelicMarkdown(failedNib).Contains("attacks played: null (InvalidOperationException: attacks getter failed)") && !RelicMarkdown(failedNib).Contains("unavailable reasons"), "public getter failure markdown shows reason and excludes metadata");
        paper.ThrowCards = true;
        var failedPaper = BuildRelicCombatState(paper);
        Unknown(failedPaper, "cards_exhausted", "player.relics.JossPaper.combat_state");
        Check(Equals(failedPaper["ethereal_count"], 0), "failed public getter preserves private field result");
        nib.ThrowAttacks = false;
        paper.ThrowCards = false;
        Check(Equals(BuildRelicCombatState(nib)["attacks_played"], 9) && Equals(BuildRelicCombatState(paper)["cards_exhausted"], 1), "public getters recover after failure");
        var wrongPaper = BuildRelicCombatState(new WrongJossPaperField());
        Unknown(wrongPaper, "ethereal_count", "player.relics.WrongJossPaperField.combat_state");
        Check(Equals(wrongPaper["cards_exhausted"], 0), "wrong private field type preserves public getter result");
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
        coat.Set([2], [5]);
        foreach (string name in new[] { "FurCoatCoordsSet", "FurCoatCoordCols", "FurCoatCoordRows" })
        {
            coat.ThrowProperties.Add(name);
            var failedCoat = BuildRelicCombatState(coat);
            Unknown(failedCoat, "affects_current_room", "player.relics.FurCoat.combat_state");
            Check(Reasons(failedCoat)["affects_current_room"].Contains($"{name} getter failed"), "private property failure retains underlying reason");
            coat.ThrowProperties.Clear();
        }
        coat.Owner.RunState.CurrentMapPoint.coord.col = 2;
        Check(BuildRelicCombatState(coat)["affects_current_room"] is true, "private handwritten getters recover without backing fields");
        foreach (var (name, read) in new (string, Func<object?>)[]
        {
            ("missing", () => ReadDeclaredStateProperty<int[]>(coat, typeof(FurCoat), "Removed")),
            ("wrong_type", () => ReadDeclaredStateProperty<int>(coat, typeof(FurCoat), "FurCoatCoordCols"))
        })
        {
            var row = new Dictionary<string, object?>();
            ReadInternalStateValue(row, name, read, "property");
            Unknown(row, name, "property");
        }
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
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noPending, wireOptions)))
            Check(json.RootElement.GetProperty("card_to_clone_uid").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("card_to_clone_readable").GetBoolean() && !json.RootElement.TryGetProperty("unavailable_reasons", out _), "known empty pending survives production wire as null with success flag");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(new WrongCloneFieldPower()), wireOptions)))
            Check(json.RootElement.GetProperty("card_to_clone_uid").ValueKind == JsonValueKind.Null && !json.RootElement.GetProperty("card_to_clone_readable").GetBoolean() && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("card_to_clone_uid", out _), "failed pending survives production wire as null, false and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(BuildRelicCombatState(new WrongScarfField()), wireOptions)))
            Check(json.RootElement.GetProperty("cards_played_this_turn").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("cards_played_this_turn", out _), "failed scarf survives production wire as null and reason");
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
