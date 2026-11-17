using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private const string Entity = "WATERFALL_GIANT_0";
    private static Dictionary<string, object?> BuildGameStateCore() => new();
    private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

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

        // 値の並びは WaterfallGiant のソース上の段階（通常 → TriggerAboutToBlowState → AboutToBlowMove）。
        // 遷移の模擬ではなく、試験が private フィールドに直接入れた値を読むだけ。
        var giant = new WaterfallGiant();
        foreach (var (blow, damage, text) in new[]
        {
            (false, 0, "**Combat state:** is about to blow: no | steam eruption damage: 0"),
            (true, 0, "**Combat state:** is about to blow: yes | steam eruption damage: 0"),
            (true, 37, "**Combat state:** is about to blow: yes | steam eruption damage: 37")
        })
        {
            giant.Set(blow, damage);
            var state = Read(giant);
            Check(state.Count == 2 && Equals(state["is_about_to_blow"], blow) && Equals(state["steam_eruption_damage"], damage),
                "false と 0 も実値として出す");
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
        var reasons = (Dictionary<string, string>)failed["unavailable_reasons"]!;
        Check(failed["is_about_to_blow"] == null && reasons["is_about_to_blow"].Contains("MissingFieldException"), "欠落フィールドは null と理由");
        Check(failed["steam_eruption_damage"] == null && reasons["steam_eruption_damage"].Contains("Expected non-null Int64"), "型違いは null と理由（0 で補完しない）");
        Check(_stateWarnings.Count == 2 && _stateWarnings.All(w => w.StartsWith($"battle.enemies.{Entity}.combat_state.")), "warnings は個体と項目を識別");
        Check(Markdown(failed).Contains("is about to blow: null (MissingFieldException") && !Markdown(failed).Contains("unavailable"), "Markdown に失敗理由を出し、理由の辞書自体は出さない");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(failed, wireOptions)))
            Check(json.RootElement.GetProperty("steam_eruption_damage").ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty("steam_eruption_damage", out _), "null と理由が wire に残る");

        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));
        string builder = Source("McpMod.StateBuilder.cs");
        int start = builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState(Creature creature)", StringComparison.Ordinal);
        string enemyBody = builder[start..builder.IndexOf("return state;", start, StringComparison.Ordinal)];
        Check(enemyBody.Contains("BuildEnemyCombatState(monster, entityId) is { } enemyCombatState")
            && enemyBody.IndexOf("BuildEnemyCombatState(", StringComparison.Ordinal) < enemyBody.IndexOf("if (!alive)", StringComparison.Ordinal),
            "生死で絞らず敵行に配線（戦闘に残る死亡中の巨人も対象）");
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildEnemyList(combatState)"), "協力プレイも共通の敵 builder");
        Check(Source("McpMod.Formatting.cs").Contains("FormatEnemyCombatState(enemy)"), "敵の Markdown 行への配線");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 4;"), "schema 4 を維持");
        Console.WriteLine($"敵の戦闘内部状態の契約試験: {Checks} 件成功");
    }
}
