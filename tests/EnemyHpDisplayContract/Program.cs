using System.Text.Json;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Creatures;

STS2_MCP.McpMod.RunContractTests();

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

        public static void RunContractTests()
        {
            BeginStateWarnings();
            Dictionary<string, object?> Enemy(Creature c, string id = "X_0")
            {
                var state = new Dictionary<string, object?> { ["entity_id"] = id, ["alive"] = c.IsAlive, ["hp"] = c.CurrentHp, ["max_hp"] = c.MaxHp };
                AttachEnemyHpDisplay(state, c, id);
                return state;
            }

            // The game's mapping, set here only as the configured answer of the double.
            HpDisplayExtensions.Answer = d => d != HpDisplay.Normal;

            var normal = new Creature();
            HpDisplayExtensions.Calls.Clear();
            var normalState = Enemy(normal);
            Check(normalState["hp_display_is_infinite"] is false && normal.Reads == 1, "Normal は false（getter を 1 回読む）");
            Check(HpDisplayExtensions.Calls.SequenceEqual(new[] { HpDisplay.Normal }), "本体の拡張 IsInfinite に getter の値をそのまま渡す（1 回）");
            Check(!normalState.ContainsKey("unavailable_reasons"), "成功時は理由なし");

            // Hard Shell at its cap: numbers still shown, HP not at any special value.
            var shell = new Creature { CurrentHp = 60, MaxHp = 70, Display = () => HpDisplay.InfiniteWithNumbers };
            var shellState = Enemy(shell, "SKULKING_COLONY_0");
            Check(shellState["hp_display_is_infinite"] is true, "InfiniteWithNumbers（硬い殻の上限到達）は true。HP から作らない");

            // Waterfall Giant's death continuation: 999999999 HP and the infinity symbol.
            var giant = new Creature { CurrentHp = 999999999, MaxHp = 999999999, Display = () => HpDisplay.InfiniteWithoutNumbers };
            var giantState = Enemy(giant, "WATERFALL_GIANT_0");
            Check(giantState["hp_display_is_infinite"] is true, "InfiniteWithoutNumbers（滝の巨人の死亡の継続）は true");

            // Huge HP with a Normal display stays false: never derived from hp / max_hp.
            var big = new Creature { CurrentHp = 999999999, MaxHp = 999999999 };
            Check(Enemy(big)["hp_display_is_infinite"] is false, "HP が巨大でも表示が Normal なら false（HP から作らない）");
            // A dead row is read too (no alive filter).
            var corpse = new Creature { CurrentHp = 0, Display = () => HpDisplay.InfiniteWithNumbers };
            var corpseState = Enemy(corpse);
            Check(corpseState["hp_display_is_infinite"] is true && corpseState["alive"] is false, "死亡個体でも読む（生死で絞らない）");

            // Whatever the extension answers is passed on, even against the enum: the MOD does not compare the enum itself.
            HpDisplayExtensions.Answer = d => d == HpDisplay.Normal;
            Check(Enemy(new Creature())["hp_display_is_infinite"] is true, "拡張の答えをそのまま出す（列挙を自前で比べない）");
            HpDisplayExtensions.Answer = d => d != HpDisplay.Normal;
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            // Getter failure.
            var broken = new Creature { Display = () => throw new NullReferenceException("creature disposed") };
            var failed = new Dictionary<string, object?> { ["unavailable_reasons"] = new Dictionary<string, string> { ["hittable"] = "kept" } };
            AttachEnemyHpDisplay(failed, broken, "WATERFALL_GIANT_0");
            var reasons = (Dictionary<string, string>)failed["unavailable_reasons"]!;
            Check(failed.ContainsKey("hp_display_is_infinite") && failed["hp_display_is_infinite"] == null
                  && reasons["hp_display_is_infinite"].Contains("creature disposed") && reasons["hittable"] == "kept",
                  "読取失敗は null と理由（既存の理由を保つ）");
            Check(_stateWarnings.Any(w => w.StartsWith("battle.enemies.WATERFALL_GIANT_0.hp_display_is_infinite:")), "敵ごとの warning パス");
            // Extension failure (also caught).
            HpDisplayExtensions.Answer = _ => throw new InvalidOperationException("extension failed");
            var failed2 = Enemy(new Creature(), "Y_0");
            Check(failed2["hp_display_is_infinite"] == null
                  && ((Dictionary<string, string>)failed2["unavailable_reasons"]!)["hp_display_is_infinite"].Contains("extension failed"),
                  "拡張の失敗も null と理由");
            HpDisplayExtensions.Answer = d => d != HpDisplay.Normal;

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(new { n = normalState, g = giantState, f = failed }, options)))
            {
                Check(json.RootElement.GetProperty("n").GetProperty("hp_display_is_infinite").ValueKind == JsonValueKind.False, "wire の false");
                Check(json.RootElement.GetProperty("g").GetProperty("hp_display_is_infinite").ValueKind == JsonValueKind.True, "wire の true");
                Check(json.RootElement.GetProperty("f").GetProperty("hp_display_is_infinite").ValueKind == JsonValueKind.Null, "wire で null が残り、false と区別できる");
            }

            Check(FormatEnemyHpDisplay(giantState) == " ∞" && FormatEnemyHpDisplay(shellState) == " ∞", "Markdown は無限表示で ∞");
            Check(FormatEnemyHpDisplay(normalState) == "", "Markdown は false で何も付けない");
            Check(FormatEnemyHpDisplay(failed) == " ∞: null (NullReferenceException: creature disposed)", "Markdown の null と理由");
            Check(FormatEnemyHpDisplay(new Dictionary<string, object?>()) == "", "旧 state（項目なし）は何も補わない");

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string own = File.ReadAllText(Path.Combine(sources, "McpMod.EnemyHpDisplay.cs"));
            Check(own.Contains("state[\"hp_display_is_infinite\"] = creature.HpDisplay.IsInfinite();"), "本体の拡張 HpDisplayExtensions.IsInfinite を呼ぶ");
            Check(!Regex.IsMatch(own, @"HpDisplay\.(Normal|InfiniteWithNumbers|InfiniteWithoutNumbers)|CurrentHp|MaxHp|999999999"), "列挙の値・HP を自前で比べない");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string enemy = builder[builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState", StringComparison.Ordinal)..];
            enemy = enemy[..enemy.IndexOf("return state;", StringComparison.Ordinal)];
            int attach = enemy.IndexOf("AttachEnemyHpDisplay(state, creature, entityId);", StringComparison.Ordinal);
            Check(attach > 0 && attach < enemy.IndexOf("if (!alive)", StringComparison.Ordinal), "生死で分けずに全ての敵の行に付ける");
            string list = builder[builder.IndexOf("private static List<Dictionary<string, object?>> BuildEnemyList", StringComparison.Ordinal)..];
            Check(list[..list.IndexOf("return enemies;", StringComparison.Ordinal)].Contains("enemies.Add(BuildEnemyState(creature))"), "列挙される全ての敵が同じ builder を通る");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("battle[\"enemies\"] = BuildEnemyList(combatState)"), "マルチも同じ");
            Check(Regex.Matches(builder + File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")), @"AttachEnemyHpDisplay\(").Count == 1, "プレイヤー・味方の行には付けない（呼ぶのは敵の builder だけ）");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("HP: {enemy[\"hp\"]}/{enemy[\"max_hp\"]}{FormatEnemyHpDisplay(enemy)} | Block"), "Markdown の HP の数字の直後に配線");

            // The state inventory explains the member (it was cosmetic before 1.7.0).
            using (var cls = JsonDocument.Parse(File.ReadAllText(Path.Combine(sources, "state-inventory-classification.json"))))
            {
                var entry = cls.RootElement.GetProperty("Creature.HpDisplay");
                Check(entry.GetProperty("class").GetString() == "exposed", "棚卸しの Creature.HpDisplay は exposed（cosmetic でない）");
                string note = entry.GetProperty("note").GetString() ?? "";
                Check(note.Contains("HellraiserPower") && note.Contains("hp_display_is_infinite") && note.Contains("cosmetic"), "備考に効果で読む箇所・出し方・見落としの理由");
            }

            string mod = File.ReadAllText(Path.Combine(sources, "McpMod.cs"));
            Check(mod.Contains("public const int StateSchemaVersion = 12;"), "現行の schema_version は 12 のまま");
            Check(Regex.IsMatch(mod, @"public const string StateSchema = ""2\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";"), "published schema MAJOR is 2 (the exact version is checked in V0113CompatContract)");
            Check(mod.Contains("1.7.0: battle.enemies[].hp_display_is_infinite"), "1.7.0 の履歴");
            Console.WriteLine($"EnemyHpDisplayContract: {Checks} checks passed");
        }
    }
}
