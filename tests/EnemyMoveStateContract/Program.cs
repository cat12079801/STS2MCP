using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;
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

    private static MonsterModel Model(string id = "WEB_CANNON_MOVE", string follow = "CURL_AND_GROW_MOVE")
        => new()
        {
            Move = new MoveState { IdValue = id, FollowUpIdValue = follow },
            Machine = new MonsterMoveStateMachine { Log = [new MonsterState { IdValue = id }] }
        };

    private static Dictionary<string, object?> Read(MonsterModel? monster)
    {
        BeginStateWarnings();
        return BuildEnemyMoveState(monster, Entity);
    }

    private static string Markdown(Dictionary<string, object?> move)
        => FormatEnemyMoveState(new() { ["move_state"] = move });

    private static void Failure(Dictionary<string, object?> state, params string[] keys)
    {
        Check(state["readable"] is false, "一項目でも取得失敗なら readable=false");
        var reasons = (Dictionary<string, string>)state["unavailable_reasons"]!;
        Check(reasons.Count == keys.Length && _stateWarnings.Count == keys.Length, "失敗項目ごとに理由と警告");
        foreach (string key in Keys)
        {
            Check(state.ContainsKey(key), $"{key} を省略しない");
            if (keys.Contains(key))
            {
                Check(state[key] == null && reasons[key].Contains("InvalidOperationException"), $"{key} の null と原因");
                Check(_stateWarnings.Any(w => w.StartsWith($"battle.enemies.{Entity}.move_state.{key}:")), "警告は個体と項目を識別");
                Check(Markdown(state).Contains(key + ":"), "Markdown に失敗項目を残す");
            }
            else Check(state[key] != null && !reasons.ContainsKey(key), "取得できた隣接項目を保持");
        }
        Check(Markdown(state).Contains("unreadable"), "Markdown で取得不能を区別");
        var response = new Dictionary<string, object?> { ["move_state"] = state };
        EndStateWarnings(response);
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        foreach (string key in keys)
            Check(json.RootElement.GetProperty("move_state").GetProperty(key).ValueKind == JsonValueKind.Null,
                "本番と同じ null 無視設定でも辞書内の null を送る");
        Check(json.RootElement.GetProperty("warnings").GetArrayLength() == keys.Length, "warnings を応答に付ける");
    }

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
            var state = Read(model);
            Check(state["readable"] is true && Equals(state["id"], id) && Equals(state["follow_up_id"], follow),
                "全モンスターで参照先 ID を優先し、不要な fallback getter を読まない");
            Check(Equals(state["last_logged_id"], id) && state["can_transition_away"] is true, "ログ末尾と遷移可否");
            Check(!state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0, "成功時に理由・警告なし");
            Check(Markdown(state) == $"**Move:** `{id}` | follow-up: `{follow}`", "通常の Markdown は短い ID 表示");
            Check(JsonSerializer.Serialize(Read(model)) == JsonSerializer.Serialize(state)
                && ReferenceEquals(model.Move.FollowUpValue, model.Move.FollowUpState)
                && model.Machine.Log.SequenceEqual(log), "繰り返し読取でモデル・ログ・参照を変更しない");
        }

        var stunned = Model("STUNNED", "EXPLICIT_RETURN");
        stunned.Move!.TransitionValue = false;
        stunned.Machine!.Log!.Add(new MonsterState { IdValue = "CURL_AND_GROW_MOVE" });
        var locked = Read(stunned);
        Check(locked["readable"] is true && locked["can_transition_away"] is false
            && Equals(locked["last_logged_id"], "CURL_AND_GROW_MOVE")
            && Equals(locked["follow_up_id"], "EXPLICIT_RETURN"), "スタンの明示復帰先・実ログ末尾・false を個別に保持");
        Check(Markdown(locked).Contains("`STUNNED`") && Markdown(locked).Contains("`EXPLICIT_RETURN`"), "スタンの ID と復帰先");
        stunned.Move.TransitionValue = true;
        Check(Read(stunned)["can_transition_away"] is true && locked["can_transition_away"] is false, "新規読取は解除を観測し以前の snapshot は不変");
        using (var wire = JsonDocument.Parse(JsonSerializer.Serialize(locked)))
            Check(wire.RootElement.GetProperty("readable").GetBoolean()
                && !wire.RootElement.GetProperty("can_transition_away").GetBoolean(), "false を bool のまま送る");

        var emptyFollow = Read(Model(follow: ""));
        Check(emptyFollow["readable"] is true && Equals(emptyFollow["follow_up_id"], ""), "初期状態への空文字列を推測で置換しない");
        var intermediateLog = Model();
        intermediateLog.Machine!.Log!.Add(new MonsterState { IdValue = "BRANCH_STATE" });
        Check(Equals(Read(intermediateLog)["last_logged_id"], "BRANCH_STATE"), "MoveState 以外の実際のログ末尾もそのまま返す");

        Failure(Read(null), Keys);
        var missingMove = Model();
        missingMove.Move = null;
        Failure(Read(missingMove), "id", "follow_up_id", "can_transition_away");
        missingMove.ThrowMove = true;
        Failure(Read(missingMove), "id", "follow_up_id", "can_transition_away");
        foreach (string failure in new[] { "machine_null", "machine_throw", "log_null", "log_empty", "log_throw", "tail_null", "tail_id_null", "tail_id_throw" })
        {
            var model = Model();
            switch (failure)
            {
                case "machine_null": model.Machine = null; break;
                case "machine_throw": model.ThrowMachine = true; break;
                case "log_null": model.Machine!.Log = null; break;
                case "log_empty": model.Machine!.Log!.Clear(); break;
                case "log_throw": model.Machine!.ThrowLog = true; break;
                case "tail_null": model.Machine!.Log!.Add(null!); break;
                case "tail_id_null": model.Machine!.Log![0].IdValue = null; break;
                case "tail_id_throw": model.Machine!.Log![0].Fail.Add(nameof(MonsterState.Id)); break;
            }
            Failure(Read(model), "last_logged_id");
        }
        foreach (var (getter, key) in new[]
        {
            ("Id", "id"), ("FollowUpState", "follow_up_id"), ("FollowUpStateId", "follow_up_id"),
            ("CanTransitionAway", "can_transition_away")
        })
        {
            var model = Model();
            model.Move!.Fail.Add(getter);
            Failure(Read(model), key);
        }
        var noId = Model();
        noId.Move!.IdValue = null;
        Failure(Read(noId), "id");
        var noFollow = Model();
        noFollow.Move!.FollowUpIdValue = null;
        Failure(Read(noFollow), "follow_up_id");
        var brokenRef = Model();
        brokenRef.Move!.FollowUpValue = new MonsterState { IdValue = "RETURN" };
        brokenRef.Move.FollowUpValue.Fail.Add(nameof(MonsterState.Id));
        Failure(Read(brokenRef), "follow_up_id");
        brokenRef.Move.FollowUpValue.Fail.Clear();
        brokenRef.Move.FollowUpValue.IdValue = null;
        Check(Equals(Read(brokenRef)["follow_up_id"], "CURL_AND_GROW_MOVE"), "参照先 Id が null のときも指定式どおり fallback");

        var valid = Read(Model());
        Check(_stateWarnings.Count == 0 && valid["readable"] is true, "次の取得に失敗状態を持ち越さない");
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
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 5;"), "現行の schema は 5（sts2#1003）");
        Console.WriteLine($"敵行動状態の契約試験: {Checks} 件成功");
    }
}
