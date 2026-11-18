using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private const string Entity = "WATERFALL_GIANT_0";
    private static Dictionary<string, object?> BuildGameStateCore() => new();
    private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();
    // 本番は McpMod.GenerationPool.cs（ロードした本体 DLL の SHA-256）と McpMod.EntityIdentity.cs。
    private static readonly Lazy<string> GenerationAssemblySha256 = new(() => "a671e9ef-test");
    internal static string GetStableEntityId(Creature creature) => creature.EntityIdValue;

    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }

    private static Dictionary<string, object?> Read(WaterfallGiant giant)
    {
        BeginStateWarnings();
        return BuildEnemyCombatState(giant, Entity)
            ?? throw new Exception("滝の巨人には combat_state を付ける");
    }

    private static string Markdown(Dictionary<string, object?> state)
        => FormatEnemyCombatState(new() { ["combat_state"] = state });

    public static void Main()
    {
        var wireOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        string[] keys =
        {
            "is_about_to_blow", "steam_eruption_damage", "current_pressure_gun_damage", "steam_boost_amount",
            "pressurize_amount", "stomp_damage", "ram_damage", "pressure_up_damage", "siphon_heal"
        };
        // A7 の通常モード（ToughEnemies・DeadlyEnemies・Dread 無し）の getter 値と初期のプレッシャーガン。
        var a7 = new GiantValues(20, 3, 15, 15, 10, 13, 10);
        const string a7Tail = " | current pressure gun damage: 20 | steam boost amount: 3 | pressurize amount: 15"
            + " | stomp damage: 15 | ram damage: 10 | pressure up damage: 13 | siphon heal: 10";

        // 値の並びは WaterfallGiant のソース上の段階（通常 → TriggerAboutToBlowState → AboutToBlowMove）。
        // 遷移の模擬ではなく、試験が private フィールドに直接入れた値を読むだけ。
        var giant = new WaterfallGiant();
        giant.Set(a7);
        foreach (var (blow, damage, text) in new[]
        {
            (false, 0, "**Combat state:** is about to blow: no | steam eruption damage: 0" + a7Tail),
            (true, 0, "**Combat state:** is about to blow: yes | steam eruption damage: 0" + a7Tail),
            (true, 37, "**Combat state:** is about to blow: yes | steam eruption damage: 37" + a7Tail)
        })
        {
            giant.Set(blow, damage);
            var state = Read(giant);
            Check(state.Keys.SequenceEqual(keys) && Equals(state["is_about_to_blow"], blow) && Equals(state["steam_eruption_damage"], damage),
                "false と 0 も実値として出す・項目の並びは固定");
            Check(!state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "成功時に理由・警告なし");
            Check(Markdown(state) == text, "Markdown は false=no・0 を表示");
            Check(giant.Peek() == (blow, damage), "読取でフィールドを変更しない");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, wireOptions));
            Check(json.RootElement.GetProperty("is_about_to_blow").GetBoolean() == blow
                && json.RootElement.GetProperty("steam_eruption_damage").GetInt32() == damage, "本番の wire 設定でも bool と int のまま");
        }
        giant.Set(true, 37);
        var snapshot = Read(giant);
        giant.Set(false, 0);
        Check(Equals(snapshot["steam_eruption_damage"], 37) && Equals(Read(giant)["steam_eruption_damage"], 0), "前回の snapshot は独立");

        // 通常段階の 7 値。A7・A9（DeadlyEnemies・ToughEnemies）+ Dread・PressureGunMove を 2 回終えた値（20+5+5）・0 も実値。
        foreach (var values in new[] { a7, new GiantValues(30, 3, 15, 15, 10, 13, 10), new GiantValues(23, 4, 20, 16, 11, 14, 15), new GiantValues(0, 0, 0, 0, 0, 0, 0) })
        {
            giant.Set(values);
            giant.Set(false, 0);
            var state = Read(giant);
            int[] expected = { values.PressureGun, values.SteamBoost, values.Pressurize, values.Stomp, values.Ram, values.PressureUp, values.Siphon };
            Check(keys.Skip(2).Select(key => state[key]).SequenceEqual(expected.Cast<object?>()), $"7 値を getter・フィールドの値のまま出す（{values}）");
            Check(!state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "7 値の成功時に理由・警告なし");
            Check(giant.PeekValues() == (values.PressureGun, values), "読取で値を変更しない");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, wireOptions));
            Check(keys.Skip(2).Zip(expected).All(pair => json.RootElement.GetProperty(pair.First).ValueKind == JsonValueKind.Number
                && json.RootElement.GetProperty(pair.First).GetInt32() == pair.Second), "wire でも int のまま（0 を省略しない）");
        }
        giant.Set(a7);
        int reads = giant.GetterReads;
        Read(giant);
        Check(giant.GetterReads - reads == 6, "getter は 6 個を 1 回ずつ読む（CurrentPressureGunDamage はフィールド）");

        // 試合の外（RunManager.IsInProgress=false）では getter が Ascension・Dread を無いものとして返すので読まない。
        RunManager.Instance.IsInProgress = false;
        reads = giant.GetterReads;
        var outside = Read(giant);
        RunManager.Instance.IsInProgress = true;
        var outsideReasons = (Dictionary<string, string>)outside["unavailable_reasons"]!;
        Check(keys.Skip(3).All(key => outside[key] == null && outsideReasons[key].Contains("RunManager is not in progress")),
            "ラン外は getter の 6 値を null と理由（既定値で補わない）");
        Check(Equals(outside["current_pressure_gun_damage"], 20) && Equals(outside["is_about_to_blow"], false) && giant.GetterReads == reads,
            "フィールドの値は読み、getter は呼ばない");
        Check(_stateWarnings.Count == 6 && _stateWarnings.All(w => w.StartsWith($"battle.enemies.{Entity}.combat_state.")), "ラン外の warnings は 6 項目");
        Check(Markdown(outside).Contains("steam boost amount: null (InvalidOperationException: RunManager is not in progress"), "Markdown に理由");

        // getter が投げたとき・負の値は、その項目だけ null と理由。他の項目は残す。
        giant.ThrowFrom = "SteamBoostAmount";
        giant.Set(a7 with { PressureGun = -5, Siphon = -1 });
        var broken = Read(giant);
        giant.ThrowFrom = null;
        var brokenReasons = (Dictionary<string, string>)broken["unavailable_reasons"]!;
        Check(broken["steam_boost_amount"] == null && brokenReasons["steam_boost_amount"].Contains("boom"), "getter の例外は null と理由");
        Check(broken["current_pressure_gun_damage"] == null && brokenReasons["current_pressure_gun_damage"].Contains("non-negative")
            && broken["siphon_heal"] == null && brokenReasons["siphon_heal"].Contains("non-negative"), "負の値は null と理由（0 に丸めない）");
        Check(Equals(broken["stomp_damage"], 15) && brokenReasons.Count == 3 && _stateWarnings.Count == 3, "他の項目は残し、warnings は失敗した 3 項目");
        giant.Set(a7);

        BeginStateWarnings();
        Check(BuildEnemyCombatState(null, Entity) == null, "Monster が無い敵には付けない");
        var louse = new LouseProgenitor();
        Check(BuildEnemyCombatState(louse, "LOUSE_PROGENITOR_0") == null && louse.Peek(), "同名フィールドを持つ別の敵には付けない");
        Check(BuildEnemyCombatState(new MonsterModel(), "OTHER_0") == null && _stateWarnings.Count == 0, "対象外の敵は警告なしで省略");
        Check(FormatEnemyCombatState(new()) == "" && Markdown(new()) == "", "欠落・空は Markdown に出さない");

        // 読取失敗の形（null・理由・warnings）は共通の ReadInternalStateValue / ReadDeclaredStateField。
        // 宣言型の検査で、欠落・型違いのフィールドは失敗になる。
        BeginStateWarnings();
        var failed = new Dictionary<string, object?>();
        ReadInternalStateValue(failed, "is_about_to_blow",
            () => ReadDeclaredStateField<bool>(giant, typeof(WaterfallGiant), "_removed"), $"battle.enemies.{Entity}.combat_state");
        ReadInternalStateValue(failed, "steam_eruption_damage",
            () => ReadDeclaredStateField<long>(giant, typeof(WaterfallGiant), "_steamEruptionDamage"), $"battle.enemies.{Entity}.combat_state");
        ReadInternalStateValue(failed, "stomp_damage",
            () => ReadDeclaredStateProperty<int>(giant, typeof(WaterfallGiant), "Removed"), $"battle.enemies.{Entity}.combat_state");
        ReadInternalStateValue(failed, "ram_damage",
            () => ReadDeclaredStateProperty<long>(giant, typeof(WaterfallGiant), "RamDamage"), $"battle.enemies.{Entity}.combat_state");
        var reasons = (Dictionary<string, string>)failed["unavailable_reasons"]!;
        Check(failed["is_about_to_blow"] == null && reasons["is_about_to_blow"].Contains("MissingFieldException"), "欠落フィールドは null と理由");
        Check(failed["steam_eruption_damage"] == null && reasons["steam_eruption_damage"].Contains("Expected non-null Int64"), "型違いは null と理由（0 で補完しない）");
        Check(failed["stomp_damage"] == null && reasons["stomp_damage"].Contains("MissingMemberException")
            && failed["ram_damage"] == null && reasons["ram_damage"].Contains("Expected readable Int64"), "欠落・型違いの getter も null と理由");
        Check(_stateWarnings.Count == 4 && _stateWarnings.All(w => w.StartsWith($"battle.enemies.{Entity}.combat_state.")), "warnings は個体と項目を識別");
        Check(Markdown(failed).Contains("is about to blow: null (MissingFieldException") && !Markdown(failed).Contains("unavailable"), "Markdown に失敗理由を出し、理由の辞書自体は出さない");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failed, wireOptions)))
            Check(json.RootElement.GetProperty("steam_eruption_damage").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("steam_eruption_damage", out _), "null と理由が wire に残る");

        CheckMoveInputMonsters(wireOptions);
        CheckEnemyMoveContext(wireOptions);

        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));
        string builder = Source("McpMod.StateBuilder.cs");
        int start = builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState(Creature creature)", StringComparison.Ordinal);
        string enemyBody = builder[start..builder.IndexOf("return state;", start, StringComparison.Ordinal)];
        Check(enemyBody.Contains("BuildEnemyCombatState(monster, entityId) is { } enemyCombatState")
            && enemyBody.IndexOf("BuildEnemyCombatState(", StringComparison.Ordinal) < enemyBody.IndexOf("if (!alive)", StringComparison.Ordinal),
            "生死で絞らず敵行に配線（戦闘に残る死亡中の巨人も対象）");
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildEnemyList(combatState)"), "協力プレイも共通の敵 builder");
        Check(Source("McpMod.Formatting.cs").Contains("FormatEnemyCombatState(enemy)"), "敵の Markdown 行への配線");
        Check(enemyBody.Contains("AttachEnemySlotName(state, creature, entityId);"), "全敵行に slot_name");
        Check(builder.Contains("battle[\"enemy_move_context\"] = BuildEnemyMoveContext(combatState);")
            && Source("McpMod.MultiplayerState.cs").Contains("battle[\"enemy_move_context\"] = BuildEnemyMoveContext(combatState);"),
            "単独・協力プレイの battle に enemy_move_context");
        Check(Source("McpMod.Formatting.cs").Contains("FormatEnemyMoveContext(battle)") && Source("McpMod.Formatting.cs").Contains("FormatEnemySlotName(enemy)"),
            "Markdown への配線");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 8;"), "現行の schema は 8（sts2#1031）");
        Console.WriteLine($"敵の戦闘内部状態の契約試験: {Checks} 件成功");
    }

    private static Dictionary<string, object?> ReadAny(MonsterModel monster, string entity)
    {
        BeginStateWarnings();
        return BuildEnemyCombatState(monster, entity) ?? throw new Exception($"{entity} には combat_state を付ける");
    }

    // sts2#1018: 行動の分岐・効果量に効く private の現在値。値は試験がフィールドへ直接入れ、遷移は模擬しない。
    private static void CheckMoveInputMonsters(JsonSerializerOptions wireOptions)
    {
        var cases = new List<(MonsterModel Monster, string[] Keys, object?[] Values, string Markdown)>();
        var aeonglass = new Aeonglass(); aeonglass.Set(2, 3);
        cases.Add((aeonglass, ["additional_strength", "wither_upgrade_count"], [2, 3], "additional strength: 2 | wither upgrade count: 3"));
        var axebot = new Axebot();
        cases.Add((axebot, ["stock_amount"], [2], "stock amount: 2"));
        var rock = new BowlbugRock(); rock.Set(true);
        cases.Add((rock, ["is_off_balance"], [true], "is off balance: yes"));
        var knight = new FrogKnight();
        cases.Add((knight, ["has_beetle_charged"], [false], "has beetle charged: no"));
        var demon = new KnowledgeDemon(); demon.Set(3);
        cases.Add((demon, ["curse_of_knowledge_counter"], [3], "curse of knowledge counter: 3"));
        cases.Add((new LivingFog(), ["bloat_amount"], [1], "bloat amount: 1"));
        var nibbit = new Nibbit(); nibbit.Set(false, true);
        cases.Add((nibbit, ["is_alone", "is_front"], [false, true], "is alone: no | is front: yes"));
        var queen = new Queen(); queen.Set(true);
        cases.Add((queen, ["has_amalgam_died"], [true], "has amalgam died: yes"));
        var subject = new TestSubject(); subject.Set(1, 2);
        cases.Add((subject, ["respawns", "extra_multi_claw_count"], [1, 2], "respawns: 1 | extra multi claw count: 2"));
        var toad = new Toadpole();
        cases.Add((toad, ["is_front"], [false], "is front: no"));
        var rat = new TwoTailedRat(); rat.Set(-1, 0);
        cases.Add((rat, ["turns_until_summonable", "call_for_backup_count"], [-1, 0], "turns until summonable: -1 | call for backup count: 0"));
        foreach (var (monster, keys, values, markdown) in cases)
        {
            var state = ReadAny(monster, "M_0");
            Check(state.Keys.SequenceEqual(keys) && keys.Zip(values).All(p => Equals(state[p.First], p.Second)),
                $"{monster.GetType().Name}: 許可リストの項目だけを値のまま出す（false・0・負も実値）");
            Check(!state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, $"{monster.GetType().Name}: 理由・警告なし");
            Check(Markdown(state) == "**Combat state:** " + markdown, $"{monster.GetType().Name}: Markdown");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, wireOptions));
            Check(keys.All(k => json.RootElement.TryGetProperty(k, out _)), $"{monster.GetType().Name}: wire でも項目を省略しない");
        }
        axebot.Set(1);
        Check(Equals(ReadAny(axebot, "AXEBOT_0")["stock_amount"], 1), "Axebot は StockAmount の getter（_stockOverrideAmount ?? 2）");

        // ファブリケーターの直前の召喚: 実 null（まだ召喚なし）と読取失敗を区別する。
        var fabricator = new Fabricator();
        var first = ReadAny(fabricator, "FABRICATOR_0");
        Check(first["last_spawned_readable"] is true && first.ContainsKey("last_spawned_id") && first["last_spawned_id"] == null
            && !first.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "初回の null は readable=true・id null");
        Check(Markdown(first) == "**Combat state:** last spawned id: none", "Markdown は none");
        var guard = new Guardbot { Id = { Entry = "GUARDBOT" } };
        fabricator.Set(guard);
        var spawned = ReadAny(fabricator, "FABRICATOR_0");
        Check(spawned["last_spawned_readable"] is true && Equals(spawned["last_spawned_id"], "GUARDBOT") && ReferenceEquals(fabricator.Peek(), guard),
            "召喚したモデルの ID。読取で参照を変えない");
        fabricator.Set(new Guardbot());
        var noEntry = ReadAny(fabricator, "FABRICATOR_0");
        Check(noEntry["last_spawned_readable"] is false && noEntry["last_spawned_id"] == null
            && ((Dictionary<string, string>)noEntry["unavailable_reasons"]!)["last_spawned_id"].Contains("Entry")
            && _stateWarnings.Single().StartsWith("battle.enemies.FABRICATOR_0.combat_state.last_spawned_id"),
            "ID が読めないときは readable=false・理由・warnings（まだ召喚なしと区別）");
        Check(Markdown(noEntry).StartsWith("**Combat state:** last spawned id: null (InvalidOperationException"), "Markdown に理由");
    }

    private static Dictionary<string, object?> Context(ICombatState combat)
    {
        BeginStateWarnings();
        return BuildEnemyMoveContext(combat);
    }

    // battle.enemy_move_context: Ascension・Dread・モード・人数・遭遇のスロットと占有個体・DLL の hash。
    private static void CheckEnemyMoveContext(JsonSerializerOptions wireOptions)
    {
        var run = RunManager.Instance;
        run.Levels.Clear();
        foreach (var level in new[] { AscensionLevel.SwarmingElites, AscensionLevel.ToughEnemies, AscensionLevel.DeadlyEnemies })
            run.Levels.Add(level);
        var front = new Creature { EntityIdValue = "NIBBIT_0", SlotValue = "front" };
        var corpse = new Creature { EntityIdValue = "NIBBIT_1", SlotValue = "back" };
        var illusion = new Creature { EntityIdValue = "EYE_WITH_TEETH_0", SlotValue = "illusion" };
        var encounter = new EncounterModel { Id = { Entry = "NIBBITS_WEAK" }, SlotValues = ["front", "back", "spare"] };
        var combat = new StubCombatState { Enemies = [front, corpse, illusion], EncounterValue = encounter };
        var ctx = Context(combat);
        Check(ctx["readable"] is true && !ctx.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "成功時に理由・警告なし");
        Check(Equals(ctx["game_assembly_sha256"], "a671e9ef-test") && Equals(ctx["game_mode"], "Standard") && ctx["is_dread"] is false
            && ((List<string>)ctx["ascension_levels"]!).SequenceEqual(["SwarmingElites", "ToughEnemies", "DeadlyEnemies"])
            && Equals(ctx["combat_player_count"], 1) && ctx["has_encounter"] is true && Equals(ctx["encounter_id"], "NIBBITS_WEAK"),
            "hash・モード・Dread・HasAscension が true の段階（列挙順）・人数・遭遇");
        var slots = (List<Dictionary<string, object?>>)ctx["encounter_slots"]!;
        Check(slots.Select(s => (string)s["name"]!).SequenceEqual(["front", "back", "spare"])
            && ((List<string>)slots[0]["occupant_ids"]!).SequenceEqual(["NIBBIT_0"])
            && ((List<string>)slots[1]["occupant_ids"]!).SequenceEqual(["NIBBIT_1"])
            && ((List<string>)slots[2]["occupant_ids"]!).Count == 0,
            "Slots の順と、Enemies 全体（戦闘に残る死亡個体を含む）からの占有個体。遭遇外のスロットは列に足さない");
        string md = FormatEnemyMoveContext(new() { ["enemy_move_context"] = ctx });
        Check(md == "**Enemy move context:** mode Standard | dread no | ascension SwarmingElites,ToughEnemies,DeadlyEnemies"
            + " | encounter NIBBITS_WEAK | slots front=NIBBIT_0, back=NIBBIT_1, spare=empty", "Markdown の 1 行");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(ctx, wireOptions)))
            Check(json.RootElement.GetProperty("ascension_levels").GetArrayLength() == 3
                && json.RootElement.GetProperty("encounter_slots")[2].GetProperty("occupant_ids").GetArrayLength() == 0, "wire でも空配列を送る");

        // Dread・Blitz の段階はゲームの判定のまま（Ascension の数値から作らない）。
        run.Dread = true;
        combat.RunState = new StubRunState { GameMode = GameMode.Dread };
        var dread = Context(combat);
        Check(dread["is_dread"] is true && Equals(dread["game_mode"], "Dread"), "Dread");
        run.Dread = false;
        combat.RunState = new StubRunState();

        // ラン外は HasAscension・IsDread・Slots の getter を「無し」で読まない。
        run.IsInProgress = false;
        int reads = run.AscensionReads;
        var outside = Context(combat);
        run.IsInProgress = true;
        var reasons = (Dictionary<string, string>)outside["unavailable_reasons"]!;
        Check(outside["readable"] is false && outside["is_dread"] == null && outside["ascension_levels"] == null && outside["encounter_slots"] == null
            && reasons.Keys.ToHashSet().SetEquals(["is_dread", "ascension_levels", "encounter_slots"])
            && reasons.Values.All(r => r.Contains("RunManager is not in progress")) && run.AscensionReads == reads,
            "ラン外は null と理由（無しで補わない）");
        Check(Equals(outside["game_mode"], "Standard") && Equals(outside["encounter_id"], "NIBBITS_WEAK"), "他の項目は保持");
        Check(_stateWarnings.Count == 3 && _stateWarnings.All(w => w.StartsWith("battle.enemy_move_context.")), "warnings は項目を識別");

        // 遭遇の無い戦闘（has_encounter=false）は空のスロット列の実値。遭遇の読取失敗は未観測。
        combat.EncounterValue = null;
        var none = Context(combat);
        Check(none["readable"] is true && none["has_encounter"] is false && none["encounter_id"] == null
            && none["encounter_slots"] is List<Dictionary<string, object?>> { Count: 0 }, "遭遇なし");
        combat.ThrowEncounter = true;
        var broken = Context(combat);
        combat.ThrowEncounter = false;
        combat.EncounterValue = encounter;
        var brokenReasons = (Dictionary<string, string>)broken["unavailable_reasons"]!;
        Check(broken["readable"] is false && broken["has_encounter"] == null && broken["encounter_slots"] == null
            && brokenReasons.Keys.ToHashSet().SetEquals(["has_encounter", "encounter_id", "encounter_slots"]), "遭遇の読取失敗");
        encounter.ThrowSlots = true;
        var slotFail = Context(combat);
        encounter.ThrowSlots = false;
        Check(slotFail["encounter_slots"] == null && ((Dictionary<string, string>)slotFail["unavailable_reasons"]!).Keys.Single() == "encounter_slots",
            "Slots の例外はその項目だけ");
        illusion.ThrowSlot = true;
        var occFail = Context(combat);
        illusion.ThrowSlot = false;
        Check(occFail["encounter_slots"] == null, "占有個体を一つでも読めなければ途中までの列を返さない");

        // 敵行の slot_name: 実値・スロット無し（null）・読取失敗。
        var row = new Dictionary<string, object?>();
        BeginStateWarnings();
        AttachEnemySlotName(row, front, "NIBBIT_0");
        Check(Equals(row["slot_name"], "front") && FormatEnemySlotName(row) == " | Slot: front", "slot_name");
        row = new();
        AttachEnemySlotName(row, new Creature(), "X_0");
        Check(row.ContainsKey("slot_name") && row["slot_name"] == null && !row.ContainsKey("unavailable_reasons") && FormatEnemySlotName(row) == " | Slot: none",
            "スロット無しは null（理由なし）");
        row = new();
        AttachEnemySlotName(row, new Creature { ThrowSlot = true }, "X_0");
        Check(row["slot_name"] == null && ((Dictionary<string, string>)row["unavailable_reasons"]!).ContainsKey("slot_name")
            && _stateWarnings.Single().StartsWith("battle.enemies.X_0.slot_name") && FormatEnemySlotName(row).StartsWith(" | Slot: null (Invalid"),
            "読取失敗は理由と warnings");
        Check(FormatEnemySlotName(new()) == "" && FormatEnemyMoveContext(new()) == "", "旧 snapshot の欠落は表示しない");
    }
}
