using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2_MCP.Rewards;

namespace STS2_MCP;

public static partial class McpMod
{
    private static readonly RewardSession _rewardSession = new();
    private static readonly Dictionary<NCardRewardSelectionScreen, RewardSession.Entry> _cardRewardParents = new();
    private static bool _rewardTrackingAvailable;

    private static void RefreshRewardRun()
    {
        _rewardSession.CheckRun(RunManager.Instance.DebugOnlyGetState());
        foreach (var pair in _cardRewardParents.ToArray())
            if (!_rewardSession.Contains(pair.Value)) _cardRewardParents.Remove(pair.Key);
    }

    // Shared by state and claim_reward: exactly the same enabled-button order as the legacy API.
    private static List<NRewardButton> GetRewardButtons(NRewardsScreen screen, bool fresh = false)
    {
        RefreshRewardRun();
        _rewardSession.Open(screen, RunManager.Instance.DebugOnlyGetState());
        var buttons = FindAll<NRewardButton>(screen).Where(b => b.Reward != null).ToList();
        foreach (var button in buttons) _rewardSession.Register(button.Reward!, fresh && _rewardTrackingAvailable);
        var enabled = buttons.Where(b => b.IsEnabled).ToList();
        _rewardSession.Sync(enabled.Select(b => (object)b.Reward!));
        return enabled.Where(b => _rewardSession.Find(b.Reward!)?.SelectionState != "claimed").ToList();
    }

    private static RewardSession.Entry? GetCardRewardParent(NCardRewardSelectionScreen screen)
    {
        RefreshRewardRun();
        if (_cardRewardParents.TryGetValue(screen, out var entry) && _rewardSession.Contains(entry)) return entry;
        // CardReward.OnSelect assigns this UI screen before awaiting OptionSelected. Only correlate
        // the already displayed screen; never inspect Cards, generation options, or future rewards.
        entry = _rewardSession.Entries.FirstOrDefault(e => e.Key is CardReward reward
            && ReferenceEquals(GetInstanceFieldValue(reward, "_currentlyShownScreen"), screen));
        if (entry != null) _cardRewardParents[screen] = entry;
        return entry;
    }

    private static void CloseRewardSession(NRewardsScreen screen)
    {
        if (!_rewardSession.IsScreen(screen)) return;
        _rewardSession.Clear();
        _cardRewardParents.Clear();
    }

    private static void ObserveRewardResult(NRewardsScreen screen, Control button, bool consumed)
    {
        if (!_rewardSession.IsScreen(screen) || button is not NRewardButton { Reward: { } reward }) return;
        var entry = _rewardSession.Find(reward);
        if (entry != null)
        {
            _rewardSession.Complete(entry, consumed);
            foreach (var pair in _cardRewardParents.Where(p => ReferenceEquals(p.Value, entry)).ToArray())
                _cardRewardParents.Remove(pair.Key);
        }
    }

    private static void RewardObservation(Action observe)
    {
        try { observe(); }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Reward observation failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void InstallRewardTracking()
    {
        var harmony = new Harmony("com.sts2mcp.rewards");
        try
        {
            void Patch(Type type, string name, string? prefix = null, string? postfix = null)
            {
                var method = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(McpMod), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(McpMod), postfix));
            }
            Patch(typeof(NRewardsScreen), "_Ready", postfix: nameof(RewardScreenReady));
            Patch(typeof(NRewardsScreen), "RewardCollectedFrom", prefix: nameof(RewardCollected));
            Patch(typeof(NRewardsScreen), "RewardSkippedFrom", prefix: nameof(RewardNotConsumed));
            Patch(typeof(NRewardsScreen), "AfterOverlayClosed", prefix: nameof(RewardScreenClosed));
            Patch(typeof(NRewardsScreen), "BeforeRoomExit", prefix: nameof(RewardScreenClosed));
            Patch(typeof(NMapScreen), "Open", postfix: nameof(RewardMapOpened));
            Patch(typeof(NRewardButton), "GetReward", prefix: nameof(RewardStarted), postfix: nameof(RewardExecutionTask));
            Patch(typeof(NCardRewardSelectionScreen), "OptionSelected", postfix: nameof(RewardChoiceTask));
            _rewardTrackingAvailable = true;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchAll("com.sts2mcp.rewards"); }
            catch (Exception cleanupError) { GD.PrintErr($"[STS2 MCP] Reward tracking cleanup failed: {cleanupError.Message}"); }
            _rewardTrackingAvailable = false;
            GD.PrintErr($"[STS2 MCP] Reward tracking unavailable: {ex}");
        }
    }

    private static void RewardMapOpened() => RewardObservation(() =>
    {
        _rewardSession.Clear();
        _cardRewardParents.Clear();
    });
    private static void RewardScreenReady(NRewardsScreen __instance) => RewardObservation(() => GetRewardButtons(__instance, fresh: true));
    private static void RewardScreenClosed(NRewardsScreen __instance) => RewardObservation(() => CloseRewardSession(__instance));
    // NRewardsScreen handlers receive the public RewardClaimed / RewardSkipped UI signals.
    private static void RewardCollected(NRewardsScreen __instance, Control button) => RewardObservation(() => ObserveRewardResult(__instance, button, true));
    private static void RewardNotConsumed(NRewardsScreen __instance, Control button) => RewardObservation(() => ObserveRewardResult(__instance, button, false));
    private static void RewardStarted(NRewardButton __instance) => RewardObservation(() =>
    {
        if (__instance.Reward == null) return;
        // A human can return from the map and click before any GET. Re-establish the
        // displayed parent session from its UI ancestry, with unknown prior history.
        for (Node? node = __instance.GetParent(); node != null; node = node.GetParent())
            if (node is NRewardsScreen screen) { GetRewardButtons(screen); break; }
        var entry = _rewardSession.Find(__instance.Reward);
        if (entry != null) _rewardSession.Begin(entry);
    });

    private static void RewardExecutionTask(NRewardButton __instance, ref Task __result)
    {
        var entry = __instance.Reward == null ? null : _rewardSession.Find(__instance.Reward);
        if (entry != null) __result = ObserveRewardExecution(__result, entry);
    }

    private static async Task ObserveRewardExecution(Task task, RewardSession.Entry entry)
    {
        try { await task; }
        catch
        {
            RewardObservation(() => _rewardSession.Interrupt(entry));
            throw;
        }
    }

    // Called on the main thread only, after optional settle polling. Missing observations are
    // unknown, including a session that ended before we could verify its completion.
    private static void AppendRewardActionOutcome(Dictionary<string, object?> result)
    {
        var id = (result.GetValueOrDefault("parent_reward_id") ?? result.GetValueOrDefault("reward_id")) as string;
        if (id == null) return;
        RefreshRewardRun();
        var entry = _rewardSession.Entries.FirstOrDefault(e => e.Id == id);
        bool observed = entry != null && !entry.Resolving && entry.SelectionState is "claimed" or "skipped";
        result["outcome_observed"] = observed;
        result["selection_state"] = entry?.SelectionState ?? "unknown";
        result["reward_consumed"] = observed ? entry!.SelectionState == "claimed" : null;
        if (observed)
        {
            result["action_stage"] = "resolved";
            result["claimable"] = _rewardSession.Claimable.Contains(entry!);
            result["selection_closed"] = NOverlayStack.Instance?.Peek() is not NCardRewardSelectionScreen current
                || !ReferenceEquals(GetCardRewardParent(current), entry);
        }
    }

    private static void RewardChoiceTask(NCardRewardSelectionScreen __instance, ref Task<int?> __result)
    {
        // Wrap the UI's existing task so observation happens BEFORE CardReward.OnSelect resumes.
        // No choice, task result, exception or game reward is changed by this observer.
        try
        {
            var entry = GetCardRewardParent(__instance);
            if (entry == null) return;
            _rewardSession.AwaitChoice(entry);
            var options = GetInstanceFieldValue(__instance, "_options") as System.Collections.ICollection;
            var alternatives = (GetInstanceFieldValue(__instance, "_extraOptions") as IEnumerable<CardRewardAlternative>)?.ToArray();
            __result = ObserveRewardChoice(__result, entry, options?.Count, alternatives);
        }
        catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Reward choice observation failed: {ex.Message}"); }
    }

    private static async Task<int?> ObserveRewardChoice(Task<int?> task, RewardSession.Entry entry,
        int? cardCount, CardRewardAlternative[]? alternatives)
    {
        int? choice;
        try { choice = await task; }
        catch
        {
            RewardObservation(() => _rewardSession.Interrupt(entry));
            throw;
        }
        RewardObservation(() =>
        {
            if (choice == null) _rewardSession.Choice(entry, "interrupted");
            else if (cardCount.HasValue && choice >= 0 && choice < cardCount)
                _rewardSession.Choice(entry, "card");
            else if (cardCount.HasValue && alternatives != null && choice - cardCount >= 0 && choice - cardCount < alternatives.Length)
            {
                var alternative = alternatives[(choice - cardCount)!.Value];
                bool skip = alternative.OptionId.Equals("Skip", StringComparison.OrdinalIgnoreCase)
                    && alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward;
                _rewardSession.Choice(entry, skip ? "skip" : "alternative", alternative.OptionId);
            }
            else _rewardSession.Choice(entry, "unknown");
        });
        return choice;
    }
}
