using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using WuppyRelic = MegaCrit.Sts2.Core.Models.Relics.Wuppy;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string Loyalty = "WUPPYS_LOYALTY_POWER";
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static void Main()
    {
        var combat = new CombatState();
        CombatManager.Instance.Combat = combat;
        CombatManager.Instance.IsInProgress = true;
        var player = new Player();
        var teammate = new Player();
        player.Creature.CombatState = teammate.Creature.CombatState = combat;
        var ownPets = player.PlayerCombatState!;
        var teamPets = teammate.PlayerCombatState!;
        Check(!Snapshot(player).TryGetProperty("pets", out _), "combat without an eligible pet omits pets");

        var relic = new WuppyRelic();
        player.Relics.Add(relic);
        var empty = Snapshot(player);
        Check(empty.GetProperty("pets").GetArrayLength() == 0 && !empty.TryGetProperty("unavailable_reasons", out _),
            "an owned relic before its summon has an observed empty array");
        relic.CombatWuppy = new Creature { Monster = new Wuppy(), PetOwner = player, CombatState = new CombatState() };
        Check(Snapshot(player).GetProperty("pets").GetArrayLength() == 0, "previous combat's relic reference is not a current pet");
        var wuppy = new Creature { Monster = new Wuppy(), PetOwner = player, CombatState = combat, CombatId = 0, CurrentHp = 30, MaxHp = 30, Block = 2 };
        relic.CombatWuppy = wuppy;
        Check(Snapshot(player).GetProperty("pets").GetArrayLength() == 0, "summon can create the reference before registering the pet");
        combat.AllyList.Add(wuppy);
        CheckPetsFailure(Snapshot(player), "player", "missing from PlayerCombatState.Pets");
        ownPets.PetList.Add(wuppy);
        var loyalty = new PowerModel(Loyalty, includedInStatus: false);
        wuppy.PowerList.AddRange([loyalty, new PowerModel("VISIBLE_POWER", includedInStatus: true)]);

        var alive = Snapshot(player).GetProperty("pets")[0];
        Check(alive.GetProperty("id").GetString() == "WUPPY" && alive.GetProperty("name").GetString() == "ワッピー" &&
            alive.GetProperty("combat_id").GetUInt32() == 0 && alive.GetProperty("hp").GetInt32() == 30 &&
            alive.GetProperty("max_hp").GetInt32() == 30 && alive.GetProperty("alive").GetBoolean() && alive.GetProperty("block").GetInt32() == 2,
            "living Wuppy has its actual name, health, block, and numeric combat ID (including zero)");
        Check(alive.GetProperty("status").GetArrayLength() == 1 &&
            alive.GetProperty("power_ids").EnumerateArray().Select(id => id.GetString()).SequenceEqual([Loyalty, "VISIBLE_POWER"]),
            "status can omit an ID carried by the unfiltered list, which retains power order");

        wuppy.CurrentHp = 0;
        var osty = new Creature { Monster = new Osty(), PetOwner = player, CombatState = combat, CombatId = 2, CurrentHp = 12, MaxHp = 12 };
        ownPets.PetList.Add(osty);
        var teamWuppy = new Creature { Monster = new Wuppy(), PetOwner = teammate, CombatState = combat, CombatId = 1, CurrentHp = 53, MaxHp = 60 };
        teamPets.PetList.Add(teamWuppy);
        teammate.Relics.Add(new WuppyRelic { CombatWuppy = teamWuppy });
        combat.AllyList.AddRange([osty, teamWuppy]);
        var localRows = Snapshot(player).GetProperty("pets");
        Check(localRows.GetArrayLength() == 2 && localRows[0].GetProperty("id").GetString() == "OSTY" &&
            localRows[0].EnumerateObject().Select(field => field.Name).ToHashSet().SetEquals(["id", "name", "alive", "hp", "max_hp", "block", "status"]),
            "Osty keeps the original first row with exactly its legacy fields");
        Check(localRows[1].GetProperty("combat_id").GetUInt32() == 0 && localRows[1].GetProperty("hp").GetInt32() == 0 &&
            !localRows[1].GetProperty("alive").GetBoolean() && localRows[1].GetProperty("power_ids")[0].GetString() == Loyalty,
            "dead Wuppy retains its row, ID, and loyalty alongside Osty");
        var remoteRows = Snapshot(teammate, "players[1]").GetProperty("pets");
        Check(remoteRows.GetArrayLength() == 1 && remoteRows[0].GetProperty("combat_id").GetUInt32() == 1 &&
            remoteRows[0].GetProperty("hp").GetInt32() == 53 && remoteRows[0].GetProperty("power_ids").GetArrayLength() == 0,
            "teammate rows are scoped to the owner, and no powers is a known empty array");

        ownPets.PetList.Add(teamWuppy);
        CheckPetsFailure(Snapshot(player), "player", "different owner");
        ownPets.PetList.Remove(teamWuppy);
        wuppy.CombatState = new CombatState();
        CheckPetsFailure(Snapshot(player), "player", "current combat");
        wuppy.CombatState = combat;
        combat.AllyList.Remove(wuppy);
        CheckPetsFailure(Snapshot(player), "player", "not an ally");
        combat.AllyList.Add(wuppy);
        relic.CombatWuppy = null;
        CheckPetsFailure(Snapshot(player), "player", "disagrees");
        relic.IsMelted = true;
        Check(Snapshot(player).GetProperty("pets").GetArrayLength() == 2, "melted relic uses actual pet membership");
        player.Relics.Remove(relic);
        Check(Snapshot(player).GetProperty("pets").GetArrayLength() == 2, "registered pet can outlive removal of its relic");
        player.Relics.Add(relic);
        relic.IsMelted = false;
        relic.CombatWuppy = wuppy;

        ownPets.ThrowPets = true;
        CheckPetsFailure(Snapshot(player), "player", "Pets unavailable");
        ownPets.ThrowPets = false;
        teammate.PlayerCombatState = null;
        CheckPetsFailure(Snapshot(teammate, "players[1]"), "players[1]", "PlayerCombatState is unavailable");
        teammate.PlayerCombatState = teamPets;

        wuppy.CombatId = null;
        wuppy.ThrowName = true;
        loyalty.ThrowId = true;
        var partial = Snapshot(player);
        var partialRow = partial.GetProperty("pets")[1];
        foreach (var key in new[] { "combat_id", "name", "power_ids" })
        {
            Check(partialRow.GetProperty(key).ValueKind == JsonValueKind.Null && partialRow.GetProperty("unavailable_reasons").TryGetProperty(key, out _) &&
                HasWarning(partial, $"player.pets[1].{key}"), $"{key} failure is a JSON null with a row reason and indexed warning");
        }
        Check(partialRow.GetProperty("hp").GetInt32() == 0 && partialRow.GetProperty("status").GetArrayLength() == 1,
            "independent observed values survive other field failures");
        wuppy.ThrowPowers = true;
        var missingPowers = Snapshot(player);
        Check(missingPowers.GetProperty("pets")[1].GetProperty("status").ValueKind == JsonValueKind.Null &&
            missingPowers.GetProperty("pets")[1].GetProperty("power_ids").ValueKind == JsonValueKind.Null &&
            HasWarning(missingPowers, "player.pets[1].status"), "failed power collection read invalidates both power fields");

        CombatManager.Instance.IsInProgress = false;
        ownPets.ThrowPets = true;
        var outside = Snapshot(player);
        Check(!outside.TryGetProperty("pets", out _) && !outside.TryGetProperty("warnings", out _), "outside combat ignores lingering pet data");

        CheckWiring();
        Console.WriteLine("Wuppy state contract OK");
    }

    private static JsonElement Snapshot(Player player, string path = "player")
    {
        BeginStateWarnings();
        var state = new Dictionary<string, object?>();
        AttachPetsState(state, player, path);
        EndStateWarnings(state);
        return JsonSerializer.SerializeToElement(state, WireOptions);
    }

    private static void CheckPetsFailure(JsonElement state, string path, string reason)
    {
        Check(state.GetProperty("pets").ValueKind == JsonValueKind.Null &&
            state.GetProperty("unavailable_reasons").GetProperty("pets").GetString()!.Contains(reason) &&
            HasWarning(state, $"{path}.pets"), $"{path} reports unavailable pets for {reason}");
    }

    private static bool HasWarning(JsonElement state, string path) => state.GetProperty("warnings").EnumerateArray()
        .Any(warning => warning.GetString()!.StartsWith(path + ":", StringComparison.Ordinal));

    private static void CheckWiring()
    {
        static string Source(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", file));
        Check(Source("McpMod.StateBuilder.cs").Contains("AttachPetsState(state, player, \"player\");") &&
            Source("McpMod.MultiplayerState.cs").Contains("AttachPetsState(entry, player, $\"players[{players.Count}]\");"),
            "local and teammate snapshots attach the same pets builder");
        var patches = Source("McpMod.CombatLogPatches.cs");
        Check(patches.Contains("row[\"result\"] = LogDamageResult(damage.Result);") &&
            patches.Contains("row[\"hits\"] = attacked.DamageResults.Select(LogDamageResult).ToList();") &&
            patches.Contains("[\"receiver\"] = LogCreatureRef(result.Receiver)") && patches.Contains("[\"original_target\"] = LogCreatureRef(result.OriginalTarget)") &&
            Source("McpMod.CombatLog.cs").Contains("[\"combat_id\"] = creature.CombatId"),
            "combat log connects the actual receiver and original target using Creature.CombatId");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
