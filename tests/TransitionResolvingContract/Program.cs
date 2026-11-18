STS2_MCP.McpMod.RunTransitionResolvingContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;

        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }

        // 引数の並び: mapOpen, mapVisible, mapTraveling, travelEnabled, travelActionPending
        private static string? Map(bool open, bool visible, bool traveling, bool enabled, bool pending)
            => MapTransitionReason(open, visible, traveling, enabled, pending);

        public static void RunTransitionResolvingContract()
        {
            // --- マップ（map の state にだけ使う） ---

            // 入力待ちのマップは理由なし。部屋の終わり（RunManager.Proceed → SetTravelEnabled(true) → Open）で開いたマップ。
            Check(Map(true, true, false, true, false) == null, "idle map after the room is finished");
            // トップバーで開いたマップ（部屋が終わっていない。IsTravelEnabled は false）も入力待ち。理由を付けると行き詰まる。
            Check(Map(true, true, false, false, false) == null, "map opened from the top bar during a room");

            // 移動: OnMapPointSelectedLocally → VoteForMapCoordAction → MoveToMapCoordAction → TravelToMapCoord
            // （IsTraveling = true と IsTravelEnabled = false を同時に）→ EnterMapPointInternal の ClearScreens で閉じる。
            Check(Map(true, true, false, true, true) == MapTravelingReason, "vote enqueued, IsTraveling not yet set");
            Check(Map(true, true, true, false, true) == MapTravelingReason, "MoveToMapCoordAction running TravelToMapCoord");
            Check(Map(true, true, true, false, false) == MapTravelingReason, "traveling (flag only)");
            // 部屋に入る前に例外などで IsTraveling が残っても、次の部屋の終わりで IsTravelEnabled が戻れば、人はクリックで移動できる
            // （NMapPoint.IsTravelable は IsTravelEnabled と点の状態だけを見る）。そのマップを解決中にしない。
            Check(Map(true, true, true, true, false) == null, "a stale IsTraveling does not outlive the next Proceed");

            // 閉じる演出: Close(animateOut: true) が IsOpen を下ろし、AnimClose の tween が終わってから Visible を下ろす。
            Check(Map(false, true, false, true, false) == MapClosingReason, "back arrow / top bar close, still visible");
            Check(Map(false, true, false, false, false) == MapClosingReason, "closing a map opened during a room");
            // 見えなくなれば理由は消える（MapRoom の分岐は閉じたマップでも map を返す）。
            Check(Map(false, false, false, true, false) == null, "closed map in a MapRoom");
            Check(Map(false, false, false, false, false) == null, "closed and hidden");
            // 移動が閉じるより先（移動中は ClearScreens が演出なしで閉じる）。
            Check(Map(false, true, true, false, false) == MapTravelingReason, "traveling wins over closing");

            // 時系列（A-16 の観測 poll-1a-mapstart の順）: 投票 → 移動の演出 → （map でなくなる）。最後は入力待ち。
            var travel = new[]
            {
                Map(true, true, false, true, false),   // 選ぶ前
                Map(true, true, false, true, true),    // 投票の action が列にある
                Map(true, true, true, false, true),    // MoveToMapCoordAction が走っている
                Map(true, true, true, false, true),    // 経路の演出と暗転
            };
            Check(travel[0] == null && travel.Skip(1).All(r => r == MapTravelingReason), "travel timeline");

            // 時系列（A-13）: 選択画面を表に戻すために矢印で閉じる → 演出 → 消える。
            var close = new[]
            {
                Map(true, true, false, true, false),
                Map(false, true, false, true, false),
                Map(false, false, false, true, false),
            };
            Check(close[0] == null && close[1] == MapClosingReason && close[2] == null, "close timeline");

            // --- 部屋の切り替え・ランの読み込み（ラン中の unknown の state にだけ使う） ---
            Check(RunTransitionReason(false, false) == RunLoadingReason, "run state set, no room yet (continue / new run)");
            Check(RunTransitionReason(false, true) == RoomChangingReason, "rooms exited, next not entered");
            Check(RunTransitionReason(true, true) == RoomChangingReason, "changing rooms with a room on the stack");
            Check(RunTransitionReason(true, false) == null, "an unrecognised room that is not changing stays as before");

            // 時系列（A-17 の観測 poll-6-continue の順）: SetUpSavedSingleplayer → 暗転・NGame.LoadRun（部屋なし）
            // → LoadIntoLatestMapCoord（EnterMapPointInternal）→ 部屋の state（battle の無い戦闘・報酬）。
            Check(new[] { RunTransitionReason(false, false), RunTransitionReason(false, true), RunTransitionReason(true, true) }
                      .All(r => r != null), "continue timeline stays resolving until a room screen is built");

            // --- choose_map_node の拒否 ---
            // 引数の並び: mapOpen, mapVisible, mapTraveling, travelEnabled, debugTravelEnabled, travelActionPending
            Check(ChooseMapNodeRefusal(true, true, false, true, false, false) == null, "idle map takes a choice");
            Check(ChooseMapNodeRefusal(false, true, false, true, false, false)?.Contains("closing") == true, "closing map is refused");
            Check(ChooseMapNodeRefusal(false, false, false, true, false, false) == "Map screen is not open", "closed map keeps its message");
            Check(ChooseMapNodeRefusal(true, true, true, false, false, false)?.Contains("traveling") == true, "traveling map is refused");
            Check(ChooseMapNodeRefusal(true, true, false, true, false, true)?.Contains("traveling") == true, "a pending vote is refused");
            Check(ChooseMapNodeRefusal(true, true, false, false, false, false)?.Contains("not enabled") == true,
                "map opened before the room is finished is refused (the player cannot travel from it either)");
            // 開発用の移動（IsDebugTravelEnabled）は、移動中でなければ人もクリックできる（NMapPoint.IsTravelable）。
            Check(ChooseMapNodeRefusal(true, true, false, false, true, false) == null, "debug travel");
            Check(ChooseMapNodeRefusal(true, true, true, false, true, false) != null, "debug travel while traveling");
            Check(ChooseMapNodeRefusal(true, true, true, true, false, false) == null, "stale IsTraveling with travel enabled");
            // state が解決中（map_traveling / map_closing）と返す局面では、必ず拒否する。
            foreach (var open in new[] { false, true })
            foreach (var visible in new[] { false, true })
            foreach (var traveling in new[] { false, true })
            foreach (var enabled in new[] { false, true })
            foreach (var pending in new[] { false, true })
                if (Map(open, visible, traveling, enabled, pending) != null)
                    Check(ChooseMapNodeRefusal(open, visible, traveling, enabled, false, pending) != null,
                        $"refused whenever the map is resolving ({open}/{visible}/{traveling}/{enabled}/{pending})");

            // --- 配線（ソース文字列） ---
            var sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            var stateSource = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            var actionsSource = File.ReadAllText(Path.Combine(sources, "McpMod.Actions.cs"));
            var modSource = File.ReadAllText(Path.Combine(sources, "McpMod.cs"));
            var formatSource = File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs"));
            var visibilitySource = File.ReadAllText(Path.Combine(sources, "McpMod.CardSelectVisibility.cs"));

            // AddResolvingState: 入力待ちの画面の消去の後に足す（map は入力待ちの画面）。is_resolving を書く前。
            int start = stateSource.IndexOf("private static void AddResolvingState(", StringComparison.Ordinal);
            int end = stateSource.IndexOf("result[\"is_resolving\"] = reasons.Count > 0;", start, StringComparison.Ordinal);
            Check(start >= 0 && end > start, "AddResolvingState exists");
            string body = stateSource[start..end];
            int clear = body.IndexOf("_playerInputStates.Contains(stateType)", StringComparison.Ordinal);
            int add = body.IndexOf("ReadTransitionReason(stateType)", StringComparison.Ordinal);
            Check(clear > 0 && add > clear, "transition reason is added after the input-screen clear");

            // 理由を読むのは map とラン中の unknown だけ。ほかの入力待ちの画面（event・rewards・menu・game_over など）には付かない。
            int reader = stateSource.IndexOf("private static string? ReadTransitionReason(string? stateType)", StringComparison.Ordinal);
            int readerEnd = stateSource.IndexOf("private static bool IsMapTravelActionPending()", reader, StringComparison.Ordinal);
            Check(reader > 0 && readerEnd > reader, "ReadTransitionReason exists");
            string readerBody = stateSource[reader..readerEnd];
            Check(readerBody.Contains("if (stateType == \"map\")") && readerBody.Contains("if (stateType == \"unknown\")"),
                "only map and unknown get a transition reason");
            Check(System.Text.RegularExpressions.Regex.Matches(readerBody, "stateType ==").Count == 2, "no other screen is read");
            Check(readerBody.Contains("map.IsOpen, IsNodeVisible(map), map.IsTraveling, map.IsTravelEnabled, IsMapTravelActionPending()"),
                "reads the map's public flags");
            Check(readerBody.Contains("if (!run.IsInProgress)")
                  && readerBody.Contains("RunTransitionReason(run.DebugOnlyGetState()?.CurrentRoom != null, run.IsChangingRooms)"),
                "reads the run's room and IsChangingRooms");

            int pendingStart = readerEnd;
            int pendingEnd = stateSource.IndexOf("\n    }\n", pendingStart, StringComparison.Ordinal);
            string pendingBody = stateSource[pendingStart..pendingEnd];
            Check(pendingBody.Contains("CurrentlyRunningAction is VoteForMapCoordAction or MoveToMapCoordAction"), "running map actions count");
            Check(pendingBody.Contains("CombatManager.Instance.IsInProgress"), "queued map actions are not counted in combat");
            Check(pendingBody.Contains("GameActionState.Canceled"), "cancelled actions are not counted");

            // 入力待ちの画面の一覧は変えない。unknown は入力待ちではない。
            int set = stateSource.IndexOf("_playerInputStates = new()", StringComparison.Ordinal);
            string inputStates = stateSource[set..stateSource.IndexOf("};", set, StringComparison.Ordinal)];
            foreach (var screen in new[] { "\"map\"", "\"event\"", "\"rewards\"", "\"card_select\"", "\"menu\"", "\"game_over\"", "\"rest_site\"", "\"shop\"" })
                Check(inputStates.Contains(screen), $"{screen} stays an input screen");
            Check(!inputStates.Contains("\"unknown\""), "unknown is not an input screen");

            // メニュー（ランの外）は AddResolvingState を通らずに返る。
            int build = stateSource.IndexOf("private static Dictionary<string, object?> BuildGameStateCore(", StringComparison.Ordinal);
            int menu = stateSource.IndexOf("if (!RunManager.Instance.IsInProgress)", build, StringComparison.Ordinal);
            int menuReturn = stateSource.IndexOf("return result;", menu, StringComparison.Ordinal);
            int resolvingCall = stateSource.IndexOf("AddResolvingState(result, result.GetValueOrDefault(\"state_type\")?.ToString(), combatBoundaryReason);", build, StringComparison.Ordinal);
            Check(menu > build && menuReturn > menu && resolvingCall > menuReturn, "the menu returns before the resolving reasons");

            // A-12 の流れ: 選択画面（card_select 等の overlay）はマップの判定より前で拾う。矢印で閉じた直後でも、
            // 表に戻った選択画面は card_select（入力待ち）として返る。矢印は従来どおりゲームのボタンを押す。
            int cardSelect = stateSource.IndexOf("topOverlay is NCardGridSelectionScreen cardSelectScreen", build, StringComparison.Ordinal);
            int mapBranch = stateSource.IndexOf("else if (mapIsOpen)", build, StringComparison.Ordinal);
            Check(cardSelect > build && mapBranch > cardSelect, "selection overlays are decided before the map");
            Check(visibilitySource.Contains("back.ForceClick();"), "A-12 reveal still presses the map's back arrow");

            // choose_map_node は判定関数を通し、閉じかけのマップ（IsOpen が false で見えている）を受け付けない。
            int choose = actionsSource.IndexOf("private static Dictionary<string, object?> ExecuteChooseMapNode(", StringComparison.Ordinal);
            int chooseEnd = actionsSource.IndexOf("private static Dictionary<string, object?> ExecuteClaimReward(", choose, StringComparison.Ordinal);
            string chooseBody = actionsSource[choose..chooseEnd];
            Check(chooseBody.Contains("ChooseMapNodeRefusal(") && chooseBody.Contains("mapScreen.IsTravelEnabled")
                  && chooseBody.Contains("mapScreen.IsDebugTravelEnabled") && chooseBody.Contains("IsMapTravelActionPending()"),
                "choose_map_node checks the map before pressing");
            Check(!chooseBody.Contains("!mapScreen.IsOpen && !IsNodeVisible(mapScreen)"), "a visible but closing map is no longer accepted");
            Check(chooseBody.IndexOf("ChooseMapNodeRefusal(", StringComparison.Ordinal)
                  < chooseBody.IndexOf("mapScreen.OnMapPointSelectedLocally(target);", StringComparison.Ordinal),
                "refusal comes before the press");

            // map.travel_enabled（追加項目）と markdown の行。
            Check(stateSource.Contains("state[\"travel_enabled\"] = mapScreen.IsTravelEnabled;"), "map.travel_enabled is reported");
            Check(formatSource.Contains("map.GetValueOrDefault(\"travel_enabled\") is false"), "markdown says when travel is not enabled");

            // POST の wait: true は is_resolving で待つ（理由を足せば落ち着かない）。schema は 4 のまま。
            Check(modSource.Contains("public const int StateSchemaVersion = 12;"), "published schema is 12 (sts2#1020)");
            Check(System.Text.RegularExpressions.Regex.IsMatch(modSource, @"public const string StateSchema = ""1\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";"), "published schema MAJOR is 1 (the exact version is checked in V0113CompatContract)");
            int settled = modSource.IndexOf("internal static bool IsSettledState(", StringComparison.Ordinal);
            Check(settled >= 0 && modSource.IndexOf("state.TryGetValue(\"is_resolving\"", settled, StringComparison.Ordinal) > settled,
                "POST wait still settles on is_resolving");

            Console.WriteLine($"transition resolving contract: {Checks} checks passed");
        }
    }
}
