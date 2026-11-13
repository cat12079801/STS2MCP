using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    // Same wire settings as McpMod._jsonOptions, including null handling and casing.
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }
    private static Dictionary<string, string> Reasons(Dictionary<string, object?> h) => (Dictionary<string, string>)h["unavailable_reasons"]!;
    private static Dictionary<string, object?> Plays(Dictionary<string, object?> h, string phase) => (Dictionary<string, object?>)h[$"card_plays_{phase}_this_turn"]!;
    private static Dictionary<string, int> Types(Dictionary<string, object?> h, string phase) => (Dictionary<string, int>)Plays(h, phase)["by_type"]!;
    private static List<string> Last(Dictionary<string, object?> h) => (List<string>)h["last_player_turn_card_uids"]!;
    private static List<string> ThisTurn(Dictionary<string, object?> h) => (List<string>)h["card_plays_finished_this_turn_card_uids"]!;
    private static string Markdown(Dictionary<string, object?>? history)
    {
        var sb = new StringBuilder();
        var player = new Dictionary<string, object?>();
        if (history != null) player["combat_history"] = history;
        AppendCombatHistoryMarkdown(sb, player);
        return sb.ToString();
    }

    public static void Main()
    {
        BeginStateWarnings();
        var combat = new CombatState();
        var owner = new Player { Creature = new Creature { CombatState = combat } };
        var other = new Player { Creature = new Creature { CombatState = combat } };
        var history = CombatManager.Instance.History;
        T Current<T>(T entry, bool current = true) where T : CombatHistoryEntry
        {
            entry.ThisTurn = state => ReferenceEquals(state, combat) ? current : throw new Exception("wrong combat passed to HappenedThisTurn");
            history.Items.Add(entry);
            return entry;
        }
        T Previous<T>(T entry) where T : CombatHistoryEntry
        {
            entry.LastTurn = player => ReferenceEquals(player, owner) ? true : throw new Exception("wrong owner passed to HappenedLastPlayerTurn");
            return Current(entry, false);
        }
        var attack = new CardModel(CardType.Attack);
        var attackPlay = new CardPlay(attack, owner);
        var otherPlay = new CardPlay(new CardModel(CardType.Attack), other);
        RefreshCardUidRegistry(combat);
        string pileUid = GetStableCardUid(attack);
        var empty = BuildCombatHistoryState(owner);
        Check(Equals(empty["turn_number"], 1), "turn number comes from player state");
        Check(Equals(Plays(empty, "started")["total"], 0) && Types(empty, "started").Values.All(n => n == 0), "empty history has real zero counts");
        Check(Types(empty, "started").Keys.SequenceEqual(Enum.GetNames<CardType>()), "every card type including None/Status/Curse/Quest");
        Check(Last(empty).Count == 0 && ThisTurn(empty).Count == 0 && Reasons(empty).Count == 0 && _stateWarnings.Count == 0, "known empty differs from unknown");
        Check(Markdown(empty) == "" && Markdown(null) == "", "empty history and missing field stay quiet");

        Current(new CardPlayStartedEntry(attackPlay));
        Current(new CardPlayStartedEntry(attackPlay)); // Replay is a second event, not a distinct card.
        foreach (var type in Enum.GetValues<CardType>().Where(t => t != CardType.Attack))
            Current(new CardPlayStartedEntry(new CardPlay(new CardModel(type), owner)));
        Current(new CardPlayStartedEntry(otherPlay));
        Current(new CardPlayStartedEntry(attackPlay), false);
        Current(new CardPlayFinishedEntry(attackPlay));
        var skillPlay = new CardPlay(new CardModel(CardType.Skill), owner);
        Current(new CardPlayFinishedEntry(skillPlay));
        Current(new CardPlayFinishedEntry(otherPlay));
        Current(new CardPlayFinishedEntry(otherPlay));
        Previous(new CardPlayFinishedEntry(attackPlay));
        Previous(new CardPlayFinishedEntry(attackPlay));
        Previous(new CardPlayFinishedEntry(otherPlay)); // Bolas tests identity, not the playing owner.
        Current(new CardExhaustedEntry { Actor = owner.Creature });
        Current(new CardExhaustedEntry { Actor = owner.Creature });
        Current(new CardExhaustedEntry { Actor = other.Creature });
        Current(new CardExhaustedEntry { Actor = owner.Creature }, false);
        foreach (int amount in new[] { 2, 7, 0, -1 })
            Current(new DamageReceivedEntry(new DamageResult(amount)) { Actor = owner.Creature });
        Current(new DamageReceivedEntry(new DamageResult(9)) { Actor = other.Creature });
        Current(new DamageReceivedEntry(new DamageResult(5)) { Actor = owner.Creature }, false);
        Current(new BlockGainedEntry(attackPlay, ValueProp.Move) { Actor = owner.Creature });
        Current(new BlockGainedEntry(attackPlay, ValueProp.Move) { Actor = other.Creature });
        Current(new BlockGainedEntry(attackPlay, ValueProp.Move | ValueProp.SkipHurtAnim));
        Current(new BlockGainedEntry(null, ValueProp.Move) { Actor = owner.Creature });
        Current(new BlockGainedEntry(attackPlay, ValueProp.Unpowered) { Actor = owner.Creature });
        Current(new BlockGainedEntry(otherPlay, ValueProp.Move) { Actor = owner.Creature });
        Current(new BlockGainedEntry(attackPlay, ValueProp.Move), false);
        var full = BuildCombatHistoryState(owner);
        Check(Equals(Plays(full, "started")["total"], 8) && Types(full, "started")["Attack"] == 2, "started filters turn and playing player, counts replays");
        foreach (var type in Enum.GetNames<CardType>().Where(t => t != "Attack"))
            Check(Types(full, "started")[type] == 1, $"counts {type}");
        Check(Equals(Plays(full, "finished")["total"], 2) && Types(full, "finished")["Attack"] == 1 && Types(full, "finished")["Power"] == 0, "started and finished are independent");
        Check(Last(full).SequenceEqual(new[] { pileUid, pileUid, GetStableCardUid(otherPlay.Card) }), "last turn keeps entry order, duplicate and other-player uid using shared registry");
        Check(ThisTurn(full).SequenceEqual(new[] { pileUid, GetStableCardUid(skillPlay.Card), GetStableCardUid(otherPlay.Card), GetStableCardUid(otherPlay.Card) }), "this-turn uids filter turn, keep order, duplicate and other-player plays using shared registry");
        Check(Equals(full["cards_exhausted_this_turn"], 2), "exhaust counts events for owner, not exhaust pile size");
        Check(Equals(full["unblocked_damage_events_this_turn"], 2) && Equals(full["unblocked_damage_events_this_combat"], 3), "positive unblocked hits, not HP total, with separate time scopes");
        Check(Equals(full["block_gains_this_turn"], 3), "Unmovable uses playing owner, Move flag, repeated events, no receiver filter");
        Check(Reasons(full).Count == 0 && _stateWarnings.Count == 0, "valid history has no warnings");
        string saved = JsonSerializer.Serialize(full, WireOptions);
        string markdown = Markdown(full);
        Check(markdown.Contains("player turn 1") && markdown.Contains("started=8") && markdown.Contains("finished=2") && markdown.Contains("Attack:2") && markdown.Contains(pileUid), "compact markdown shows useful counts and uid");
        Check(markdown.Contains("exhausted=2") && markdown.Contains("unblocked hits (turn)=2") && markdown.Contains("unblocked hits (combat)=3") && markdown.Contains("card block gains=3"), "compact markdown distinguishes damage scopes and block/exhaust events");
        Check(markdown.Contains($"this turn=[{string.Join(", ", ThisTurn(full))}]") && markdown.Contains($"last turn=[{string.Join(", ", Last(full))}]"), "markdown distinguishes nonempty current and previous uid arrays");
        Check(Markdown(new(empty) { ["cards_exhausted_this_turn"] = 1 }).Contains("this turn=[") == false, "empty current uid array stays hidden when another field is shown");
        Check(!markdown.Contains("Power:0") && JsonSerializer.Serialize(full, WireOptions) == saved, "markdown omits empty type counts and does not mutate JSON");
        using (var json = JsonDocument.Parse(saved))
        {
            Check(json.RootElement.GetProperty("turn_number").GetInt32() == 1, "turn number is numeric");
            Check(json.RootElement.GetProperty("card_plays_started_this_turn").GetProperty("by_type").GetProperty("Attack").GetInt32() == 2, "type counts are numeric");
            Check(!json.RootElement.GetProperty("card_plays_started_this_turn").GetProperty("by_type").TryGetProperty("attack", out _), "wire naming policy leaves dictionary type keys unchanged");
            Check(json.RootElement.GetProperty("last_player_turn_card_uids")[0].GetString() == pileUid, "uids are strings");
            Check(json.RootElement.GetProperty("card_plays_finished_this_turn_card_uids")[0].GetString() == pileUid, "this-turn uids are strings with production wire options");
        }
        history.Items.Clear();
        owner.PlayerCombatState!.Number = 3;
        var next = BuildCombatHistoryState(owner);
        Check(Equals(next["turn_number"], 3) && Equals(next["cards_exhausted_this_turn"], 0) && Last(next).Count == 0 && ThisTurn(next).Count == 0, "fresh snapshot follows engine reset and player-specific turn number");
        Check(JsonSerializer.Serialize(full, WireOptions) == saved, "previous snapshot detached from history changes");

        var brokenCard = new CardModel(CardType.Attack) { ThrowType = true };
        Current(new CardPlayStartedEntry(attackPlay));
        var brokenEntry = Current(new CardPlayStartedEntry(new CardPlay(brokenCard, owner)));
        var failedType = BuildCombatHistoryState(owner);
        Check(failedType["card_plays_started_this_turn"] == null && Reasons(failedType)["card_plays_started_this_turn"].Contains("Type renamed"), "late getter failure discards partial counts");
        Check(Equals(Plays(failedType, "finished")["total"], 0), "one section failure preserves others");
        Check(Markdown(failedType).Contains("=null (") && Markdown(failedType).Contains("Type renamed"), "markdown displays null and reason");
        history.Items.Remove(brokenEntry);
        var brokenPredicate = Current(new CardExhaustedEntry { Actor = owner.Creature });
        brokenPredicate.ThisTurn = _ => throw new MissingMethodException("HappenedThisTurn renamed");
        var predicateFailure = BuildCombatHistoryState(owner);
        Check(predicateFailure["cards_exhausted_this_turn"] == null && Equals(Plays(predicateFailure, "started")["total"], 1), "predicate failure is isolated");
        history.Items.Clear();
        Previous(new CardPlayFinishedEntry(attackPlay));
        var badLast = Previous(new CardPlayFinishedEntry(attackPlay));
        badLast.LastTurn = _ => throw new MissingMethodException("HappenedLastPlayerTurn renamed");
        var lastFailure = BuildCombatHistoryState(owner);
        Check(lastFailure["last_player_turn_card_uids"] == null && Reasons(lastFailure)["last_player_turn_card_uids"].Contains("HappenedLastPlayerTurn renamed"), "late last-turn predicate failure discards partial uid array");
        history.Items.Clear();
        Current(new CardPlayFinishedEntry(attackPlay));
        var badCurrent = Current(new CardPlayFinishedEntry(otherPlay));
        badCurrent.ThisTurn = _ => throw new MissingMethodException("HappenedThisTurn renamed");
        var currentFailure = BuildCombatHistoryState(owner);
        const string currentUidKey = "card_plays_finished_this_turn_card_uids";
        Check(currentFailure[currentUidKey] == null && Reasons(currentFailure)[currentUidKey].Contains("HappenedThisTurn renamed"), "late this-turn predicate failure discards partial uid array and records reason");
        Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{currentUidKey}:")), "this-turn uid failure records warning");
        Check(Markdown(currentFailure).Contains($"{currentUidKey}=null (") && Markdown(currentFailure).Contains("HappenedThisTurn renamed"), "markdown shows current uid failure reason");
        Check(Last(currentFailure).Count == 0 && Equals(currentFailure["cards_exhausted_this_turn"], 0), "current uid failure preserves independent values");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(currentFailure, WireOptions)))
            Check(json.RootElement.GetProperty(currentUidKey).ValueKind == JsonValueKind.Null, "current uid failure stays null with production wire options");
        history.Items.Clear();
        history.ThrowStarted = true;
        Check(BuildCombatHistoryState(owner)["card_plays_started_this_turn"] == null, "missing public started getter is unknown");
        history.ThrowStarted = false;
        history.ThrowFinished = true;
        var failedFinished = BuildCombatHistoryState(owner);
        Check(failedFinished["card_plays_finished_this_turn"] == null && failedFinished["last_player_turn_card_uids"] == null && failedFinished[currentUidKey] == null && Equals(failedFinished["cards_exhausted_this_turn"], 0), "finished getter failure affects only dependencies");
        history.ThrowFinished = false;
        history.ThrowEntries = true;
        var failedEntries = BuildCombatHistoryState(owner);
        Check(Reasons(failedEntries).Count == 8 && Equals(failedEntries["turn_number"], 3), "all history failures preserve player turn number");
        history.ThrowEntries = false;
        CombatManager.Instance.ThrowHistory = true;
        var failedHistory = BuildCombatHistoryState(owner);
        Check(Reasons(failedHistory).Count == 8 && failedHistory["block_gains_this_turn"] == null, "missing History getter does not fabricate zero");
        CombatManager.Instance.ThrowHistory = false;
        CombatManager.Instance.Value = null;
        Check(Reasons(BuildCombatHistoryState(owner)).Count == 8, "null history is unavailable");
        CombatManager.Instance.Value = history;
        owner.Creature.CombatState = null;
        var failedCombat = BuildCombatHistoryState(owner);
        Check(Reasons(failedCombat).Count == 7 && failedCombat["cards_exhausted_this_turn"] == null && failedCombat[currentUidKey] == null && Equals(failedCombat["unblocked_damage_events_this_combat"], 0), "missing combat never silently makes HappenedThisTurn false");
        owner.Creature.CombatState = combat;
        owner.PlayerCombatState!.ThrowTurn = true;
        var failedTurn = BuildCombatHistoryState(owner);
        Check(failedTurn["turn_number"] == null && Reasons(failedTurn)["turn_number"].Contains("TurnNumber renamed"), "turn getter failure is null/reason");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failedTurn, WireOptions)))
            Check(json.RootElement.GetProperty("turn_number").ValueKind == JsonValueKind.Null, "null dictionary turn value survives production WhenWritingNull policy");
        Check(Markdown(failedTurn).Contains("turn=null ("), "turn-only failure remains visible");
        owner.PlayerCombatState = null;
        var missingPlayerState = BuildCombatHistoryState(owner);
        Check(missingPlayerState["turn_number"] == null && missingPlayerState["last_player_turn_card_uids"] == null, "missing player state cannot claim empty last turn");
        var response = new Dictionary<string, object?> { ["state_type"] = "combat", ["player"] = new Dictionary<string, object?> { ["combat_history"] = failedHistory } };
        EndStateWarnings(response);
        Check(Equals(response["state_type"], "combat") && ((List<string>)response["warnings"]!).Any(w => w.StartsWith("player.combat_history.block_gains_this_turn:")), "real collector attaches warnings to surviving state");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions)))
            Check(json.RootElement.GetProperty("player").GetProperty("combat_history").GetProperty("block_gains_this_turn").ValueKind == JsonValueKind.Null, "unavailable wire value is null");
        BeginStateWarnings();
        owner.PlayerCombatState = new();
        owner.Creature.CombatState = new CombatState();
        var recovered = BuildCombatHistoryState(owner);
        Check(Reasons(recovered).Count == 0 && _stateWarnings.Count == 0 && Markdown(recovered) == "", "new combat recovers without cached errors");
        var recoveredResponse = new Dictionary<string, object?>();
        EndStateWarnings(recoveredResponse);
        Check(!recoveredResponse.ContainsKey("warnings"), "recovery has no stale warnings");

        string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
        string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
        string playerBuilder = builder[builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal)];
        int guard = playerBuilder.IndexOf("if (combatState != null && CombatManager.Instance.IsInProgress)", StringComparison.Ordinal);
        int wiring = playerBuilder.IndexOf("state[\"combat_history\"] = BuildCombatHistoryState(player)", StringComparison.Ordinal);
        Check(guard >= 0 && wiring > guard && wiring < playerBuilder.IndexOf("state[\"gold\"]", StringComparison.Ordinal), "history wired inside shared combat-only player block");
        int refresh = playerBuilder.IndexOf("RefreshCardUidRegistry(creature.CombatState)", StringComparison.Ordinal);
        Check(refresh > guard && refresh < wiring, "shared uid registry refreshed before serializing combat cards/history, including overlays");
        Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("BuildPlayerState(localPlayer)"), "multiplayer uses same local-player history");
        Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("AppendCombatHistoryMarkdown(sb, player)"), "battle markdown wired");
        Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "additive change keeps schema 4");
        Console.WriteLine($"CombatHistoryContract: {Checks} checks passed");
    }
}
