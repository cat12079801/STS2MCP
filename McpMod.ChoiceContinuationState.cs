using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int ChoiceContinuationVersion = 1;

    // What runs after a choose-a-card pick of the potion screens (sts2-a671e9ef):
    // SetToFreeThisTurn -> CardPileCmd.AddGeneratedCardToCombat (Hook.AfterCardEnteredCombat,
    // then Hook.AfterCardGeneratedForCombat) -> PotionModel.OnUseWrapper (Hook.AfterPotionUsed)
    // -> CombatManager.CheckForEmptyHand (Hook.AfterHandEmptied). Listed in that order.
    // Run-scope notifications iterate IRunState.IterateHookListeners(combat), the others
    // ICombatState.IterateHookListeners() (Hook.IterateCombatHookListeners).
    private static readonly (string Notification, string Method, Type[] Parameters, bool RunScope)[] ChoiceContinuationHooks =
    {
        ("after_card_entered_combat", "AfterCardEnteredCombat", new[] { typeof(CardModel) }, false),
        ("after_card_generated_for_combat", "AfterCardGeneratedForCombat", new[] { typeof(CardModel), typeof(Player) }, false),
        ("after_potion_used", "AfterPotionUsed", new[] { typeof(PotionModel), typeof(Creature) }, true),
        ("after_hand_emptied", "AfterHandEmptied", new[] { typeof(PlayerChoiceContext), typeof(Player) }, false),
    };

    private static readonly Dictionary<(Type, string), Type?> _hookOverrideCache = new();

    // The declaring type of the hook implementation, or null when it is AbstractModel's
    // default (a no-op). Reflection only: the hook itself is never invoked.
    private static Type? HookOverrideDeclarer(Type modelType, string method, Type[] parameters)
    {
        if (_hookOverrideCache.TryGetValue((modelType, method), out var cached)) return cached;
        var info = modelType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public, null, parameters, null)
            ?? throw new MissingMethodException(modelType.Name, method);
        var declarer = info.DeclaringType == typeof(AbstractModel) ? null : info.DeclaringType;
        _hookOverrideCache[(modelType, method)] = declarer;
        return declarer;
    }

    private static Dictionary<string, object?> BuildChoiceContinuationContext(
        IRunState runState, ICombatState combatState)
    {
        var result = new Dictionary<string, object?>
        {
            ["version"] = ChoiceContinuationVersion,
            ["known"] = false
        };
        var reasons = new List<string>();
        try
        {
            // ModHelper subscribers append mod-made models whose origin cannot be tied to public
            // state. (Other parts of the same read, e.g. CanPlay -> Hook.ShouldPlay, may already
            // reach those delegates; this guard is about the listener list, not about calls.)
            int modSubscribers = CountModHookSubscribers("_runHookSubscribers") + CountModHookSubscribers("_combatHookSubscribers");
            if (modSubscribers > 0)
                throw new InvalidOperationException($"{modSubscribers} ModHelper hook subscriber(s) registered; their models cannot be tied to public state");

            RefreshEntityIdRegistry(combatState);
            result["should_skip_combat_effect"] = CombatManager.Instance.ShouldSkipCombatEffect;
            var combatListeners = combatState.IterateHookListeners().ToList();
            var runListeners = runState.IterateHookListeners(combatState).ToList();
            var notifications = new Dictionary<string, object?>();
            foreach (var hook in ChoiceContinuationHooks)
            {
                var listeners = hook.RunScope ? runListeners : combatListeners;
                notifications[hook.Notification] = new Dictionary<string, object?>
                {
                    ["listener_count"] = listeners.Count,
                    ["listeners"] = BuildHookListenerRows(listeners, hook, reasons)
                };
            }
            result["notifications"] = notifications;
            result["known"] = reasons.Count == 0;
        }
        catch (Exception ex)
        {
            result.Remove("notifications");
            reasons.Add($"{ex.GetType().Name}: {ex.Message}");
            Warn("card_select.continuation_context", ex);
        }
        if (reasons.Count > 0) result["unavailable_reasons"] = reasons;
        return result;
    }

    // IterateHookListeners walks PlayerCombatState.AllPiles card by card, so a draw-pile
    // listener's position is its hidden place in the draw order. Never publish it:
    // draw-pile rows get order=null plus the (public) position where that player's draw-pile
    // block starts, and are re-sorted into the draw pile's display order
    // (OrderPileCardsForDisplay), so neither the numbers nor the row order reveal the draw order.
    // Positions outside the block depend only on public counts and are kept.
    private static List<Dictionary<string, object?>> BuildHookListenerRows(List<AbstractModel> listeners,
        (string Notification, string Method, Type[] Parameters, bool RunScope) hook, List<string> reasons)
    {
        var drawStart = new Dictionary<CardPile, int>(ReferenceEqualityComparer.Instance);
        var drawRank = new Dictionary<CardModel, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (DrawPileOf(listeners[i]) is not { } pile || drawStart.ContainsKey(pile)) continue;
            drawStart[pile] = i;
            int rank = 0;
            foreach (var card in OrderPileCardsForDisplay(pile.Cards, PileType.Draw))
                drawRank[card] = rank++;
        }

        var rows = new List<(Dictionary<string, object?> Row, CardPile? Draw, int Rank, int Kind)>();
        for (int order = 0; order < listeners.Count; order++)
        {
            var model = listeners[order];
            var declarer = HookOverrideDeclarer(model.GetType(), hook.Method, hook.Parameters);
            if (declarer == null) continue;
            var row = BuildHookListenerRow(model, reasons);
            row["declared_by"] = declarer.Name;
            var draw = DrawPileOf(model);
            if (draw == null)
            {
                row["order"] = order;
                rows.Add((row, null, 0, 0));
                continue;
            }
            var card = ListenerCard(model)!;
            row["order"] = null;
            row["order_group"] = "draw_pile";
            row["order_group_start"] = drawStart[draw];
            row["order_group_owner_player_id"] = card.Owner.NetId.ToString();
            int kind = model is CardModel ? 0 : model is AfflictionModel ? 1 : 2; // game order within a card
            rows.Add((row, draw, drawRank.TryGetValue(card, out int r) ? r : int.MaxValue, kind));
        }

        // A player's draw-pile rows are contiguous (one AllPiles block): sort each run.
        var result = new List<Dictionary<string, object?>>(rows.Count);
        for (int start = 0; start < rows.Count;)
        {
            int end = start + 1;
            if (rows[start].Draw is { } pile)
                while (end < rows.Count && ReferenceEquals(rows[end].Draw, pile)) end++;
            result.AddRange(rows.Skip(start).Take(end - start)
                .OrderBy(x => x.Rank).ThenBy(x => x.Kind).Select(x => x.Row));
            start = end;
        }
        return result;
    }

    private static CardModel? ListenerCard(AbstractModel model) => model switch
    {
        CardModel card => card,
        AfflictionModel affliction => affliction.Card,
        EnchantmentModel enchantment => enchantment.Card,
        _ => null
    };

    private static CardPile? DrawPileOf(AbstractModel model)
        => ListenerCard(model)?.Pile is { Type: PileType.Draw } pile ? pile : null;

    // Hook overrides on a not-yet-placed candidate (the card, its affliction, its enchantment).
    // They join the listener lists only after the pick puts the card in a pile.
    private static List<Dictionary<string, object?>> BuildCandidateContinuationListeners(CardModel card)
    {
        var rows = new List<Dictionary<string, object?>>();
        var models = new List<(string Kind, AbstractModel Model)> { ("card", card) };
        if (card.Affliction is { } affliction) models.Add(("affliction", affliction));
        if (card.Enchantment is { } enchantment) models.Add(("enchantment", enchantment));
        foreach (var (kind, model) in models)
            foreach (var hook in ChoiceContinuationHooks)
                if (HookOverrideDeclarer(model.GetType(), hook.Method, hook.Parameters) is { } declarer)
                    rows.Add(new Dictionary<string, object?>
                    {
                        ["notification"] = hook.Notification,
                        ["kind"] = kind,
                        ["model_type"] = model.GetType().Name,
                        ["model_id"] = model.Id.Entry,
                        ["declared_by"] = declarer.Name
                    });
        return rows;
    }

    private static List<Dictionary<string, object?>>? BuildCandidateContinuationListenersSafe(CardModel card)
    {
        try { return BuildCandidateContinuationListeners(card); }
        catch (Exception ex)
        {
            // null (unknown) with a warning, never an empty "no listeners" list.
            Warn("card_select.cards.continuation_listeners", ex);
            return null;
        }
    }

    private static int CountModHookSubscribers(string field)
    {
        var value = typeof(ModHelper).GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.GetValue(null)
            ?? throw new MissingFieldException(nameof(ModHelper), field);
        return value is System.Collections.ICollection collection
            ? collection.Count
            : throw new InvalidOperationException($"ModHelper.{field} is not a collection");
    }

    // Ties a listener to public state: card uid / pile, owner's entity id or player id,
    // potion slot. Unrecognized model kinds make the whole context known=false.
    private static Dictionary<string, object?> BuildHookListenerRow(AbstractModel model, List<string> reasons)
    {
        var row = new Dictionary<string, object?>
        {
            ["model_type"] = model.GetType().Name,
            ["model_id"] = model.Id.Entry
        };
        switch (model)
        {
            case PowerModel power:
                row["kind"] = "power";
                AddCreatureRef(row, "owner_", power.Owner);
                break;
            case RelicModel relic:
                row["kind"] = "relic";
                row["owner_player_id"] = relic.Owner.NetId.ToString();
                break;
            case PotionModel potion:
                row["kind"] = "potion";
                row["owner_player_id"] = potion.Owner.NetId.ToString();
                int slot = potion.Owner.PotionSlots.ToList().FindIndex(p => ReferenceEquals(p, potion));
                row["slot"] = slot >= 0 ? slot : null;
                break;
            case OrbModel orb:
                row["kind"] = "orb";
                row["owner_player_id"] = orb.Owner.NetId.ToString();
                break;
            case CardModel card:
                row["kind"] = "card";
                AddCardRef(row, "", card);
                break;
            case AfflictionModel affliction:
                row["kind"] = "affliction";
                AddCardRef(row, "parent_", affliction.Card);
                break;
            case EnchantmentModel enchantment:
                row["kind"] = "enchantment";
                AddCardRef(row, "parent_", enchantment.Card);
                break;
            case MonsterModel monster:
                row["kind"] = "monster";
                AddCreatureRef(row, "", monster.Creature);
                break;
            case ModifierModel:
                row["kind"] = "modifier";
                break;
            case BadgeModel:
                row["kind"] = "badge";
                break;
            case RunModel:
                row["kind"] = "run_model";
                break;
            case MegaCrit.Sts2.Core.Models.Dread.DominionModel:
                row["kind"] = "dominion";
                break;
            case AchievementModel:
                row["kind"] = "achievement";
                break;
            default:
                row["kind"] = null;
                reasons.Add($"unrecognized hook listener kind {model.GetType().Name}");
                break;
        }
        return row;

        void AddCardRef(Dictionary<string, object?> target, string prefix, CardModel card)
        {
            var pile = card.Pile;
            if (pile != null && pile.IsCombatPile)
            {
                target[prefix + "uid"] = GetStableCardUid(card);
                target[prefix + "pile"] = pile.Type.ToString();
                return;
            }
            // Run-scope listeners include deck cards (IRunState.IterateHookListeners): they are
            // not combat instances, so give their public deck position instead of a combat uid.
            int deckIndex = card.Owner.Deck.Cards.ToList().FindIndex(c => ReferenceEquals(c, card));
            if (deckIndex < 0)
            {
                target[prefix + "pile"] = pile?.Type.ToString();
                reasons.Add($"{card.Id.Entry} listener is neither in a combat pile nor in its owner's deck");
                return;
            }
            target[prefix + "pile"] = "Deck";
            target[prefix + "deck_index"] = deckIndex;
            target[prefix + "owner_player_id"] = card.Owner.NetId.ToString();
        }

        void AddCreatureRef(Dictionary<string, object?> target, string prefix, Creature creature)
        {
            target[prefix + "combat_id"] = creature.CombatId;
            if (creature.Player is { } player) target[prefix + "player_id"] = player.NetId.ToString();
            else if (creature.IsEnemy) target[prefix + "entity_id"] = GetStableEntityId(creature);
            if (creature.CombatId == null) reasons.Add($"{model.Id.Entry} listener owner has no CombatId");
        }
    }
}
