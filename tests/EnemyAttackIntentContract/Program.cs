using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private const string Entity = "TEST_SUBJECT_0";
    private static readonly string[] Keys = ["base_damage", "hits", "resolved_damage"];
    private static Dictionary<string, object?> BuildGameStateCore() => new();
    private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }

    private static Creature Enemy(ICombatState? combat = null) => new() { CombatState = combat ?? new CombatState() };

    private static Dictionary<string, object?> Read(AbstractIntent intent, Creature? enemy = null, int index = 0)
    {
        BeginStateWarnings();
        return BuildEnemyAttackIntent(intent, enemy ?? Enemy(), Entity, index)
            ?? throw new Exception("攻撃の意図には attack を付ける");
    }

    private static string Markdown(Dictionary<string, object?> attack)
        => FormatEnemyAttackIntent(new() { ["attack"] = attack });

    // 敵の筋力 s と、自分の脆弱（×1.5）・敵の脱力（×0.75）。Hook.ModifyDamageInternal の加算 → 倍率の順。
    private static Func<decimal, decimal> Powers(int strength, bool vulnerable = false, bool weak = false)
        => damage => (damage + strength) * (vulnerable ? 1.5m : 1m) * (weak ? 0.75m : 1m);

    private static void Success(Dictionary<string, object?> state, string baseDamage, int hits, int resolved, string message)
    {
        Check(state["readable"] is true && !state.ContainsKey("unavailable_reasons") && _stateWarnings.Count == 0,
            $"{message}: readable=true・理由と警告なし");
        Check(Equals(state["base_damage"], baseDamage) && Equals(state["hits"], hits) && Equals(state["resolved_damage"], resolved),
            $"{message}: base={state["base_damage"]} hits={state["hits"]} resolved={state["resolved_damage"]}");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions));
        Check(json.RootElement.GetProperty("base_damage").GetString() == baseDamage
            && json.RootElement.GetProperty("hits").GetInt32() == hits
            && json.RootElement.GetProperty("resolved_damage").GetInt32() == resolved,
            $"{message}: 本番の wire 設定で素値は文字列、回数・解決値は int");
    }

    private static void Failure(Dictionary<string, object?> state, string[] failed, string reason)
    {
        Check(state["readable"] is false, "一項目でも取得失敗なら readable=false");
        var reasons = (Dictionary<string, string>)state["unavailable_reasons"]!;
        Check(reasons.Count == failed.Length && _stateWarnings.Count == failed.Length, "失敗項目ごとに理由と警告");
        foreach (string key in Keys)
        {
            Check(state.ContainsKey(key), $"{key} を省略しない");
            if (failed.Contains(key))
            {
                Check(state[key] == null && reasons[key].Contains(reason), $"{key} は null と理由（{reasons.GetValueOrDefault(key)}）");
                Check(_stateWarnings.Any(w => w.StartsWith($"battle.enemies.{Entity}.intents.0.attack.{key}:")), "警告は個体・意図・項目を識別");
                Check(Markdown(state).Contains("null (") && Markdown(state).Contains(reason), "Markdown に null と理由");
            }
            else Check(state[key] != null && !reasons.ContainsKey(key), "取得できた隣の項目は保持");
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions));
        foreach (string key in failed)
            Check(json.RootElement.GetProperty(key).ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").TryGetProperty(key, out _), "null と理由が wire に残る");
    }

    public static void Main()
    {
        // 小数点がカンマの文化でも invariant で出す。
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        // 要件の例（cat12079801/sts2#956）: 素 20、筋力 4 → 6。脆弱なら 36 → 39、脱力なら 18 → 19（19.5 は int へ切り捨て）。
        // 値の並びはソースの演算順（StrengthPower 加算 → VulnerablePower / WeakPower 倍率 → GetSingleDamage の (int)）から作った。
        // 実機の観測の代用ではない。
        var intent = new SingleAttackIntent(20);
        foreach (var (strength, vulnerable, weak, resolved) in new[]
        {
            (0, false, false, 20), (4, true, false, 36), (6, true, false, 39), (4, false, true, 18), (6, false, true, 19)
        })
        {
            AttackIntent.Modify = Powers(strength, vulnerable, weak);
            var state = Read(intent);
            Success(state, "20", 1, resolved, $"素 20・筋力 {strength}・脆弱 {vulnerable}・脱力 {weak}");
            Check(Markdown(state) == $" [attack: base 20 | hits 1 | resolved {resolved}]", "Markdown は素値・回数・解決値");
        }
        // 素 21 でも脱力・筋力 4 の解決値は同じ 18。表示からは素値を一意に戻せないので、素値を直接出す。
        AttackIntent.Modify = Powers(4, weak: true);
        Success(Read(new SingleAttackIntent(21)), "21", 1, 18, "素 21・筋力 4・脱力");
        AttackIntent.Modify = Powers(6, weak: true);
        Success(Read(new SingleAttackIntent(21)), "21", 1, 20, "素 21・筋力 6・脱力");

        // decimal の素値はそのまま（丸めない）。delegate は現在の値を毎回読む（滝の巨人の CurrentPressureGunDamage など）。
        decimal live = 12.5m;
        var lambda = new SingleAttackIntent(() => live);
        AttackIntent.Modify = damage => damage;
        Success(Read(lambda), "12.5", 1, 12, "decimal の素値と (int) の解決値");
        live = 25m;
        Success(Read(lambda), "25", 1, 25, "delegate の現在値");

        // 多段: TestSubject の MULTI_CLAW_MOVE は new MultiAttackIntent(MultiClawDamage, () => MultiClawTotalCount)。
        int claws = 3;
        var multi = new MultiAttackIntent(10, () => claws);
        AttackIntent.Modify = Powers(2);
        Success(Read(multi), "10", 3, 12, "多段の回数は Repeats");
        claws = 4;
        Success(Read(multi), "10", 4, 12, "多段の回数は現在の _repeatCalc");
        Success(Read(new MultiAttackIntent(5, 0)), "5", 0, 7, "回数 0 も実値");

        // DeathBlow（GasBomb・WaterfallGiant の EXPLODE_MOVE）は SingleAttackIntent の派生。
        var deathBlow = new DeathBlowIntent(() => 9m);
        Check(deathBlow.IntentType == IntentType.DeathBlow, "DeathBlow の型");
        Success(Read(deathBlow), "9", 1, 11, "DeathBlow にも attack");

        // 攻撃以外には付けない。警告も出さない。
        BeginStateWarnings();
        Check(BuildEnemyAttackIntent(new BuffIntent(), Enemy(), Entity, 1) == null && _stateWarnings.Count == 0, "攻撃以外の意図には付けない");
        Check(FormatEnemyAttackIntent(new() { ["type"] = "Attack" }) == "", "attack の無い意図（旧 MOD）は Markdown に足さない");

        // 読取がゲームの状態を変えない: delegate の参照・ローカルの判定以外の呼び出しをしない。
        var calc = intent.DamageCalc;
        AttackIntent.Modify = Powers(4, vulnerable: true);
        var first = Read(intent);
        Check(ReferenceEquals(calc, intent.DamageCalc), "DamageCalc を差し替えない");
        Check(Equals(Read(intent)["resolved_damage"], first["resolved_damage"]), "繰り返し読んでも同じ値");

        // ローカルのプレイヤーが居ない: GetSingleDamage は補正前を返すので、解決値は null にする。素値・回数は保持。
        AttackIntent.Modify = Powers(4, vulnerable: true);
        LocalContext.Me = null;
        var noLocal = Read(intent);
        Failure(noLocal, ["resolved_damage"], "LocalContext.GetMe が null");
        Check(Equals(noLocal["base_damage"], "20") && Equals(noLocal["hits"], 1), "素値・回数は保持");
        LocalContext.Me = new();
        Failure(Read(intent, new Creature { CombatState = null }), ["resolved_damage"], "Creature.CombatState が null");
        LocalContext.Throw = true;
        Failure(Read(intent), ["resolved_damage"], "Local player not found in combat.");
        LocalContext.Throw = false;

        // DamageCalc が null: 素値・解決値は null。回数は読める。
        var noCalc = new SingleAttackIntent(20) { DamageCalc = null };
        Failure(Read(noCalc), ["base_damage", "resolved_damage"], "AttackIntent.DamageCalc が null です");
        // DamageCalc の例外: 素値・解決値は null（ラベルから補わない）。
        var throwing = new SingleAttackIntent(() => throw new InvalidOperationException("DamageCalc 失敗"));
        Failure(Read(throwing), ["base_damage", "resolved_damage"], "DamageCalc 失敗");
        // Repeats が負・例外: 回数だけ null。
        Failure(Read(new BrokenAttackIntent { DamageCalc = () => 7m }), ["hits"], "AttackIntent.Repeats が負です: -1");
        Failure(Read(new BrokenAttackIntent { DamageCalc = () => 7m, ThrowRepeats = true }), ["hits"], "Repeats 読取失敗");
        Check(Markdown(Read(noCalc)).StartsWith(" [attack: base null (InvalidOperationException: AttackIntent.DamageCalc", StringComparison.Ordinal),
            "Markdown の失敗表示");

        // 前回の snapshot は独立。
        AttackIntent.Modify = Powers(4, vulnerable: true);
        var before = Read(intent);
        AttackIntent.Modify = Powers(6, vulnerable: true);
        Check(Equals(before["resolved_damage"], 36) && Equals(Read(intent)["resolved_damage"], 39), "前回の snapshot は独立");

        // 配線（ソース文字列）: ラベルの try とは独立に、全意図に index を付けて呼ぶ。協力プレイも共通の敵 builder。
        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));
        string builder = Source("McpMod.StateBuilder.cs");
        int start = builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState(Creature creature)", StringComparison.Ordinal);
        string enemyBody = builder[start..builder.IndexOf("return state;", start, StringComparison.Ordinal)];
        int wire = enemyBody.IndexOf("BuildEnemyAttackIntent(intent, creature, entityId, intentIndex++) is { } attack", StringComparison.Ordinal);
        Check(wire > 0 && enemyBody.IndexOf("intentData[\"attack\"] = attack;", StringComparison.Ordinal) > wire, "意図ごとに attack を配線");
        Check(wire < enemyBody.IndexOf("GetIntentLabel", StringComparison.Ordinal), "ラベルの取得（try）より前で、ラベルの失敗に巻き込まれない");
        Check(Source("McpMod.MultiplayerState.cs").Contains("BuildEnemyList(combatState)"), "協力プレイも共通の敵 builder");
        Check(Source("McpMod.Formatting.cs").Contains("{label}{FormatEnemyAttackIntent(i)}{desc}"), "意図の Markdown 行への配線");
        Check(Source("McpMod.cs").Contains("public const int StateSchemaVersion = 8;"), "現行の schema は 8（sts2#1031）");
        Console.WriteLine($"敵の攻撃の意図の契約試験: {Checks} 件成功");
    }
}
