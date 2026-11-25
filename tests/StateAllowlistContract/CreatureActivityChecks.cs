using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void RunCreatureActivityContract()
    {
        const string key = "creature_activity";
        BeginStateWarnings();
        Dictionary<string, object?> Battle(ICombatState? combat)
        {
            if (combat != null) RefreshEntityIdRegistry(combat);
            var battle = new Dictionary<string, object?>();
            AttachCreatureActivity(battle, combat);
            return battle;
        }
        List<Dictionary<string, object?>> Rows(ICombatState combat) =>
            (List<Dictionary<string, object?>>)Battle(combat)[key]!;

        var self = new Creature { CombatId = 0 };
        self.Player = new Player(self) { NetId = 123 };
        var teammate = new Creature { CombatId = 1 };
        teammate.Player = new Player(teammate) { NetId = 456 };
        var pet = new Creature
        {
            CombatId = 2, PetOwner = self.Player, Monster = new MonsterModel("WUPPY"), Attacking = true
        };
        var enemy = new Creature
        {
            CombatId = 3, IsEnemy = true, IsDead = true,
            Monster = new MonsterModel("NIBBIT") { PerformingMove = true }
        };
        var combat = new CombatState { Enemies = [enemy, enemy] };
        combat.SetAllies(self, teammate, pet);

        var rows = Rows(combat);
        Check(rows.Count == 4 && rows.All(row => Reasons(row) == null), "戦闘に残る個体ごとに 1 行");
        Check(Equals(rows[0]["kind"], "player") && Equals(rows[0]["player_id"], "123")
            && Equals(rows[1]["player_id"], "456"), "本人と他のプレイヤーを player_id で照合できる");
        Check(Equals(rows[0]["combat_id"], 0u) && Equals(rows[0]["is_attacking"], false)
            && !rows[0].ContainsKey("is_performing_move"), "ID 0、false、非 Monster の適用外");
        Check(Equals(rows[2]["kind"], "pet") && Equals(rows[2]["owner_player_id"], "123")
            && Equals(rows[2]["is_attacking"], true) && Equals(rows[2]["is_performing_move"], false),
            "ペットの所有者と両値");
        Check(Equals(rows[3]["kind"], "enemy") && Equals(rows[3]["entity_id"], GetStableEntityId(enemy))
            && Equals(rows[3]["is_attacking"], false) && Equals(rows[3]["is_performing_move"], true),
            "死亡処理を保留中の敵も既存の entity_id と両値を持つ");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rows));
        Check(json.RootElement[0].GetProperty("combat_id").ValueKind == JsonValueKind.Number
            && json.RootElement[2].GetProperty("is_attacking").ValueKind == JsonValueKind.True
            && json.RootElement[2].GetProperty("is_performing_move").ValueKind == JsonValueKind.False,
            "JSON でも整数と真偽");

        self.Attacking = true;
        Check(Equals(Rows(combat)[0]["is_attacking"], true), "本人の攻撃開始が反映される");
        self.Attacking = false;
        Check(Equals(Rows(combat)[0]["is_attacking"], false), "本人の攻撃終了が反映される");

        self.AttackUnreadable = true;
        enemy.Monster!.MoveUnreadable = true;
        teammate.CombatId = pet.CombatId;
        enemy.CombatId = null;
        rows = Rows(combat);
        Check(rows[0]["is_attacking"] == null && Reasons(rows[0])!["is_attacking"].Contains("attack unavailable")
            && Equals(rows[0]["combat_id"], 0u), "攻撃の読取失敗はその値だけ null と理由");
        Check(rows[1]["combat_id"] == null && rows[2]["combat_id"] == null
            && Reasons(rows[1])!["combat_id"].Contains("重複") && Reasons(rows[2])!["combat_id"].Contains("重複")
            && Equals(rows[2]["is_attacking"], true), "重複 ID は両方を欠測にし、活動の実値を保持");
        Check(rows[3]["combat_id"] == null && Reasons(rows[3])!.ContainsKey("combat_id")
            && rows[3]["entity_id"] == null && Reasons(rows[3])!.ContainsKey("entity_id")
            && rows[3]["is_performing_move"] == null && Reasons(rows[3])!["is_performing_move"].Contains("move unavailable")
            && Equals(rows[3]["is_attacking"], false), "ID の欠落と行動の読取失敗は項目別に記録");
        Check(_stateWarnings.Any(w => w.StartsWith("battle.creature_activity.0.is_attacking:"))
            && _stateWarnings.Any(w => w.StartsWith("battle.creature_activity.3.is_performing_move:")),
            "警告に個体の添字と項目名を載せる");

        Check(Rows(new CombatState()).Count == 0, "読めた空の個体列は空配列");
        combat.CreaturesUnreadable = true;
        var failedBattle = Battle(combat);
        Check(failedBattle[key] == null && Reasons(failedBattle)![key].Contains("roster unavailable")
            && _stateWarnings.Any(w => w.StartsWith("battle.creature_activity:")),
            "個体列の読取失敗は列ごと null と理由と警告");
        var missingBattle = Battle(null);
        Check(missingBattle[key] == null && Reasons(missingBattle)!.ContainsKey(key), "戦闘 state の欠落も欠測");
    }
}
