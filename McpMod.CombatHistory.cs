using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_MCP;

public static partial class McpMod
{
    // 各項目の式・ゲーム側の出典と参照版は docs/raw-full.md に記載。
    // Read engine history on every snapshot; never accumulate counts in the MOD.
    // BuildPlayerState refreshes the shared UID registry before serializing any cards.
    private static Dictionary<string, object?> BuildCombatHistoryState(Player owner)
    {
        var reasons = new Dictionary<string, string>();
        var state = new Dictionary<string, object?> { ["unavailable_reasons"] = reasons };

        ICombatState CurrentCombat() => owner.Creature.CombatState
            ?? throw new InvalidOperationException("Owner combat state is unavailable");

        Read("turn_number", () => (owner.PlayerCombatState
            ?? throw new InvalidOperationException("PlayerCombatState is unavailable")).TurnNumber);
        Read("card_plays_started_this_turn", () =>
        {
            var combat = CurrentCombat();
            return SummarizeCardPlays(CombatManager.Instance.History.CardPlaysStarted
                .Where(e => e.HappenedThisTurn(combat) && e.CardPlay.Player == owner).Select(e => e.CardPlay));
        });
        Read("card_plays_finished_this_turn", () =>
        {
            var combat = CurrentCombat();
            return SummarizeCardPlays(CombatManager.Instance.History.CardPlaysFinished
                .Where(e => e.HappenedThisTurn(combat) && e.CardPlay.Player == owner).Select(e => e.CardPlay));
        });
        // GoldAxe.CanonicalVars: CalculatedDamage の倍率は CombatManager.Instance.History.CardPlaysFinished.Count()。
        // 戦闘全体・全員分（Player で絞らない）。完了（CardModel.OnPlayWrapper の CardPlayFinished）だけを数える。
        Read("card_plays_finished_this_combat", () => CombatManager.Instance.History.CardPlaysFinished.Count());
        Read("any_manual_cards_played_this_turn", () =>
        {
            var combat = CurrentCombat();
            // v0.113.1 PaelsEye.AnyCardsPlayedThisTurn の履歴条件をそのまま読む。
            // WhisperingEarring 所持時の初手番の例外はレリック側の条件。
            return CombatManager.Instance.History.CardPlaysFinished
                .Any(e => e.Actor == owner.Creature && e.HappenedThisTurn(combat) && !e.CardPlay.IsAutoPlay);
        });
        Read("card_plays_finished_this_turn_card_uids", () =>
        {
            var combat = CurrentCombat();
            // Like Bolas/ThrummingHatchet, match card identity, not the playing Player.
            return CombatManager.Instance.History.CardPlaysFinished
                .Where(e => e.HappenedThisTurn(combat))
                .Select(e => GetStableCardUid(e.CardPlay.Card)).ToList();
        });
        Read("card_play_events_this_turn", () =>
        {
            var combat = CurrentCombat();
            // Targets that died and left combat keep the id they were first seen with.
            RefreshEntityIdRegistry(combat);
            // History order as recorded by CardModel.OnPlayWrapper: a parent's start,
            // its auto-played children, then the parent's finish. Never re-sort.
            var events = new List<Dictionary<string, object?>>();
            foreach (var entry in CombatManager.Instance.History.Entries)
            {
                string phase;
                CardPlay play;
                if (entry is CardPlayStartedEntry started) { phase = "started"; play = started.CardPlay; }
                else if (entry is CardPlayFinishedEntry finished) { phase = "finished"; play = finished.CardPlay; }
                else continue;
                // Same owner/turn condition as card_plays_started/finished_this_turn.
                if (!entry.HappenedThisTurn(combat) || play.Player != owner) continue;
                events.Add(BuildCardPlayEvent(phase, play));
            }
            return events;
        });
        Read("card_resolution_events", () =>
        {
            var combat = CurrentCombat();
            RefreshEntityIdRegistry(combat);
            // The whole combat's draw / play / generated entries in History order, across the end of a
            // turn, the enemy turn and the next turn's start (sts2#1088). Every row is fixed once
            // recorded (entry, CardPlay and the combat's uid / entity id registries never change), so a
            // later snapshot's array starts with an earlier one's. Never re-sort, dedupe or summarize.
            var events = new List<Dictionary<string, object?>>();
            foreach (var entry in CombatManager.Instance.History.Entries)
            {
                var row = BuildCardResolutionEvent(entry, owner);
                if (row != null) events.Add(row);
            }
            return events;
        });
        Read("last_player_turn_card_uids", () =>
        {
            _ = owner.PlayerCombatState ?? throw new InvalidOperationException("PlayerCombatState is unavailable");
            _ = CurrentCombat();
            // Bolas/ThrummingHatchet test card identity, not CardPlay.Player. Keep
            // other players' plays too: ownership can change after a card is played.
            return CombatManager.Instance.History.CardPlaysFinished
                .Where(e => e.HappenedLastPlayerTurn(owner))
                .Select(e => GetStableCardUid(e.CardPlay.Card)).ToList();
        });
        Read("cards_exhausted_this_turn", () =>
        {
            var combat = CurrentCombat();
            return CombatManager.Instance.History.Entries.OfType<CardExhaustedEntry>()
                .Count(e => e.HappenedThisTurn(combat) && e.Actor == owner.Creature);
        });
        Read("bound_afflictions_this_turn", () =>
        {
            var combat = CurrentCombat();
            // ChainsOfBindingPower.AfterCardDrawn counts affliction events, not
            // cards still carrying Bound or currently remaining in hand.
            return CombatManager.Instance.History.Entries.OfType<CardAfflictedEntry>()
                .Count(e => e.HappenedThisTurn(combat) && e.Actor == owner.Creature && e.Affliction is Bound);
        });
        Read("unblocked_damage_events_this_turn", () =>
        {
            var combat = CurrentCombat();
            return CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Count(e => e.HappenedThisTurn(combat) && e.Receiver == owner.Creature && e.Result.UnblockedDamage > 0);
        });
        Read("unblocked_damage_events_this_combat", () => CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>().Count(e => e.Receiver == owner.Creature && e.Result.UnblockedDamage > 0));
        Read("block_gains_this_turn", () =>
        {
            var combat = CurrentCombat();
            // UnmovablePower.ModifyBlockMultiplicative with cardPlay=null (next
            // play). No receiver/amount filter or Distinct: the engine has none.
            return CombatManager.Instance.History.Entries.OfType<BlockGainedEntry>()
                .Count(e => e.HappenedThisTurn(combat) && e.CardPlay != null
                    && e.CardPlay.Player.Creature == owner.Creature && e.Props.IsCardOrMonsterMove());
        });
        return state;

        void Read(string key, Func<object?> read)
        {
            try { state[key] = read() ?? throw new InvalidOperationException("History value is null"); }
            catch (Exception ex)
            {
                state[key] = null;
                reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
                Warn($"player.combat_history.{key}", ex);
            }
        }
    }

    // One row of card_resolution_events, or null for an entry that is not the owner's draw, play
    // or generated card. Draws and generated cards belong to the owner whose pile the card entered:
    // their entries record Actor = Card.Owner.Creature when created (CardDrawnEntry /
    // CardGeneratedEntry constructors), so an enemy's Burn sits in its target player's stream with
    // creator=null. Plays use CardPlay.Player like card_play_events_this_turn.
    private static Dictionary<string, object?>? BuildCardResolutionEvent(CombatHistoryEntry entry, Player owner)
    {
        switch (entry)
        {
            case CardDrawnEntry drawn when drawn.Actor == owner.Creature:
                return new Dictionary<string, object?>
                {
                    ["kind"] = "draw",
                    ["uid"] = GetStableCardUid(drawn.Card),
                    ["from_hand_draw"] = drawn.FromHandDraw
                };
            case CardGeneratedEntry generated when generated.Actor == owner.Creature:
                return new Dictionary<string, object?>
                {
                    ["kind"] = "generated",
                    ["card_id"] = generated.Card.Id.Entry,
                    ["uid"] = GetStableCardUid(generated.Card),
                    // Player? Creator: null when no player made it (an enemy move, a status effect).
                    ["creator"] = generated.Creator == null ? null
                        : generated.Creator == owner ? "owner" : "other_player"
                };
            case CardPlayStartedEntry started when started.CardPlay.Player == owner:
                return WithKind(BuildCardPlayEvent("started", started.CardPlay));
            case CardPlayFinishedEntry finished when finished.CardPlay.Player == owner:
                return WithKind(BuildCardPlayEvent("finished", finished.CardPlay));
            default:
                return null;
        }

        static Dictionary<string, object?> WithKind(Dictionary<string, object?> play)
        {
            var row = new Dictionary<string, object?> { ["kind"] = "play" };
            foreach (var (key, value) in play) row[key] = value;
            return row;
        }
    }

    // One CardPlay as the engine recorded it (CardPlay.IsAutoPlay/Target/PlayIndex/PlayCount).
    // Any unreadable value throws so the whole array becomes null: never a guessed target.
    private static Dictionary<string, object?> BuildCardPlayEvent(string phase, CardPlay play)
    {
        Dictionary<string, object?>? target = null;
        if (play.Target is { } creature)
        {
            uint combatId = creature.CombatId
                ?? throw new InvalidOperationException("CardPlay.Target has no CombatId");
            target = new Dictionary<string, object?>
            {
                ["combat_id"] = combatId,
                // Non-enemy targets (allies/self) have no entity_id in battle.enemies.
                ["entity_id"] = creature.IsEnemy ? GetStableEntityId(creature) : null
            };
        }
        return new Dictionary<string, object?>
        {
            ["phase"] = phase,
            ["uid"] = GetStableCardUid(play.Card),
            ["is_auto_play"] = play.IsAutoPlay,
            ["target"] = target,
            ["play_index"] = play.PlayIndex,
            ["play_count"] = play.PlayCount
        };
    }

    private static Dictionary<string, object?> SummarizeCardPlays(IEnumerable<CardPlay> plays)
    {
        var byType = Enum.GetValues<CardType>().ToDictionary(type => type.ToString(), _ => 0);
        int total = 0;
        foreach (var play in plays)
        {
            string type = play.Card.Type.ToString();
            byType[type] = byType.GetValueOrDefault(type) + 1;
            total++;
        }
        return new Dictionary<string, object?> { ["total"] = total, ["by_type"] = byType };
    }
}
