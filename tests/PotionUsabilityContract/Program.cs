using System.Text.Json;

STS2_MCP.McpMod.RunContractTests();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

        private static int Checks;
        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }

        // Reads of a usable CombatOnly potion on the player's turn whose only destination is the owner
        // (solo AnyPlayer, e.g. Block Potion). Each getter counts its calls.
        private sealed class Probe
        {
            public readonly Dictionary<string, int> Calls = new();
            public readonly PotionUsabilityReads Reads;
            private Func<T> Count<T>(string name, Func<T> f) => () => { Calls[name] = Calls.GetValueOrDefault(name) + 1; return f(); };
            public int this[string name] => Calls.GetValueOrDefault(name);
            public Probe(string usage = "CombatOnly", PotionTargetRead? targets = null)
            {
                Reads = new PotionUsabilityReads
                {
                    HolderHoldsThisPotion = Count("holder", () => true),
                    IsQueued = Count("queued", () => false),
                    OwnerDead = Count("dead", () => false),
                    IsGameOver = Count("game_over", () => false),
                    CanUseOrRemovePotions = Count("can_use_or_remove", () => true),
                    IsPotionUseLocked = Count("locked", () => false),
                    HolderUsable = Count("holder_usable", () => true),
                    HolderDisabledUntilPotionRemoved = Count("holder_disabled", () => false),
                    PassesCustomUsabilityCheck = Count("custom", () => true),
                    Usage = Count("usage", () => usage),
                    CombatInProgress = Count("in_progress", () => true),
                    OwnerSideActing = Count("side", () => true),
                    OwnerAlive = Count("alive", () => true),
                    InCardSelection = Count("card_selection", () => false),
                    PlayerActionsDisabled = Count("actions_disabled", () => false),
                    OpenPopupIsUsable = Count<bool?>("popup", () => null),
                    Targets = Count("targets", () => targets ?? OwnerOnly())
                };
                // The game's static rule, as NPotionPopup.CanEnableUseButton reads (sts2-a671e9ef).
                var reads = Reads;
                Reads.CanEnableUseButton = (now, passes) =>
                {
                    Calls["button"] = Calls.GetValueOrDefault("button") + 1;
                    if (!passes) return false;
                    return reads.Usage() switch
                    {
                        "AnyTime" => true,
                        "CombatOnly" => now,
                        "Automatic" => false,
                        "None" => throw new InvalidOperationException("No potions should have 'None' usage."),
                        _ => throw new ArgumentOutOfRangeException("usage")
                    };
                };
            }
        }

        private static PotionTargetRead OwnerOnly() => new()
        {
            TargetOptional = true,
            Candidates = new() { new PotionTargetCandidate { Id = PotionPlayerTargetId("1"), Label = "owner", Allowed = () => true } }
        };

        private static PotionTargetCandidate Enemy(string? id, bool allowed) => new() { Id = id, Label = id ?? "enemy", Allowed = () => allowed };

        public static void RunContractTests()
        {
            BeginStateWarnings();
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

            // --- Usable, owner as the only destination (direct use).
            var p = new Probe();
            var v = DecidePotionUsability(p.Reads);
            Check(v.CanUse == true && v.Reason == null && v.TargetIds!.SequenceEqual(new[] { "player:1" }) && v.TargetOptional,
                "使用可・自分宛: true / [player:<id>] / null、target は省略可");
            Check(p["button"] == 1 && p["targets"] == 1 && p["popup"] == 1, "ボタン判定・popup・対象を 1 回ずつ読む");

            // --- Ban order: each ban is the first one, and nothing after it is read.
            (string Name, Action<PotionUsabilityReads> Set, string Reason, string FirstUnread)[] bans =
            {
                ("queued", r => r.IsQueued = () => true, "queued", "dead"),
                ("dead", r => r.OwnerDead = () => true, "owner_dead", "game_over"),
                ("game_over", r => r.IsGameOver = () => true, "game_over", "can_use_or_remove"),
                ("owner_locked", r => r.CanUseOrRemovePotions = () => false, "owner_locked", "locked"),
                ("room_locked", r => r.IsPotionUseLocked = () => true, "room_locked", "holder_usable"),
                ("holder_not_usable", r => r.HolderUsable = () => false, "holder_not_usable", "holder_disabled"),
                ("holder_disabled", r => r.HolderDisabledUntilPotionRemoved = () => true, "holder_disabled", "custom"),
                ("outside_combat", r => r.CombatInProgress = () => false, "outside_combat", "side"),
                ("not_player_turn", r => r.OwnerSideActing = () => false, "not_player_turn", "alive"),
                ("card_selection", r => r.InCardSelection = () => true, "card_selection", "actions_disabled"),
                ("actions_disabled", r => r.PlayerActionsDisabled = () => true, "actions_disabled", "popup"),
            };
            foreach (var ban in bans)
            {
                var probe = new Probe();
                ban.Set(probe.Reads);
                var verdict = DecidePotionUsability(probe.Reads);
                Check(verdict.CanUse == false && verdict.Reason == ban.Reason && verdict.TargetIds is { Count: 0 },
                    $"{ban.Name}: false / [] / {ban.Reason}");
                Check(probe[ban.FirstUnread] == 0 && probe["targets"] == 0, $"{ban.Name}: 禁止が確定した後の読取（{ban.FirstUnread}・対象）をしない");
            }
            // Two bans at once: the earlier stage wins (queued before a locked room).
            var two = new Probe();
            two.Reads.IsQueued = () => true;
            two.Reads.IsPotionUseLocked = () => true;
            Check(DecidePotionUsability(two.Reads).Reason == "queued", "複数の禁止は先の段の理由（予約 > 部屋のロック）");

            // CombatOnly's in-combat condition is not applied to AnyTime (Blood Potion on the map, during a card selection).
            var anyTime = new Probe("AnyTime");
            anyTime.Reads.CombatInProgress = () => throw new Exception("must not be read");
            anyTime.Reads.InCardSelection = () => throw new Exception("must not be read");
            var anyTimeVerdict = DecidePotionUsability(anyTime.Reads);
            Check(anyTimeVerdict.CanUse == true, "AnyTime は戦闘中の手番条件を読まずにボタン有効");
            var automatic = new Probe("Automatic");
            Check(DecidePotionUsability(automatic.Reads) is { CanUse: false, Reason: "automatic" } && automatic["targets"] == 0, "Automatic（妖精）は false / automatic");
            var foulAway = new Probe("AnyTime");
            foulAway.Reads.PassesCustomUsabilityCheck = () => false;
            Check(DecidePotionUsability(foulAway.Reads) is { CanUse: false, Reason: "custom_check_failed" }, "個別条件の false は custom_check_failed");
            var none = new Probe("None");
            var noneVerdict = DecidePotionUsability(none.Reads);
            Check(noneVerdict.CanUse == null && noneVerdict.TargetIds == null && noneVerdict.Reason == "unobserved:can_enable_use_button",
                "None・未知の使用区分はゲームの判定が例外 → 未観測（false にしない）");

            // --- Unobserved: never a ban.
            var notShown = new Probe();
            notShown.Reads.HolderHoldsThisPotion = () => false;
            Check(DecidePotionUsability(notShown.Reads) is { CanUse: null, TargetIds: null, Reason: "unobserved:holder_model" } && notShown["queued"] == 0,
                "holder が別の瓶を示す（UI 未整備）は未観測で、以降を読まない");
            var noUi = new Probe();
            noUi.Reads.HolderHoldsThisPotion = () => throw new InvalidOperationException("NRun.GlobalUi.TopBar.PotionContainer is not ready");
            var noUiVerdict = DecidePotionUsability(noUi.Reads);
            Check(noUiVerdict.Reason == "unobserved:holder" && noUiVerdict.Detail!.Contains("PotionContainer is not ready"), "UI 不在は unobserved:holder と理由");
            var badSide = new Probe();
            badSide.Reads.OwnerSideActing = () => throw new NullReferenceException("CombatState");
            Check(DecidePotionUsability(badSide.Reads).Reason == "unobserved:current_side", "getter の例外はその段の未観測");
            var badPopup = new Probe();
            badPopup.Reads.OpenPopupIsUsable = () => false;
            Check(DecidePotionUsability(badPopup.Reads).Reason == "unobserved:popup", "開いている popup がボタン判定と食い違えば未観測");
            var livePopup = new Probe();
            livePopup.Reads.OpenPopupIsUsable = () => true;
            Check(DecidePotionUsability(livePopup.Reads).CanUse == true, "開いている popup が一致すれば使用可");

            // --- Targets.
            var fire = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", true), Enemy("NIBBIT_1", false), Enemy("SHRINKER_BEETLE_0", true) } });
            var fireVerdict = DecidePotionUsability(fire.Reads);
            Check(fireVerdict.CanUse == true && fireVerdict.TargetIds!.SequenceEqual(new[] { "NIBBIT_0", "SHRINKER_BEETLE_0" }) && !fireVerdict.TargetOptional,
                "単体敵: 許可された安定 ID だけ（死者・選べない敵を外す）、順序は本体の列挙順、target は必須");
            var allDead = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", false) } });
            Check(DecidePotionUsability(allDead.Reads) is { CanUse: false, Reason: "no_valid_target" } v2 && v2.TargetIds!.Count == 0,
                "ボタンが有効でも完全に列挙して 0 件なら false / no_valid_target");
            var ampoule = new Probe(targets: new PotionTargetRead { NoCreatureTarget = true });
            Check(DecidePotionUsability(ampoule.Reads) is { CanUse: true, Reason: null } v3 && v3.TargetIds!.Count == 0, "全体効果・商人: true / []");
            var blocked = new Probe(targets: new PotionTargetRead { Blocked = "targeting_blocked" });
            Check(DecidePotionUsability(blocked.Reads) is { CanUse: false, Reason: "targeting_blocked" }, "選択の中断条件（オーバーレイ・capstone）は targeting_blocked");
            var unknownForm = new Probe(targets: new PotionTargetRead { Unobserved = "target_type", UnobservedDetail = "AnyEnemy outside combat" });
            Check(DecidePotionUsability(unknownForm.Reads) is { CanUse: null, TargetIds: null, Reason: "unobserved:target_type" }, "未知の対象形式は未観測");
            var noId = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", true), Enemy(null, true) } });
            Check(DecidePotionUsability(noId.Reads) is { CanUse: null, Reason: "unobserved:entity_id" }, "許可される敵に安定 ID が無ければ未観測（unknown_0 や位置で代用しない）");
            var noIdDead = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", true), Enemy(null, false) } });
            Check(DecidePotionUsability(noIdDead.Reads).TargetIds!.SequenceEqual(new[] { "NIBBIT_0" }), "許可されない敵の ID 欠測は結果に影響しない");
            var dup = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", true), Enemy("NIBBIT_0", false) } });
            Check(DecidePotionUsability(dup.Reads) is { CanUse: null, Reason: "unobserved:entity_id_duplicate" }, "重複 ID は未観測");
            var throwing = new Probe(targets: new PotionTargetRead { Candidates = new() { Enemy("NIBBIT_0", true), new PotionTargetCandidate { Id = "SLIME_0", Allowed = () => throw new Exception("hook") } } });
            Check(DecidePotionUsability(throwing.Reads) is { CanUse: null, Reason: "unobserved:target.SLIME_0" }, "候補の判定の例外は未観測（読めた候補だけを完全な列挙にしない）");
            var missingCandidates = new Probe(targets: new PotionTargetRead());
            Check(DecidePotionUsability(missingCandidates.Reads) is { CanUse: null, Reason: "unobserved:targets" }, "候補の無い読取結果は未観測");

            // --- Rows, warnings and the wire.
            Check(_stateWarnings.Count == 0, "判定だけでは warning を出さない");
            var usableRow = new Dictionary<string, object?> { ["id"] = "FIRE_POTION", ["slot"] = 1 };
            ApplyPotionUsability(usableRow, fireVerdict, 1);
            var bannedRow = new Dictionary<string, object?> { ["id"] = "FAIRY_IN_A_BOTTLE", ["slot"] = 0 };
            ApplyPotionUsability(bannedRow, DecidePotionUsability(new Probe("Automatic").Reads), 0);
            var noTargetRow = new Dictionary<string, object?> { ["id"] = "EXPLOSIVE_AMPOULE", ["slot"] = 2 };
            ApplyPotionUsability(noTargetRow, DecidePotionUsability(ampoule.Reads), 2);
            var failedRow = new Dictionary<string, object?> { ["id"] = "BLOCK_POTION", ["slot"] = 2, ["unavailable_reasons"] = new Dictionary<string, string> { ["target_type"] = "kept" } };
            ApplyPotionUsability(failedRow, noUiVerdict, 2);
            var reasons = (Dictionary<string, string>)failedRow["unavailable_reasons"]!;
            Check(reasons["target_type"] == "kept" && reasons["can_use_now"].StartsWith("unobserved:holder: InvalidOperationException"), "未観測は行の unavailable_reasons に段と理由（既存の理由を保つ）");
            Check(_stateWarnings.Count == 1 && _stateWarnings[0].StartsWith("player.potions.2.can_use_now: unobserved:holder"), "warnings にスロットと項目のパス");
            Check(!usableRow.ContainsKey("unavailable_reasons") && !bannedRow.ContainsKey("unavailable_reasons"), "使用可・既知の禁止は unavailable_reasons を作らない");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(new { u = usableRow, b = bannedRow, n = noTargetRow, f = failedRow }, options)))
            {
                var u = json.RootElement.GetProperty("u");
                Check(u.GetProperty("can_use_now").ValueKind == JsonValueKind.True && u.GetProperty("can_use_now_reason").ValueKind == JsonValueKind.Null
                    && u.GetProperty("valid_target_entity_ids").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "NIBBIT_0", "SHRINKER_BEETLE_0" }),
                    "wire: true・ID 配列・reason null（キーが残る）");
                var b = json.RootElement.GetProperty("b");
                Check(b.GetProperty("can_use_now").ValueKind == JsonValueKind.False && b.GetProperty("valid_target_entity_ids").GetArrayLength() == 0
                    && b.GetProperty("can_use_now_reason").GetString() == "automatic", "wire: false・[]・理由");
                var n = json.RootElement.GetProperty("n");
                Check(n.GetProperty("can_use_now").ValueKind == JsonValueKind.True && n.GetProperty("valid_target_entity_ids").ValueKind == JsonValueKind.Array
                    && n.GetProperty("valid_target_entity_ids").GetArrayLength() == 0, "wire: 全体効果は true と空配列");
                var f = json.RootElement.GetProperty("f");
                Check(f.GetProperty("can_use_now").ValueKind == JsonValueKind.Null && f.GetProperty("valid_target_entity_ids").ValueKind == JsonValueKind.Null
                    && f.GetProperty("can_use_now_reason").GetString() == "unobserved:holder", "wire: 未観測は null・null・unobserved:<段>（空配列・false にしない）");
            }
            var warned = new Dictionary<string, object?>();
            EndStateWarnings(warned);
            Check(warned["warnings"] is List<string> { Count: 1 }, "トップレベルの warnings に載る");

            // --- Markdown.
            Check(FormatPotionUsability(usableRow) == " | Use now: yes → NIBBIT_0, SHRINKER_BEETLE_0", "Markdown: 使用可と対象");
            Check(FormatPotionUsability(noTargetRow) == " | Use now: yes (no target)", "Markdown: 対象指定なし");
            Check(FormatPotionUsability(bannedRow) == " | Use now: no (automatic)", "Markdown: 使用不可と理由");
            Check(FormatPotionUsability(failedRow).StartsWith(" | Use now: unobserved (unobserved:holder: InvalidOperationException"), "Markdown: 未観測と理由");
            Check(FormatPotionUsability(new Dictionary<string, object?> { ["id"] = "X" }) == "", "旧 state（項目なし）は何も補わない");

            // --- expected_potion_id.
            Dictionary<string, JsonElement> Body(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            Check(ReadExpectedPotionId(Body("{\"slot\":0}"), out var absent) == null && absent == null, "expected_potion_id なしは照合しない（従来の呼び出し）");
            Check(ReadExpectedPotionId(Body("{\"expected_potion_id\":\"\"}"), out _) != null, "空文字は拒否");
            Check(ReadExpectedPotionId(Body("{\"expected_potion_id\":3}"), out _) != null, "非文字列は拒否");
            Check(ReadExpectedPotionId(Body("{\"expected_potion_id\":\"FIRE_POTION\"}"), out var expected) == null && expected == "FIRE_POTION", "文字列を読む");

            string? Guard(string? actual, PotionUsabilityVerdict? verdict, string? target, out string? resolved, out string stage)
                => PotionUseGuardRejection(1, actual, "FIRE_POTION", verdict, target, out resolved, out stage);
            Check(Guard(null, fireVerdict, "NIBBIT_0", out _, out var s1) != null && s1 == "identity", "空き枠は identity で拒否");
            Check(Guard("BLOCK_POTION", fireVerdict, "NIBBIT_0", out _, out var s2)!.Contains("holds BLOCK_POTION") && s2 == "identity", "slot の差し替えは identity で拒否");
            Check(Guard("FIRE_POTION", noUiVerdict, "NIBBIT_0", out _, out var s3)!.Contains("unobserved:holder") && s3 == "can_use_now", "未観測は拒否（使用可とみなさない）");
            Check(Guard("FIRE_POTION", null, "NIBBIT_0", out _, out var s3b) != null && s3b == "can_use_now", "判定なしは拒否");
            Check(Guard("FIRE_POTION", PotionUsabilityVerdict.Banned("not_player_turn"), "NIBBIT_0", out _, out var s4)!.Contains("not_player_turn") && s4 == "can_use_now", "既知の禁止は理由付きで拒否");
            Check(Guard("FIRE_POTION", fireVerdict, null, out _, out var s5)!.Contains("needs a target") && s5 == "target", "単体敵は target を補完しない");
            Check(Guard("FIRE_POTION", fireVerdict, "NIBBIT_1", out _, out var s6)!.Contains("not a valid target") && s6 == "target", "死者・対象外の ID は拒否");
            Check(Guard("FIRE_POTION", fireVerdict, "3", out _, out _) != null, "combat_id の数値は許可集合の ID でないので拒否（明示 ID のみ）");
            Check(Guard("FIRE_POTION", fireVerdict, "SHRINKER_BEETLE_0", out var r7, out _) == null && r7 == "SHRINKER_BEETLE_0", "許可集合の ID はそのまま使う");
            var ownerVerdict = DecidePotionUsability(new Probe().Reads);
            Check(Guard("FIRE_POTION", ownerVerdict, null, out var r8, out _) == null && r8 == "player:1", "直接使用は target を省略できる");
            Check(Guard("FIRE_POTION", ownerVerdict, "player:1", out var r9, out _) == null && r9 == "player:1", "直接使用で自分の ID を明示してもよい");
            Check(Guard("FIRE_POTION", ownerVerdict, "player:2", out _, out _) != null, "他人の ID は拒否");
            var ampouleVerdict = DecidePotionUsability(ampoule.Reads);
            Check(Guard("FIRE_POTION", ampouleVerdict, null, out var r10, out _) == null && r10 == null, "全体効果は target なし");
            Check(Guard("FIRE_POTION", ampouleVerdict, "NIBBIT_0", out _, out var s11)!.Contains("no creature target") && s11 == "target", "全体効果に敵 ID を渡したら拒否（対象別の重複送信を防ぐ）");
            var err = PotionGuardError("x", "mod_acceptance", 1, "FIRE_POTION", "FIRE_POTION", fireVerdict);
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(err, options)))
            {
                var check = json.RootElement.GetProperty("potion_check");
                Check(json.RootElement.GetProperty("status").GetString() == "error" && check.GetProperty("stage").GetString() == "mod_acceptance"
                    && check.GetProperty("can_use_now").ValueKind == JsonValueKind.True && check.GetProperty("valid_target_entity_ids").GetArrayLength() == 2,
                    "拒否はエラーと照合の内訳（ゲームのボタン可否と MOD の受付を stage で分ける）");
            }

            // --- Wiring in the production sources (game types: checked as text).
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Src(string name) => File.ReadAllText(Path.Combine(sources, name));
            string Between(string text, string start, string end)
            {
                int i = text.IndexOf(start, StringComparison.Ordinal);
                Check(i >= 0, $"source has {start}");
                int j = text.IndexOf(end, i + start.Length, StringComparison.Ordinal);
                return j < 0 ? text[i..] : text[i..j];
            }
            string builder = Src("McpMod.StateBuilder.cs");
            string playerState = Between(builder, "private static Dictionary<string, object?> BuildPlayerState(Player player)", "return state;");
            Check(playerState.Contains("potions.Add(BuildPotionRow(player, potion, slotIndex));") && !playerState.Contains("[\"target_type\"]"),
                "BuildPlayerState は所持スロットごとに BuildPotionRow を通す（空き枠の行は作らない）");
            Check(Src("McpMod.MultiplayerState.cs").Contains("result[\"player\"] = BuildPlayerState(localPlayer);"), "マルチのローカルプレイヤーも同じ builder");
            string shop = Between(builder, "private static Dictionary<string, object?> BuildShopState", "state[\"sellable_potions\"] = sellable;");
            Check(shop.Contains("_passiveRead\n                        ? CanSellPotionToMerchantNow(localPlayer, held)"), "passive の sellable_potions は在庫を閉じない照会");

            string live = Src("McpMod.PotionUsabilityLive.cs");
            // Code only: comments name the game members the reads mirror (RefreshButtons, OnCreatureHovered...).
            static string CodeOnly(string text) => string.Join("\n", text.Split('\n')
                .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.TrimStart().StartsWith("///", StringComparison.Ordinal) ? "" : line));
            string observation = CodeOnly(Between(live, "private static Dictionary<string, object?> BuildPotionRow", "private static Dictionary<string, object?> ExecuteGuardedUsePotion"));
            foreach (var forbidden in new[] { "NPotionPopup.Create", "RefreshButtons", "StartTargeting", "AfterTargetingBlockedVfx", "OnNodeHovered", "OnCreatureHovered",
                         "EnqueueManualUse", "RequestEnqueue", "ForceClick", "OpenInventory", "TryCloseMerchantInventory", "OpenPotionPopup", "UsePotion(", "TryGrabFocus", ".Enable()", ".Disable()", "IsPotionUsable" })
                Check(!observation.Contains(forbidden), $"観測は {forbidden} を呼ばない（popup 生成・Refresh・選択開始・VFX・enqueue・在庫の開閉なし）");
            Check(observation.Contains("NPotionPopup.CanEnableUseButton(usage, canUseInCombatNow, passes)"), "使用区分の分岐は本体の公開 static をそのまま使う");
            Check(observation.Contains("Hook.ShouldAllowTargeting(") && observation.Contains("creature.IsHittable") && observation.Contains("potion.IsValidTarget(creature)")
                && observation.Contains("room.GetCreatureNode(creature) != null"), "敵は IsValidTarget・ノード・targeting・hitting の全部を通す");
            Check(observation.Contains("RefreshEntityIdRegistry(combatState);") && observation.Contains("GetStableEntityId(creature)"), "敵 ID は battle と同じ registry");
            string nowSell = CodeOnly(Between(live, "internal static bool CanSellPotionToMerchantNow", "\n    }\n"));
            Check(!nowSell.Contains("TryCloseMerchantInventory") && !nowSell.Contains("ForceClick"), "passive の販売可否は在庫を閉じない");
            string guarded = CodeOnly(Between(live, "private static Dictionary<string, object?> ExecuteGuardedUsePotion", "private static Creature? ResolveGuardedPotionTarget"));
            int guardAt = guarded.IndexOf("PotionUseGuardRejection(", StringComparison.Ordinal);
            int modAt = guarded.IndexOf("\"mod_acceptance\"", StringComparison.Ordinal);
            int enqueueAt = guarded.IndexOf("potion.EnqueueManualUse(target);", StringComparison.Ordinal);
            Check(guarded.Contains("DecidePotionUsability(BuildPotionUsabilityReads(player, potion, slot))") && guardAt > 0 && guardAt < modAt && modAt < enqueueAt,
                "expected_potion_id: 最新の共通判定 → 照合 → MOD の受付 → enqueue の順");
            Check(!guarded.Contains("TryCloseMerchantInventory") && !guarded.Contains("alive.Count") && !guarded.Contains("Where(e => e.IsAlive)"),
                "照合付きの使用は敵の補完・在庫の操作をしない");
            string actions = Src("McpMod.Actions.cs");
            string use = Between(actions, "private static Dictionary<string, object?> ExecuteUsePotion", "private static Dictionary<string, object?> ExecuteSellPotion");
            int readAt = use.IndexOf("ReadExpectedPotionId(data, out string? expectedPotionId)", StringComparison.Ordinal);
            int branchAt = use.IndexOf("return ExecuteGuardedUsePotion(player, slot, potion, expectedPotionId, data);", StringComparison.Ordinal);
            Check(readAt > 0 && readAt < branchAt && branchAt < use.IndexOf("TryCloseMerchantInventory", StringComparison.Ordinal)
                && branchAt < use.IndexOf("EnqueueManualUse", StringComparison.Ordinal), "use_potion は照合の分岐を従来の処理（在庫を閉じる・補完・enqueue）より前に置く");
            Check(Src("McpMod.MultiplayerActions.cs").Contains("\"use_potion\" => ExecuteUsePotion(player, data)"), "マルチの use_potion も同じ入口");
            Check(Src("McpMod.Formatting.cs").Contains("{FormatPotionUsability(p)}"), "Markdown のポーション行に配線");
            Check(Src("McpMod.ScenarioGuard.cs").Contains("[\"potion_availability\"] = 1"), "scenario_capabilities.potion_availability は 1");
            Check(Src("server.py").Contains("expected_potion_id"), "MCP サーバーの use_potion に expected_potion_id");
            Check(Src("McpMod.cs").Contains("public const int StateSchemaVersion = 11;"), "現行の schema は 11（sts2#1012）");

            Console.WriteLine($"PotionUsabilityContract: {Checks} checks passed");
        }
    }
}
