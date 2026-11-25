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
                var state = new Dictionary<string, object?> { ["entity_id"] = id, ["alive"] = c.IsAlive, ["hp"] = c.CurrentHp };
                AttachEnemyCanReceivePowers(state, c, id);
                return state;
            }

            // A living teammate the game lets receive powers (Queen's Burn Bright for Me gives it Strength).
            var amalgam = new Creature();
            var alive = Enemy(amalgam, "TORCH_HEAD_AMALGAM_0");
            Check(alive["can_receive_powers"] is true && amalgam.Reads == 1 && !alive.ContainsKey("unavailable_reasons"),
                "本体の CanReceivePowers を 1 回読み、true をそのまま出す");
            Check(amalgam.HittableReads == 0, "IsHittable を読まない（hittable から作らない）");

            // Alive but the game refuses (a listener's ShouldAllowHitting is false).
            var refused = new Creature { Receives = () => false };
            Check(Enemy(refused)["can_receive_powers"] is false, "生存でも本体が false なら false（alive・HP から作らない）");

            // A corpse kept in the fight: hittable is always false there, CanReceivePowers is the listeners' answer.
            var corpse = new Creature { CurrentHp = 0, Hittable = () => false, Receives = () => true };
            var corpseState = Enemy(corpse, "SEGMENT_2");
            Check(corpseState["can_receive_powers"] is true && corpseState["alive"] is false,
                "死亡個体でも読み、hittable が false でも本体が true なら true（生死・hittable で絞らない）");
            var reviving = new Creature { CurrentHp = 0, Receives = () => false };
            Check(Enemy(reviving)["can_receive_powers"] is false, "復活中の死亡個体の本体の false");
            // Alive, hittable false but the getter says true: still the getter's value.
            var oddAlive = new Creature { Hittable = () => false, Receives = () => true };
            Check(Enemy(oddAlive)["can_receive_powers"] is true, "hittable と食い違っても本体の値");
            Check(_stateWarnings.Count == 0, "成功時は warning なし");

            var broken = new Creature { Receives = () => throw new NullReferenceException("listener disposed") };
            var failed = new Dictionary<string, object?> { ["unavailable_reasons"] = new Dictionary<string, string> { ["hittable"] = "kept" } };
            AttachEnemyCanReceivePowers(failed, broken, "QUEEN_0");
            var reasons = (Dictionary<string, string>)failed["unavailable_reasons"]!;
            Check(failed.ContainsKey("can_receive_powers") && failed["can_receive_powers"] == null
                  && reasons["can_receive_powers"] == "NullReferenceException: listener disposed" && reasons["hittable"] == "kept",
                "読取失敗は null と理由（既存の理由を保つ）");
            Check(_stateWarnings.Any(w => w.StartsWith("battle.enemies.QUEEN_0.can_receive_powers:")), "敵ごとの warning パス");
            var fresh = new Dictionary<string, object?>();
            AttachEnemyCanReceivePowers(fresh, broken, "QUEEN_0");
            Check(fresh["unavailable_reasons"] is Dictionary<string, string> made && made.ContainsKey("can_receive_powers"),
                "理由の辞書が無ければ作る");

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(new { a = alive, r = Enemy(refused), f = failed }, options)))
            {
                Check(json.RootElement.GetProperty("a").GetProperty("can_receive_powers").ValueKind == JsonValueKind.True, "wire の true");
                Check(json.RootElement.GetProperty("r").GetProperty("can_receive_powers").ValueKind == JsonValueKind.False, "wire の false");
                Check(json.RootElement.GetProperty("f").GetProperty("can_receive_powers").ValueKind == JsonValueKind.Null, "wire で null が残り、false と区別できる");
            }

            Check(FormatEnemyCanReceivePowers(alive) == " | Receives powers: yes" && FormatEnemyCanReceivePowers(Enemy(refused)) == " | Receives powers: no",
                "Markdown の yes / no");
            Check(FormatEnemyCanReceivePowers(failed) == " | Receives powers: null (NullReferenceException: listener disposed)", "Markdown の null と理由");
            Check(FormatEnemyCanReceivePowers(new Dictionary<string, object?>()) == "", "旧 state（項目なし）は何も補わない");

            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Read(string name) => File.ReadAllText(Path.Combine(sources, name));
            string builder = Read("McpMod.StateBuilder.cs");
            string enemy = builder[builder.IndexOf("private static Dictionary<string, object?> BuildEnemyState", StringComparison.Ordinal)..];
            enemy = enemy[..enemy.IndexOf("return state;", StringComparison.Ordinal)];
            int attach = enemy.IndexOf("AttachEnemyCanReceivePowers(state, creature, entityId);", StringComparison.Ordinal);
            Check(attach > 0 && attach < enemy.IndexOf("if (!alive)", StringComparison.Ordinal), "生死で分けずに全ての敵の行に付ける");
            Check(!Regex.IsMatch(enemy[..attach], @"\bif\s*\("), "敵の種類・戦闘（クイーンの有無）で条件を付けない");
            string list = builder[builder.IndexOf("private static List<Dictionary<string, object?>> BuildEnemyList", StringComparison.Ordinal)..];
            Check(list[..list.IndexOf("return enemies;", StringComparison.Ordinal)].Contains("enemies.Add(BuildEnemyState(creature))"),
                "列挙される全ての敵（復活待ちの死亡個体を含む）が同じ builder を通る");
            string multiplayer = Read("McpMod.MultiplayerState.cs");
            Check(multiplayer.Contains("battle[\"enemies\"] = BuildEnemyList(combatState)"), "マルチも同じ");
            Check(Regex.Matches(builder + multiplayer, @"AttachEnemyCanReceivePowers\(").Count == 1, "プレイヤー・味方の行には付けない（呼ぶのは敵の builder だけ）");
            Check(Read("McpMod.Formatting.cs").Contains("{FormatEnemyHittable(enemy)}{FormatEnemyCanReceivePowers(enemy)}"), "Markdown の HP 行の Hittable の後に配線");

            // The inventories record where the hook's answer is exposed (classification fix, not a one-off field).
            using (var events = JsonDocument.Parse(Read("event-inventory-classification.json")))
            {
                var item = events.RootElement.GetProperty("items").GetProperty("computed:Hook.ShouldAllowHitting");
                Check(item.GetProperty("class").GetString() == "exposed", "出来事の棚卸しで ShouldAllowHitting は exposed");
                var cites = item.GetProperty("exposed_by").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                Check(cites.Any(c => c.StartsWith("McpMod.EnemyCanReceivePowers.cs: ") && c.Contains("creature.CanReceivePowers"))
                      && cites.Any(c => c.StartsWith("McpMod.EnemyHittable.cs: ")), "exposed_by に hittable と can_receive_powers の両方");
                Check(item.GetProperty("proposed_key").GetString()!.Contains("enemies[].can_receive_powers"), "キーに can_receive_powers");
                Check(item.GetProperty("reason").GetString()!.Contains("PowerCmd.Apply"), "見落としの理由（PowerCmd.Apply が読む getter）を備考に");
            }
            using (var stateInv = JsonDocument.Parse(Read("state-inventory-classification.json")))
            {
                string note = stateInv.RootElement.GetProperty("Creature.CombatState").GetProperty("note").GetString()!;
                Check(note.Contains("CanReceivePowers") && note.Contains("computed:Hook.ShouldAllowHitting"), "状態の棚卸しの Creature.CombatState の備考が出した先を指す");
            }

            string mod = Read("McpMod.cs");
            Check(mod.Contains("public const int StateSchemaVersion = 12;"), "整数の schema_version は 12 のまま");
            Check(mod.Contains("/// 1.11.0: battle.enemies[].can_receive_powers"), "追加は MINOR（1.11.0）の履歴");
            Check(Regex.IsMatch(mod, @"public const string StateSchema = ""2\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"";"),
                "published schema MAJOR is 2 (the exact version is checked in V0113CompatContract)");
            Console.WriteLine($"EnemyCanReceivePowersContract: {Checks} checks passed");
        }
    }
}
