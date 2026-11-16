using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CheckManualCardPlayHistory()
    {
        const string key = "any_manual_cards_played_this_turn";
        var combat = new CombatState();
        var owner = new Player { Creature = new Creature { CombatState = combat } };
        var other = new Player { Creature = new Creature { CombatState = combat } };
        var history = new CombatHistory();
        CombatManager.Instance.Value = history;
        var manual = new CardPlay(new CardModel(CardType.Skill), owner);
        var auto = new CardPlay(new CardModel(CardType.Attack), owner) { AutoPlay = true };
        var otherPlay = new CardPlay(new CardModel(CardType.Power), other);

        T Current<T>(T entry, bool current = true) where T : CombatHistoryEntry
        {
            entry.ThisTurn = state => ReferenceEquals(state, combat)
                ? current : throw new Exception("所有者の CombatState を渡すこと");
            history.Items.Add(entry);
            return entry;
        }

        BeginStateWarnings();
        var empty = BuildCombatHistoryState(owner);
        Check(empty[key] is false && !Reasons(empty).ContainsKey(key), "履歴が空なら手動プレイは既知の false");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(empty, WireOptions)))
            Check(json.RootElement.GetProperty(key).ValueKind == JsonValueKind.False, "wire に bool の false が残る");
        Check(Markdown(empty).Contains("manual card played=no"), "Markdown に既知の false を表示する");
        var legacy = new Dictionary<string, object?> { ["turn_number"] = 1 };
        Check(Markdown(legacy) == "", "手動プレイ項目がない旧形式に no を補わない");

        Current(new CardPlayStartedEntry(manual) { Actor = owner.Creature });
        Check(BuildCombatHistoryState(owner)[key] is false, "開始だけで完了前の札は含めない");
        Current(new CardPlayFinishedEntry(auto));
        Current(new CardPlayFinishedEntry(otherPlay));
        Current(new CardPlayFinishedEntry(manual) { Actor = null });
        Current(new CardPlayFinishedEntry(manual), false);
        Current(new CardPlayFinishedEntry(manual) { Actor = other.Creature });
        var excluded = BuildCombatHistoryState(owner);
        Check(excluded[key] is false && Equals(Plays(excluded, "finished")["total"], 3), "自動・他 Actor・null Actor・別手番を除き、既存完了数は保持する");

        // Actor は履歴に記録済みの値。CardPlay.Player と食い違う境界も検査する。
        var recordedOwner = Current(new CardPlayFinishedEntry(otherPlay) { Actor = owner.Creature });
        var entries = history.Items.ToArray();
        var played = BuildCombatHistoryState(owner);
        Check(played[key] is true && Reasons(played).Count == 0 && _stateWarnings.Count == 0, "Actor が所有者なら Player が別でも手動完了を検出する");
        Check(history.Items.SequenceEqual(entries) && recordedOwner.Actor == owner.Creature && !otherPlay.IsAutoPlay, "再読取で履歴とプレイ情報を変更しない");
        Check(Markdown(played).Contains("manual card played=yes"), "Markdown に true を表示する");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(played, WireOptions)))
            Check(json.RootElement.GetProperty(key).ValueKind == JsonValueKind.True, "wire に bool の true を出す");
        var unreadLater = Current(new CardPlayFinishedEntry(manual));
        unreadLater.ThisTurn = _ => throw new MissingMethodException("短絡後は参照しない手番判定");
        Check(BuildCombatHistoryState(owner)[key] is true, "本体の Any と同じく最初の一致で true を確定する");
        history.Items.Clear();
        Check(BuildCombatHistoryState(owner)[key] is false && played[key] is true, "履歴リセットを再読取し、前の snapshot を保持する");

        foreach (var type in Enum.GetValues<CardType>())
        {
            history.Items.Clear();
            var play = new CardPlay(new CardModel(type), owner);
            Current(new CardPlayFinishedEntry(play));
            Current(new CardPlayFinishedEntry(play));
            Check(BuildCombatHistoryState(owner)[key] is true, $"{type} の手動完了とリプレイを種別条件なしで検出する");
        }

        void Unknown(Dictionary<string, object?> state, string reason)
        {
            Check(state[key] == null && Reasons(state)[key].Contains(reason), "取得失敗は null とキー別理由を出す");
            Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{key}:")), "取得失敗は項目パス付き warning を出す");
            Check(Equals(state["turn_number"], 1), "失敗しても独立した PlayerCombatState の値を保持する");
            Check(Markdown(state).Contains($"{key}=null (") && Markdown(state).Contains(reason), "Markdown に null と理由を出す");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, WireOptions));
            Check(json.RootElement.GetProperty(key).ValueKind == JsonValueKind.Null
                && json.RootElement.GetProperty("unavailable_reasons").GetProperty(key).GetString()!.Contains(reason), "wire に null と理由を保持する");
        }

        history.Items.Clear();
        BeginStateWarnings();
        var badTurn = Current(new CardPlayFinishedEntry(manual));
        badTurn.ThisTurn = _ => throw new MissingMethodException("手番判定失敗");
        Unknown(BuildCombatHistoryState(owner), "手番判定失敗");
        history.Items.Clear();
        BeginStateWarnings();
        manual.ThrowAutoPlay = true;
        Current(new CardPlayFinishedEntry(manual));
        Unknown(BuildCombatHistoryState(owner), "IsAutoPlay renamed");
        manual.ThrowAutoPlay = false;
        BeginStateWarnings();
        history.ThrowFinished = true;
        Unknown(BuildCombatHistoryState(owner), "CardPlaysFinished renamed");
        history.ThrowFinished = false;
        BeginStateWarnings();
        CombatManager.Instance.ThrowHistory = true;
        Unknown(BuildCombatHistoryState(owner), "History renamed");
        CombatManager.Instance.ThrowHistory = false;
        BeginStateWarnings();
        owner.Creature.CombatState = null;
        var missingCombat = BuildCombatHistoryState(owner);
        Check(missingCombat[key] == null && Reasons(missingCombat)[key].Contains("Owner combat state is unavailable"), "CombatState 欠落を false と解釈しない");
        Check(_stateWarnings.Any(w => w.StartsWith($"player.combat_history.{key}:")), "CombatState 欠落時にも warning を出す");
        owner.Creature.CombatState = combat;
        BeginStateWarnings();
        var recovered = BuildCombatHistoryState(owner);
        Check(recovered[key] is true && Reasons(recovered).Count == 0 && _stateWarnings.Count == 0, "読取元の復旧後に値と warning が正常に戻る");
    }
}
