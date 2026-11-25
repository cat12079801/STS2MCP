using System.Text.Json;
using System.Text.RegularExpressions;

STS2_MCP.McpMod.RunScenarioContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static void Check(bool condition, string label)
        {
            Checks++;
            if (!condition) throw new Exception(label);
        }

        // Stand-in for CardModel: the helpers are generic over the holder's card.
        private sealed class Card(string id, string uid)
        {
            public string Id = id;
            public string Uid = uid;
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static void RunScenarioContract()
        {
            // --- scenario_capabilities: build-level versions (the potion contract is in this build, sts2#1012) ---
            var caps = BuildScenarioCapabilities();
            Check(caps.Count == 3 && Equals(caps["setup_guard"], 1) && Equals(caps["selection_uid"], 1)
                && Equals(caps["potion_availability"], 1), "capabilities: setup_guard 1, selection_uid 1, potion_availability 1");
            Check(caps.Values.All(value => value is int), "capability values are ints (0 = not implemented)");
            Check(!ReferenceEquals(caps, BuildScenarioCapabilities()), "each payload gets its own capability object");

            // --- setup guard: the decision from the reads ---
            ScenarioSetupGuardReads Reads(string screen = "map", string? room = "MapRoom", bool? neow = null)
                => new()
                {
                    RunInProgress = true, Screen = screen, IsResolving = false, Room = room, EventIsNeow = neow,
                    CombatStarting = false, CombatInProgress = false, RunOver = false
                };
            void Allowed(ScenarioSetupGuardReads reads, string label)
            {
                var guard = DecideScenarioSetupGuard(reads);
                Check(guard["allowed"] is true && guard.ContainsKey("reason") && guard["reason"] == null, "allowed: " + label);
            }
            void Refused(ScenarioSetupGuardReads reads, string reason, string label)
            {
                var guard = DecideScenarioSetupGuard(reads);
                Check(guard["allowed"] is false && Equals(guard["reason"], reason), $"refused ({reason}): {label} -> {guard["reason"]}");
            }

            var mapGuard = DecideScenarioSetupGuard(Reads());
            Check(mapGuard.Keys.SequenceEqual(new[] { "version", "screen", "room", "combat_starting", "combat_in_progress", "run_over", "allowed", "reason" }),
                "guard keys in the design's order, plus screen");
            Check(Equals(mapGuard["version"], 1) && Equals(mapGuard["room"], "MapRoom") && mapGuard["combat_starting"] is false
                && mapGuard["combat_in_progress"] is false && mapGuard["run_over"] is false, "guard values are the reads");
            Check(JsonSerializer.Serialize(mapGuard, Json).Contains("\"reason\":null"), "reason stays present as null in JSON when allowed");
            Allowed(Reads(), "map in a MapRoom");
            Allowed(Reads("event", "EventRoom", neow: true), "Neow's event");
            Allowed(Reads("rest_site", "RestSiteRoom"), "rest site");

            Refused(new ScenarioSetupGuardReads { RunInProgress = false, Screen = "menu" }, "no_run", "main menu");
            Refused(new ScenarioSetupGuardReads { RunInProgress = null, Screen = "map" }, "unreadable", "run unreadable");
            var over = Reads("game_over", "CombatRoom"); over.RunOver = true;
            Refused(over, "run_over", "game over");
            var overMap = Reads(); overMap.RunOver = true;
            Refused(overMap, "run_over", "map after the run ended");
            var fighting = Reads("monster", "CombatRoom"); fighting.CombatInProgress = true;
            Refused(fighting, "combat_in_progress", "fight");
            var mapOverFight = Reads("map", "CombatRoom"); mapOverFight.CombatInProgress = true;
            Refused(mapOverFight, "combat_in_progress", "map opened over a live fight");
            var starting = Reads("monster", "CombatRoom"); starting.CombatStarting = true;
            Refused(starting, "combat_starting", "fight starting");
            Refused(Reads("map", "CombatRoom"), "room_not_setup", "map over a finished fight's room");
            Refused(Reads("map", "EventRoom", neow: true), "screen_room_mismatch", "map over Neow's room");
            Refused(Reads("map", "RestSiteRoom"), "screen_room_mismatch", "map over the rest site");
            Refused(Reads("event", "EventRoom", neow: false), "event_not_neow", "another event");
            Refused(Reads("event", "EventRoom", neow: null), "unreadable", "event identity unreadable");
            Refused(Reads("map", "MerchantRoom"), "room_not_setup", "map over a shop");
            Refused(Reads("map", "TreasureRoom"), "room_not_setup", "map over a treasure room");
            Refused(Reads("map", null), "no_current_room", "no room (travelling)");
            var roomUnread = Reads("map", null); roomUnread.RoomRead = false; roomUnread.Unreadable.Add("room: boom");
            var unreadGuard = DecideScenarioSetupGuard(roomUnread);
            Check(unreadGuard["allowed"] is false && Equals(unreadGuard["reason"], "unreadable")
                && unreadGuard["unavailable_reasons"] is List<string> why && why.Single() == "room: boom", "room unreadable: null, reason and cause");
            foreach (var screen in new[] { "shop", "treasure", "card_select", "hand_select", "card_reward", "rewards", "menu", "game_over", "overlay", "unknown", "monster" })
                Refused(Reads(screen, "MapRoom"), "screen_not_setup", "front screen " + screen);
            var resolving = Reads("rest_site", "RestSiteRoom"); resolving.IsResolving = true;
            Refused(resolving, "resolving", "resolving");
            var unknownResolving = Reads(); unknownResolving.IsResolving = null;
            Refused(unknownResolving, "unreadable", "is_resolving missing");
            foreach (var field in new[] { "starting", "in_progress", "over" })
            {
                var reads = Reads();
                if (field == "starting") reads.CombatStarting = null;
                if (field == "in_progress") reads.CombatInProgress = null;
                if (field == "over") reads.RunOver = null;
                var guard = DecideScenarioSetupGuard(reads);
                Check(guard["allowed"] is false && Equals(guard["reason"], "unreadable"), "null read is not allowed: " + field);
            }
            var nullCombat = Reads(); nullCombat.CombatStarting = null;
            Check(DecideScenarioSetupGuard(nullCombat)["combat_starting"] == null, "an unread flag is reported as null, not false");

            // --- dev_console guard request ---
            ScenarioSetupGuardRequest? Parse(string json, out string? error)
            {
                error = ParseScenarioSetupGuardRequest(JsonDocument.Parse(json).RootElement, out var parsed);
                return parsed;
            }
            foreach (var screen in new[] { "map", "event", "rest_site" })
                Check(Parse($"{{\"version\":1,\"expected_screen\":\"{screen}\"}}", out var error) is { Version: 1 } parsed
                    && parsed.ExpectedScreen == screen && error == null, "guard request accepted: " + screen);
            foreach (var bad in new[] { "null", "1", "\"map\"", "[]", "{}", "{\"version\":1}", "{\"expected_screen\":\"map\"}",
                "{\"version\":2,\"expected_screen\":\"map\"}", "{\"version\":0,\"expected_screen\":\"map\"}", "{\"version\":1.5,\"expected_screen\":\"map\"}",
                "{\"version\":\"1\",\"expected_screen\":\"map\"}", "{\"version\":1,\"expected_screen\":\"shop\"}",
                "{\"version\":1,\"expected_screen\":\"MAP\"}", "{\"version\":1,\"expected_screen\":null}",
                "{\"version\":1,\"expected_screen\":\"map\",\"allow_combat\":true}" })
                Check(Parse(bad, out var error) == null && error != null && error.Contains("not executed"), "guard request refused: " + bad);

            var mapRequest = new ScenarioSetupGuardRequest(1, "map");
            Check(ScenarioSetupGuardConsoleRejection(DecideScenarioSetupGuard(Reads()), mapRequest) == null, "same screen, allowed: run");
            Check(ScenarioSetupGuardConsoleRejection(DecideScenarioSetupGuard(Reads("rest_site", "RestSiteRoom")), mapRequest)!
                .Contains("expected map, now rest_site"), "moved to another setup screen since the GET: refused");
            Check(ScenarioSetupGuardConsoleRejection(DecideScenarioSetupGuard(fighting), mapRequest)!.Contains("combat_in_progress"),
                "moved into a fight since the GET: refused with the guard's reason");
            Check(ScenarioSetupGuardConsoleRejection(null, mapRequest) != null, "no guard read: refused");
            Check(ScenarioSetupGuardConsoleRejection(new() { ["allowed"] = "true", ["screen"] = "map" }, mapRequest) != null,
                "allowed must be the boolean true");

            // --- expected_uid parameter ---
            Dictionary<string, JsonElement> Body(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            Check(ReadExpectedUid(Body("{\"index\":0}"), out var noUid) == null && noUid == null, "expected_uid absent: unchecked");
            Check(ReadExpectedUid(Body("{\"expected_uid\":\"STRIKE_IRONCLAD#3\"}"), out var someUid) == null && someUid == "STRIKE_IRONCLAD#3", "expected_uid read");
            foreach (var bad in new[] { "{\"expected_uid\":\"\"}", "{\"expected_uid\":3}", "{\"expected_uid\":null}", "{\"expected_uid\":[\"a\"]}" })
                Check(ReadExpectedUid(Body(bad), out var badUid) != null && badUid == null, "malformed expected_uid refused: " + bad);
            Check(ExpectedUidMismatch("A#1", 2, "A#1") == null, "uid match");
            Check(ExpectedUidMismatch("A#1", 2, "A#2")!.Contains("does not match") && ExpectedUidMismatch("A#1", 2, "A#2")!.Contains("nothing was pressed"),
                "uid mismatch refuses");
            Check(ExpectedUidMismatch("A#1", 2, null, "out of combat")!.Contains("out of combat"), "no uid to compare refuses with the reason");

            // --- hand_select: holder positions, not compacted ---
            var strikeA = new Card("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#0");
            var strikeB = new Card("STRIKE_IRONCLAD", "STRIKE_IRONCLAD#1");
            var defend = new Card("DEFEND_IRONCLAD", "DEFEND_IRONCLAD#5");
            var holders = new Card?[] { strikeA, null, strikeB, defend };
            var rows = HandSelectHolderRows<Card>(holders);
            Check(rows.Select(row => row.Index).SequenceEqual(new[] { 0, 2, 3 }), "a holder without a card leaves a gap: indices 0, 2, 3");
            Check(ReferenceEquals(rows[1].Card, strikeB) && ReferenceEquals(rows[2].Card, defend), "rows keep their holder's card");
            Check(HandSelectHolderRows<Card>(Array.Empty<Card?>()).Count == 0 && HandSelectHolderRows<Card>(new Card?[] { null }).Count == 0,
                "no rows for no / empty holders");

            string? Press(int index, string? expected, string? blocked = null, Card?[]? list = null)
                => HandSelectPressRejection<Card>(list ?? holders, index, blocked, expected, card => card.Uid);
            // Each row's index presses the holder whose card the row shows.
            foreach (var (index, card) in rows)
                Check(Press(index, card.Uid) == null, $"row {index}: index and uid name the same holder");
            Check(Press(1, null)!.Contains("no card") && Press(1, "STRIKE_IRONCLAD#0")!.Contains("no card"),
                "the gap's index is refused with or without expected_uid");
            Check(Press(4, null)!.Contains("out of range") && Press(-1, "x")!.Contains("out of range"), "out of range");
            Check(Press(0, "STRIKE_IRONCLAD#1")!.Contains("does not match"), "same-name card with another uid is refused");
            Check(Press(0, null) == null, "without expected_uid a press that lands is kept");

            // OnHolderPressed's early returns (peek, overlay on the stack, no combat): every press there
            // would be a silent no-op, so it is refused with the reason, with or without expected_uid.
            Check(HandPressBlocker(false, 0, true) == null, "no peek, no overlay, combat in progress: presses land");
            var blockers = new (bool? Peek, int? Overlays, bool? Combat, string Word)[]
            {
                (true, 0, true, "peeked"), (null, 0, true, "peeked"), (false, 1, true, "overlay"), (false, 3, true, "overlay"),
                (false, null, true, "overlay stack"), (false, 0, false, "no combat"), (false, 0, null, "combat")
            };
            foreach (var (peek, overlays, combat, word) in blockers)
            {
                var blocker = HandPressBlocker(peek, overlays, combat);
                Check(blocker != null && blocker.Contains(word), $"blocked: {word} -> {blocker}");
                Check(Press(0, null, blocker)!.Contains("not pressed") && Press(0, "STRIKE_IRONCLAD#0", blocker)!.Contains(word),
                    $"blocked press refused with or without expected_uid: {word}");
                Check(Press(1, null, blocker)!.Contains("no card"), $"an empty holder is still reported as such: {word}");
            }
            var reordered = new Card?[] { strikeB, strikeA, defend };
            Check(Press(0, "STRIKE_IRONCLAD#0", list: reordered)!.Contains("does not match")
                && Press(1, "STRIKE_IRONCLAD#0", list: reordered) == null, "reordered hand: the old index no longer matches the uid");
            Check(HandSelectPressRejection<Card>(holders, 0, null, "STRIKE_IRONCLAD#0", _ => null, "no live combat")!.Contains("no live combat"),
                "uid unavailable refuses with the reason");

            // --- wiring in the production sources (not linked: they need the game) ---
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Source(string file) => File.ReadAllText(Path.Combine(sources, file));
            string Slice(string text, string start, string end)
            {
                int from = text.IndexOf(start, StringComparison.Ordinal);
                Check(from >= 0, "slice start: " + start);
                int to = text.IndexOf(end, from, StringComparison.Ordinal);
                Check(to > from, "slice end: " + end);
                return text[from..to];
            }
            string mod = Source("McpMod.cs");
            Check(mod.Contains("public const int StateSchemaVersion = 12;"), "published schema is 12 (sts2#1020)");
            Check(System.Text.RegularExpressions.Regex.IsMatch(mod, @"public const string StateSchema = ""2\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";"), "published schema MAJOR is 2 (the exact version is checked in V0113CompatContract)");
            string getState = Slice(mod, "private static void HandleGetState", "if (format == \"markdown\")");
            Check(getState.Contains("bool setupGuard = passive\n") && getState.Contains("ScenarioSetupGuardKey, false)")
                && getState.Contains("built[ScenarioSetupGuardKey] = BuildScenarioSetupGuard(built);"),
                "GET adds the guard only on a passive read, from the state built in the same main-thread call");
            Check(Slice(mod, "if (path == \"/\")", "else if (path == \"/api/v1/singleplayer\")").Contains("[\"scenario_capabilities\"] = BuildScenarioCapabilities()"),
                "GET / reports the capabilities");
            Check(Source("McpMod.StateStamp.cs").Contains("[\"scenario_capabilities\"] = BuildScenarioCapabilities()"), "every state payload reports the capabilities");

            string live = Source("McpMod.ScenarioGuardLive.cs");
            Check(live.Contains("runState.CurrentRoom") && live.Contains("room?.GetType().Name") && live.Contains("runState.IsGameOver")
                && live.Contains("combat.IsStarting") && live.Contains("combat.IsInProgress") && live.Contains("eventRoom.CanonicalEvent is Neow"),
                "guard reads CurrentRoom's type, IsGameOver, IsStarting/IsInProgress and Neow");
            Check(live.Contains("_passiveRead = true;") && live.Contains("finally { _passiveRead = previous; }")
                && live.Contains("BuildScenarioSetupGuard(BuildGameState())"), "the console's guard read builds a passive state");

            string console = Source("McpMod.DevConsole.cs");
            string start = Slice(console, "private static Dictionary<string, object?> StartDevConsole(\n        string command", "private static void CompleteDevConsole");
            Check(start.IndexOf("CheckScenarioSetupGuardForConsole(command, guard)", StringComparison.Ordinal) > 0
                && start.IndexOf("CheckScenarioSetupGuardForConsole(command, guard)", StringComparison.Ordinal)
                    < start.IndexOf("console.ProcessCommand(command)", StringComparison.Ordinal),
                "the guard is checked in the main-thread call, immediately before ProcessCommand");

            string builder = Source("McpMod.StateBuilder.cs");
            string handSelect = Slice(builder, "private static Dictionary<string, object?> BuildHandSelectState", "private static Dictionary<string, object?> BuildRelicSelectState");
            Check(handSelect.Contains("var holderCards = hand.ActiveHolders.Select(holder => holder.CardModel).ToList();")
                && handSelect.Contains("foreach (var (index, card) in HandSelectHolderRows(holderCards))")
                && handSelect.Contains("cardInfo[\"index\"] = index;") && !handSelect.Contains("index++;"),
                "hand_select rows: ActiveHolders positions via the linked helper, no compaction");
            Check(handSelect.Contains("cardInfo[\"uid\"] = combatState != null ? GetStableCardUid(card) : null;")
                && handSelect.Contains("string? pressBlocked = ReadHandPressBlocker(hand);")
                && handSelect.Contains("cardInfo[\"enabled\"] = pressBlocked == null;"), "hand_select rows carry uid and enabled (same blocker as the action)");
            Check(handSelect.Contains("ReadHandSelectedCards(hand, out var selectedReason)")
                && handSelect.Contains("[\"uid\"] = combatState != null ? GetStableCardUid(card) : null")
                && handSelect.Contains("state[\"selected_cards\"] = null;") && handSelect.Contains("state[\"selected_cards\"] = selectedCards;")
                && !handSelect.Contains("NSelectedHandCardHolder"), "selected_cards from _selectedCards with uid; [] vs null kept apart");
            Check(handSelect.Contains("state[\"min_select\"] = prefs?.MinSelect;") && handSelect.Contains("state[\"max_select\"] = prefs?.MaxSelect;")
                && handSelect.Contains("state[\"can_confirm\"] = IsHandSelectConfirmPressable(hand);"), "min/max from _prefs, can_confirm shared with the action");
            string hand = Source("McpMod.HandSelection.cs");
            Check(hand.Contains("GetInstanceFieldValue(hand, \"_selectedCards\") is List<CardModel>") && hand.Contains("GetInstanceFieldValue(hand, \"_prefs\") is CardSelectorPrefs")
                && hand.Contains("hand.PeekButton?.IsPeeking") && hand.Contains("button.IsEnabled && IsNodeVisible(button)")
                && hand.Contains("NOverlayStack.Instance?.ScreenCount") && hand.Contains("CombatManager.Instance.IsInProgress")
                && hand.Contains("return HandPressBlocker(peeking, overlays, inCombat);"), "hand reads: _selectedCards, _prefs, peek, overlay stack, combat, shown+enabled confirm");

            string actions = Source("McpMod.Actions.cs");
            string combatSelect = Slice(actions, "private static Dictionary<string, object?> ExecuteCombatSelectCard", "private static Dictionary<string, object?> ExecuteCombatConfirmSelection");
            Check(combatSelect.Contains("ReadExpectedUid(data, out var expectedUid)") && combatSelect.Contains("var holders = hand.ActiveHolders;")
                && combatSelect.Contains("string? pressBlocked = ReadHandPressBlocker(hand);")
                && combatSelect.Contains("HandSelectPressRejection(holderCards, index, pressBlocked, expectedUid,")
                && combatSelect.IndexOf("HandSelectPressRejection", StringComparison.Ordinal) < combatSelect.IndexOf("EmitSignal", StringComparison.Ordinal),
                "combat_select_card checks the same holders and the uid before pressing");
            Check(Slice(actions, "private static Dictionary<string, object?> ExecuteCombatConfirmSelection", "private static Dictionary<string, object?> ExecuteSelectRelic")
                .Contains("if (!IsHandSelectConfirmPressable(hand))"), "confirm uses the state's can_confirm check");
            string selectCard = Slice(actions, "private static Dictionary<string, object?> ExecuteSelectCard(", "private static Dictionary<string, object?> ExecuteConfirmSelection");
            Check(selectCard.Contains("return ExecuteSelectGridCard(gridScreen, index, expectedUid);"), "grid select passes expected_uid");
            Check(selectCard.IndexOf("ExpectedUidMismatch(expectedUid, index,", StringComparison.Ordinal) > 0
                && selectCard.IndexOf("ExpectedUidMismatch(expectedUid, index,", StringComparison.Ordinal) < selectCard.IndexOf("RevealCardSelectScreen(chooseScreen, revealed)", StringComparison.Ordinal),
                "choose-a-card checks the uid at the holder index before revealing or pressing");
            string grid = Slice(Source("McpMod.CardSelection.cs"), "private static Dictionary<string, object?> ExecuteSelectGridCard", "private static string? GridExpectedUidMismatch");
            Check(Regex.Matches(grid, @"GridExpectedUidMismatch\(expectedUid, index, ").Count == 2
                && grid.IndexOf("GridExpectedUidMismatch", StringComparison.Ordinal) < grid.IndexOf("RevealCardSelectScreen(screen, revealed)", StringComparison.Ordinal)
                && grid.LastIndexOf("GridExpectedUidMismatch", StringComparison.Ordinal) < grid.IndexOf("method.Invoke(screen", StringComparison.Ordinal),
                "grid checks the uid before revealing and again on the snapshot it clicks");

            string formatting = Source("McpMod.Formatting.cs");
            Check(formatting.Contains("state.GetValueOrDefault(ScenarioSetupGuardKey) is Dictionary<string, object?> setupGuard")
                && formatting.Contains("**Select:** {minSelect}-{maxSelect}"), "Markdown shows the guard and the selection range");
            string server = Source("server.py");
            Check(server.Contains("async def combat_select_card(card_index: int, expected_uid: str | None = None)")
                && server.Contains("async def deck_select_card(card_index: int, expected_uid: str | None = None)")
                && Regex.Matches(server, "body\\[\"expected_uid\"\\] = expected_uid").Count == 2, "MCP tools forward expected_uid only when given");

            Console.WriteLine($"ScenarioContract: {Checks} checks passed");
        }
    }
}
