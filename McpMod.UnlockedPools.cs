using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.PotionPools;

namespace STS2_MCP;

public static partial class McpMod
{
    private static ICombatState? _unlockedPoolsCombatToken;
    private static Player? _unlockedPoolsPlayer;
    private static Dictionary<string, object?>? _unlockedPools;
    private static readonly List<(string Context, string Message)> _unlockedPoolsWarnings = new();

    private static Dictionary<string, object?> BuildUnlockedPools(Player player, ICombatState? combatState)
    {
        // Like RefreshCardUidRegistry, use combat identity, not floor/round numbers.
        // Also guard player identity so a different owner cannot reuse these unlocks.
        if (_unlockedPools == null || !ReferenceEquals(_unlockedPoolsCombatToken, combatState)
            || !ReferenceEquals(_unlockedPoolsPlayer, player))
        {
            _unlockedPoolsCombatToken = combatState;
            _unlockedPoolsPlayer = player;
            _unlockedPoolsWarnings.Clear();
            _unlockedPools = new Dictionary<string, object?>
            {
                // JackOfAllTrades.OnPlay and Jackpot.OnPlay use these exact arguments.
                // Report unlocked pools, before the cards' additional generation filters.
                ["colorless_cards"] = ReadUnlockedPool("colorless_cards", () =>
                    ModelDb.CardPool<ColorlessCardPool>()
                        .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                        .Select(card => card.Id.Entry)),
                ["character_cards"] = ReadUnlockedPool("character_cards", () =>
                    player.Character.CardPool
                        .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                        .Select(card => card.Id.Entry)),
                ["potions"] = new Dictionary<string, object?>
                {
                    // PotionFactory.GetPotionOptions uses these two sources separately.
                    ["character"] = ReadUnlockedPool("potions.character", () =>
                        player.Character.PotionPool.GetUnlockedPotions(player.UnlockState)
                            .Select(potion => potion.Id.Entry)),
                    ["shared"] = ReadUnlockedPool("potions.shared", () =>
                        ModelDb.PotionPool<SharedPotionPool>().GetUnlockedPotions(player.UnlockState)
                            .Select(potion => potion.Id.Entry))
                }
            };
        }

        // BeginStateWarnings clears warnings on every read. Replay cached failures so
        // null never silently becomes an apparently successful empty pool next time.
        foreach (var (context, message) in _unlockedPoolsWarnings)
            Warn(context, message);

        return _unlockedPools;
    }

    private static List<string>? ReadUnlockedPool(string field, Func<IEnumerable<string>> read)
    {
        try
        {
            // Materialize inside the catch: lazy enumeration can fail partway through.
            // Sorting changes only presentation; unlock/multiplayer filters stay in engine.
            return read().OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            _unlockedPoolsWarnings.Add(($"player.unlocked_pools.{field}", $"{ex.GetType().Name}: {ex.Message}"));
            return null;
        }
    }
}
