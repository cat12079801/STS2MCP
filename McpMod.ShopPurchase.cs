using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> BuildShopPurchaseResult(Task<bool> purchase, int cost)
    {
        // PurchaseLocalEntry synchronizes the purchase with peers, but is still async.
        // Never block the main thread: removal and some relics await a player choice.
        if (purchase.IsCompleted)
        {
            try
            {
                if (!purchase.GetAwaiter().GetResult())
                    return Error("Shop purchase failed or was cancelled (another purchase may still be in progress)");
            }
            catch (Exception ex)
            {
                return Error($"Shop purchase failed: {ex.Message}");
            }

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Purchased item for {cost} gold",
                ["purchase_completed"] = true,
                ["purchase_success"] = true
            };
        }

        // Observe eventual failures instead of leaving a faulted Task unobserved.
        _ = ObserveShopPurchase(purchase);
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Purchasing item for {cost} gold; re-read state for completion or a card selection",
            ["purchase_completed"] = false
        };
    }

    private static async Task ObserveShopPurchase(Task<bool> purchase)
    {
        try
        {
            if (!await purchase)
                GD.PushWarning("[STS2 MCP] Shop purchase failed or was cancelled; re-read state before retrying");
        }
        catch (Exception ex)
        {
            GD.PushError($"[STS2 MCP] Shop purchase failed: {ex}");
        }
    }
}
