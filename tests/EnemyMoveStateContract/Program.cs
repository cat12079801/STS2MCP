using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private const string Entity = "TEST_MONSTER_7";
    private static readonly string[] Keys = ["id", "follow_up_id", "last_logged_id", "can_transition_away"];
    private static Dictionary<string, object?> BuildGameStateCore() => new();
    private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }

    private static T Prepare<T>(T model, string id = "WEB_CANNON_MOVE", string follow = "CURL_AND_GROW_MOVE")
        where T : MonsterModel
    {
        var move = new MoveState { IdValue = id, FollowUpIdValue = follow };
        model.Move = move;
        model.Machine = new MonsterMoveStateMachine { Log = [new MoveState { IdValue = id }] };
        model.Machine.States[id] = move;
        // 状態機械の現在地点は NextMove と同じ行動、初期地点は別の分岐状態（Nibbit の INIT_MOVE のように）。
        model.Machine.SetInternals(move, new MonsterState { IdValue = "INIT_MOVE" }, true);
        return model;
    }

    private static MonsterModel Model(string id = "WEB_CANNON_MOVE", string follow = "CURL_AND_GROW_MOVE")
        => Prepare(new MonsterModel(), id, follow);

    private static Dictionary<string, object?> Read(MonsterModel? monster)
    {
        BeginStateWarnings();
        return BuildEnemyMoveState(monster, Entity);
    }

    private static string Markdown(Dictionary<string, object?> move)
        => FormatEnemyMoveState(new() { ["move_state"] = move });

    private static Dictionary<string, string> Reasons(Dictionary<string, object?> state)
        => state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string> ?? new();

    // core: 既存の四値のうち失敗する項目。inputs: schema 6 の項目のうち失敗する項目。
    private static void Failure(Dictionary<string, object?> state, string[] core, string[] inputs)
    {
        Check(state["readable"] is bool readable && readable == (core.Length == 0), "readable は四値だけで決まる");
        Check(state["inputs_readable"] is bool inputsReadable && inputsReadable == (inputs.Length == 0), $"inputs_readable は追加項目だけで決まる: {string.Join(";", Reasons(state).Select(p => p.Key + "=" + p.Value))}");
        var reasons = Reasons(state);
        var expected = core.Concat(inputs).ToHashSet();
        Check(reasons.Keys.ToHashSet().SetEquals(expected) && _stateWarnings.Count == expected.Count,
            $"失敗項目ごとに理由と警告（期待 {string.Join(",", expected)}、実際 {string.Join(",", reasons.Keys)}）");
        foreach (string key in Keys.Concat(EnemyMoveStateInputKeys))
        {
            Check(state.ContainsKey(key), $"{key} を省略しない");
            if (expected.Contains(key))
            {
                Check(state[key] == null && reasons[key].Contains("Exception"), $"{key} の null と原因");
                Check(_stateWarnings.Any(w => w.StartsWith($"battle.enemies.{Entity}.move_state.{key}:")), "警告は個体と項目を識別");
                Check(Markdown(state).Contains(key + ":"), "Markdown に失敗項目を残す");
            }
            else if (Keys.Contains(key))
                Check(state[key] != null, "取得できた隣接項目を保持");
        }
        Check(Markdown(state).Contains(core.Length > 0 ? "| unreadable" : "| inputs unreadable"), "Markdown で取得不能を区別");
        var response = new Dictionary<string, object?> { ["move_state"] = state };
        EndStateWarnings(response);
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        foreach (string key in expected)
            Check(json.RootElement.GetProperty("move_state").GetProperty(key).ValueKind == JsonValueKind.Null,
                "本番と同じ null 無視設定でも辞書内の null を送る");
        Check(json.RootElement.GetProperty("warnings").GetArrayLength() == expected.Count, "warnings を応答に付ける");
    }

    private static readonly string[] MachineInputs = ["current_state_id", "current_state_registered", "initial_state_id", "performed_first_move", "state_log"];
    private static readonly string[] MoveInputs = ["has_follow_up", "must_perform_once", "is_stun", "stun_handler"];

    private static List<(string Id, bool IsMove)> Log(Dictionary<string, object?> state)
        => ((List<Dictionary<string, object?>>)state["state_log"]!).Select(e => ((string)e["id"]!, (bool)e["is_move"]!)).ToList();

    public static void Main()
    {
        // ソース由来の通常三手。遷移のシミュレーション・実機観測の代用ではない。
        foreach (var (id, follow) in new[]
        {
            ("WEB_CANNON_MOVE", "CURL_AND_GROW_MOVE"), ("CURL_AND_GROW_MOVE", "POUNCE_MOVE"),
            ("POUNCE_MOVE", "WEB_CANNON_MOVE"), ("OTHER_MONSTER_MOVE", "RANDOM_BRANCH")
        })
        {
            var model = Model(id, follow);
            model.Move!.FollowUpValue = new MonsterState { IdValue = follow };
            model.Move.FollowUpIdValue = "使われないID";
            model.Move.Fail.Add(nameof(MoveState.FollowUpStateId));
            var log = model.Machine!.Log!.ToArray();
            var internals = model.Machine.PeekInternals();
            var state = Read(model);
            Check(state["readable"] is true && Equals(state["id"], id) && Equals(state["follow_up_id"], follow),
                "全モンスターで参照先 ID を優先し、不要な fallback getter を読まない");
            Check(Equals(state["last_logged_id"], id) && state["can_transition_away"] is true, "ログ末尾と遷移可否");
            Check(state["inputs_readable"] is true && state["has_follow_up"] is true && state["must_perform_once"] is false
                && Equals(state["current_state_id"], id) && state["current_state_registered"] is true && Equals(state["initial_state_id"], "INIT_MOVE")
                && state["performed_first_move"] is true && state["spawned_this_turn"] is false
                && state["is_stun"] is false && state.ContainsKey("stun_handler") && state["stun_handler"] == null,
                "追加項目: follow-up の有無・ロック・現在地点・初期地点・初回実行・召喚直後・非スタン");
            Check(Log(state).SequenceEqual([(id, true)]), "行動ログの全要素（MoveState は is_move=true）");
            Check(!state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "成功時に理由・警告なし");
            Check(Markdown(state) == $"**Move:** `{id}` | follow-up: `{follow}` | log(1): `{id}`", "通常の Markdown は短い ID 表示とログ");
            Check(JsonSerializer.Serialize(Read(model)) == JsonSerializer.Serialize(state)
                && ReferenceEquals(model.Move.FollowUpValue, model.Move.FollowUpState)
                && model.Machine.Log.SequenceEqual(log) && model.Machine.PeekInternals() == internals,
                "繰り返し読取でモデル・ログ・参照・状態機械を変更しない");
        }

        // 行動ログ: 順序と重複を保ち、分岐状態（IsMove=false）も実際の要素として返す。召喚直後・初回未実行も実値。
        var history = Model("SLICE_MOVE", "HISS_MOVE");
        history.Machine!.Log = new[] { "BUTT_MOVE", "SLICE_MOVE", "BRANCH", "BUTT_MOVE", "SLICE_MOVE" }
            .Select(i => i == "BRANCH" ? new MonsterState { IdValue = i } : (MonsterState)new MoveState { IdValue = i }).ToList();
        history.Machine.SetInternals(new MonsterState { IdValue = "RAND" }, history.Move!, false);
        history.SpawnedValue = true;
        var histState = Read(history);
        Check(Log(histState).SequenceEqual([("BUTT_MOVE", true), ("SLICE_MOVE", true), ("BRANCH", false), ("BUTT_MOVE", true), ("SLICE_MOVE", true)]),
            "ログの順序・重複・is_move を保持");
        Check(Equals(histState["current_state_id"], "RAND") && Equals(histState["initial_state_id"], "SLICE_MOVE")
            && histState["performed_first_move"] is false && histState["spawned_this_turn"] is true
            && histState["current_state_registered"] is false, "現在地点が分岐（未登録）・初回未実行・召喚直後");
        Check(Markdown(histState) == "**Move:** `SLICE_MOVE` | follow-up: `HISS_MOVE` | log(5): … `SLICE_MOVE` > `BRANCH` > `BUTT_MOVE` > `SLICE_MOVE`",
            "長いログは末尾 4 件と件数");

        // 正当に空のログ（分岐状態から始まり、まだ抽選していない）: state_log は [] の実値、last_logged_id は従来どおり失敗。
        var empty = Model();
        empty.Machine!.Log!.Clear();
        var emptyState = Read(empty);
        Failure(emptyState, ["last_logged_id"], []);
        Check(emptyState["state_log"] is List<Dictionary<string, object?>> { Count: 0 } && Markdown(emptyState).Contains("log: empty"),
            "空ログは [] と読取失敗を区別");

        // 終端の行動（GasBomb.EXPLODE_MOVE）: 両参照が null。follow_up_id は従来どおり失敗、has_follow_up=false は実値。
        var terminal = Model("EXPLODE_MOVE");
        terminal.Move!.FollowUpIdValue = null;
        var terminalState = Read(terminal);
        Failure(terminalState, ["follow_up_id"], []);
        Check(terminalState["has_follow_up"] is false && Markdown(terminalState).Contains("(none: terminal move)"),
            "次の行動が無いことを読取失敗と区別");

        // 初期地点へ戻る空文字列は実値。has_follow_up=true。
        var emptyFollow = Read(Model(follow: ""));
        Check(emptyFollow["readable"] is true && Equals(emptyFollow["follow_up_id"], "") && emptyFollow["has_follow_up"] is true,
            "初期状態への空文字列を推測で置換しない");
        var intermediateLog = Model();
        intermediateLog.Machine!.Log!.Add(new MonsterState { IdValue = "BRANCH_STATE" });
        Check(Equals(Read(intermediateLog)["last_logged_id"], "BRANCH_STATE"), "MoveState 以外の実際のログ末尾もそのまま返す");

        // スタン（CreatureCmd.Stun の Wrapper）。引数なしの Stun は no-op、明示の stunMove は許可リストの処理。
        var stunned = Model("CURL_AND_GROW_MOVE", "POUNCE_MOVE");
        CreatureCmd.Stun(stunned.Creature, "EXPLICIT_RETURN");
        var locked = Read(stunned);
        Check(locked["readable"] is true && locked["can_transition_away"] is false && Equals(locked["id"], "STUNNED")
            && Equals(locked["last_logged_id"], "CURL_AND_GROW_MOVE")
            && Equals(locked["follow_up_id"], "EXPLICIT_RETURN"), "スタンの明示復帰先・実ログ末尾・false を個別に保持");
        Check(locked["is_stun"] is true && Equals(locked["stun_handler"], "CreatureCmd.Stun.no_op") && locked["must_perform_once"] is true
            && locked["inputs_readable"] is true && Equals(locked["current_state_id"], "STUNNED") && locked["current_state_registered"] is false,
            "通常スタン（no-op の lambda）を IL で照合。強制遷移した動的な STUNNED は未登録");
        Check(Markdown(locked) == "**Move:** `STUNNED` | follow-up: `EXPLICIT_RETURN` | stun: `CreatureCmd.Stun.no_op` | log(1): `CURL_AND_GROW_MOVE`",
            "スタンの ID・復帰先・処理");
        stunned.Move!.TransitionValue = true;
        Check(Read(stunned)["can_transition_away"] is true && locked["can_transition_away"] is false, "新規読取は解除を観測し以前の snapshot は不変");
        using (var wire = JsonDocument.Parse(JsonSerializer.Serialize(locked)))
            Check(wire.RootElement.GetProperty("readable").GetBoolean()
                && !wire.RootElement.GetProperty("can_transition_away").GetBoolean()
                && wire.RootElement.GetProperty("is_stun").GetBoolean()
                && wire.RootElement.GetProperty("state_log")[0].GetProperty("is_move").GetBoolean(), "false・配列を JSON の型のまま送る");

        // 同じ復帰 ID でも処理が違う（通常スタンと起床）。復帰 ID の一致で起床扱いにしない。
        var beetle = Prepare(new SlumberingBeetle(), "SLEEP_MOVE", "SLEEP_MOVE");
        beetle.StunSelf();
        var beetleNormal = Prepare(new SlumberingBeetle(), "SLEEP_MOVE", "SLEEP_MOVE");
        CreatureCmd.Stun(beetleNormal.Creature, "ROLL_OUT_MOVE");
        var wake = Read(beetle);
        var plain = Read(beetleNormal);
        Check(Equals(wake["follow_up_id"], "ROLL_OUT_MOVE") && Equals(plain["follow_up_id"], "ROLL_OUT_MOVE")
            && Equals(wake["stun_handler"], "SlumberingBeetle.WakeUpMove") && Equals(plain["stun_handler"], "CreatureCmd.Stun.no_op"),
            "同じ復帰先の起床と通常スタンを処理で区別");

        var rock = Prepare(new BowlbugRock(), "HEADBUTT_MOVE", "POST_HEADBUTT");
        rock.StunSelf();
        var tunneler = Prepare(new Tunneler(), "BURROW_MOVE", "BELOW_MOVE_1");
        tunneler.StunSelf();
        var beast = Prepare(new CeremonialBeast(), "PLOW_MOVE", "PLOW_MOVE");
        beast.StunSelf();
        var matriarch = Prepare(new LagavulinMatriarch(), "SLEEP_MOVE", "SLEEP_MOVE");
        matriarch.StunSelf();
        var slug = Model("GLOMP_MOVE", "GOOP_MOVE");
        new RavenousPower { Owner = slug.Creature }.StunOwner(slug.Creature);
        var hopper = Model("HOVER_MOVE", "FLUTTER_MOVE");
        new FlutterPower { Owner = hopper.Creature }.StunOwner(hopper.Creature, "NAB_MOVE");
        foreach (var (model, handler) in new (MonsterModel, string)[]
        {
            (rock, "BowlbugRock.DizzyMove"), (tunneler, "Tunneler.StillDizzyMove"), (beast, "CeremonialBeast.StunnedMove"),
            (matriarch, "LagavulinMatriarch.WakeUpMove"), (slug, "RavenousPower.StunnedMove"), (hopper, "FlutterPower.StunnedMove")
        })
        {
            var s = Read(model);
            Check(s["is_stun"] is true && Equals(s["stun_handler"], handler) && s["inputs_readable"] is true && _stateWarnings.Count == 0,
                $"許可リストの処理 {handler}: {s["stun_handler"]} {string.Join(";", Reasons(s).Values)}");
        }

        // 許可リストに無い stunMove（CreatureCmd の外の no-op を含む）は未観測。復帰先などの四値は保持。
        var unknown = Model();
        CreatureCmd.Stun(unknown.Creature, (IReadOnlyList<Creature> _) => Task.CompletedTask, "BITE_MOVE");
        var unknownState = Read(unknown);
        Failure(unknownState, [], ["stun_handler"]);
        Check(unknownState["is_stun"] is true && Reasons(unknownState)["stun_handler"].Contains("未対応の stunMove")
            && Markdown(unknownState).Contains("stun: unknown"), "CreatureCmd の外の lambda は no-op と見なさない");
        // 別の個体を包んだ Wrapper・別の個体のメソッドは未観測。
        var other = Model();
        CreatureCmd.Stun(other.Creature, "BITE_MOVE");
        var borrowed = Model();
        borrowed.Move = other.Move;
        var borrowedState = Read(borrowed);
        Failure(borrowedState, [], ["stun_handler"]);
        Check(Reasons(borrowedState)["stun_handler"].Contains("この個体ではありません"), "Wrapper の creature を照合");
        var beetleA = Prepare(new SlumberingBeetle());
        var beetleB = Prepare(new SlumberingBeetle());
        CreatureCmd.Stun(beetleA.Creature, beetleB.WakeUpMove, "ROLL_OUT_MOVE");
        var crossed = Read(beetleA);
        Failure(crossed, [], ["stun_handler"]);
        Check(Reasons(crossed)["stun_handler"].Contains("Target がこの個体ではありません"), "敵のメソッドの Target を照合");

        // STUNNED なのに Wrapper ではない delegate（Wrapper の形が変わった版）は is_stun を未観測にする。
        var fakeStun = Model("STUNNED", "BITE_MOVE");
        var fakeState = Read(fakeStun);
        Failure(fakeState, [], ["is_stun", "stun_handler"]);
        Check(Reasons(fakeState)["is_stun"].Contains("Wrapper の形が既知と違います"), "STUNNED と Wrapper の食い違いは null と理由");
        // Wrapper ではない通常の行動は is_stun=false のまま。
        Check(Read(Model("BITE_MOVE", "BITE_MOVE"))["is_stun"] is false && _stateWarnings.Count == 0, "通常の行動は is_stun=false");
        // 逆向き: Wrapper なのに ID が STUNNED ではない。
        var renamed = Model();
        CreatureCmd.Stun(renamed.Creature, "BITE_MOVE");
        renamed.Move!.IdValue = "OTHER_MOVE";
        var renamedState = Read(renamed);
        Failure(renamedState, [], ["is_stun", "stun_handler"]);
        Check(Reasons(renamedState)["is_stun"].Contains("STUNNED ではありません"), "Wrapper と ID の食い違いは null と理由");
        // パワーの処理は、パワーの所有者がこの個体か照合する。
        var slugB = Model("GLOMP_MOVE", "GOOP_MOVE");
        new RavenousPower { Owner = Model().Creature }.StunOwner(slugB.Creature);
        var ownerState = Read(slugB);
        Failure(ownerState, [], ["stun_handler"]);
        Check(Reasons(ownerState)["stun_handler"].Contains("所有者がこの個体ではありません"), "パワーの所有者を照合");

        // 読取失敗。四値の readable と追加項目の inputs_readable は別に決まる。
        Failure(Read(null), Keys, EnemyMoveStateInputKeys);
        var missingMove = Model();
        missingMove.Move = null;
        Failure(Read(missingMove), ["id", "follow_up_id", "can_transition_away"], MoveInputs);
        missingMove.ThrowMove = true;
        Failure(Read(missingMove), ["id", "follow_up_id", "can_transition_away"], MoveInputs);
        foreach (var (failure, inputs) in new (string, string[])[]
        {
            ("machine_null", MachineInputs), ("machine_throw", MachineInputs), ("log_null", ["state_log"]),
            ("log_throw", ["state_log"]), ("tail_null", ["state_log"]), ("tail_id_null", ["state_log"]), ("tail_id_throw", ["state_log"])
        })
        {
            var model = Model();
            switch (failure)
            {
                case "machine_null": model.Machine = null; break;
                case "machine_throw": model.ThrowMachine = true; break;
                case "log_null": model.Machine!.Log = null; break;
                case "log_throw": model.Machine!.ThrowLog = true; break;
                case "tail_null": model.Machine!.Log!.Add(null!); break;
                case "tail_id_null": model.Machine!.Log![0].IdValue = null; break;
                case "tail_id_throw": model.Machine!.Log![0].Fail.Add(nameof(MonsterState.Id)); break;
            }
            Failure(Read(model), ["last_logged_id"], inputs);
        }
        foreach (var (getter, key, inputs) in new (string, string, string[])[]
        {
            ("Id", "id", ["current_state_id", "current_state_registered", "is_stun", "stun_handler"]), ("FollowUpState", "follow_up_id", ["has_follow_up"]), ("FollowUpStateId", "follow_up_id", ["has_follow_up"]),
            ("CanTransitionAway", "can_transition_away", [])
        })
        {
            var model = Model();
            model.Move!.Fail.Add(getter);
            Failure(Read(model), [key], inputs);
        }
        // 追加項目だけの失敗では四値の readable を落とさない。途中までのログを完全なログとして返さない。
        foreach (var (name, inputs, apply) in new (string, string[], Action<MonsterModel>)[]
        {
            ("must_perform", ["must_perform_once"], m => m.Move!.Fail.Add(nameof(MoveState.MustPerformOnceBeforeTransitioning))),
            ("spawned", ["spawned_this_turn"], m => m.ThrowSpawned = true),
            ("current_null", ["current_state_id", "current_state_registered"], m => m.Machine!.SetInternals(null, m.Move, true)),
            ("initial_id_null", ["initial_state_id"], m => m.Machine!.SetInternals(m.Move, new MonsterState(), true)),
            ("middle_entry", ["state_log"], m => m.Machine!.Log!.Insert(0, new MonsterState { IdValue = "A", Fail = { nameof(MonsterState.IsMove) } })),
        })
        {
            var model = Model();
            apply(model);
            var s = Read(model);
            Failure(s, [], inputs);
            Check(s["readable"] is true, $"{name}: 四値の readable は保つ");
        }
        // 登録済みと同じ ID 文字列の別の状態（動的に作られた状態）は未登録として区別する。
        var sameId = Model();
        sameId.Machine!.SetInternals(new MoveState { IdValue = "WEB_CANNON_MOVE" }, sameId.Move, true);
        var sameIdState = Read(sameId);
        Check(Equals(sameIdState["current_state_id"], "WEB_CANNON_MOVE") && sameIdState["current_state_registered"] is false
            && sameIdState["inputs_readable"] is true && _stateWarnings.Count == 0, "同じ ID でも参照が違えば current_state_registered=false");

        // 二度の読取の間にログが変われば、末尾と last_logged_id の不一致として全体を未観測にする。
        var racing = Model();
        int reads = 0;
        racing.Machine!.OnLogRead = () => { if (++reads == 2) racing.Machine.Log!.Add(new MoveState { IdValue = "LATE_MOVE" }); };
        var raced = Read(racing);
        Failure(raced, [], ["state_log"]);
        Check(Reasons(raced)["state_log"].Contains("一致しません"), "ログの末尾と last_logged_id を照合");

        var noId = Model();
        noId.Move!.IdValue = null;
        Failure(Read(noId), ["id"], ["current_state_id", "current_state_registered", "is_stun", "stun_handler"]);
        var brokenRef = Model();
        brokenRef.Move!.FollowUpValue = new MonsterState { IdValue = "RETURN" };
        brokenRef.Move.FollowUpValue.Fail.Add(nameof(MonsterState.Id));
        Failure(Read(brokenRef), ["follow_up_id"], ["has_follow_up"]);
        brokenRef.Move.FollowUpValue.Fail.Clear();
        brokenRef.Move.FollowUpValue.IdValue = null;
        Check(Equals(Read(brokenRef)["follow_up_id"], "CURL_AND_GROW_MOVE"), "参照先 Id が null のときも指定式どおり fallback");

        var valid = Read(Model());
        Check(_stateWarnings.Count == 0 && valid["readable"] is true && valid["inputs_readable"] is true, "次の取得に失敗状態を持ち越さない");
        foreach (string key in Keys)
        {
            var incomplete = new Dictionary<string, object?>(valid);
            incomplete.Remove(key);
            Check(!IsEnemyMoveStateReadable(incomplete), "必須キー欠落は unreadable");
            incomplete[key] = null;
            Check(!IsEnemyMoveStateReadable(incomplete), "必須値 null は unreadable");
            incomplete[key] = 0;
            Check(!IsEnemyMoveStateReadable(incomplete), "型違いは unreadable");
        }
        Check(FormatEnemyMoveState(new()) == "", "旧 snapshot の欠落を補完しない");

        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));
        string builder = Source("McpMod.StateBuilder.cs");
        int start = builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState(Creature creature)", StringComparison.Ordinal);
        string enemyBody = builder[start..builder.IndexOf("return state;", start, StringComparison.Ordinal)];
        Check(enemyBody.Contains("[\"move_state\"] = BuildEnemyMoveState(monster, entityId),")
            && enemyBody.IndexOf("BuildEnemyMoveState(", StringComparison.Ordinal) < enemyBody.IndexOf("if (!alive)", StringComparison.Ordinal),
            "敵種・生死で絞らず全ての敵行に配線");
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildEnemyList(combatState)"), "協力プレイも共通の敵 builder");
        Check(Source("McpMod.Formatting.cs").Contains("FormatEnemyMoveState(enemy)"), "敵の Markdown 行への配線");
        string stun = Source("McpMod.EnemyStunHandler.cs");
        Check(!stun.Contains(".Invoke(") && !stun.Contains("DynamicInvoke") && !Source("McpMod.EnemyMoveState.cs").Contains("GetNextState(")
            && !Source("McpMod.EnemyMoveState.cs").Contains("RollMove("), "delegate・抽選・遷移を呼ばない");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 6;"), "現行の schema は 6（sts2#1018）");
        Console.WriteLine($"敵行動状態の契約試験: {Checks} 件成功");
    }
}
