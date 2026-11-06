using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int TaskWithoutSettleTimeoutMs = 10_000;

    // A monotonic deadline shared by console dispatch, Task completion and settle polls.
    private sealed class ActionDeadline(int timeoutMs)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        internal int TimeoutMs { get; } = timeoutMs;
        internal int ElapsedMs => (int)_elapsed.ElapsedMilliseconds;
        internal int RemainingMs => Math.Max(0, TimeoutMs - ElapsedMs);
        internal int MainThreadWaitMs => Math.Min(MainThreadTimeoutMs, RemainingMs);
    }

    /// <summary>
    /// Polls the state from the HTTP thread until the game has settled, then appends
    /// `waited_ms` / `polls` / `settled` / `changed` (and `state`) to the action's own result.
    /// </summary>
    private static void AppendSettleResult(
        Dictionary<string, object?> result, string? before, int timeoutMs, bool includeState, ActionDeadline? deadline = null)
    {
        var elapsed = Stopwatch.StartNew();
        int polls = 0;
        int consecutiveSettled = 0;
        bool changed = false;
        bool settled = false;
        string? waitError = null;
        Dictionary<string, object?>? last = null;

        while (true)
        {
            if (deadline?.RemainingMs == 0) break;
            Dictionary<string, object?>? state = null;
            try
            {
                state = RunOnMainThreadBlocking(() => BuildGameState(), deadline?.MainThreadWaitMs ?? MainThreadTimeoutMs);
            }
            catch (MainThreadUnavailableException ex)
            {
                // The action itself already ran; only our observation of it failed. Turning this
                // into a 503 would tell the caller "nothing was applied", which is a lie.
                waitError = ex.Message;
                break;
            }
            catch (Exception ex)
            {
                // BuildGameState throws while a scene is swapping (run and room models are torn
                // down mid-frame). That is the opposite of settled, so keep polling.
                waitError = ex.Message;
                consecutiveSettled = 0;
            }
            polls++;

            if (state != null)
            {
                last = state;
                waitError = null;
                if (before != null && StateFingerprint(state) != before)
                    changed = true;

                if (IsSettledState(state))
                {
                    if (++consecutiveSettled >= SettleConsecutivePolls)
                    {
                        settled = true;
                        break;
                    }
                }
                else
                {
                    consecutiveSettled = 0;
                }
            }

            // Nothing moved at all: the action was a no-op as far as the game is concerned, and
            // no amount of further waiting will change that.
            if (before != null && !changed && elapsed.ElapsedMilliseconds >= SettleNoChangeGiveUpMs)
            {
                settled = true;
                break;
            }

            if (elapsed.ElapsedMilliseconds >= timeoutMs)
                break;

            Thread.Sleep(deadline == null ? SettlePollIntervalMs : Math.Min(SettlePollIntervalMs, deadline.RemainingMs));
        }

        elapsed.Stop();
        result["waited_ms"] = (int)elapsed.ElapsedMilliseconds;
        result["polls"] = polls;
        result["settled"] = settled;
        result["changed"] = changed;
        if (waitError != null)
            result["wait_error"] = waitError;
        if (includeState && last != null)
            result["state"] = last;
    }

    /// <summary>
    /// Runs a singleplayer action on the main thread and, when the caller passed `wait`, keeps
    /// reading the state until the game has settled - so a single round trip answers both
    /// "was the action accepted?" and "what does the game look like now?".
    /// </summary>
    private static Dictionary<string, object?> ExecuteWithOptionalWait(
        HttpListenerRequest request, Dictionary<string, JsonElement> parsed,
        Func<Dictionary<string, object?>> action,
        Action<Dictionary<string, object?>, ActionDeadline>? completeOnHttpThread = null)
    {
        bool wait = ReadFlagParam(parsed, request, "wait", false);
        int timeoutMs = wait ? Math.Clamp(
            ReadOptionalInt(parsed, "wait_timeout_ms")
                ?? (int.TryParse(request.QueryString["wait_timeout_ms"], out int q) ? q : SettleDefaultTimeoutMs),
            SettleMinTimeoutMs, SettleMaxTimeoutMs) : TaskWithoutSettleTimeoutMs;
        // Other actions retain their existing wait behavior. Console requests also bound the
        // pre-state and dispatch, so stalled frames cannot add separate main-thread timeouts.
        var deadline = completeOnHttpThread == null ? null : new ActionDeadline(timeoutMs);
        if (!wait)
        {
            var immediate = RunOnMainThreadBlocking(action, deadline?.MainThreadWaitMs ?? MainThreadTimeoutMs);
            completeOnHttpThread?.Invoke(immediate, deadline!);
            if (deadline != null) immediate["waited_ms"] = deadline.ElapsedMs;
            return EnsureStatus(immediate);
        }

        bool includeState = ReadFlagParam(parsed, request, "include_state", true);

        // Snapshot before acting, so an action that changes nothing can be recognised instead of
        // being waited out. A MainThreadUnavailableException here is a genuine 503: nothing ran yet.
        string? before = null;
        try { before = StateFingerprint(RunOnMainThreadBlocking(() => BuildGameState(), deadline?.MainThreadWaitMs ?? MainThreadTimeoutMs)); }
        catch (MainThreadUnavailableException) { throw; }
        catch { /* an unreadable pre-state only costs us the no-op shortcut */ }

        if (deadline?.RemainingMs == 0)
            throw new MainThreadUnavailableException("dev_console deadline expired before dispatch; re-read state before retrying");
        var result = EnsureStatus(RunOnMainThreadBlocking(action, deadline?.MainThreadWaitMs ?? MainThreadTimeoutMs));
        // Console tasks resume on the game's synchronization context. Only the HTTP worker
        // may wait for them; completion must precede both the response and the settle polls.
        completeOnHttpThread?.Invoke(result, deadline!);

        // Waiting on an action the game rejected would just burn the whole timeout.
        if (result.GetValueOrDefault("status") as string == "ok")
        {
            AppendSettleResult(result, before, deadline?.RemainingMs ?? timeoutMs, includeState, deadline);
            if (result.ContainsKey("reward_id") || result.ContainsKey("parent_reward_id"))
            {
                try { RunOnMainThreadBlocking(() => { AppendRewardActionOutcome(result); return true; }); }
                catch (MainThreadUnavailableException) { result["outcome_observed"] = false; }
            }
        }

        if (deadline != null) result["waited_ms"] = deadline.ElapsedMs;
        return result;
    }
}
