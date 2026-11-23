using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> ReadStarX(CardModel card, PileType pile)
    {
        var state = BuildCardStarCostState(card);
        AttachCombatStarXValues(state, card, pile);
        return state;
    }

    private static void CheckStarX()
    {
        const string last = "last_stars_spent";
        const string resolved = "resolved_x_value";
        const string spend = "stars_if_played";
        const string forecast = "x_value_if_played";
        string[] xKeys = [last, resolved, spend, forecast];
        BeginStateWarnings();
        var owner = new Player();
        var resources = owner.PlayerCombatState!;
        resources.Stars = 5;
        resources.Energy = 2;
        resources.History.Add(16);
        var card = new CardModel { Owner = owner, Captured = 3 };
        var sameModel = new CardModel { Owner = owner, Captured = 0 };
        resources.PileCards.AddRange([card, sameModel]);
        card.AddTemporaryCost(new TemporaryCardCost { Cost = 0, ClearsWhenTurnEnds = true });
        Hook.X = (instance, value) => value + (ReferenceEquals(instance.Owner, owner) ? 2 : 0);
        Hook.XCalls.Clear();

        var hand = ReadStarX(card, PileType.Hand);
        Check(hand["costs_x"] is true && Equals(hand[last], 3) && Equals(hand[resolved], 5) &&
            Equals(hand[spend], 5) && Equals(hand[forecast], 7), "saved X and manual X use separate inputs");
        Check(Hook.XCalls.SequenceEqual(new[] { (card.CombatState!, card, 3), (card.CombatState!, card, 5) }),
            "Chemical X hook called once with each input and the real card and its combat");
        Check(Equals(hand["current"], 0) && JsonSerializer.Serialize(hand["temporary_costs"], WireOptions)
            == "[{\"cost\":0,\"clears_when_turn_ends\":true,\"clears_when_card_is_played\":false}]",
            "existing displayed cost and temporary costs keep their meaning");
        var secondHand = ReadStarX(sameModel, PileType.Hand);
        Check(sameModel.Id == card.Id && Equals(secondHand[last], 0) && Equals(secondHand[resolved], 2),
            "same card ID retains its own captured value including initial zero");

        foreach (var pile in new[] { PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play })
        {
            var row = ReadStarX(card, pile);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, WireOptions));
            Check(Equals(row[last], 3) && Equals(row[resolved], 5) &&
                json.RootElement.GetProperty(spend).ValueKind == JsonValueKind.Null &&
                json.RootElement.GetProperty(forecast).ValueKind == JsonValueKind.Null &&
                Reasons(row)[spend] == "not_in_hand" && Reasons(row)[forecast] == "not_in_hand",
                $"{pile} provides the two stored values and two out-of-hand reasons");
        }
        Check(_stateWarnings.Count == 0, "not_in_hand is ordinary applicability");
        string first = JsonSerializer.Serialize(hand, WireOptions);
        Check(first == JsonSerializer.Serialize(ReadStarX(card, PileType.Hand), WireOptions) &&
            card.LastStarsSpent == 3 && sameModel.LastStarsSpent == 0 && resources.Stars == 5 && resources.Energy == 2 &&
            resources.PileCards.SequenceEqual(new[] { card, sameModel }) && resources.History.SequenceEqual(new[] { 16 }),
            "snapshot reads are repeatable and do not mutate capture, resources, piles or history");

        resources.Stars = 0;
        Hook.X = (_, value) => value;
        var zero = ReadStarX(card, PileType.Hand);
        Check(Equals(zero[spend], 0) && Equals(zero[forecast], 0) && Equals(zero[last], 3) && Equals(zero[resolved], 3),
            "zero current stars does not erase saved X");
        var other = new CardModel();
        Check(Equals(ReadStarX(other, PileType.Hand)[last], 0), "an untouched instance starts at zero");

        resources.Stars = 4;
        // Input after CardCmd.AutoPlay captures X with skipXCapture=false and pays zero.
        card.Captured = resources.Stars;
        var captured = ReadStarX(card, PileType.Play);
        // Input after a replay with skipXCapture=true and a different current star balance.
        resources.Stars = 1;
        var retained = ReadStarX(card, PileType.Play);
        Check(Equals(captured[last], 4) && Equals(retained[last], 4) && Equals(retained[resolved], 4)
            && resources.Stars == 1, "both auto-play capture and replay retention come from the per-card getter");

        card.EnergyCost.Amount = 4;
        card.StarCostOverride = -1;
        Hook.PayWithStars = player => ReferenceEquals(player, owner);
        var deficit = ReadStarX(card, PileType.Hand);
        Check(Equals(deficit[spend], 4) && Equals(deficit[forecast], 4),
            "negative star cost is clamped before adding two stars per energy deficit");
        Hook.PayWithStars = _ => false;
        Check(Equals(ReadStarX(card, PileType.Hand)[spend], 0), "no conversion when the payment hook declines");

        var ordinary = ReadStarX(new CardModel { StarX = false }, PileType.Hand);
        Check(ordinary["costs_x"] is false && xKeys.All(key => !ordinary.ContainsKey(key)), "non-star-X fields are omitted");
        var detached = BuildCardStarCostState(new CardModel { CombatState = null });
        Check(detached["costs_x"] is true && xKeys.All(key => !detached.ContainsKey(key)),
            "detached candidates retain the cost base without the combat attachment");

        card.ThrowCostsX = true;
        var unreadableX = ReadStarX(card, PileType.Hand);
        Check(unreadableX["costs_x"] == null && Reasons(unreadableX)["costs_x"].Contains("HasStarCostX") &&
            xKeys.All(key => !unreadableX.ContainsKey(key)), "unknown X flag is preserved");
        card.ThrowCostsX = false;
        card.ThrowLastStarsSpent = true;
        var unreadableCapture = ReadStarX(card, PileType.Hand);
        Check(unreadableCapture[last] == null && unreadableCapture[resolved] == null &&
            Reasons(unreadableCapture).ContainsKey(last) && Reasons(unreadableCapture).ContainsKey(resolved) &&
            Equals(unreadableCapture[forecast], 0), "failed saved getter does not prevent manual-X reading");
        card.ThrowLastStarsSpent = false;
        card.ThrowStarCost = true;
        var unreadableSpend = ReadStarX(card, PileType.Hand);
        Check(unreadableSpend[spend] == null && unreadableSpend[forecast] == null &&
            Reasons(unreadableSpend).ContainsKey(spend) && Reasons(unreadableSpend).ContainsKey(forecast) &&
            Equals(unreadableSpend[resolved], 4), "failed manual estimate does not prevent saved-X reading");
        card.ThrowStarCost = false;
        Hook.X = (_, _) => throw new MissingMemberException("ModifyXValue");
        var unreadableHook = ReadStarX(card, PileType.Hand);
        Check(unreadableHook[resolved] == null && unreadableHook[forecast] == null &&
            Equals(unreadableHook[last], 4) && Equals(unreadableHook[spend], 0), "hook failure preserves both raw values");
        Check(xKeys.Append("costs_x").All(key => _stateWarnings.Any(w => w.StartsWith($"card.star_cost_state.{key}:"))),
            "individual getter failures report their own warning");
    }
}
