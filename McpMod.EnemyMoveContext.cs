using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    internal static readonly string[] EnemyMoveContextKeys =
    [
        "game_assembly_sha256", "game_mode", "is_dread", "ascension_levels", "combat_player_count",
        "has_encounter", "encounter_id", "encounter_slots"
    ];

    // battle.enemy_move_context（schema 6、sts2#1018）。敵の行動表（ソースから生成）を評価するための戦闘全体の入力。
    // v0.113.1（sts2-a671e9ef）:
    // - AscensionHelper.GetValueIfAscension / IsDread は RunManager.HasAscension（AscensionManager.HasLevel）と
    //   RunManager.IsDread（State.GameMode == Dread）を読む。どちらも IsInProgress が false なら「無し」を返すので、
    //   そのときは読まず理由付きの null にする（Ascension の数値から推測しない。Blitz は段階の対応が別）。
    // - 召喚の空きスロットは EncounterModel.Slots と CombatState.Enemies の Creature.SlotName で決まる
    //   （GetNextSlot は先頭、Ovicopter.LayEggsMove・TwoTailedRat.CallForBackup は末尾の空き）。
    //   Enemies は戦闘に残る死亡個体を含むので、occupant_ids も battle.enemies の絞り込みでなく Enemies 全体から作る。
    // getter・列の読み取りだけで、召喚・抽選・初期化はしない。
    private static Dictionary<string, object?> BuildEnemyMoveContext(ICombatState combatState)
    {
        var state = new Dictionary<string, object?>();
        const string context = "battle.enemy_move_context";
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key, read, context);
        RunManager InProgressRun(string what)
        {
            var run = RunManager.Instance;
            return run.IsInProgress ? run
                : throw new InvalidOperationException($"RunManager is not in progress; {what} would ignore ascension and game mode");
        }

        // 行動表の版との照合用（本体の DLL 全体の SHA-256。generation_pool と同じ値）。
        Read("game_assembly_sha256", () => GenerationAssemblySha256.Value);
        Read("game_mode", () => (combatState.RunState ?? throw new InvalidOperationException("RunState がありません")).GameMode.ToString());
        Read("is_dread", () => InProgressRun("IsDread").IsDread());
        Read("ascension_levels", () =>
        {
            var run = InProgressRun("HasAscension");
            return Enum.GetValues<AscensionLevel>()
                .Where(level => level != AscensionLevel.None && run.HasAscension(level))
                .Select(level => level.ToString())
                .ToList();
        });
        // KnowledgeDemon.PonderMove・WaterfallGiant.SiphonMove などが掛ける人数（CombatState.Players.Count）。
        Read("combat_player_count", () => (combatState.Players ?? throw new InvalidOperationException("Players がありません")).Count);
        EncounterModel? encounter = null;
        bool encounterRead = false;
        try
        {
            encounter = combatState.Encounter;
            state["has_encounter"] = encounter != null;
            encounterRead = true;
        }
        catch (Exception ex)
        {
            state["has_encounter"] = null;
            RecordInternalStateFailure(state, "has_encounter", ex, context);
        }
        if (!encounterRead)
        {
            state["encounter_id"] = null;
            state["encounter_slots"] = null;
            var cause = new InvalidOperationException("Encounter が読めません");
            RecordInternalStateFailure(state, "encounter_id", cause, context);
            RecordInternalStateFailure(state, "encounter_slots", cause, context);
        }
        else if (encounter == null)
        {
            // Encounter の無い戦闘（has_encounter=false）。CanSummon・LivingFog は空きスロット無しとして扱う。
            state["encounter_id"] = null;
            state["encounter_slots"] = new List<Dictionary<string, object?>>();
        }
        else
        {
            Read("encounter_id", () => encounter.Id.Entry);
            Read("encounter_slots", () =>
            {
                // TheKinBoss.Slots などの getter は IsDread を読む。
                InProgressRun("EncounterModel.Slots");
                var slots = encounter.Slots ?? throw new InvalidOperationException("Encounter.Slots が null です");
                var enemies = (combatState.Enemies ?? throw new InvalidOperationException("Enemies がありません")).ToArray();
                return slots.Select(slot => new Dictionary<string, object?>
                {
                    ["name"] = slot ?? throw new InvalidOperationException("Encounter.Slots に null があります"),
                    ["occupant_ids"] = enemies.Where(c => c.SlotName == slot).Select(GetStableEntityId).ToList()
                }).ToList();
            });
        }
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        state["readable"] = EnemyMoveContextKeys.All(key => state.ContainsKey(key) && reasons?.ContainsKey(key) != true);
        return state;
    }

    // battle.enemies[].slot_name: Creature.SlotName（auto-property）。null は「スロット無し」の実値で、
    // 読取失敗は null と行の unavailable_reasons.slot_name・warnings。
    private static void AttachEnemySlotName(Dictionary<string, object?> state, Creature creature, string entityId)
        => ReadInternalStateValue(state, "slot_name", () => creature.SlotName, $"battle.enemies.{entityId}");

    // " | Slot: <name>"（スロット無しは none、読取失敗は null (理由)）。旧 snapshot の欠落は何も出さない。
    private static string FormatEnemySlotName(Dictionary<string, object?> enemy)
    {
        if (!enemy.TryGetValue("slot_name", out var value)) return "";
        if (value is string slot) return $" | Slot: {slot}";
        string? reason = (enemy.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>)?.GetValueOrDefault("slot_name");
        return reason != null ? $" | Slot: null ({Flatten(reason)})" : " | Slot: none";
    }

    // Markdown: 戦闘の見出しの下に 1 行。スロットは「名前=占有個体」。
    private static string FormatEnemyMoveContext(Dictionary<string, object?> battle)
    {
        if (battle.GetValueOrDefault("enemy_move_context") is not Dictionary<string, object?> ctx)
            return "";
        var reasons = ctx.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Text(string key, Func<object, string> format)
            => ctx.GetValueOrDefault(key) is { } value ? format(value)
                : reasons?.GetValueOrDefault(key) is { } reason ? $"null ({Flatten(reason)})" : "none";
        string asc = Text("ascension_levels", v => v is IEnumerable<string> levels && levels.Any() ? string.Join(",", levels) : "none");
        string slots = Text("encounter_slots", v => v is IEnumerable<Dictionary<string, object?>> rows && rows.Any()
            ? string.Join(", ", rows.Select(r => $"{r.GetValueOrDefault("name")}={(r.GetValueOrDefault("occupant_ids") is IEnumerable<string> ids && ids.Any() ? string.Join("+", ids) : "empty")}"))
            : "none");
        return $"**Enemy move context:** mode {Text("game_mode", v => v.ToString() ?? "?")}"
            + $" | dread {Text("is_dread", v => v is true ? "yes" : "no")} | ascension {asc}"
            + $" | encounter {Text("encounter_id", v => v.ToString() ?? "?")} | slots {slots}";
    }
}
