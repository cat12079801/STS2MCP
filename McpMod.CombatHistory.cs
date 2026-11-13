using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_MCP;

public static partial class McpMod
{
    // sts2-d009478a: sources and exact predicates are documented in raw-full.md.
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
        Read("card_plays_finished_this_turn_card_uids", () =>
        {
            var combat = CurrentCombat();
            // Like Bolas/ThrummingHatchet, match card identity, not the playing Player.
            return CombatManager.Instance.History.CardPlaysFinished
                .Where(e => e.HappenedThisTurn(combat))
                .Select(e => GetStableCardUid(e.CardPlay.Card)).ToList();
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
