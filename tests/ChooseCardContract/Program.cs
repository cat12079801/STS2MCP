using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

STS2_MCP.McpMod.RunContractTests();

// Listener doubles. Each override throws: a state read must never invoke a hook.
public sealed class GalvanicLike(Creature owner) : PowerModel("GALVANIC_POWER", owner)
{ public override Task AfterCardEnteredCombat(CardModel card) => throw new Exception("hook invoked"); }
public sealed class FertilizerLike(Player owner) : RelicModel("FERTILIZER", owner)
{ public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => throw new Exception("hook invoked"); }
public sealed class BeltBuckleLike(Player owner) : RelicModel("BELT_BUCKLE", owner)
{ public override Task AfterPotionUsed(PotionModel potion, Creature? target) => throw new Exception("hook invoked"); }
public sealed class OverloadOnly(Player owner) : RelicModel("OVERLOAD_ONLY", owner)
{ public override Task AfterPotionUsed(PotionModel potion) => throw new Exception("hook invoked"); }
public sealed class TopLike(Player owner) : RelicModel("UNCEASING_TOP", owner)
{ public override Task AfterHandEmptied(PlayerChoiceContext c, Player p) => throw new Exception("hook invoked"); }
public sealed class StompLike(Player owner) : CardModel("STOMP", owner)
{ public override Task AfterCardEnteredCombat(CardModel card) => throw new Exception("hook invoked"); }
public sealed class HexedLike(CardModel card) : AfflictionModel("HEXED", card)
{ public override Task AfterCardEnteredCombat(CardModel c) => throw new Exception("hook invoked"); }
public sealed class DeckEnchantLike(CardModel card) : EnchantmentModel("DECK_ENCHANT", card)
{ public override Task AfterPotionUsed(PotionModel potion, Creature? target) => throw new Exception("hook invoked"); }
public sealed class SmokestackLike(Creature owner) : PowerModel("SMOKESTACK_POWER", owner)
{ public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => throw new Exception("hook invoked"); }
public sealed class StrangeModel() : AbstractModel("STRANGE")
{ public override Task AfterCardEnteredCombat(CardModel card) => throw new Exception("hook invoked"); }

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static readonly List<string> Calls = new();
        private static readonly Dictionary<CardModel, string> Uids = new(ReferenceEqualityComparer.Instance);
        private static int UidRefreshes, EntityRefreshes;
        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }
        // Existing serializers are boundaries (their contracts have their own tests):
        // record the call order the choose builder uses.
        private static Dictionary<string, object?> BuildCardInfo(CardModel card, bool includeType = true)
        {
            Calls.Add($"info:{card.Id.Entry}:{includeType}");
            var row = new Dictionary<string, object?> { ["id"] = card.Id.Entry, ["name"] = card.Id.Entry, ["cost"] = "2", ["rarity"] = "Rare", ["description"] = "text" };
            if (includeType) row["type"] = "Attack";
            return row;
        }
        private static void AttachCardDetails(Dictionary<string, object?> state, CardModel card)
        {
            Calls.Add($"details:{card.Id.Entry}");
            state["unavailable_reasons"] = new Dictionary<string, string>();
            state["type"] = "Attack";
        }
        private static void AttachCardPlayability(Dictionary<string, object?> state, CardModel card)
        {
            Calls.Add($"playability:{card.Id.Entry}");
            Check(state["unavailable_reasons"] is Dictionary<string, string>, "可否は details の理由辞書の後に読む");
            state["can_play"] = false;
            state["unplayable_reason"] = "EnergyCostTooHigh";
        }
        private static Dictionary<string, object?> BuildCardEnergyCostState(CardModel card)
        {
            Calls.Add($"energy:{card.Id.Entry}");
            return new() { ["base"] = 2, ["with_all_modifiers"] = 2, ["local_modifiers"] = new List<object>() };
        }
        private static void RefreshCardUidRegistry(ICombatState state) => UidRefreshes++;
        private static string GetStableCardUid(CardModel card)
        {
            if (!Uids.TryGetValue(card, out var uid)) Uids[card] = uid = $"{card.Id.Entry}#g{Uids.Count}";
            return uid;
        }
        private static bool? SafeGetBool(Func<bool> read) => read();
        // Display order boundary (the real one sorts by Rarity, CompareTo, uid): any order
        // that does not depend on the hidden pile order.
        private static IEnumerable<CardModel> OrderPileCardsForDisplay(IEnumerable<CardModel> cards, PileType pile)
            => cards.OrderBy(c => c.Id.Entry, StringComparer.Ordinal).ThenBy(GetStableCardUid, StringComparer.Ordinal).ToList();
        private static void RefreshEntityIdRegistry(ICombatState state) => EntityRefreshes++;
        private static string GetStableEntityId(Creature creature) => $"{creature.MonsterId}_0";
        private static Dictionary<string, object?> BuildGameStateCore() => new();
        private static Dictionary<string, object?> BuildMultiplayerGameStateCore() => new();

        private static List<Dictionary<string, object?>> Rows(Dictionary<string, object?> notifications, string key)
            => (List<Dictionary<string, object?>>)((Dictionary<string, object?>)notifications[key]!)["listeners"]!;

        public static void RunContractTests()
        {
            BeginStateWarnings();
            var me = new Player { NetId = 1 };
            var combat = new CombatState();
            CombatManager.Instance.State = combat;
            CombatManager.Instance.IsInProgress = true;

            var cruelty = new CardModel("CRUELTY", me) { CanonicalStarCost = -1 };
            cruelty.TemporaryStarCostsForTest.Add(new TemporaryCardCost(0, true, true));
            var stomp = new StompLike(me);
            stomp.Affliction = new HexedLike(stomp);
            var armaments = new CardModel("ARMAMENTS", me) { ExhaustOnNextPlay = true };

            // --- rows: in combat ---
            var rows = BuildChooseCardRows(new CardModel?[] { cruelty, null, stomp, armaments }, GetLiveCombatStateForSelection());
            Check(rows.Select(r => r["index"]).SequenceEqual(new object?[] { 0, 2, 3 }), "index は holder の位置（null holder は行を出さず位置を詰めない）");
            Check(_stateWarnings.Any(w => w.StartsWith("card_select.cards[1]:")), "null holder は warning");
            Check(UidRefreshes == 1, "uid registry を行の前に更新する");
            Check(Calls.Take(5).SequenceEqual(new[] { "info:CRUELTY:False", "details:CRUELTY", "energy:CRUELTY", "playability:CRUELTY", "info:STOMP:False" }), "手札と同じ順: 情報 → details → コスト → 可否");
            Check(Equals(rows[0]["uid"], "CRUELTY#g0") && Equals(rows[1]["uid"], "STOMP#g1") && GetStableCardUid(stomp) == "STOMP#g1", "実個体の uid（選択後のパイルでも同じ registry の値）");
            Check(rows[2]["exhaust_on_next_play"] is true && rows[0]["exhaust_on_next_play"] is false, "ExhaustOnNextPlay の現在値");
            Check(Equals(rows[0]["cost"], "2") && Equals(((Dictionary<string, object?>)rows[0]["energy_cost"]!)["base"], 2), "選択前のコストを 0 に加工しない");
            Check(((Dictionary<string, object?>)rows[0]["energy_cost"]!)["global_modifiers_applied"] is false, "パイルに無い候補は global の修正が入っていないことを行で示す");
            Check(rows[0]["can_play"] is false && Equals(rows[0]["unplayable_reason"], "EnergyCostTooHigh"), "可否は本体の判定をそのまま");
            var star = (Dictionary<string, object?>)rows[0]["star_cost_state"]!;
            var temps = (List<Dictionary<string, object?>>)star["temporary_costs"]!;
            Check(star["costs_x"] is false && Equals(star["canonical"], -1) && Equals(star["base"], -1) && Equals(star["current"], -1), "スターの基底・現在値（負の基底に 0 の一時値は無視される本体の値）");
            Check(temps.Count == 1 && Equals(temps[0]["cost"], 0) && temps[0]["clears_when_turn_ends"] is true && temps[0]["clears_when_card_is_played"] is true, "一時スターコストを順に出す");
            Check(cruelty.TemporaryStarCostsForTest.Count == 1 && !stomp.ExhaustOnNextPlay, "読み取りで札を変えない（無料化・一時値の追加をしない）");

            // --- rows: outside combat ---
            Calls.Clear();
            CombatManager.Instance.IsInProgress = false;
            var plain = BuildChooseCardRows(new CardModel?[] { cruelty }, GetLiveCombatStateForSelection());
            Check(Calls.SequenceEqual(new[] { "info:CRUELTY:True" }) && !plain[0].ContainsKey("uid") && !plain[0].ContainsKey("energy_cost"), "戦闘外の choose は従来の行だけ");
            CombatManager.Instance.IsInProgress = true;

            // --- star cost read failure ---
            var broken = new CardModel("BROKEN", me);
            broken.BreakStarField();
            var brokenStar = BuildCardStarCostState(broken);
            Check(brokenStar["temporary_costs"] == null && ((Dictionary<string, string>)brokenStar["unavailable_reasons"]!).ContainsKey("temporary_costs") && Equals(brokenStar["base"], -1), "一時値の読取失敗はその項目だけ null と理由");
            Check(_stateWarnings.Any(w => w.StartsWith("card.star_cost_state.temporary_costs:")), "スターの warning パス");

            // --- candidate listeners ---
            var candidate = BuildCandidateContinuationListeners(stomp);
            Check(candidate.Select(r => $"{r["notification"]}:{r["kind"]}:{r["model_id"]}:{r["declared_by"]}").SequenceEqual(new[]
            {
                "after_card_entered_combat:card:STOMP:StompLike", "after_card_entered_combat:affliction:HEXED:HexedLike"
            }), "候補自身と付属効果の override を出す（選択後に参加する）");
            Check(BuildCandidateContinuationListeners(cruelty).Count == 0, "override の無い候補は空配列");

            // --- continuation context ---
            var self = new Creature { CombatId = 1, Player = me };
            var enemy = new Creature { CombatId = 5, IsEnemy = true, MonsterId = "GALVANIC_THING" };
            var handPile = new CardPile(PileType.Hand);
            var handStomp = new StompLike(me) { Pile = handPile };
            var deckCard = new CardModel("DECK_CARD", me);
            me.Deck.Cards.AddRange([new CardModel("FIRST", me), deckCard]);
            var deckEnchant = new DeckEnchantLike(deckCard);
            var potion = new PotionModel("FIRE_POTION", me);
            me.Slots.AddRange([null, potion]);
            combat.Listeners.AddRange(new AbstractModel[]
            {
                new GalvanicLike(enemy), new PowerModel("STRENGTH_POWER", self), new SmokestackLike(self),
                new FertilizerLike(me), new BeltBuckleLike(me), new OverloadOnly(me), new TopLike(me), potion, handStomp,
                new ModifierModel("SOME_MODIFIER")
            });
            var run = new RunState();
            run.DeckListeners.AddRange(new AbstractModel[] { deckCard, deckEnchant });
            var context = BuildChoiceContinuationContext(run, combat);
            Check(context["known"] is true && Equals(context["version"], 1) && context["should_skip_combat_effect"] is false && EntityRefreshes == 1, "既知・版1・ShouldSkipCombatEffect を出す");
            var notes = (Dictionary<string, object?>)context["notifications"]!;
            Check(notes.Keys.SequenceEqual(new[] { "after_card_entered_combat", "after_card_generated_for_combat", "after_potion_used", "after_hand_emptied" }), "本体の解決順に4通知");
            var entered = Rows(notes, "after_card_entered_combat");
            Check(entered.Select(r => r["model_id"]).SequenceEqual(new object?[] { "GALVANIC_POWER", "STOMP" }) && Equals(entered[0]["order"], 0) && Equals(entered[1]["order"], 8), "override した listener だけを列順の位置付きで出す");
            Check(Equals(entered[0]["kind"], "power") && Equals(entered[0]["owner_entity_id"], "GALVANIC_THING_0") && Equals(entered[0]["owner_combat_id"], 5u) && entered[0]["declared_by"] as string == "GalvanicLike", "敵の power は所有者の entity_id");
            Check(Equals(entered[1]["uid"], GetStableCardUid(handStomp)) && Equals(entered[1]["pile"], "Hand"), "戦闘パイルの札は uid とパイル");
            Check(Equals(((Dictionary<string, object?>)notes["after_card_entered_combat"]!)["listener_count"], 10), "全 listener 数も出す");
            var generated = Rows(notes, "after_card_generated_for_combat");
            Check(generated.Select(r => r["model_id"]).SequenceEqual(new object?[] { "SMOKESTACK_POWER", "FERTILIZER" }) && Equals(generated[0]["owner_player_id"], "1") && Equals(generated[1]["owner_player_id"], "1"), "生成時の listener と所有者");
            var used = Rows(notes, "after_potion_used");
            Check(used.Select(r => r["model_id"]).SequenceEqual(new object?[] { "DECK_ENCHANT", "BELT_BUCKLE" }), "使用後は run scope（デッキ由来を先頭に）、別 overload だけの型は除く");
            Check(Equals(used[0]["kind"], "enchantment") && Equals(used[0]["parent_pile"], "Deck") && Equals(used[0]["parent_deck_index"], 1) && !used[0].ContainsKey("parent_uid") && Equals(used[1]["order"], 6), "デッキの札は戦闘 uid を作らずデッキ位置");
            Check(Rows(notes, "after_hand_emptied").Single()["model_id"] as string == "UNCEASING_TOP", "空手札の listener");
            Check(!Uids.ContainsKey(deckCard), "デッキの札に戦闘 uid を発行しない");
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(context)))
                Check(json.RootElement.GetProperty("notifications").GetProperty("after_potion_used").GetProperty("listeners").GetArrayLength() == 2, "JSON 化できる");

            combat.Listeners.Add(new StrangeModel());
            var strange = BuildChoiceContinuationContext(run, combat);
            Check(strange["known"] is false && ((List<string>)strange["unavailable_reasons"]!).Any(r => r.Contains("StrangeModel")), "公開状態へ結べない listener の種類は known=false");
            combat.Listeners.RemoveAt(combat.Listeners.Count - 1);

            ModHelper.RunSubscribersForTest.Add(new object());
            int before = combat.Iterations;
            var modded = BuildChoiceContinuationContext(run, combat);
            Check(modded["known"] is false && !modded.ContainsKey("notifications") && combat.Iterations == before, "MOD の subscriber があれば列挙せず（デリゲートを呼ばず）known=false");
            Check(_stateWarnings.Any(w => w.StartsWith("card_select.continuation_context:")), "context の warning パス");
            var md = new System.Text.StringBuilder();
            FormatCardSelectMarkdown(md, new Dictionary<string, object?> { ["screen_type"] = "choose", ["cards"] = rows, ["continuation_context"] = modded });
            Check(md.ToString().Contains("Continuation listeners: unknown (InvalidOperationException") && md.ToString().Contains("`CRUELTY#g0`"), "Markdown は未知の理由と uid を出す");
            ModHelper.RunSubscribersForTest.Clear();
            Check(BuildChoiceContinuationContext(run, combat)["known"] is true, "復旧後は再び既知");
            md.Clear();
            FormatCardSelectMarkdown(md, new Dictionary<string, object?> { ["screen_type"] = "choose", ["cards"] = rows, ["continuation_context"] = context });
            Check(md.ToString().Contains("Continuation listeners: after_card_entered_combat=2, after_card_generated_for_combat=2, after_potion_used=2, after_hand_emptied=1"), "Markdown は通知ごとの listener 数");

            // --- draw pile order must not leak through listener order ---
            var drawPlayer = new Player { NetId = 2 };
            var stompX = new StompLike(drawPlayer);
            var stompY = new StompLike(drawPlayer);
            var zed = new CardModel("ZED", drawPlayer);
            zed.Affliction = new HexedLike(zed);
            var drawDiscard = new StompLike(me) { Pile = new CardPile(PileType.Discard) };
            string DrawContext(CardModel[] hiddenOrder)
            {
                var drawPile = new CardPile(PileType.Draw);
                var cards = hiddenOrder.ToList();
                drawPile.Cards.AddRange(cards);
                foreach (var c in cards) c.Pile = drawPile;
                var state = new CombatState();
                var discardStomp = drawDiscard;
                state.Listeners.Add(new GalvanicLike(enemy));
                state.Listeners.Add(handStomp);
                foreach (var c in cards)
                {
                    state.Listeners.Add(c);
                    if (c.Affliction != null) state.Listeners.Add(c.Affliction);
                }
                state.Listeners.Add(discardStomp);
                var ctx = BuildChoiceContinuationContext(new RunState(), state);
                Check(ctx["known"] is true, "山札の listener があっても既知");
                return JsonSerializer.Serialize(((Dictionary<string, object?>)ctx["notifications"]!)["after_card_entered_combat"]);
            }
            foreach (var c in new CardModel[] { stompY, stompX, zed, drawDiscard }) GetStableCardUid(c); // uids fixed before any read
            string first = DrawContext(new CardModel[] { stompX, zed, stompY });
            string second = DrawContext(new CardModel[] { stompY, stompX, zed });
            Check(first == second, "山札の並びを入れ替えても listener の行・order が完全に同じ（引く順が漏れない）");
            using (var json = JsonDocument.Parse(first))
            {
                var listeners = json.RootElement.GetProperty("listeners");
                Check(listeners.GetArrayLength() == 6, "Galvanic・手札・山札 3 行・捨て札");
                Check(listeners[0].GetProperty("order").GetInt32() == 0 && listeners[1].GetProperty("order").GetInt32() == 1, "山札より前の order は公開の件数");
                var drawRows = Enumerable.Range(2, 3).Select(i => listeners[i]).ToList();
                Check(drawRows.All(r => r.GetProperty("order").ValueKind == JsonValueKind.Null
                    && r.GetProperty("order_group").GetString() == "draw_pile"
                    && r.GetProperty("order_group_start").GetInt32() == 2
                    && r.GetProperty("order_group_owner_player_id").GetString() == "2"), "山札の行は order=null とまとまりの開始位置だけ");
                Check(drawRows.Select(r => r.GetProperty("model_id").GetString()).SequenceEqual(new[] { "STOMP", "STOMP", "HEXED" })
                    && drawRows[0].GetProperty("uid").GetString() == GetStableCardUid(stompY) && drawRows[1].GetProperty("uid").GetString() == GetStableCardUid(stompX)
                    && drawRows[2].GetProperty("parent_uid").GetString() == GetStableCardUid(zed), "山札の行は表示順（OrderPileCardsForDisplay）で、付属効果は親の札の位置");
                Check(listeners[5].GetProperty("order").GetInt32() == 6, "山札より後ろの order は山札の公開の枚数（札 3・付属効果 1）から決まる");
            }

            // --- wiring (source checks) ---
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string builder = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            string choose = builder[builder.IndexOf("private static Dictionary<string, object?> BuildChooseCardState", StringComparison.Ordinal)..builder.IndexOf("private static Dictionary<string, object?> BuildBundleSelectState", StringComparison.Ordinal)];
            Check(choose.Contains("FindAllSortedByPosition<NGridCardHolder>(screen).Select(holder => holder.CardModel)") && choose.Contains("BuildChooseCardRows(holderCards, combatState)"), "choose は holder 列（action と同じ）から行を作る");
            Check(choose.Contains("state[\"continuation_context\"] = BuildChoiceContinuationContext(runState, combatState)") && choose.Contains("BuildCandidateContinuationListenersSafe"), "戦闘中の choose に継続の listener を付ける");
            Check(!choose.Contains("SetToFreeThisTurn") && !choose.Contains("CreateCard") && !choose.Contains("EmitSignal"), "GET で無料化・生成・選択をしない");
            int chooseBranch = builder.IndexOf("BuildChooseCardState(chooseCardScreen, runState);", StringComparison.Ordinal);
            string afterChoose = builder[chooseBranch..builder.IndexOf("NChooseABundleSelectionScreen bundleScreen", chooseBranch, StringComparison.Ordinal)];
            Check(afterChoose.Contains("currentRoom is CombatRoom chooseCombatRoom && CombatManager.Instance.IsInProgress") && afterChoose.Contains("result[\"battle\"] = BuildBattleState(runState, chooseCombatRoom)"), "戦闘中の choose だけに battle を付ける");
            string multi = File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs"));
            Check(multi.Contains("result[\"battle\"] = BuildMultiplayerBattleState(runState, chooseCombatRoom)"), "マルチも同じ");
            string cardState = builder[builder.IndexOf("private static Dictionary<string, object?> BuildCardState", StringComparison.Ordinal)..];
            Check(cardState[..cardState.IndexOf("return state;", StringComparison.Ordinal)].Contains("state[\"star_cost_state\"] = BuildCardStarCostState(card)"), "手札にもスターの状態");
            string piles = builder[builder.IndexOf("private static List<Dictionary<string, object?>> BuildPileCardList", StringComparison.Ordinal)..];
            Check(piles[..piles.IndexOf("return list;", StringComparison.Ordinal)].Contains("[\"star_cost_state\"] = BuildCardStarCostState(card)"), "4パイルにもスターの状態");
            string actions = File.ReadAllText(Path.Combine(sources, "McpMod.Actions.cs"));
            int nullGuard = actions.IndexOf("if (holders[index].CardModel == null)", StringComparison.Ordinal);
            int reveal = actions.IndexOf("RevealCardSelectScreen(chooseScreen, revealed)", StringComparison.Ordinal);
            Check(nullGuard > 0 && nullGuard < reveal, "CardModel の無い holder は覆いを外す前に拒否する");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 6;"), "現行の schema は 6（sts2#1018）");
            Console.WriteLine($"ChooseCardContract: {Checks} checks passed");
        }
    }
}
