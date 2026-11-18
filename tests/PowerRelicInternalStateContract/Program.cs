using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
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
        // "type" is PowerModel.Type, as BuildPowersState writes it.
        var row = new Dictionary<string, object?> { ["id"] = power.GetType().Name, ["amount"] = 1, ["type"] = power.Type.ToString() };
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
    // sts2#1019: The Bomb's damage, Inferno / Crimson Mantle self-damage, Panache's countdown.
    private static void RunCardEffectPowerContract()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        BeginStateWarnings();
        var bomb = new TheBombPower();
        Check(Equals(Power(bomb)["damage"], 40m) && !Power(bomb).ContainsKey("unavailable_reasons"), "Bomb canonical damage 40 is read");
        bomb.SetDamage(50m);
        var bombRow = Power(bomb);
        Check(Equals(bombRow["damage"], 50m), "Bomb damage follows SetDamage, not Amount");
        var otherBomb = new TheBombPower();
        otherBomb.SetDamage(0m);
        Check(Equals(Power(otherBomb)["damage"], 0m) && Equals(Power(bomb)["damage"], 50m), "each Instanced Bomb row has its own damage; 0 is a value");
        Check(Equals(Power(bomb, false)["damage"], 50m) && !Power(bomb, false).ContainsKey("skip_next_duration_tick"), "hidden rows carry the damage too");
        bomb.SetDamage(60m);
        Check(Equals(bombRow["damage"], 50m), "snapshot independent of later SetDamage");
        Check(FormatPowerInternalState(bombRow).Contains("damage: 50"), "markdown shows the Bomb damage");
        bomb.DynamicVars.Damage.ThrowBase = true;
        Unknown(Power(bomb), "damage", "status.TheBombPower");
        bomb.DynamicVars.Damage.ThrowBase = false;
        var noDamage = new TheBombPower();
        noDamage.DynamicVars.Vars.Clear();
        var noDamageRow = Power(noDamage);
        Unknown(noDamageRow, "damage", "status.TheBombPower");
        Check(noDamageRow["skip_next_duration_tick"] is false && FormatPowerInternalState(noDamageRow).Contains("damage: null ("), "a missing var is unknown, not 0; other fields kept");

        foreach (PowerModel power in new PowerModel[] { new InfernoPower(), new CrimsonMantlePower() })
        {
            string name = power.GetType().Name;
            Check(Equals(Power(power)["self_damage"], 0m), $"{name}: initial self damage 0 is a value");
            if (power is InfernoPower inferno) { inferno.IncrementSelfDamage(); inferno.IncrementSelfDamage(); }
            else ((CrimsonMantlePower)power).IncrementSelfDamage();
            decimal expected = power is InfernoPower ? 2m : 1m;
            var row = Power(power);
            Check(Equals(row["self_damage"], expected) && !row.ContainsKey("damage") && !row.ContainsKey("cards_left"), $"{name}: IncrementSelfDamage is read; no other power's keys");
            Check(FormatPowerInternalState(row).Contains($"self damage: {expected}"), $"{name}: markdown");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(row, options)))
                Check(json.RootElement.GetProperty("self_damage").GetDecimal() == expected, $"{name}: wire decimal");
            power.DynamicVars["SelfDamage"].ThrowBase = true;
            Unknown(Power(power), "self_damage", $"status.{name}");
            power.DynamicVars["SelfDamage"].ThrowBase = false;
        }

        BeginStateWarnings();
        var panache = new PanachePower();
        var fresh = Power(panache);
        Check(Equals(fresh["cards_left"], 5) && fresh["already_applied"] is false && !fresh.ContainsKey("unavailable_reasons"), "fresh Panache: 5 left, not yet applied (real false)");
        panache.Applied();
        panache.DynamicVars["CardsLeft"].Value = 3m;
        var counting = Power(panache);
        Check(Equals(counting["cards_left"], 3) && counting["already_applied"] is true, "Panache countdown and the applied phase are read");
        Check(panache.PeekApplied() && Equals(fresh["cards_left"], 5), "reading leaves Data as it is; snapshot independent");
        Check(Equals(Power(panache, false)["cards_left"], 3) && Power(panache, false)["already_applied"] is true, "hidden Panache rows carry both");
        var secondPanache = new PanachePower();
        Check(Equals(Power(secondPanache)["cards_left"], 5) && Power(secondPanache)["already_applied"] is false, "Instanced Panache rows are independent");
        Check(FormatPowerInternalState(counting).Contains("cards left: 3") && FormatPowerInternalState(counting).Contains("already applied: yes"), "Panache markdown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(counting, options)))
            Check(json.RootElement.GetProperty("cards_left").GetInt32() == 3 && json.RootElement.GetProperty("already_applied").ValueKind == JsonValueKind.True, "Panache wire int and bool");
        var noData = new MissingPanacheDataPower();
        var noDataRow = Power(noData);
        Unknown(noDataRow, "already_applied", "status.MissingPanacheDataPower");
        Check(Equals(noDataRow["cards_left"], 5) && noData.PeekData() == null, "missing Data is unknown (never initialized); cards_left kept");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noDataRow, options)))
            Check(json.RootElement.GetProperty("already_applied").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("already_applied", out _),
                "wire null and reason for already_applied");
        panache.DynamicVars["CardsLeft"].ThrowBase = true;
        var failedLeft = Power(panache);
        Unknown(failedLeft, "cards_left", "status.PanachePower");
        Check(failedLeft["already_applied"] is true, "cards_left failure keeps already_applied");
        Check(!Power(new ToricToughnessPower()).ContainsKey("damage") && !Power(new ToricToughnessPower()).ContainsKey("cards_left"), "other powers carry none of these keys");

        // An uninitialized PowerModel._dynamicVars is a failed read: never built by the getter.
        foreach (PowerModel power in new PowerModel[] { new TheBombPower(), new InfernoPower(), new CrimsonMantlePower(), new PanachePower() })
        {
            string name = power.GetType().Name;
            string key = power switch { TheBombPower => "damage", PanachePower => "cards_left", _ => "self_damage" };
            power.ClearDynamicVars();
            var row = Power(power);
            Unknown(row, key, $"status.{name}");
            Check(!power.DynamicVarsInitialized && Reasons(row)[key].Contains("_dynamicVars"), $"{name}: a null _dynamicVars is not built by the read");
            if (power is PanachePower)
                Check(row["already_applied"] is false, "Panache: the Data read is independent of the var set");
        }
    }

    // sts2#1020: every status row carries dynamic_vars = { name -> BaseValue } from PowerModel._dynamicVars.
    private static void RunPowerDynamicVarsContract()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        Dictionary<string, object?> Vars(Dictionary<string, object?> row) => (Dictionary<string, object?>)row["dynamic_vars"]!;
        BeginStateWarnings();

        // A readable empty set (most powers) is {}, not null; no reason, no warning, no markdown.
        var plain = Power(new PowerModel());
        Check(Vars(plain).Count == 0 && !plain.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "empty var set is {} with no reason or warning");
        Check(FormatPowerInternalState(plain) == "", "an empty var set adds no markdown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(plain, options)))
            Check(json.RootElement.GetProperty("dynamic_vars").ValueKind == JsonValueKind.Object
                && !json.RootElement.GetProperty("dynamic_vars").EnumerateObject().Any(), "wire empty object");

        // Several vars: every name with its exact BaseValue, in the set's order.
        var tank = new TankPower();
        var tankRow = Power(tank);
        Check(Vars(tankRow).Keys.SequenceEqual(new[] { "DamageIncrease", "DamageDecrease" })
            && Equals(Vars(tankRow)["DamageIncrease"], 1.5m) && Equals(Vars(tankRow)["DamageDecrease"], 0.5m)
            && !tankRow.ContainsKey("unavailable_reasons"), "two vars by name, exact decimals, set order");
        Check(FormatPowerInternalState(tankRow).Contains("vars: DamageIncrease=1.5, DamageDecrease=0.5"), "markdown lists the vars compactly");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(tankRow, options)))
        {
            var vars = json.RootElement.GetProperty("dynamic_vars");
            Check(vars.GetProperty("DamageIncrease").GetDecimal() == 1.5m && vars.GetProperty("DamageDecrease").GetDecimal() == 0.5m,
                "wire keeps the var names as they are (no snake_case) and decimal values");
        }
        tank.DynamicVars["DamageIncrease"].Value = 2m;
        Check(Equals(Vars(tankRow)["DamageIncrease"], 1.5m) && Equals(Vars(Power(tank))["DamageIncrease"], 2m), "snapshot independent of a later BaseValue write");
        tank.DynamicVars["DamageIncrease"].Value = 0m;
        Check(Equals(Vars(Power(tank))["DamageIncrease"], 0m), "0 is a value");

        // Hidden rows carry the vars too (the effect acts whatever the HUD shows).
        var hiddenTank = Power(tank, false);
        Check(Vars(hiddenTank).Count == 2 && !hiddenTank.ContainsKey("skip_next_duration_tick"), "hidden rows carry dynamic_vars");

        // StringVar's numeric base is a dummy: left out, like the card and enchantment dynamic_vars.
        var barricade = Power(new BarricadePower());
        Check(Vars(barricade).Count == 0 && !barricade.ContainsKey("unavailable_reasons"), "StringVar is left out");

        // The per-type fields stay as they are and agree with the generic set.
        var inferno = new InfernoPower();
        inferno.IncrementSelfDamage();
        var infernoRow = Power(inferno);
        Check(Equals(infernoRow["self_damage"], 1m) && Equals(Vars(infernoRow)["SelfDamage"], 1m), "Inferno: self_damage kept, SelfDamage in dynamic_vars");
        var bombRow = Power(new TheBombPower());
        Check(Equals(bombRow["damage"], 40m) && Equals(Vars(bombRow)["Damage"], 40m), "The Bomb: damage kept, Damage in dynamic_vars");
        var toric = new ToricToughnessPower();
        toric.DynamicVars.Block.Value = 5m;
        Check(Equals(Power(toric)["block_base_value"], 5m) && Equals(Vars(Power(toric))["Block"], 5m), "Toric Toughness: block_base_value kept, Block in dynamic_vars");

        // One var that cannot be read: null under its name with its own reason; the others kept.
        BeginStateWarnings();
        tank.DynamicVars["DamageDecrease"].ThrowBase = true;
        var partial = Power(tank);
        Check(Vars(partial).ContainsKey("DamageDecrease") && Vars(partial)["DamageDecrease"] == null && Equals(Vars(partial)["DamageIncrease"], 0m),
            "a failed var is null under its name; the others are kept");
        Check(Reasons(partial).TryGetValue("dynamic_vars.DamageDecrease", out var varReason) && varReason.Contains("base failed")
            && !Reasons(partial).ContainsKey("dynamic_vars"), "the reason is at dynamic_vars.<name>; the set itself is read");
        Check(_stateWarnings.Any(w => w.StartsWith("status.TankPower.dynamic_vars.DamageDecrease:")), "a failed var has a path warning");
        Check(FormatPowerInternalState(partial).Contains("vars: DamageIncrease=0, DamageDecrease=null (InvalidOperationException: base failed)"), "markdown shows the failed var and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(partial, options)))
            Check(json.RootElement.GetProperty("dynamic_vars").GetProperty("DamageDecrease").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("dynamic_vars.DamageDecrease", out _), "wire null and reason for a failed var");
        tank.DynamicVars["DamageDecrease"].ThrowBase = false;

        // A null PowerModel._dynamicVars is a failed read (never {}), and the getter does not build it.
        BeginStateWarnings();
        foreach (bool visible in new[] { true, false })
        {
            var cleared = new TankPower();
            cleared.ClearDynamicVars();
            var row = Power(cleared, visible);
            Unknown(row, "dynamic_vars", "status.TankPower");
            Check(!cleared.DynamicVarsInitialized && Reasons(row)["dynamic_vars"].Contains("_dynamicVars"), $"null _dynamicVars is not built by the read (visible={visible})");
            Check(row["is_temporary_power"] is false && Equals(row["type_for_current_amount"], "Buff"), "a failed set keeps the other fields");
            Check(FormatPowerInternalState(row).Contains("vars: null (InvalidOperationException"), "markdown shows the failed set");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, options));
            Check(json.RootElement.GetProperty("dynamic_vars").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("dynamic_vars", out _), "wire null and reason for a null set");
        }
        // The per-type readers fail the same way and do not build the set either.
        var clearedBomb = new TheBombPower();
        clearedBomb.ClearDynamicVars();
        var clearedBombRow = Power(clearedBomb);
        Unknown(clearedBombRow, "dynamic_vars", "status.TheBombPower");
        Unknown(clearedBombRow, "damage", "status.TheBombPower");
        Check(!clearedBomb.DynamicVarsInitialized, "The Bomb: neither read builds the set");

        // A set that cannot be enumerated fails the whole key.
        var broken = new TankPower();
        broken.DynamicVars.ThrowEnumerate = true;
        var brokenRow = Power(broken);
        Unknown(brokenRow, "dynamic_vars", "status.TankPower");
        Check(Reasons(brokenRow)["dynamic_vars"].Contains("enumerate failed"), "an enumeration failure is the set's reason");
    }

    private static void SetF(object target, string name, object? value)
        => (target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().Name, name)).SetValue(target, value);
    private static object? GetF(object target, string name)
        => (target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().Name, name)).GetValue(target);

    // sts2#1020: relic private counters (combat_state / run_state) and relics[].vars from _dynamicVars.
    private static void RunRelicCounterContract()
    {
        BeginStateWarnings();
        // Per-turn counters in combat_state: the private count, not the counter's display value.
        foreach (var (relic, key, field) in new (RelicModel, string, string)[] {
            (new Kunai(), "attacks_played_this_turn", "_attacksPlayedThisTurn"),
            (new Shuriken(), "attacks_played_this_turn", "_attacksPlayedThisTurn"),
            (new OrnamentalFan(), "attacks_played_this_turn", "_attacksPlayedThisTurn"),
            (new Kusarigama(), "attacks_played_this_turn", "_attacksPlayedThisTurn"),
            (new LetterOpener(), "skills_played_this_turn", "_skillsPlayedThisTurn"),
            (new Pocketwatch(), "cards_played_this_turn", "_cardsPlayedThisTurn") })
        {
            string name = relic.GetType().Name;
            var zero = BuildRelicCombatState(relic);
            Check(Equals(zero[key], 0) && !zero.ContainsKey("unavailable_reasons"), $"{name}: 0 is a value");
            SetF(relic, field, 4);
            var four = BuildRelicCombatState(relic);
            Check(Equals(four[key], 4), $"{name}: the raw count 4 (not 4 % threshold)");
            Check(RelicMarkdown(four).Contains(": 4"), $"{name}: markdown");
            SetF(relic, field, 0);
            Check(Equals(four[key], 4) && Equals(BuildRelicCombatState(relic)[key], 0), $"{name}: snapshot independent of a reset");
            Check(!BuildRelicRunState(relic).Any(), $"{name}: per-turn counters are not run_state");
        }
        Check(Equals(BuildRelicCombatState(new Pocketwatch())["cards_played_last_turn"], 0), "Pocketwatch keeps cards_played_last_turn");
        foreach (RelicModel relic in new RelicModel[] { new Orichalcum(), new FakeOrichalcum() })
        {
            Check(BuildRelicCombatState(relic)["should_trigger"] is false, $"{relic.GetType().Name}: false is a value");
            SetF(relic, "_shouldTrigger", true);
            Check(BuildRelicCombatState(relic)["should_trigger"] is true && RelicMarkdown(BuildRelicCombatState(relic)).Contains("should trigger: yes"), $"{relic.GetType().Name}: true");
        }
        Check(_stateWarnings.Count == 0, "readable relic counters add no warnings");
        var missingKunai = BuildRelicCombatState(new MissingKunaiField());
        Unknown(missingKunai, "attacks_played_this_turn", "player.relics.MissingKunaiField.combat_state");
        Check(RelicMarkdown(missingKunai).Contains("attacks this turn: null ("), "a missing field is null with its reason in markdown");

        // Saved counters in run_state (emitted in and out of combat by the builder).
        BeginStateWarnings();
        foreach (var (relic, key, field, value) in new (RelicModel, string, string, object)[] {
            (new Nunchaku(), "attacks_played", "_attacksPlayed", 7),
            (new TuningFork(), "skills_played", "_skillsPlayed", 2),
            (new IronClub(), "cards_played", "_cardsPlayed", 3),
            (new HappyFlower(), "turns_seen", "_turnsSeen", 2),
            (new FakeHappyFlower(), "turns_seen", "_turnsSeen", 4),
            (new Pendulum(), "turns_seen", "_turnsSeen", 1),
            (new PollinousCore(), "turns_seen", "_turnsSeen", 3),
            (new Girya(), "times_lifted", "_timesLifted", 2),
            (new LastingCandy(), "combat_rewards_seen", "_combatRewardsSeen", 5),
            (new BookOfFiveRings(), "cards_added", "_cardsAdded", 9),
            (new VenerableTeaSet(), "gain_energy_in_next_combat", "_gainEnergyInNextCombat", true),
            (new FakeVenerableTeaSet(), "gain_energy_in_next_combat", "_gainEnergyInNextCombat", true),
            (new PumpkinCandle(), "kindle_count", "_kindleCount", 4),
            (new SwordOfStone(), "elites_defeated", "_elitesDefeated", 2),
            (new WingedBoots(), "times_used", "_timesUsed", 1),
            (new FishingRod(), "combats_seen", "_combatsSeen", 6),
            (new ToyBox(), "combats_seen", "_combatsSeen", 3),
            (new WongosMysteryTicket(), "combats_finished", "_combatsFinished", 4),
            (new PaelsWing(), "rewards_sacrificed", "_rewardsSacrificed", 1),
            (new LavaRock(), "has_triggered", "_hasTriggered", true) })
        {
            string name = relic.GetType().Name;
            var initial = BuildRelicRunState(relic);
            Check(initial.Count == 1 && initial[key] is 0 or false, $"{name}: 0 / false is a value");
            SetF(relic, field, value);
            var row = BuildRelicRunState(relic);
            Check(Equals(row[key], value) && !row.ContainsKey("unavailable_reasons"), $"{name}: {key} is read");
            Check(Equals(GetF(relic, field), value), $"{name}: the read leaves the field as it is");
            Check(!BuildRelicCombatState(relic).Any(), $"{name}: saved counters are not combat_state");
            var relicRow = new Dictionary<string, object?> { ["run_state"] = row };
            Check(FormatRelicCombatState(relicRow).Contains(key.Replace('_', ' ') + ": "), $"{name}: run_state markdown");
        }
        var crucible = new SilverCrucible();
        SetF(crucible, "_treasureRoomsEntered", 2);
        var crucibleState = BuildRelicRunState(crucible);
        Check(crucibleState.Count == 2 && Equals(crucibleState["times_used"], 0) && Equals(crucibleState["treasure_rooms_entered"], 2), "Silver Crucible: both counters");
        Check(_stateWarnings.Count == 0, "readable run_state adds no warnings");
        var wrongNunchaku = BuildRelicRunState(new WrongNunchakuField());
        Unknown(wrongNunchaku, "attacks_played", "player.relics.WrongNunchakuField.run_state");
        Check(!BuildRelicRunState(new PenNib()).Any() && !BuildRelicRunState(new Kunai()).Any(), "other relics have no run_state");

        // vars from RelicModel._dynamicVars.
        BeginStateWarnings();
        var kunai = new Kunai();
        kunai.DynamicVars.Vars["Cards"] = new() { Value = 3m };
        kunai.DynamicVars.Vars["DexterityPower"] = new() { Value = 1m };
        kunai.DynamicVars.Vars["Name"] = new MegaCrit.Sts2.Core.Localization.DynamicVars.StringVar();
        var kunaiRow = new Dictionary<string, object?>();
        AttachRelicDynamicVars(kunaiRow, kunai);
        var vars = (Dictionary<string, object?>)kunaiRow["vars"]!;
        Check(vars.Keys.SequenceEqual(new[] { "Cards", "DexterityPower" }) && Equals(vars["Cards"], 3m) && Equals(vars["DexterityPower"], 1m)
            && !kunaiRow.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "relic vars: names, BaseValue, StringVar left out");
        Check(FormatRelicCombatState(kunaiRow) == "", "readable relic vars are JSON-only in markdown");
        var emptyRow = new Dictionary<string, object?>();
        AttachRelicDynamicVars(emptyRow, new Girya());
        Check(emptyRow["vars"] is Dictionary<string, object?> { Count: 0 } && !emptyRow.ContainsKey("unavailable_reasons"), "an empty relic set is {}");
        kunai.DynamicVars.Vars["Cards"].ThrowBase = true;
        var partialRow = new Dictionary<string, object?>();
        AttachRelicDynamicVars(partialRow, kunai);
        var partialVars = (Dictionary<string, object?>)partialRow["vars"]!;
        Check(partialVars["Cards"] == null && Equals(partialVars["DexterityPower"], 1m)
            && Reasons(partialRow).ContainsKey("vars.Cards") && !Reasons(partialRow).ContainsKey("vars"), "a failed relic var is null under its name; the others kept");
        Check(_stateWarnings.Any(w => w.StartsWith("player.relics.Kunai.vars.Cards:")), "a failed relic var has a path warning");
        Check(FormatRelicCombatState(partialRow).Contains("(vars: Cards=null (InvalidOperationException: base failed))"), "markdown shows the failed relic var");
        kunai.DynamicVars.Vars["Cards"].ThrowBase = false;
        var cleared = new Girya();
        cleared.ClearDynamicVars();
        var clearedRow = new Dictionary<string, object?> { ["id"] = "GIRYA" };
        AttachRelicDynamicVars(clearedRow, cleared);
        Unknown(clearedRow, "vars", "player.relics.Girya");
        Check(!cleared.DynamicVarsInitialized && Reasons(clearedRow)["vars"].Contains("_dynamicVars") && Equals(clearedRow["id"], "GIRYA"),
            "a null relic _dynamicVars is not built by the read; vars is null with a reason, not dropped");
        Check(FormatRelicCombatState(clearedRow).Contains("(vars: null (InvalidOperationException"), "markdown shows the failed relic vars");

        // AttachRelicSection: a covered section is attached, an empty one is left out, a throwing builder is null + reason.
        BeginStateWarnings();
        var nunchakuRow = new Dictionary<string, object?>();
        AttachRelicSection(nunchakuRow, "run_state", () => BuildRelicRunState(new Nunchaku()), "NUNCHAKU");
        Check(nunchakuRow["run_state"] is Dictionary<string, object?> { Count: 1 } && !nunchakuRow.ContainsKey("unavailable_reasons"), "a covered run_state is attached");
        var plainRelicRow = new Dictionary<string, object?>();
        AttachRelicSection(plainRelicRow, "run_state", () => BuildRelicRunState(new PenNib()), "PEN_NIB");
        AttachRelicSection(plainRelicRow, "combat_state", () => new Dictionary<string, object?>(), "PEN_NIB");
        Check(plainRelicRow.Count == 0 && _stateWarnings.Count == 0, "an uncovered relic has no run_state / combat_state key");
        foreach (string section in new[] { "run_state", "combat_state" })
        {
            var brokenRelicRow = new Dictionary<string, object?> { ["id"] = "NUNCHAKU" };
            AttachRelicSection(brokenRelicRow, section, () => throw new TypeLoadException("type moved"), "NUNCHAKU");
            Unknown(brokenRelicRow, section, "player.relics.NUNCHAKU");
            Check(FormatRelicCombatState(brokenRelicRow).Contains($"({section.Replace('_', ' ')}: null (TypeLoadException: type moved))"), $"{section} failure markdown");
            Check(Reasons(brokenRelicRow)[section].Contains("type moved") && Equals(brokenRelicRow["id"], "NUNCHAKU"),
                $"a throwing {section} builder is null with unavailable_reasons.{section}, not dropped; the row is kept");
        }
    }

    // sts2#1020: power private Data / fields that neither Amount nor DynamicVars hold.
    private static void RunPowerPrivateDataContract()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        BeginStateWarnings();
        var deckStrike = new CardModel("STRIKE_R");
        var deckBash = new CardModel("BASH");
        var owner = new Player();
        owner.Deck.Cards.Add(deckStrike);
        owner.Deck.Cards.Add(deckBash);
        RefreshCardUidRegistry(new CombatState { Players = [owner] });
        var strike = new CardModel("STRIKE_R") { DeckVersion = deckStrike };
        var bash = new CardModel("BASH") { DeckVersion = deckBash };
        string strikeUid = GetStableCardUid(strike), bashUid = GetStableCardUid(bash);
        var unseen = new CardModel("ANGER");

        // Card -> int maps: Rupture (pending Strength), Calamity / Strangle (amount at play).
        foreach (var (power, key, valueKey, field) in new (PowerModel, string, string, string)[] {
            (new RupturePower(), "played_cards", "pending_strength", "playedCards"),
            (new CalamityPower(), "amounts_for_played_cards", "amount", "amountsForPlayedCards"),
            (new StranglePower(), "amounts_for_played_cards", "amount", "amountsForPlayedCards") })
        {
            string name = power.GetType().Name;
            object data = power.PeekData()!;
            var empty = Power(power, false);
            Check(empty[key] is List<Dictionary<string, object?>> { Count: 0 } && !empty.ContainsKey("unavailable_reasons"), $"{name}: an empty map is [] on hidden rows too");
            Check(FormatPowerInternalState(empty).Contains(key.Replace('_', ' ') + ": none"), $"{name}: empty markdown");
            var map = (Dictionary<CardModel, int>)GetF(data, field)!;
            map.Add(strike, 3);
            map.Add(bash, 0);
            var row = Power(power);
            var rows = (List<Dictionary<string, object?>>)row[key]!;
            Check(rows.Count == 2 && Equals(rows[0]["uid"], bashUid) && Equals(rows[0][valueKey], 0) && Equals(rows[1]["uid"], strikeUid) && Equals(rows[1][valueKey], 3)
                && rows.All(r => r["uid_known"] is true) && Equals(rows[1]["card_id"], "STRIKE_R"), $"{name}: registry uids sorted, values kept (0 too)");
            Check(FormatPowerInternalState(row).Contains($"{key.Replace('_', ' ')}: {bashUid}=0, {strikeUid}=3"), $"{name}: markdown");
            map.Add(unseen, 2);
            var withUnseen = (List<Dictionary<string, object?>>)Power(power)[key]!;
            Check(withUnseen.Count == 3 && withUnseen[2]["uid"] == null && withUnseen[2]["uid_known"] is false && Equals(withUnseen[2][valueKey], 2)
                && withUnseen[2]["uid_unavailable_reason"] is string r && r.Contains("ANGER") && !TryGetRegisteredCardUid(unseen, out _),
                $"{name}: an unregistered card is uid_known=false with a reason; no uid is minted");
            Check(_stateWarnings.Any(w => w.StartsWith($"status.{name}.{key}.uid:")), $"{name}: an unregistered card warns");
            Check(ReferenceEquals(power.PeekData(), data) && map.Count == 3 && rows.Count == 2, $"{name}: Data untouched; snapshot independent");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(row, options)))
                Check(json.RootElement.GetProperty(key)[1].GetProperty(valueKey).GetInt32() == 3, $"{name}: wire int");
            power.SetData(null);
            Unknown(Power(power), key, $"status.{name}");
            Check(power.PeekData() == null, $"{name}: null Data is never initialized");
        }

        // Hellraiser: the infinite-HP auto-play count and the Strike being auto-played.
        BeginStateWarnings();
        var hell = new HellraiserPower();
        var hellData = hell.PeekData()!;
        var hellRow = Power(hell);
        Check(Equals(hellRow["infinite_auto_plays_this_turn"], 0) && hellRow["auto_playing_cards"] is List<Dictionary<string, object?>> { Count: 0 }
            && !hellRow.ContainsKey("showed_cap_reached_message"), "Hellraiser: 0 and [] are values; the message flag is left out");
        SetF(hellData, "infiniteAutoPlaysThisTurn", 9);
        ((HashSet<CardModel>)GetF(hellData, "autoPlayingCards")!).Add(strike);
        var hellBusy = Power(hell, false);
        Check(Equals(hellBusy["infinite_auto_plays_this_turn"], 9) && hellBusy["auto_playing_cards"] is List<Dictionary<string, object?>> busy && busy.Count == 1
            && Equals(busy[0]["uid"], strikeUid) && !busy[0].ContainsKey("amount"), "Hellraiser: hidden row carries the count and the auto-played uid");
        Check(FormatPowerInternalState(hellBusy).Contains("infinite auto plays this turn: 9") && FormatPowerInternalState(hellBusy).Contains($"auto playing cards: {strikeUid}"), "Hellraiser markdown");

        // Scalar Data fields.
        foreach (var (power, key, field, value) in new (PowerModel, string, string, object)[] {
            (new AutomationPower(), "cards_left", "cardsLeft", 3),
            (new DarkEmbracePower(), "ethereal_count", "etherealCount", 2),
            (new HardenedShellPower(), "damage_received_this_turn", "damageReceivedThisTurn", 7.5m),
            (new AdaptablePower(), "is_reviving", "isReviving", true),
            (new ReattachPower(), "is_reviving", "isReviving", true),
            (new IllusionPower(), "is_reviving", "isReviving", true) })
        {
            string name = power.GetType().Name;
            object data = power.PeekData()!;
            var before = Power(power, false);
            Check(before.ContainsKey(key) && !before.ContainsKey("unavailable_reasons"), $"{name}: hidden row carries {key}");
            SetF(data, field, value);
            var row = Power(power);
            Check(Equals(row[key], value) && ReferenceEquals(power.PeekData(), data), $"{name}: {key} is read; Data untouched");
            Check(FormatPowerInternalState(row).Contains(key.Replace('_', ' ') + ": "), $"{name}: markdown");
            power.SetData(null);
            Unknown(Power(power), key, $"status.{name}");
            Check(power.PeekData() == null, $"{name}: null Data is never initialized");
        }
        var automation = new AutomationPower();
        Check(Equals(Power(automation)["cards_left"], 10) && Equals(((Dictionary<string, object?>)Power(automation)["dynamic_vars"]!)["BaseCards"], 10m),
            "Automation: the countdown is cards_left; BaseCards stays the text's 10");

        // Gigantification: bound / unbound command.
        var giant = new GigantificationPower();
        Check(Power(giant)["has_command_to_modify"] is false, "Gigantification: unbound is a readable false");
        SetF(giant.PeekData()!, "commandToModify", new AttackCommand());
        Check(Power(giant, false)["has_command_to_modify"] is true, "Gigantification: bound, hidden row too");
        giant.SetData(null);
        Unknown(Power(giant), "has_command_to_modify", "status.GigantificationPower");

        // Illusion: the follow-up move id (readable null = none).
        BeginStateWarnings();
        var illusion = new IllusionPower();
        var noFollow = Power(illusion);
        Check(noFollow["follow_up_state_id"] == null && noFollow["follow_up_state_readable"] is true && !noFollow.ContainsKey("unavailable_reasons"), "Illusion: readable none");
        Check(FormatPowerInternalState(noFollow).Contains("follow up state id: none") && !FormatPowerInternalState(noFollow).Contains("readable"), "Illusion: none markdown");
        illusion.FollowUpValue = "SLASH_MOVE";
        Check(Equals(Power(illusion, false)["follow_up_state_id"], "SLASH_MOVE"), "Illusion: the id, hidden row too");
        illusion.ThrowFollowUp = true;
        var failedFollow = Power(illusion);
        Unknown(failedFollow, "follow_up_state_id", "status.IllusionPower");
        Check(failedFollow["follow_up_state_readable"] is false && FormatPowerInternalState(failedFollow).Contains("follow up state readable: no"), "Illusion: failed read differs from none");

        // Declared private fields.
        foreach (var (power, key, field, value) in new (PowerModel, string, string, object)[] {
            (new NemesisPower(), "should_apply_intangible", "_shouldApplyIntangible", true),
            (new SlothPower(), "cards_played_this_turn", "_cardsPlayedThisTurn", 2),
            (new TenderPower(), "cards_played_this_turn", "_cardsPlayedThisTurn", 3),
            (new BeaconOfHopePower(), "has_already_been_given_block", "_hasAlreadyBeenGivenBlock", true) })
        {
            string name = power.GetType().Name;
            Check(Power(power, false)[key] is 0 or false, $"{name}: 0 / false is a value on hidden rows");
            SetF(power, field, value);
            Check(Equals(Power(power)[key], value) && Equals(GetF(power, field), value), $"{name}: {key} is read and left as it is");
        }

        // Swipe: the stolen card as a registry uid (readable null = nothing stolen).
        BeginStateWarnings();
        var swipe = new SwipePower();
        var nothing = Power(swipe, false);
        Check(nothing["stolen_card"] == null && nothing["stolen_card_readable"] is true && !nothing.ContainsKey("unavailable_reasons"), "Swipe: nothing stolen is a readable null");
        Check(FormatPowerInternalState(nothing).Contains("stolen card: none"), "Swipe: none markdown");
        SetF(swipe, "_stolenCard", bash);
        var stolen = Power(swipe);
        Check(stolen["stolen_card"] is Dictionary<string, object?> stolenRow && Equals(stolenRow["uid"], bashUid) && stolenRow["uid_known"] is true
            && Equals(stolenRow["card_id"], "BASH") && stolen["stolen_card_readable"] is true, "Swipe: the stolen card's registry uid");
        Check(FormatPowerInternalState(stolen).Contains($"stolen card: {bashUid}"), "Swipe markdown");
        SetF(swipe, "_stolenCard", unseen);
        Check(Power(swipe)["stolen_card"] is Dictionary<string, object?> unseenRow && unseenRow["uid_known"] is false && !TryGetRegisteredCardUid(unseen, out _),
            "Swipe: an unregistered stolen card is not given a uid");
        Check(!Power(new ToricToughnessPower()).ContainsKey("stolen_card") && !Power(new PanachePower()).ContainsKey("played_cards"), "other powers carry none of these keys");
    }

    // sts2#1034: Rend counts a power when TypeForCurrentAmount == Debuff && !(power is ITemporaryPower).
    private static void RunRendInputContract()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        BeginStateWarnings();
        // Debuff vs buff: the current type follows the power, read on hidden rows too.
        var weak = new WeakPower();
        var vulnerable = new VulnerablePower();
        var strength = new StrengthPower();
        foreach (var (power, expected) in new (PowerModel, string)[] { (weak, "Debuff"), (vulnerable, "Debuff"), (strength, "Buff") })
        {
            string name = power.GetType().Name;
            foreach (bool visible in new[] { true, false })
            {
                var row = Power(power, visible);
                Check(Equals(row["type_for_current_amount"], expected) && row["is_temporary_power"] is false
                    && !row.ContainsKey("unavailable_reasons"), $"{name} (visible={visible}): current type {expected}, not temporary");
            }
            Check(!FormatPowerInternalState(Power(power)).Contains("type for current amount")
                && !FormatPowerInternalState(Power(power)).Contains("temporary"), $"{name}: same-as-type and false are JSON-only in markdown");
        }
        Check(_stateWarnings.Count == 0, "readable Rend inputs add no warnings");

        // Sign flip: Strength below 0 keeps Type Buff but GetTypeForAmount gives Debuff.
        strength.CurrentType = MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff;
        var negative = Power(strength);
        Check(Equals(negative["type"], "Buff") && Equals(negative["type_for_current_amount"], "Debuff") && negative["is_temporary_power"] is false,
            "negative Strength: type stays PowerModel.Type, type_for_current_amount is Debuff (Rend counts it)");
        Check(FormatPowerInternalState(negative).Contains("type for current amount: Debuff"), "a flipped type is shown in markdown");
        strength.CurrentType = null;
        Check(Equals(negative["type_for_current_amount"], "Debuff") && Equals(Power(strength)["type_for_current_amount"], "Buff"), "snapshot independent of a later amount change");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(negative, options)))
            Check(json.RootElement.GetProperty("type_for_current_amount").GetString() == "Debuff"
                && json.RootElement.GetProperty("is_temporary_power").ValueKind == JsonValueKind.False, "wire string and real false");

        // Temporary powers: a debuff-typed one (Mangle) and buff-typed ones (Flex, Speed).
        foreach (var (power, expected) in new (PowerModel, string)[] { (new ManglePower(), "Debuff"), (new FlexPotionPower(), "Buff"), (new SpeedPotionPower(), "Buff") })
        {
            string name = power.GetType().Name;
            var row = Power(power, false);
            Check(Equals(row["type_for_current_amount"], expected) && row["is_temporary_power"] is true && !row.ContainsKey("unavailable_reasons"),
                $"{name}: ITemporaryPower on the instance (hidden row too), current type {expected}");
            Check(FormatPowerInternalState(Power(power)).Contains("is temporary power: yes"), $"{name}: markdown shows temporary");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, options));
            Check(json.RootElement.GetProperty("is_temporary_power").ValueKind == JsonValueKind.True, $"{name}: wire true");
        }
        Check(Power(new PossessStrengthPower())["is_temporary_power"] is false, "a non-temporary Strength-related power is not temporary");

        // Read failure: null, reason and warning; never PowerModel.Type in its place, other fields kept.
        BeginStateWarnings();
        var failing = new WeakPower { ThrowCurrentType = true };
        var failed = Power(failing);
        Unknown(failed, "type_for_current_amount", "status.WeakPower");
        Check(Equals(failed["type"], "Debuff") && failed["is_temporary_power"] is false && failed["skip_next_duration_tick"] is false,
            "a failed current type keeps type, is_temporary_power and the other fields");
        Check(FormatPowerInternalState(failed).Contains("type for current amount: null (InvalidOperationException: type for amount failed)"), "markdown shows the failure and reason");
        var failedHidden = Power(new ManglePower { ThrowCurrentType = true }, false);
        Unknown(failedHidden, "type_for_current_amount", "status.ManglePower");
        Check(failedHidden["is_temporary_power"] is true, "hidden row failure keeps is_temporary_power");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failed, options)))
            Check(json.RootElement.GetProperty("type_for_current_amount").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("type_for_current_amount", out _), "wire null and reason");

        // Side-effect free: one getter read per row; Data, _dynamicVars and Applier untouched.
        var probe = new WeakPower();
        probe.ClearDynamicVars();
        var before = probe.PeekData();
        Power(probe);
        Check(probe.CurrentTypeReads == 1 && !probe.DynamicVarsInitialized && ReferenceEquals(probe.PeekData(), before) && probe.ApplierReads == 0,
            "reading the Rend inputs touches only TypeForCurrentAmount");
        Check(Power(new PowerModel()).Keys.Take(5).SequenceEqual(new[] { "id", "amount", "type", "type_for_current_amount", "is_temporary_power" }),
            "Rend inputs follow the base row keys");
    }

    // sts2#1031: DampenPower.Data (casters, downgraded cards -> original upgrade level).
    private static void RunDampenContract()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        BeginStateWarnings();
        var strike = new CardModel("STRIKE_R");
        var bash = new CardModel("BASH");
        var owner = new Player();
        owner.Deck.Cards.Add(strike);
        owner.Deck.Cards.Add(bash);
        RefreshCardUidRegistry(new CombatState { Players = [owner] });
        var strikeCombat = new CardModel("STRIKE_R") { DeckVersion = strike };
        var bashCombat = new CardModel("BASH") { DeckVersion = bash };
        // Pile rows are built before status rows; this stands in for them.
        string strikeUid = GetStableCardUid(strikeCombat), bashUid = GetStableCardUid(bashCombat);

        var dampen = new DampenPower();
        object? data = dampen.PeekData();
        var empty = Power(dampen);
        Check(empty["caster_combat_ids"] is List<uint> none && none.Count == 0 && empty["downgraded_cards"] is List<Dictionary<string, object?>> noCards && noCards.Count == 0
            && !empty.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "Dampen: 0 casters and an empty map are a readable empty, no reason or warning");
        Check(FormatPowerInternalState(empty).Contains("caster combat ids: none") && FormatPowerInternalState(empty).Contains("downgraded cards: none"), "Dampen: empty markdown is none");

        var knight = new Creature { CombatId = 0 };
        dampen.AddCaster(knight);
        dampen.Record(strikeCombat, 1);
        dampen.Record(bashCombat, 1);
        var one = Power(dampen);
        Check(one["caster_combat_ids"] is List<uint> oneIds && oneIds.SequenceEqual(new uint[] { 0 }), "Dampen: one caster, CombatId 0 is a real id");
        var cards = (List<Dictionary<string, object?>>)one["downgraded_cards"]!;
        Check(cards.Count == 2 && Equals(cards[0]["uid"], bashUid) && Equals(cards[1]["uid"], strikeUid)
            && string.CompareOrdinal(bashUid, strikeUid) < 0, "Dampen: rows sorted by uid, not by the map's insertion (AllCards) order");
        Check(cards.All(r => r["uid_known"] is true && Equals(r["original_upgrade_level"], 1) && !r.ContainsKey("uid_unavailable_reason"))
            && Equals(cards[0]["card_id"], "BASH") && Equals(cards[1]["card_id"], "STRIKE_R"), "Dampen: registry uid, card id and original level per row");
        Check(FormatPowerInternalState(one).Contains("caster combat ids: 0") && FormatPowerInternalState(one).Contains($"downgraded cards: {bashUid} from +1, {strikeUid} from +1"), "Dampen markdown");

        var second = new Creature { CombatId = 5 };
        var first = new Creature { CombatId = 2 };
        dampen.RemoveCaster(knight);
        dampen.AddCaster(second);
        dampen.AddCaster(first);
        var two = Power(dampen);
        Check(two["caster_combat_ids"] is List<uint> twoIds && twoIds.SequenceEqual(new uint[] { 2, 5 }), "Dampen: two casters in ascending combat_id order");
        Check(one["caster_combat_ids"] is List<uint> kept && kept.SequenceEqual(new uint[] { 0 }), "Dampen: snapshot independent of later caster changes");
        Check(Power(dampen, false)["caster_combat_ids"] is List<uint> hiddenIds && hiddenIds.Count == 2 && Power(dampen, false)["downgraded_cards"] is List<Dictionary<string, object?>> { Count: 2 }
            && !Power(dampen, false).ContainsKey("skip_next_duration_tick"), "Dampen: hidden rows carry both fields");
        Check(FormatPowerInternalState(two).Contains("caster combat ids: 2, 5"), "Dampen: two casters markdown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(two, options)))
        {
            var ids = json.RootElement.GetProperty("caster_combat_ids");
            var row = json.RootElement.GetProperty("downgraded_cards")[0];
            Check(ids.GetArrayLength() == 2 && ids[0].GetUInt32() == 2 && row.GetProperty("uid").GetString() == bashUid
                && row.GetProperty("uid_known").GetBoolean() && row.GetProperty("original_upgrade_level").GetInt32() == 1 && row.GetProperty("card_id").GetString() == "BASH",
                "Dampen: wire array of uint and rows");
        }

        // A card the registry has never seen: reported per row, never given a uid.
        var unseen = new CardModel("INFLAME");
        dampen.Record(unseen, 2);
        var withUnseen = Power(dampen);
        var unseenRows = (List<Dictionary<string, object?>>)withUnseen["downgraded_cards"]!;
        var unseenRow = unseenRows[^1];
        Check(unseenRows.Count == 3 && unseenRow["uid"] == null && unseenRow["uid_known"] is false && Equals(unseenRow["card_id"], "INFLAME")
            && Equals(unseenRow["original_upgrade_level"], 2) && unseenRow["uid_unavailable_reason"] is string reason && reason.Contains("INFLAME"),
            "Dampen: an unregistered card keeps its level with uid=null, uid_known=false and a reason; it sorts after known uids");
        Check(unseenRows.Take(2).All(r => r["uid_known"] is true) && !withUnseen.ContainsKey("unavailable_reasons"), "Dampen: a missing uid does not fail the field or the other rows");
        Check(_stateWarnings.Any(w => w.StartsWith("status.DampenPower.downgraded_cards.uid:") && w.Contains("INFLAME")), "Dampen: a missing uid is a warning");
        Check(!TryGetRegisteredCardUid(unseen, out _), "Dampen: the read does not register a uid for the unseen card");
        Check(FormatPowerInternalState(withUnseen).Contains("INFLAME (uid unknown) from +2"), "Dampen: unknown uid markdown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(withUnseen, options)))
        {
            var row = json.RootElement.GetProperty("downgraded_cards")[2];
            Check(row.GetProperty("uid").ValueKind == JsonValueKind.Null && !row.GetProperty("uid_known").GetBoolean() && row.GetProperty("original_upgrade_level").GetInt32() == 2
                && row.GetProperty("uid_unavailable_reason").GetString()!.Contains("INFLAME"),
                "Dampen: the production wire keeps the explicit null uid, uid_known=false, the level and the reason");
        }

        // No side effects: Data, its contents, the var set and the registry stay as they are.
        dampen.ClearDynamicVars();
        Power(dampen);
        Check(ReferenceEquals(dampen.PeekData(), data) && dampen.PeekCasterCount() == 2 && dampen.PeekRecordCount() == 3, "Dampen: read never replaces or mutates Data");
        Check(!dampen.DynamicVarsInitialized && dampen.ApplierReads == 0, "Dampen: read builds no _dynamicVars and reads no Applier");

        // A caster without a CombatId fails casters only; no id is substituted.
        var noId = new Creature();
        dampen.AddCaster(noId);
        var badCaster = Power(dampen);
        Unknown(badCaster, "caster_combat_ids", "status.DampenPower");
        Check(badCaster["downgraded_cards"] is List<Dictionary<string, object?>> { Count: 3 } && FormatPowerInternalState(badCaster).Contains("caster combat ids: null ("), "Dampen: a caster failure keeps downgraded_cards");

        // Data null: both fields fail and Data is never initialized.
        BeginStateWarnings();
        var nullData = new DampenPower();
        nullData.SetData(null);
        var nullRow = Power(nullData);
        Unknown(nullRow, "caster_combat_ids", "status.DampenPower");
        Unknown(nullRow, "downgraded_cards", "status.DampenPower");
        Check(nullData.PeekData() == null && nullRow["skip_next_duration_tick"] is false && Equals(nullRow["amount"], 1), "Dampen: null Data is not initialized; other fields kept");
        Check(FormatPowerInternalState(nullRow).Contains("downgraded cards: null ("), "Dampen: failed markdown");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(new MissingDampenDataPower()), options)))
            Check(json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("caster_combat_ids", out _)
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("downgraded_cards", out _), "Dampen: wire reasons for null Data");
        var wrongCasters = Power(new WrongDampenCastersFieldPower());
        Unknown(wrongCasters, "caster_combat_ids", "status.WrongDampenCastersFieldPower");
        Check(wrongCasters["downgraded_cards"] is List<Dictionary<string, object?>> { Count: 0 }, "Dampen: a wrong casters type fails only casters");
        var missingMap = Power(new MissingDampenMapFieldPower());
        Unknown(missingMap, "downgraded_cards", "status.MissingDampenMapFieldPower");
        Check(missingMap["caster_combat_ids"] is List<uint> { Count: 0 }, "Dampen: a missing map field fails only downgraded_cards");
        Check(!Power(new ToricToughnessPower()).ContainsKey("caster_combat_ids") && !Power(new PanachePower()).ContainsKey("downgraded_cards"), "other powers carry no Dampen keys");
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
        Check(Power(plain, false).Count == 6 && Power(plain, false).ContainsKey("type_for_current_amount") && Power(plain, false).ContainsKey("is_temporary_power") && Power(plain, false)["dynamic_vars"] is Dictionary<string, object?> { Count: 0 },
            "hidden or visibility-unknown powers carry only the Rend inputs (sts2#1034) and dynamic_vars (sts2#1020)");
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
        var chains = new ChainsOfBindingPower();
        foreach (bool value in new[] { false, true, false })
        {
            chains.Set(value); // Boundary fixture supplies updates; no game hook is simulated.
            object? data = chains.PeekData();
            var row = Power(chains);
            Check(Equals(row["bound_card_played"], value) && !row.ContainsKey("unavailable_reasons"), "Bound flag reads current private Data bool, including reset");
            Check(ReferenceEquals(chains.PeekData(), data) && chains.Peek() == value, "Bound read never replaces or mutates Data");
            Check(FormatPowerInternalState(row).Contains("bound card played: " + (value ? "yes" : "no")), "Bound markdown preserves both true and false");
        }
        chains.Set(true);
        var boundSnapshot = Power(chains);
        chains.Set(false);
        Check(boundSnapshot["bound_card_played"] is true && Power(chains)["bound_card_played"] is false, "Bound snapshot stays independent of game reset");
        Check(Power(chains, false).Count == 6, "hidden Bound power omits internal state (only the Rend inputs and dynamic_vars)");
        foreach (object? invalidData in new object?[] { null, new object() })
        {
            chains.SetData(invalidData);
            var row = Power(chains);
            Unknown(row, "bound_card_played", "status.ChainsOfBindingPower");
            Check(row["skip_next_duration_tick"] is false && Equals(row["amount"], 1), "Bound Data failure keeps other fields");
            Check(ReferenceEquals(chains.PeekData(), invalidData), "failed Bound read never initializes Data");
            Check(FormatPowerInternalState(row).Contains("bound card played: null ("), "unavailable Bound flag markdown includes reason");
        }
        foreach (var invalidPower in new ChainsOfBindingPower[] { new MissingBoundDataPower(), new MissingBoundFieldPower(), new WrongBoundFieldPower() })
        {
            object? data = invalidPower.PeekData();
            Unknown(Power(invalidPower), "bound_card_played", "status." + invalidPower.GetType().Name);
            Check(ReferenceEquals(invalidPower.PeekData(), data), "missing or wrong Bound field leaves Data unchanged");
        }
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
        var curl = new CurlUpPower();
        var noTracked = Power(curl);
        Check(noTracked["played_card_uid"] == null && noTracked["played_card_readable"] is true && !noTracked.ContainsKey("unavailable_reasons"), "CurlUp readable null means no tracked attack");
        Check(FormatPowerInternalState(noTracked).Contains("played card uid: none") && !FormatPowerInternalState(noTracked).Contains("played card readable"), "CurlUp known none markdown omits readable success");
        Check(!Power(curl, false).ContainsKey("played_card_uid") && !Power(curl, false).ContainsKey("played_card_readable"), "hidden CurlUp omits played card fields");
        Check(!noTracked.ContainsKey("card_to_clone_uid") && !Power(juggling).ContainsKey("played_card_uid"), "CurlUp and Juggling fields stay on their own power");
        curl.SetPlayed(pendingCard);
        var tracked = Power(curl);
        Check(Equals(tracked["played_card_uid"], uid) && Equals(tracked["played_card_uid"], "STRIKE_R#0") && tracked["played_card_readable"] is true, "CurlUp tracked card uses shared deck uid registry");
        Check(FormatPowerInternalState(tracked).Contains("played card uid: " + uid) && !FormatPowerInternalState(tracked).Contains("played card readable"), "CurlUp tracked uid markdown");
        Check(ReferenceEquals(curl.PeekPlayed(), pendingCard), "CurlUp read keeps game reference unchanged");
        curl.SetPlayed(generated);
        Check(Equals(Power(curl)["played_card_uid"], GetStableCardUid(generated)) && !Equals(Power(curl)["played_card_uid"], uid), "CurlUp generated card gets its own shared uid");
        curl.SetPlayed(null);
        Check(Power(curl)["played_card_uid"] == null && Equals(tracked["played_card_uid"], uid), "CurlUp clear observed and old snapshot detached");
        foreach (object? invalidData in new object?[] { null, new object() })
        {
            curl.SetData(invalidData);
            var failedCurl = Power(curl);
            Unknown(failedCurl, "played_card_uid", "status.CurlUpPower");
            Check(failedCurl["played_card_readable"] is false && failedCurl["skip_next_duration_tick"] is false && Equals(failedCurl["amount"], 1), "CurlUp Data failure keeps other fields");
            Check(FormatPowerInternalState(failedCurl).Contains("played card uid: null (") && FormatPowerInternalState(failedCurl).Contains("played card readable: no"), "unreadable CurlUp differs from known none in markdown");
            Check(ReferenceEquals(curl.PeekData(), invalidData), "failed CurlUp read never initializes Data");
        }
        foreach (var invalidCurl in new CurlUpPower[] { new MissingCurlUpDataPower(), new MissingPlayedCardFieldPower(), new WrongPlayedCardFieldPower() })
        {
            object? data = invalidCurl.PeekData();
            var invalidRow = Power(invalidCurl);
            Unknown(invalidRow, "played_card_uid", "status." + invalidCurl.GetType().Name);
            Check(invalidRow["played_card_readable"] is false && ReferenceEquals(invalidCurl.PeekData(), data), "missing or wrong CurlUp field leaves Data unchanged");
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

        // Thievery / Heist: per-target instances. Values come from the boundary double;
        // ThieveryPower.Steal / SurprisePower.AfterDeath are not simulated.
        var thiefA = new ThieveryPower { Target = first };
        var thiefB = new ThieveryPower { Target = second };
        thiefA.DynamicVars.Gold.Value = 0m;
        thiefB.DynamicVars.Gold.Value = 13m;
        var rowA = Power(thiefA);
        var rowB = Power(thiefB);
        Check(Equals(rowA["stolen_gold"], 0) && Equals(rowA["target_player_id"], "17") && !rowA.ContainsKey("unavailable_reasons"), "Thievery zero stolen gold is a real int, not unknown");
        Check(Equals(rowB["stolen_gold"], 13) && Equals(rowB["target_player_id"], "23") && Equals(rowB["amount"], 1), "Thievery stolen gold is Gold.IntValue, independent of amount");
        Check(FormatPowerInternalState(rowA).Contains("stolen gold: 0; target player id: 17"), "Thievery markdown shows zero and target");
        thiefB.DynamicVars.Gold.Value = 20m;
        Check(Equals(rowB["stolen_gold"], 13) && Equals(Power(thiefB)["stolen_gold"], 20), "Thievery snapshot detached from later steal");
        var hiddenThief = Power(thiefB, false);
        Check(Equals(hiddenThief["stolen_gold"], 20) && Equals(hiddenThief["target_player_id"], "23") && !hiddenThief.ContainsKey("skip_next_duration_tick"), "hidden (other player's target) Thievery row keeps target and stolen gold only");
        var noTargetThief = new ThieveryPower();
        noTargetThief.DynamicVars.Gold.Value = 5m;
        var noTargetRow = Power(noTargetThief);
        Unknown(noTargetRow, "target_player_id", "status.ThieveryPower");
        Check(Equals(noTargetRow["stolen_gold"], 5) && Reasons(noTargetRow)["target_player_id"].Contains("Target がありません"), "missing Target is null with reason, stolen gold kept");
        var monsterTarget = Power(new ThieveryPower { Target = new Creature() });
        Unknown(monsterTarget, "target_player_id", "status.ThieveryPower");
        Check(Reasons(monsterTarget)["target_player_id"].Contains("player ではありません"), "non-player Target is not replaced by a default id");
        thiefA.DynamicVars.Gold.ThrowBase = true;
        var failedGold = Power(thiefA);
        Unknown(failedGold, "stolen_gold", "status.ThieveryPower");
        Check(Equals(failedGold["target_player_id"], "17") && FormatPowerInternalState(failedGold).Contains("stolen gold: null (InvalidOperationException: base failed)"), "Gold failure keeps target and shows reason");
        thiefA.DynamicVars.Gold.ThrowBase = false;
        var heistA = Power(new HeistPower { Target = first });
        var heistB = Power(new HeistPower { Target = second }, false);
        Check(Equals(heistA["target_player_id"], "17") && Equals(heistB["target_player_id"], "23") && !heistA.ContainsKey("stolen_gold"), "Heist rows keep their own target; amount untouched; no stolen_gold");
        Check(Equals(heistA["amount"], 1) && FormatPowerInternalState(heistA).Contains("target player id: 17"), "Heist markdown target");
        Unknown(Power(new HeistPower()), "target_player_id", "status.HeistPower");
        Check(!Power(new PowerModel { Target = first }).ContainsKey("target_player_id"), "other targeted powers do not get target fields");

        // Hex: PowerModel.Applier read as-is. AfterDeath / PowerCmd.Apply are not simulated.
        BeginStateWarnings();
        var knight = new Creature { CombatId = 7 };
        var hex = new HexPower { Applier = knight };
        var hexRow = Power(hex);
        Check(hexRow["applier_readable"] is true && hexRow["applier_combat_id"] is uint knightId && knightId == 7
            && !hexRow.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "Hex applier is the applier's CombatId");
        Check(ReferenceEquals(hex.PeekApplier(), knight) && knight.CombatId == 7, "Hex read leaves the applier untouched");
        Check(FormatPowerInternalState(hexRow).Contains("applier combat id: 7") && !FormatPowerInternalState(hexRow).Contains("applier readable"),
            "Hex markdown shows the id; readable=true is JSON-only");
        knight.CombatId = 0;
        var zeroHex = Power(hex);
        Check(zeroHex["applier_readable"] is true && zeroHex["applier_combat_id"] is uint zeroId && zeroId == 0 && !zeroHex.ContainsKey("unavailable_reasons"),
            "CombatId 0 is a real id, not unknown or none");
        Check(FormatPowerInternalState(zeroHex).Contains("applier combat id: 0"), "Hex markdown shows id 0");
        Check(hexRow["applier_combat_id"] is uint keptId && keptId == 7, "Hex snapshot independent of later id changes");
        var noApplier = Power(new HexPower());
        Check(noApplier["applier_readable"] is true && noApplier.ContainsKey("applier_combat_id") && noApplier["applier_combat_id"] == null
            && !noApplier.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "readable null applier is a known none without reasons");
        Check(FormatPowerInternalState(noApplier).Contains("applier combat id: none") && !FormatPowerInternalState(noApplier).Contains("readable"),
            "known none applier markdown");
        var hiddenHex = Power(hex, false);
        Check(hiddenHex["applier_readable"] is true && hiddenHex["applier_combat_id"] is uint && !hiddenHex.ContainsKey("skip_next_duration_tick"),
            "hidden / visibility-unknown Hex row keeps the applier only");
        var idless = Power(new HexPower { Applier = new Creature() });
        Unknown(idless, "applier_combat_id", "status.HexPower");
        Check(idless["applier_readable"] is false && Reasons(idless)["applier_combat_id"].Contains("CombatId"), "applier without CombatId is a failed read, not none");
        Check(FormatPowerInternalState(idless).Contains("applier combat id: null (InvalidOperationException")
            && FormatPowerInternalState(idless).Contains("applier readable: no"), "failed applier markdown shows reason and readable: no");
        var failedHex = Power(new HexPower { Applier = knight, ThrowApplier = true, Skip = true });
        Unknown(failedHex, "applier_combat_id", "status.HexPower");
        Check(failedHex["applier_readable"] is false && failedHex["skip_next_duration_tick"] is true, "applier getter failure keeps other row fields");
        Check(!Power(new PowerModel { Applier = knight }).ContainsKey("applier_combat_id") && !Power(new HeistPower { Applier = knight, Target = first }).ContainsKey("applier_readable"),
            "other powers do not get applier fields");
        Check(new PowerModel { Applier = knight }.ApplierReads == 0, "construction does not read Applier");

        // Ritual (sts2#1018): the private flag as-is, on every emitted row (hidden too); false is a real value.
        BeginStateWarnings();
        var ritual = new RitualPower();
        var freshRitual = Power(ritual);
        Check(freshRitual["was_just_applied_by_enemy"] is false && !freshRitual.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0,
            "Ritual flag false is a real value");
        ritual.Set(true);
        var justApplied = Power(ritual, visible: false);
        Check(justApplied["was_just_applied_by_enemy"] is true && ritual.Peek() && !justApplied.ContainsKey("skip_next_duration_tick"),
            "Ritual flag is read on hidden rows and the read leaves it untouched");
        Check(FormatPowerInternalState(justApplied).Contains("was just applied by enemy: yes")
            && FormatPowerInternalState(freshRitual).Contains("was just applied by enemy: no"), "Ritual flag markdown");
        Check(freshRitual["was_just_applied_by_enemy"] is false, "Ritual snapshot independent of later changes");
        Check(!Power(new HexPower()).ContainsKey("was_just_applied_by_enemy"), "other powers do not get the Ritual flag");
        var ritualFail = new Dictionary<string, object?>();
        ReadInternalStateValue(ritualFail, "was_just_applied_by_enemy",
            () => ReadDeclaredStateField<bool>(ritual, typeof(RitualPower), "_removed"), "status.RitualPower");
        Unknown(ritualFail, "was_just_applied_by_enemy", "status.RitualPower");

        // Vigor: VigorPower.Data binding, read as-is. Before/After attack are not simulated.
        // Own warning window: the response list is capped at MaxStateWarnings.
        BeginStateWarnings();
        var eel = new Creature();
        var other = new Creature();
        Dictionary<string, object?> Vigor(Dictionary<string, object?> row) => (Dictionary<string, object?>)row["vigor_attack"]!;
        void VigorUnknown(Dictionary<string, object?> row, string key, string context)
        {
            Check(Vigor(row).ContainsKey(key) && Vigor(row)[key] == null, $"vigor {key} is explicit null");
            Check(Reasons(row).TryGetValue("vigor_attack." + key, out var reason) && reason.Length > 0, $"vigor {key} has row reason");
            Check(_stateWarnings.Any(w => w.StartsWith(context + ".vigor_attack." + key + ":")), $"vigor {key} has path warning");
            Check(Vigor(row)["readable"] is false, "vigor failure is readable=false");
        }
        var vigor = new VigorPower { Owner = eel };
        var unbound = Power(vigor);
        Check(Vigor(unbound)["readable"] is true && Vigor(unbound)["has_command"] is false && Vigor(unbound)["attacker_is_owner"] == null
            && Equals(Vigor(unbound)["amount_when_started"], 0) && Vigor(unbound).Count == 4 && !unbound.ContainsKey("unavailable_reasons"),
            "fresh Vigor is a known unbound state, not unknown");
        Check(FormatPowerInternalState(unbound).Contains("vigor attack: has command: no, attacker is owner: none, amount when started: 0")
            && !FormatPowerInternalState(unbound).Contains("readable"), "unbound Vigor markdown; readable=true is JSON-only");
        var ownCommand = new AttackCommand { Attacker = eel };
        vigor.Bind(ownCommand, 6);
        var bound = Power(vigor);
        Check(Vigor(bound)["readable"] is true && Vigor(bound)["has_command"] is true && Vigor(bound)["attacker_is_owner"] is true
            && Equals(Vigor(bound)["amount_when_started"], 6) && !bound.ContainsKey("unavailable_reasons"), "Vigor bound to the owner's attack");
        Check(FormatPowerInternalState(bound).Contains("vigor attack: has command: yes, attacker is owner: yes, amount when started: 6"), "bound Vigor markdown");
        Check(vigor.Peek() == (ownCommand, 6) && ReferenceEquals(vigor.Peek().Item1, ownCommand), "Vigor read leaves the binding untouched");
        vigor.Bind(new AttackCommand { Attacker = other }, 3);
        var foreign = Power(vigor);
        Check(Vigor(foreign)["has_command"] is true && Vigor(foreign)["attacker_is_owner"] is false && Equals(Vigor(foreign)["amount_when_started"], 3),
            "binding to another attacker is a known false, distinct from unbound null");
        vigor.Bind(new AttackCommand(), 2);
        Check(Vigor(Power(vigor))["attacker_is_owner"] is false && Vigor(Power(vigor))["readable"] is true, "null Attacker compares unequal like the game");
        vigor.Bind(null, 5);
        var staleAmount = Power(vigor);
        Check(Vigor(staleAmount)["has_command"] is false && Vigor(staleAmount)["attacker_is_owner"] == null && Equals(Vigor(staleAmount)["amount_when_started"], 5),
            "unbound reports the real started field, not a default");
        Check(Equals(Vigor(bound)["amount_when_started"], 6) && Vigor(bound)["attacker_is_owner"] is true, "Vigor snapshot independent of later binding");
        vigor.Bind(ownCommand, 6);
        var hiddenVigor = Power(vigor, false);
        Check(Vigor(hiddenVigor)["has_command"] is true && Vigor(hiddenVigor)["attacker_is_owner"] is true && !hiddenVigor.ContainsKey("skip_next_duration_tick"),
            "hidden / visibility-unknown Vigor row keeps the binding only");
        ownCommand.ThrowAttacker = true;
        var failedAttacker = Power(vigor);
        VigorUnknown(failedAttacker, "attacker_is_owner", "status.VigorPower");
        Check(Vigor(failedAttacker)["has_command"] is true && Equals(Vigor(failedAttacker)["amount_when_started"], 6) && failedAttacker["skip_next_duration_tick"] is false,
            "Attacker failure keeps the other Vigor values and row fields");
        Check(FormatPowerInternalState(failedAttacker).Contains("vigor attack: readable: no, has command: yes, attacker is owner: null (InvalidOperationException: attacker failed), amount when started: 6"),
            "Vigor attacker failure markdown");
        ownCommand.ThrowAttacker = false;
        vigor.ThrowOwner = true;
        VigorUnknown(Power(vigor), "attacker_is_owner", "status.VigorPower");
        vigor.Bind(null, 0);
        Check(Vigor(Power(vigor))["readable"] is true && Vigor(Power(vigor))["attacker_is_owner"] == null, "unbound never reads Owner");
        vigor.ThrowOwner = false;
        foreach (object? invalidData in new object?[] { null, new object() })
        {
            var broken = new VigorPower { Owner = eel };
            broken.SetData(invalidData);
            var row = Power(broken);
            foreach (string key in new[] { "has_command", "attacker_is_owner", "amount_when_started" })
                VigorUnknown(row, key, "status.VigorPower");
            Check(ReferenceEquals(broken.PeekData(), invalidData), "failed Vigor read never initializes Data");
            Check(FormatPowerInternalState(row).Contains("vigor attack: readable: no, has command: null (") && !FormatPowerInternalState(row).Contains("attacker is owner: none"),
                "unreadable Vigor differs from unbound in markdown");
        }
        var noData = Power(new MissingVigorDataPower { Owner = eel });
        foreach (string key in new[] { "has_command", "attacker_is_owner", "amount_when_started" })
            VigorUnknown(noData, key, "status.MissingVigorDataPower");
        foreach (var badCommand in new VigorPower[] { new MissingVigorCommandFieldPower(), new WrongVigorCommandFieldPower() })
        {
            object? data = badCommand.PeekData();
            var row = Power(badCommand);
            VigorUnknown(row, "has_command", "status." + badCommand.GetType().Name);
            VigorUnknown(row, "attacker_is_owner", "status." + badCommand.GetType().Name);
            Check(Equals(Vigor(row)["amount_when_started"], 4) && ReferenceEquals(badCommand.PeekData(), data), "command field failure keeps amount and Data");
        }
        var badAmount = Power(new WrongVigorAmountFieldPower { Owner = eel });
        VigorUnknown(badAmount, "amount_when_started", "status.WrongVigorAmountFieldPower");
        Check(Vigor(badAmount)["has_command"] is false && !Reasons(badAmount).ContainsKey("vigor_attack.has_command"), "amount field failure keeps known unbound");
        Check(!Power(new PowerModel()).ContainsKey("vigor_attack") && !Power(new JugglingPower()).ContainsKey("vigor_attack"), "other powers have no vigor_attack");
        BeginStateWarnings();

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
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(rowA, wireOptions)))
            Check(json.RootElement.GetProperty("stolen_gold").GetInt32() == 0 && json.RootElement.GetProperty("target_player_id").GetString() == "17", "Thievery zero int and string target survive production wire");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(zeroHex, wireOptions)))
            Check(json.RootElement.GetProperty("applier_combat_id").GetUInt32() == 0 && json.RootElement.GetProperty("applier_readable").GetBoolean(), "Hex applier id 0 survives production wire as a number");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noApplier, wireOptions)))
            Check(json.RootElement.GetProperty("applier_combat_id").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("applier_readable").GetBoolean()
                && !json.RootElement.TryGetProperty("unavailable_reasons", out _), "known none applier survives production wire as null with success flag");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(idless, wireOptions)))
            Check(json.RootElement.GetProperty("applier_combat_id").ValueKind == JsonValueKind.Null && !json.RootElement.GetProperty("applier_readable").GetBoolean()
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("applier_combat_id", out _), "failed applier survives production wire as null, false and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noTargetRow, wireOptions)))
            Check(json.RootElement.GetProperty("target_player_id").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("target_player_id", out _), "missing Thievery target survives wire as null and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failedSkip, wireOptions)))
            Check(json.RootElement.GetProperty("skip_next_duration_tick").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("skip_next_duration_tick", out _), "power null and reason survive wire");
        foreach (bool value in new[] { false, true })
        {
            var wireChains = new ChainsOfBindingPower();
            wireChains.Set(value);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(wireChains), wireOptions));
            Check(json.RootElement.GetProperty("bound_card_played").GetBoolean() == value, "Bound flag retains boolean wire type including false");
        }
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(chains), wireOptions)))
            Check(json.RootElement.GetProperty("bound_card_played").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("bound_card_played", out _), "Bound null and reason survive production wire settings");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(partialPaper, wireOptions)))
            Check(json.RootElement.GetProperty("cards_exhausted").GetInt32() == 4 && json.RootElement.GetProperty("ethereal_count").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("ethereal_count", out _), "relic partial values and null reason survive wire");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noPending, wireOptions)))
            Check(json.RootElement.GetProperty("card_to_clone_uid").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("card_to_clone_readable").GetBoolean() && !json.RootElement.TryGetProperty("unavailable_reasons", out _), "known empty pending survives production wire as null with success flag");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(new WrongCloneFieldPower()), wireOptions)))
            Check(json.RootElement.GetProperty("card_to_clone_uid").ValueKind == JsonValueKind.Null && !json.RootElement.GetProperty("card_to_clone_readable").GetBoolean() && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("card_to_clone_uid", out _), "failed pending survives production wire as null, false and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noTracked, wireOptions)))
            Check(json.RootElement.GetProperty("played_card_uid").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("played_card_readable").GetBoolean() && !json.RootElement.TryGetProperty("unavailable_reasons", out _), "CurlUp known none survives production wire as null with success flag");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(tracked, wireOptions)))
            Check(json.RootElement.GetProperty("played_card_uid").GetString() == uid, "CurlUp tracked uid survives production wire as string");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(Power(new WrongPlayedCardFieldPower()), wireOptions)))
            Check(json.RootElement.GetProperty("played_card_uid").ValueKind == JsonValueKind.Null && !json.RootElement.GetProperty("played_card_readable").GetBoolean() && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("played_card_uid", out _), "failed CurlUp survives production wire as null, false and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(BuildRelicCombatState(new WrongScarfField()), wireOptions)))
            Check(json.RootElement.GetProperty("cards_played_this_turn").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("cards_played_this_turn", out _), "failed scarf survives production wire as null and reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(unbound, wireOptions)))
        {
            var va = json.RootElement.GetProperty("vigor_attack");
            Check(va.GetProperty("readable").GetBoolean() && va.GetProperty("has_command").ValueKind == JsonValueKind.False
                && va.GetProperty("attacker_is_owner").ValueKind == JsonValueKind.Null && va.GetProperty("amount_when_started").GetInt32() == 0,
                "unbound Vigor survives production wire with false, explicit null and int 0");
        }
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(bound, wireOptions)))
        {
            var va = json.RootElement.GetProperty("vigor_attack");
            Check(va.GetProperty("has_command").GetBoolean() && va.GetProperty("attacker_is_owner").GetBoolean() && va.GetProperty("amount_when_started").GetInt32() == 6,
                "bound Vigor survives production wire");
        }
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(noData, wireOptions)))
        {
            var va = json.RootElement.GetProperty("vigor_attack");
            Check(!va.GetProperty("readable").GetBoolean() && va.GetProperty("has_command").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("vigor_attack.has_command", out _),
                "failed Vigor survives production wire as readable=false, null and row reason");
        }
        RunCardEffectPowerContract();
        RunPowerDynamicVarsContract();
        RunPowerPrivateDataContract();
        RunRelicCounterContract();
        RunDampenContract();
        RunRendInputContract();
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
        {
            // sts2#1020: vars and run_state are attached for every relic, before (outside) the combat guard.
            int vars = builder.IndexOf("try { AttachRelicDynamicVars(relicState, relic); }", StringComparison.Ordinal);
            int run = builder.IndexOf("AttachRelicSection(relicState, \"run_state\", () => BuildRelicRunState(relic), relic.Id.Entry);", StringComparison.Ordinal);
            int guard = builder.IndexOf("AttachRelicSection(relicState, \"combat_state\", () => BuildRelicCombatState(relic), relic.Id.Entry);", StringComparison.Ordinal);
            Check(vars > 0 && run > vars && guard > run && builder.LastIndexOf("if (inCombat)", guard, StringComparison.Ordinal) > run
                && !builder.Contains("ReadRelicDynamicVarValues"),
                "relic vars (field read) and run_state are wired outside the combat guard; both sections go through AttachRelicSection");
        }
        {
            // Dampen's downgraded_cards only looks up uids (TryGetRegisteredCardUid): in BuildPlayerState
            // the hand and pile rows, which register them, must be built before the status rows.
            int playerStart = builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState(Player player)", StringComparison.Ordinal);
            int hand = builder.IndexOf("state[\"hand\"] = hand;", playerStart, StringComparison.Ordinal);
            int playPile = builder.IndexOf("state[\"play_pile\"] = BuildPileCardList(", playerStart, StringComparison.Ordinal);
            int status = builder.IndexOf("state[\"status\"] = BuildPowersState(creature);", playerStart, StringComparison.Ordinal);
            Check(playerStart >= 0 && hand > playerStart && playPile > hand && status > playPile,
                "BuildPlayerState builds the hand and pile rows before the status rows (Dampen uid lookup)");
        }
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildPlayerState("), "multiplayer local player shares path");
        Check(Source("McpMod.Formatting.cs").Split("{FormatPowerInternalState(p)}").Length == 4, "player, enemy, pet markdown wired");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 12;"), "current schema is 12 (sts2#1020)");
        Check(Source("McpMod.StateBuilder.cs").Contains("[\"type\"] = power.Type.ToString(),")
            && Source("McpMod.StateBuilder.cs").Contains("[\"type\"] = SafeGetText(() => power.Type.ToString()),"),
            "status type stays PowerModel.Type in full and minimal rows (TypeForCurrentAmount is the new key)");
        Check(Source("McpMod.StateBuilder.cs").Contains("if (!isVisible && !HasLiveStack(power) && !IsCurrentDebuffOrUnknown(power)) continue;")
            && Source("McpMod.StateBuilder.cs").Contains("try { return power.TypeForCurrentAmount == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff; }\n        catch { return true; }"),
            "only hidden, zero-amount rows that are not a current Debuff are dropped; an unreadable type keeps the row (sts2#1034)");
        Console.WriteLine($"PASS: {Checks} power/relic internal-state contract checks");
    }
}
