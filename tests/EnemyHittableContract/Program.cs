using System.Text.Json;
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
                var state = new Dictionary<string, object?> { ["entity_id"] = id, ["alive"] = c.IsAlive, ["hp"] = c.CurrentHp };
                AttachEnemyHittable(state, c, id);
                return state;
            }

            var normal = new Creature();
            var alive = Enemy(normal);
            Check(alive["hittable"] is true && normal.Reads == 1 && !alive.ContainsKey("unavailable_reasons"), "本体の IsHittable を 1 回読み、true をそのまま出す");

            // Alive but the game says not hittable (e.g. a reviving power's ShouldAllowHitting).
            var reviving = new Creature { Hittable = () => false };
            Check(Enemy(reviving)["hittable"] is false, "生存でも本体が false なら false（alive・HP から作らない）");
            // Dead but the getter is what decides: no derivation from hp/alive.
            var corpse = new Creature { CurrentHp = 0, Hittable = () => true };
            var corpseState = Enemy(corpse);
            Check(corpseState["hittable"] is true && corpseState["alive"] is false, "死亡個体でも読む（生死で絞らない・HP から作らない）");
            var deadFalse = new Creature { CurrentHp = 0, Hittable = () => false };
            Check(Enemy(deadFalse)["hittable"] is false, "死亡個体の本体の false");
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            var broken = new Creature { Hittable = () => throw new NullReferenceException("CombatState missing") };
            var failed = new Dictionary<string, object?> { ["unavailable_reasons"] = new Dictionary<string, string> { ["other"] = "kept" } };
            AttachEnemyHittable(failed, broken, "SEGMENT_1");
            var reasons = (Dictionary<string, string>)failed["unavailable_reasons"]!;
            Check(failed.ContainsKey("hittable") && failed["hittable"] == null && reasons["hittable"].Contains("CombatState missing") && reasons["other"] == "kept", "読取失敗は null と理由（既存の理由を保つ）");
            Check(_stateWarnings.Any(w => w.StartsWith("battle.enemies.SEGMENT_1.hittable:")), "敵ごとの warning パス");

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(new { a = alive, r = Enemy(reviving), f = failed }, options)))
            {
                Check(json.RootElement.GetProperty("a").GetProperty("hittable").ValueKind == JsonValueKind.True, "wire の true");
                Check(json.RootElement.GetProperty("r").GetProperty("hittable").ValueKind == JsonValueKind.False, "wire の false");
                Check(json.RootElement.GetProperty("f").GetProperty("hittable").ValueKind == JsonValueKind.Null, "wire で null が残り、false と区別できる");
            }

            Check(FormatEnemyHittable(alive) == " | Hittable: yes" && FormatEnemyHittable(Enemy(reviving)) == " | Hittable: no", "Markdown の yes / no");
            Check(FormatEnemyHittable(failed).Contains("Hittable: null (NullReferenceException: CombatState missing)"), "Markdown の null と理由");
            Check(FormatEnemyHittable(new Dictionary<string, object?>()) == "", "旧 state（項目なし）は何も補わない");

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string enemy = builder[builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState", StringComparison.Ordinal)..];
            enemy = enemy[..enemy.IndexOf("return state;", StringComparison.Ordinal)];
            int attach = enemy.IndexOf("AttachEnemyHittable(state, creature, entityId);", StringComparison.Ordinal);
            Check(attach > 0 && attach < enemy.IndexOf("if (!alive)", StringComparison.Ordinal), "生死で分けずに全ての敵の行に付ける");
            string list = builder[builder.IndexOf("private static List<Dictionary<string, object?>> BuildEnemyList", StringComparison.Ordinal)..];
            Check(list[..list.IndexOf("return enemies;", StringComparison.Ordinal)].Contains("enemies.Add(BuildEnemyState(creature))"), "列挙される全ての敵（復活待ちの死亡個体を含む）が同じ builder を通る");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("battle[\"enemies\"] = BuildEnemyList(combatState)"), "マルチも同じ");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.Formatting.cs")).Contains("{FormatEnemyHittable(enemy)}"), "Markdown の HP 行に配線");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 7;"), "現行の schema は 7（sts2#1019）");
            Console.WriteLine($"EnemyHittableContract: {Checks} checks passed");
        }
    }
}
