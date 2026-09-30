using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_MCP.Recorder;

/// <summary>
/// Keeps the game's own combat replay (CombatReplayWriter) as a durable, gap-detecting record.
///
/// The game already records everything a replay needs - the run as it was on entering the room,
/// every enqueued action, every choice result and resumption, a checksum of the full combat state
/// after each action - but only in memory, and writes it to replays/latest.mcr, overwriting the
/// previous combat. This class copies each entry out as the game appends it, serialized by the
/// game's own PacketWriter, and adds what the game's replay does not carry: which API request (if
/// any) an action came from, which action a choice belongs to, the round/turn it happened in, and
/// a public observation at each decision boundary.
///
/// Rules this class keeps (docs/recording.md):
///  - It never changes what the game does. Everything reads; nothing here writes game state,
///    awaits, or calls game logic beyond property getters and the game's serializers. In
///    particular it does not call Anonymized() while a combat runs: IdAnonymizer draws from
///    Rng.Chaotic the first time it sees an id. The one call it makes is at close, on the replay
///    the game is about to anonymize itself in the same synchronous call (same draws, same order).
///  - It never throws into the game. Every entry point goes through Guard, and a failure marks the
///    record faulted instead of being lost.
///  - Hidden information (RNG state, draw order, full-state checksums, the run save) goes to
///    hidden.jsonl / replay.mcr only - never to public.jsonl and never to the state API. Player
///    ids (platform ids) never go to public.jsonl either; public lines use the player's slot.
/// </summary>
internal static class CombatRecorder
{
    internal const string Schema = "sts2-mod-record/1";

    private static readonly object Gate = new();
    private static readonly RecordWriter Writer = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    internal static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static readonly DateTimeOffset SessionStarted = DateTimeOffset.UtcNow;

    // --- configuration / installation -------------------------------------------------------

    internal static bool ConfigEnabled { get; private set; } = true;
    internal static string Root { get; private set; } = DefaultRoot();

    /// <summary>"not_installed" / "installed" / "failed". Patches are only applied when asked to.</summary>
    internal static string InstallState { get; private set; } = "not_installed";
    internal static string? InstallError { get; private set; }

    /// <summary>Runtime switch. Off means every hook returns at its first line.</summary>
    private static volatile bool _active;

    private static string? _dllSha256;

    // --- API attribution ---------------------------------------------------------------------

    private static long _apiSeq;
    /// <summary>The request whose handler is running on the main thread right now (0 = none).</summary>
    [ThreadStatic] private static long _handlerRequest;
    [ThreadStatic] private static Type? _expectType;
    /// <summary>The request between receipt and response (HTTP thread). A time window, not a cause.</summary>
    private static long _windowRequest;
    private static readonly ConditionalWeakTable<GameAction, StrongBox<long>> ApiActions = new();
    /// <summary>The first id each action got. Resuming after a choice gives it a new id.</summary>
    private static readonly ConditionalWeakTable<GameAction, StrongBox<uint>> ActionKeys = new();

    // --- records -----------------------------------------------------------------------------

    private sealed class PendingInitial
    {
        internal required CombatReplay Replay;
        internal required byte[] HeaderPacket;
        internal required JsonObject Ids;
        internal required JsonObject Run;
        internal required long StartTime;
        internal required int Act;
        internal required int Floor;
        /// <summary>MOD requests between entering the room and the combat (an event that starts a fight).</summary>
        internal readonly JsonArray Requests = new();
    }

    private sealed class OpenRecord
    {
        internal required string Id;
        internal required string Dir;
        internal required CombatReplayWriter? Writer;
        internal required CombatReplay? Replay;
        internal long Seq;
        internal long OpenedMs;
        internal int GameEventsBeforeOpen;
        internal int ChecksumsBeforeOpen;
        internal int Backfilled;
        internal readonly List<string> EventHashes = new();
        internal readonly List<string> ChecksumKeys = new();
        internal readonly List<string> Faults = new();
        internal readonly HashSet<GameAction> JournaledActions = new(ReferenceEqualityComparer.Instance);
        /// <summary>Choices reserved and not yet answered, per player slot.</summary>
        internal readonly Dictionary<int, SortedSet<uint>> OpenChoices = new();
        /// <summary>(slot, choice id) -> the action that owns it (from SignalPlayerChoiceBegun).</summary>
        internal readonly Dictionary<(int, uint), JsonObject> ChoiceOwners = new();
        /// <summary>The action currently paused for a choice (single player: at most one).</summary>
        internal GameAction? Paused;
        internal JsonObject? LossOutcome;
        internal readonly List<double> HookMicros = new();
        internal int SentryAtOpen;
        internal string PublicPath => Path.Combine(Dir, "public.jsonl");
        internal string HiddenPath => Path.Combine(Dir, "hidden.jsonl");
    }

    private static PendingInitial? _pending;
    private static OpenRecord? _rec;
    private static bool _inCleanup;
    private static int _faultsTotal;
    private static string? _lastFault;
    private static int _recordsOpened;
    private static int _sentryCaptures;

    // ========================================================================================
    // Configuration and install
    // ========================================================================================

    private static string DefaultRoot()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "state", "sts2", "records");
    }

    /// <summary>Reads "record" / "record_dir" from STS2_MCP.conf. Missing keys keep the defaults.</summary>
    internal static void Configure(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
            return;
        if (config.TryGetProperty("record", out var rec) && rec.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ConfigEnabled = rec.GetBoolean();
        if (config.TryGetProperty("record_dir", out var dir) && dir.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(dir.GetString()))
        {
            string d = dir.GetString()!;
            if (d.StartsWith("~/", StringComparison.Ordinal))
                d = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), d[2..]);
            Root = d;
        }
    }

    /// <summary>
    /// Applies the recorder's patches and subscriptions, all or nothing. Called at startup when the
    /// config enables recording, or later from set_recording. With "record": false in the config
    /// nothing is patched at all - the game runs exactly as with a mod that has no recorder.
    /// </summary>
    internal static bool Install()
    {
        lock (Gate)
        {
            if (InstallState == "installed")
                return true;
            try
            {
                RecorderPatches.Apply();
                SubscribeCombatEvents();
                InstallState = "installed";
                InstallError = null;
                StartDllHash();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => OnProcessExit();
                return true;
            }
            catch (Exception ex)
            {
                RecorderPatches.RemoveAll();
                InstallState = "failed";
                InstallError = $"{ex.GetType().Name}: {ex.Message}";
                GD.PrintErr($"[STS2 MCP] Recorder not installed: {InstallError}");
                return false;
            }
        }
    }

    internal static void StartIfConfigured()
    {
        if (ConfigEnabled && Install())
            _active = true;
    }

    private static void StartDllHash()
    {
        try
        {
            string path = typeof(RunManager).Assembly.Location;
            Task.Run(() =>
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    _dllSha256 = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                }
                catch (Exception ex) { NoteFault("dll_hash", ex); }
            });
        }
        catch (Exception ex) { NoteFault("dll_hash", ex); }
    }

    private static void SubscribeCombatEvents()
    {
        // CombatManager is a process-wide singleton (CombatManager.Instance { get; } = new()).
        var cm = CombatManager.Instance;
        cm.CombatSetUp += s => Guard("combat_setup", () => OnCombatSetUp(s));
        cm.CombatBegan += s => Guard("combat_began", () => OnSimple("combat_began"));
        cm.TurnStarted += s => Guard("turn_started", () => OnTurnStarted());
        cm.TurnEnded += s => Guard("turn_ended", () => OnSimple("turn_ended"));
        cm.PlayerEndedTurn += (p, canBackOut) => Guard("player_ended_turn", () => OnPlayerEndedTurn(canBackOut));
        cm.PlayerUnendedTurn += p => Guard("player_unended_turn", () => OnSimple("player_unended_turn"));
        cm.CombatWon += r => Guard("combat_won", () => OnSimple("combat_won"));
        cm.CombatEnded += r => Guard("combat_ended", () => OnCombatEnded());
    }

    // ========================================================================================
    // Runtime switch (set_recording) and status
    // ========================================================================================

    internal static Dictionary<string, object?> SetRecording(bool enabled)
    {
        if (enabled)
        {
            if (!Install())
                return new() { ["status"] = "error", ["error"] = $"Recorder could not be installed: {InstallError}" };
            lock (Gate)
            {
                if (!_active)
                {
                    _active = true;
                    // A combat already under way is recorded from here on, marked as started late.
                    // (Not after it: a finished combat's state lingers until the next room, and
                    // its replay has already been written and stopped.)
                    if (CombatManager.Instance.IsInProgress || CombatManager.Instance.IsStarting)
                        Guard("open_on_enable", () => TryOpen("recording_enabled"));
                }
            }
        }
        else
        {
            lock (Gate)
            {
                if (_rec != null)
                    CloseRecord("recording_disabled", null);
                _active = false;
                _pending = null;
            }
        }
        return new() { ["status"] = "ok", ["message"] = enabled ? "Recording on" : "Recording off", ["recording"] = Status() };
    }

    internal static Dictionary<string, object?> Status()
    {
        lock (Gate)
        {
            return new()
            {
                ["schema"] = Schema,
                ["config_enabled"] = ConfigEnabled,
                ["active"] = _active,
                ["install"] = InstallState,
                ["install_error"] = InstallError,
                ["patches"] = RecorderPatches.Applied,
                ["root"] = Root,
                ["session"] = SessionId,
                ["records_opened"] = _recordsOpened,
                ["current"] = _rec == null ? null : new Dictionary<string, object?>
                {
                    ["id"] = _rec.Id,
                    ["dir"] = _rec.Dir,
                    ["seq"] = _rec.Seq,
                    ["faults"] = _rec.Faults.Count,
                },
                ["pending_initial_state"] = _pending != null,
                ["faults_total"] = _faultsTotal,
                ["last_fault"] = _lastFault,
                ["sentry_captures"] = _sentryCaptures,
                ["writer_failures"] = Writer.Failures,
                ["writer_last_error"] = Writer.LastError,
                ["writer_pending"] = Writer.Pending,
                ["sts2_dll_sha256"] = _dllSha256,
            };
        }
    }

    // ========================================================================================
    // API attribution
    // ========================================================================================

    /// <summary>Opens the request window. Returns the request id (0 when not recording).</summary>
    internal static long ApiBegin(string action, Dictionary<string, JsonElement> parsed)
    {
        if (!_active)
            return 0;
        long id = Interlocked.Increment(ref _apiSeq);
        Interlocked.Exchange(ref _windowRequest, id);
        var args = new JsonObject();
        foreach (var (k, v) in parsed)
        {
            if (k == "action")
                continue;
            try { args[k] = JsonNode.Parse(v.GetRawText()); }
            catch { args[k] = v.GetRawText(); }
        }
        var line = new JsonObject { ["request"] = id, ["action"] = action, ["args"] = args };
        lock (Gate)
        {
            // Outside a record, kept with the room's pending state: an event that turns into a
            // fight needs the choices that led to it (P2-3 of the design review).
            if (_rec == null)
                _pending?.Requests.Add(line.DeepClone());
            else
                EmitNoGameRead("api_request", line);
        }
        return id;
    }

    internal static void ApiEnd(long id, string? status)
    {
        if (id == 0)
            return;
        Interlocked.CompareExchange(ref _windowRequest, 0, id);
        lock (Gate)
        {
            if (_rec != null)
                EmitNoGameRead("api_response", new JsonObject { ["request"] = id, ["status"] = status });
        }
    }

    /// <summary>Wraps a handler that runs on the main thread, so the action it enqueues carries the request.</summary>
    internal static Func<T> InHandler<T>(long id, Func<T> body) => () =>
    {
        long outer = _handlerRequest;
        _handlerRequest = id;
        try { return body(); }
        finally { _handlerRequest = outer; }
    };

    /// <summary>
    /// Marks the GameAction an API handler is about to enqueue as coming from the running request.
    /// Tied to the object, so an action the game holds back (a card played during the enemy turn
    /// waits for the player turn) keeps its origin when it is finally enqueued.
    ///
    /// Only the action the handler itself creates is marked: whatever the game enqueues as a
    /// consequence while the handler is still on the stack (a continuation that runs inline) is
    /// the game's own and stays "game".
    /// </summary>
    internal static T TagApiAction<T>(T action) where T : GameAction
    {
        if (_active && _handlerRequest != 0)
        {
            long request = _handlerRequest;
            Guard("tag_api_action", () => ApiActions.AddOrUpdate(action, new StrongBox<long>(request)));
        }
        return action;
    }

    /// <summary>
    /// For handlers that ask the game to build the action (PotionModel.EnqueueManualUse builds the
    /// UsePotionAction itself): the next action of <paramref name="type"/> that reaches
    /// ActionQueueSynchronizer.RequestEnqueue during the returned scope is marked.
    /// </summary>
    internal static IDisposable ExpectApiAction(Type type)
    {
        var outer = _expectType;
        _expectType = type;
        return new Scope(() => _expectType = outer);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    /// <summary>Prefix of ActionQueueSynchronizer.RequestEnqueue.</summary>
    internal static void OnRequestEnqueue(GameAction action)
    {
        if (!_active || _handlerRequest == 0 || _expectType == null || action.GetType() != _expectType)
            return;
        _expectType = null;
        long request = _handlerRequest;
        Guard("request_enqueue", () => ApiActions.AddOrUpdate(action, new StrongBox<long>(request)));
    }

    private static JsonObject OriginOf(GameAction action)
    {
        if (ApiActions.TryGetValue(action, out var box))
            return new JsonObject { ["kind"] = "mod_api", ["request"] = box.Value };
        if (action is not GenericHookGameAction && ActionQueueSet.IsGameActionPlayerDriven(action))
            return new JsonObject { ["kind"] = "not_mod_api" };
        return new JsonObject { ["kind"] = "game" };
    }

    private static JsonObject WindowOrigin()
    {
        long w = Interlocked.Read(ref _windowRequest);
        return w != 0
            ? new JsonObject { ["kind"] = "in_api_window", ["request"] = w }
            : new JsonObject { ["kind"] = "outside_api_window" };
    }

    // ========================================================================================
    // Game hooks (called from RecorderPatches and the CombatManager events)
    // ========================================================================================

    /// <summary>Postfix of CombatReplayWriter.RecordInitialState: the run as it was on entering the room.</summary>
    internal static void OnInitialState(CombatReplayWriter writer, SerializableRun serializableRun)
    {
        if (!_active)
            return;
        Guard("initial_state", () =>
        {
            lock (Gate)
            {
                _inCleanup = false;
                // A new room while a record is still open: the old one never saw its end.
                if (_rec != null)
                    CloseRecord("superseded_by_room_entry", null);

                var replay = RecorderPatches.ReplayOf(writer);
                if (replay == null)
                {
                    _pending = null;
                    return;
                }
                var run = RunManager.Instance.DebugOnlyGetState();
                _pending = new PendingInitial
                {
                    Replay = replay,
                    HeaderPacket = Packet(HeaderOf(replay)),
                    Ids = IdsOf(replay),
                    Run = RunInfo(run, serializableRun),
                    StartTime = serializableRun.StartTime,
                    Act = run?.CurrentActIndex ?? serializableRun.CurrentActIndex,
                    Floor = run?.ActFloor ?? -1,
                };
            }
        });
    }

    private static void OnCombatSetUp(CombatState state)
    {
        if (!_active)
            return;
        lock (Gate)
        {
            TryOpen("combat_setup");
            if (_rec == null)
                return;
            var data = RoomInfo(state);
            data["combat_local_id"] = CombatManager.Instance.CurrentCombatId?.Value;
            EmitWithObservation("combat_setup", data, hidden: true);
        }
    }

    private static void OnSimple(string kind)
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec != null)
                Emit(kind, null);
        }
    }

    /// <summary>A turn start is a decision boundary: public and hidden observation.</summary>
    private static void OnTurnStarted()
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec != null)
                EmitWithObservation("turn_started", null, hidden: true);
        }
    }

    /// <summary>
    /// The end-turn button ends the turn from inside an EndPlayerTurnAction, and card effects that
    /// end it (VOID_FORM) do so from inside their card's action - both are in the game's replay.
    /// A turn ended with no action running bypassed the queue, so no replay can reproduce it.
    /// </summary>
    private static void OnPlayerEndedTurn(bool canBackOut)
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec == null)
                return;
            var running = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
            if (running == null && CombatManager.Instance.IsInProgress)
                _rec.Faults.Add("end_turn_outside_action_queue");
            Emit("player_ended_turn", new JsonObject
            {
                ["can_back_out"] = canBackOut,
                ["running_action"] = running == null ? null : RecordObserve.DescribeAction(running, KeyOf(running)),
            });
        }
    }

    /// <summary>
    /// CombatEnded fires after the replay is written when the combat is won (EndCombatInternal),
    /// and with nothing written when it is lost (ProcessPendingLoss) - the replay then waits for
    /// RunManager.CleanUp, by which time CombatManager.Reset has dropped the combat. So a loss
    /// takes its observation and outcome here and closes at CleanUp.
    /// </summary>
    private static void OnCombatEnded()
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec == null)
                return;
            _rec.LossOutcome = Outcome();
            EmitWithObservation("combat_lost", new JsonObject { ["outcome"] = _rec.LossOutcome?.DeepClone() }, hidden: true);
        }
    }

    /// <summary>Postfix of the writer's RecordGameAction / RecordActionResume / RecordPlayerChoice.</summary>
    internal static void OnGameEvent(CombatReplayWriter writer, int countBefore, GameAction? action)
    {
        if (!_active)
            return;
        Guard("game_event", () =>
        {
            lock (Gate)
            {
                var replay = RecorderPatches.ReplayOf(writer);
                // The writer returns early outside combat / when disabled: nothing was appended.
                if (replay == null || countBefore < 0 || replay.events.Count != countBefore + 1)
                    return;
                if (_rec == null)
                    TryOpen("game_event");
                if (_rec == null || !ReferenceEquals(_rec.Replay, replay))
                    return;
                JournalEvent(replay, replay.events.Count - 1, action, backfilled: false);
            }
        });
    }

    private static void JournalEvent(CombatReplay replay, int index, GameAction? action, bool backfilled)
    {
        var rec = _rec!;
        var ev = replay.events[index];
        byte[] packet = Packet(ev);
        rec.EventHashes.Add(Sha(packet));

        var data = new JsonObject
        {
            ["index"] = index,
            ["type"] = ev.eventType.ToString(),
            ["player_slot"] = SlotOf(ev.playerId),
        };
        if (backfilled)
            data["backfilled"] = true;
        switch (ev.eventType)
        {
            case CombatReplayEventType.GameAction:
                if (action != null)
                {
                    uint? key = action.Id;
                    if (key != null)
                        ActionKeys.AddOrUpdate(action, new StrongBox<uint>(key.Value));
                    foreach (var (k, v) in RecordObserve.DescribeAction(action, key))
                        data[k] = v?.DeepClone();
                    data["origin"] = OriginOf(action);
                    data["cause_action_id"] = RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id;
                    rec.JournaledActions.Add(action);
                    // BeforeCancelled, not a patch on the small GameAction.Cancel (see RecorderPatches).
                    action.BeforeCancelled += OnCancelled;
                }
                data["action"] = RecordObserve.DescribeNetAction(ev.action, SlotOf);
                break;
            case CombatReplayEventType.HookAction:
                data["hook_id"] = ev.hookId;
                data["game_action_type"] = ev.gameActionType?.ToString();
                if (action != null)
                {
                    uint? key = action.Id;
                    if (key != null)
                        ActionKeys.AddOrUpdate(action, new StrongBox<uint>(key.Value));
                    data["action_id"] = action.Id;
                    data["action_key"] = key;
                    data["cause_action_id"] = RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id;
                    rec.JournaledActions.Add(action);
                    action.BeforeCancelled += OnCancelled;
                }
                break;
            case CombatReplayEventType.ResumeAction:
                data["resumed_action_id"] = ev.actionId;
                break;
            case CombatReplayEventType.PlayerChoice:
                data["choice_id"] = ev.choiceId;
                data["result"] = RecordObserve.DescribeChoiceResult(ev.playerChoiceResult, SlotOf);
                data["origin"] = WindowOrigin();
                // Copied late, the reservation was never seen: nothing to settle or to check.
                if (!backfilled)
                    AttachChoiceParent(rec, data, SlotOf(ev.playerId), ev.choiceId);
                break;
        }
        long seq = Emit("game_event", data, hidden: true, noAt: backfilled);
        EmitHidden(seq, "game_event", new JsonObject
        {
            ["index"] = index,
            ["packet"] = Convert.ToBase64String(packet),
        });
    }

    /// <summary>
    /// The owner of a choice, settled when its result arrives (the point where the choice id is
    /// final): the action SignalPlayerChoiceBegun named for it, and the action paused for a choice
    /// right now. In single player the queue is one and GetReadyAction skips a queue that is
    /// gathering a choice, so at most one action is paused. A choice with no pause at all came from
    /// a context that does not pause (BlockingPlayerChoiceContext / ThrowingPlayerChoiceContext).
    /// </summary>
    private static void AttachChoiceParent(OpenRecord rec, JsonObject data, int? slot, uint? choiceId)
    {
        int s = slot ?? -1;
        if (choiceId is { } c)
        {
            if (rec.ChoiceOwners.Remove((s, c), out var owner))
                data["owner"] = owner;
            if (rec.OpenChoices.TryGetValue(s, out var open))
            {
                if (!open.Remove(c))
                    Fault(rec, $"patch_missed:reserve:{c}", "choice result for an id ReserveChoiceId was not seen giving out");
            }
            else
            {
                Fault(rec, $"patch_missed:reserve:{c}", "choice result for an id ReserveChoiceId was not seen giving out");
            }
        }
        data["paused_action"] = rec.Paused == null ? null : RecordObserve.DescribeAction(rec.Paused, KeyOf(rec.Paused));
        data["pending_choice_ids"] = PendingChoices(rec, s);
    }

    /// <summary>Postfix of the writer's RecordChecksum.</summary>
    internal static void OnChecksum(CombatReplayWriter writer, int countBefore)
    {
        if (!_active)
            return;
        Guard("checksum", () =>
        {
            lock (Gate)
            {
                var replay = RecorderPatches.ReplayOf(writer);
                if (replay == null || countBefore < 0 || replay.checksumData.Count != countBefore + 1)
                    return;
                if (_rec == null)
                    TryOpen("checksum");
                if (_rec == null || !ReferenceEquals(_rec.Replay, replay))
                    return;
                JournalChecksum(replay, replay.checksumData.Count - 1, backfilled: false);
            }
        });
    }

    /// <summary>
    /// A checksum line carries the id and context in public and the checksum value in hidden: the
    /// value is a hash of the full state, draw order included, so a searcher that could read it
    /// could test guesses about the hidden state against it. The full state itself is not copied
    /// per action (it would need Anonymized() mid-combat); it is in replay.mcr at close.
    /// </summary>
    private static void JournalChecksum(CombatReplay replay, int index, bool backfilled)
    {
        var rec = _rec!;
        var cs = replay.checksumData[index];
        rec.ChecksumKeys.Add(ChecksumKey(cs));
        var data = new JsonObject
        {
            ["index"] = index,
            ["checksum_id"] = cs.checksumData.id,
            ["context"] = cs.context,
            // The action running now is only the checksum's action when it is copied as it happens.
            ["action_id"] = backfilled ? null : RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id,
        };
        if (backfilled)
            data["backfilled"] = true;
        long seq = Emit("checksum", data, hidden: true, noAt: backfilled);
        EmitHidden(seq, "checksum", new JsonObject
        {
            ["index"] = index,
            ["checksum_id"] = cs.checksumData.id,
            ["checksum"] = cs.checksumData.checksum,
        });
    }

    /// <summary>
    /// Postfix of ActionQueueSet.EnqueueWithoutSynchronizing. The game's writer records every
    /// action enqueued while the combat is in progress; one it did not record (its handler threw,
    /// or another handler before it did) makes the replay unreproducible, so it is flagged.
    /// Before IsInProgress (combat setup) and after it (the combat is ending) the game does not
    /// record actions at all; a replay regenerates the setup ones from the initial state.
    /// </summary>
    internal static void OnEnqueued(GameAction action)
    {
        if (!_active)
            return;
        Guard("enqueued", () =>
        {
            lock (Gate)
            {
                if (_rec == null || _rec.JournaledActions.Contains(action))
                    return;
                var data = RecordObserve.DescribeAction(action, action.Id);
                data["origin"] = OriginOf(action);
                var cm = CombatManager.Instance;
                data["phase"] = cm.IsInProgress ? "in_progress" : cm.IsStarting ? "starting" : cm.IsEnding ? "ending" : "outside";
                if (cm.IsInProgress && _rec.Writer?.IsEnabled == true)
                {
                    _rec.Faults.Add($"enqueue_unrecorded:{action.Id}");
                    Emit("enqueue_unrecorded", data);
                }
                else
                {
                    Emit("enqueue_outside_combat", data);
                }
            }
        });
    }

    internal static void OnChoiceReserved(Player player, uint choiceId)
    {
        if (!_active)
            return;
        Guard("choice_reserved", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                int slot = PlayerSlot(player) ?? -1;
                if (!_rec.OpenChoices.TryGetValue(slot, out var open))
                    _rec.OpenChoices[slot] = open = new SortedSet<uint>();
                open.Add(choiceId);
                var running = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
                Emit("choice_reserved", new JsonObject
                {
                    ["choice_id"] = choiceId,
                    ["player_slot"] = slot,
                    ["running_action"] = running == null ? null : RecordObserve.DescribeAction(running, KeyOf(running)),
                });
            }
        });
    }

    /// <summary>
    /// Prefix of SignalPlayerChoiceBegun: the choice being begun is the one this player reserved
    /// last. Every caller of ReserveChoiceId with a context (CardSelectCmd, v0.111.0) calls
    /// SignalPlayerChoiceBegun on the next line with nothing awaited in between. Read in the
    /// prefix, before the hook context's synchronous part can run other code.
    /// </summary>
    internal static uint? OnChoiceBegunPrefix(Player chooser)
    {
        if (!_active)
            return null;
        lock (Gate)
        {
            if (_rec == null)
                return null;
            int slot = PlayerSlot(chooser) ?? -1;
            return _rec.OpenChoices.TryGetValue(slot, out var open) && open.Count > 0 ? open.Max : null;
        }
    }

    /// <summary>
    /// Postfix of the concrete SignalPlayerChoiceBegun. The context names the action that owns the
    /// choice - for a hook's choice, the hook action it has just generated (set before the method's
    /// first await, so it is there when the async method returns to us).
    /// </summary>
    internal static void OnChoiceBegun(PlayerChoiceContext context, Player chooser, uint? choiceId)
    {
        if (!_active)
            return;
        Guard("choice_begun", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                GameAction? owner = context switch
                {
                    GameActionPlayerChoiceContext g => g.Action,
                    HookPlayerChoiceContext h => h.GameAction,
                    _ => null,
                };
                int slot = PlayerSlot(chooser) ?? -1;
                var data = new JsonObject
                {
                    ["choice_id"] = choiceId,
                    ["context"] = context.GetType().Name,
                    ["player_slot"] = slot,
                    ["owner"] = owner == null ? null : RecordObserve.DescribeAction(owner, KeyOf(owner)),
                };
                if (choiceId is { } c)
                {
                    var ownerJson = new JsonObject { ["context"] = context.GetType().Name };
                    if (owner != null)
                        foreach (var (k, v) in RecordObserve.DescribeAction(owner, KeyOf(owner)))
                            ownerJson[k] = v?.DeepClone();
                    _rec.ChoiceOwners[(slot, c)] = ownerJson;
                }
                Emit("choice_begun", data);
            }
        });
    }

    internal static uint? OnPausingForChoice(GameAction action) => action.Id;

    /// <summary>
    /// Postfix of ActionQueueSet.PauseActionForPlayerChoice - the pending-choice boundary. When a
    /// resumption was already waiting, the game resumes the action inside this very call and the
    /// id changes under us; both ids are written.
    /// </summary>
    internal static void OnPausedForChoice(GameAction action, PlayerChoiceOptions options, uint? idBefore)
    {
        if (!_active)
            return;
        Guard("choice_paused", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                var data = RecordObserve.DescribeAction(action, KeyOf(action));
                data["action_id_before"] = idBefore;
                data["options"] = options.ToString();
                data["state_after"] = action.State.ToString();
                data["pending_choice_ids"] = PendingChoices(_rec, OwnerSlot(action));
                if (action.State == GameActionState.GatheringPlayerChoice)
                    _rec.Paused = action;
                EmitWithObservation("choice_paused", data, hidden: true);
            }
        });
    }

    internal static void OnResumed(uint oldId, uint nextActionId)
    {
        if (!_active)
            return;
        Guard("action_resumed", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                var data = new JsonObject
                {
                    ["old_action_id"] = oldId,
                    // ResumeActionWithoutSynchronizing hands out exactly one new id after the event.
                    ["new_action_id"] = nextActionId - 1,
                };
                // A choice the game settles without asking (e.g. HEADBUTT with one card in the
                // discard pile) is reserved and resumed with no PlayerChoice result in between.
                if (_rec.Paused != null)
                {
                    var paused = _rec.Paused;
                    uint? key = KeyOf(paused);
                    uint? hook = (paused as GenericHookGameAction)?.HookId;
                    var unanswered = new JsonArray();
                    foreach (var ((slot, choice), owner) in _rec.ChoiceOwners.ToList())
                    {
                        bool mine = (key != null && AsUInt(owner["action_key"]) == key)
                                    || (hook != null && AsUInt(owner["hook_id"]) == hook);
                        if (!mine)
                            continue;
                        _rec.ChoiceOwners.Remove((slot, choice));
                        if (_rec.OpenChoices.TryGetValue(slot, out var open))
                            open.Remove(choice);
                        unanswered.Add(choice);
                    }
                    data["unanswered_choice_ids"] = unanswered;
                }
                if (_rec.Paused != null && (_rec.Paused.Id == oldId || _rec.Paused.Id == nextActionId - 1))
                {
                    data["action_key"] = KeyOf(_rec.Paused);
                    data["class"] = _rec.Paused.GetType().Name;
                    _rec.Paused = null;
                }
                Emit("action_resumed", data);
            }
        });
    }

    private static void OnCancelled(GameAction action)
    {
        if (!_active)
            return;
        Guard("action_cancelled", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                Emit("action_cancelled", RecordObserve.DescribeAction(action, KeyOf(action)));
            }
        });
    }

    /// <summary>
    /// Prefix of ActionExecutor.AfterActionFinished (reached through the action's AfterFinished
    /// delegate). A player-driven action finishing is a decision boundary and gets a public
    /// observation; the game's hook actions only get a line.
    /// </summary>
    internal static void OnActionFinished(GameAction action)
    {
        if (!_active)
            return;
        Guard("action_finished", () =>
        {
            lock (Gate)
            {
                if (_rec == null || action.State != GameActionState.Finished)
                    return;
                var data = RecordObserve.DescribeAction(action, KeyOf(action));
                bool boundary = action is not GenericHookGameAction && ActionQueueSet.IsGameActionPlayerDriven(action);
                if (boundary)
                    EmitWithObservation("action_finished", data, hidden: false);
                else
                    Emit("action_finished", data);
            }
        });
    }

    internal static void OnSentryCapture() => Interlocked.Increment(ref _sentryCaptures);

    internal static void OnCleanUpStart() => _inCleanup = true;
    internal static void OnCleanUpEnd() => _inCleanup = false;

    /// <summary>
    /// Prefix of CombatReplayWriter.WriteReplay. When the game is about to write (and stop) its
    /// replay, the record is closed with a copy of exactly that replay.
    /// </summary>
    internal static void OnWriteReplay(CombatReplayWriter writer, bool stopRecording)
    {
        if (!_active || !stopRecording)
            return;
        Guard("write_replay", () =>
        {
            lock (Gate)
            {
                var replay = RecorderPatches.ReplayOf(writer);
                if (_rec == null || replay == null || !ReferenceEquals(_rec.Replay, replay))
                    return;
                // Only two callers stop the replay: CombatManager.EndCombatInternal (won; the
                // CombatWon / CombatEnded events fire after this) and RunManager.CleanUp (after a
                // loss, or leaving mid-combat: save and quit, closing the window).
                string reason = !_inCleanup ? "combat_won" : _rec.LossOutcome != null ? "combat_lost" : "cleanup";
                CloseRecord(reason, replay);
                if (_inCleanup)
                    Writer.Drain(1000); // the window may be closing (CleanUp(graceful: false))
            }
        });
    }

    private static void OnProcessExit()
    {
        try
        {
            lock (Gate)
            {
                if (_rec != null)
                    CloseRecord("process_exit", null);
            }
            Writer.Drain(2000);
        }
        catch { /* the process is going away */ }
    }

    // ========================================================================================
    // Record lifecycle
    // ========================================================================================

    private static void TryOpen(string trigger)
    {
        if (_rec != null)
            return;
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null)
            return;
        var writer = RunManager.Instance.CombatReplayWriter;
        var replay = writer == null ? null : RecorderPatches.ReplayOf(writer);

        var pending = _pending != null && replay != null && ReferenceEquals(_pending.Replay, replay) ? _pending : null;
        int eventsBefore = replay?.events.Count ?? 0;
        int checksBefore = replay?.checksumData.Count ?? 0;
        var room = run.CurrentRoom;
        bool eventRoom = (room as CombatRoom)?.ParentEventId != null || run.CurrentRoomCount > 1;
        string boundary = eventRoom
            ? "event_room"
            : pending != null && eventsBefore == 0 && checksBefore == 0
                ? "room_entry"
                : CombatManager.Instance.IsInProgress ? "mid_combat" : "mid_room";

        long startTime = pending?.StartTime ?? RecorderPatches.RunStartTime();
        int act = pending?.Act ?? run.CurrentActIndex;
        int floor = pending?.Floor ?? run.ActFloor;

        string runDir = Path.Combine(Root, startTime.ToString());
        (int number, int attempt) = NextNumbers(runDir, act, floor);
        string name = $"{number:D3}-a{act}f{floor:D2}";
        _rec = new OpenRecord
        {
            Id = $"{startTime}/{name}",
            Dir = Path.Combine(runDir, name),
            Writer = writer,
            Replay = replay,
            OpenedMs = Clock.ElapsedMilliseconds,
            GameEventsBeforeOpen = eventsBefore,
            ChecksumsBeforeOpen = checksBefore,
            SentryAtOpen = _sentryCaptures,
        };
        _recordsOpened++;

        Emit("record_open", new JsonObject
        {
            ["schema"] = Schema,
            ["record_id"] = _rec.Id,
            ["trigger"] = trigger,
            ["attempt"] = attempt,
            ["session"] = new JsonObject
            {
                ["id"] = SessionId,
                ["pid"] = System.Environment.ProcessId,
                ["started_utc"] = SessionStarted.ToString("O"),
            },
            ["mod"] = new JsonObject
            {
                ["version"] = McpMod.Version,
                ["commit"] = McpMod.BuildCommit,
                ["state_schema"] = McpMod.StateSchemaVersion,
            },
            ["game"] = new JsonObject
            {
                ["version"] = ReleaseInfoManager.Instance.ReleaseInfo?.Version,
                ["commit"] = ReleaseInfoManager.Instance.ReleaseInfo?.Commit,
                ["model_id_hash"] = ModelIdSerializationCache.Hash,
                ["sts2_dll_sha256"] = _dllSha256,
            },
            ["profile_id"] = SafeProfileId(),
            ["run"] = pending?.Run.DeepClone() ?? RunInfo(run, null),
            ["start"] = new JsonObject
            {
                ["boundary"] = boundary,
                // The game's replay starts at the room's entry either way; whether this record
                // copied that start itself (room_entry) or read it back from the game later.
                ["initial_state"] = replay != null ? (pending != null ? "room_entry" : "read_back") : "missing",
                ["game_events_before_open"] = eventsBefore,
                ["checksums_before_open"] = checksBefore,
                ["game_writer_enabled"] = writer?.IsEnabled,
                ["game_writer_recording"] = replay != null,
                ["reproducible_from_start"] = boundary == "room_entry",
                ["pre_combat_requests"] = eventRoom ? pending?.Requests.DeepClone() : null,
            },
        });

        if (replay != null)
        {
            // Counters as the game holds them now; the header's are from the room entry. They
            // differ when something ran in between (an event before its fight).
            var idsNow = IdsNow();
            var ids = pending?.Ids.DeepClone() ?? IdsOf(replay);
            long seq = Emit("initial_state", new JsonObject { ["ids"] = ids, ["ids_at_open"] = idsNow }, hidden: true);
            EmitHidden(seq, "initial_state", new JsonObject
            {
                ["format"] = "CombatReplay packet (events and checksums empty), game PacketWriter; player ids as the game holds them",
                ["packet"] = Convert.ToBase64String(pending?.HeaderPacket ?? Packet(HeaderOf(replay))),
            });

            // Enabled mid-combat: copy what the game already holds, marked as such. The MOD's own
            // lines (origin, turn context, observations) for that stretch do not exist.
            for (int i = 0; i < eventsBefore; i++)
                JournalEvent(replay, i, null, backfilled: true);
            for (int i = 0; i < checksBefore; i++)
                JournalChecksum(replay, i, backfilled: true);
            _rec.Backfilled = eventsBefore + checksBefore;
        }
        _pending = null;
    }

    private static void CloseRecord(string reason, CombatReplay? replay)
    {
        var rec = _rec;
        if (rec == null)
            return;

        var selfCheck = new JsonObject();
        if (replay != null)
        {
            try
            {
                selfCheck["events"] = Compare(replay.events.Select(e => Sha(Packet(e))).ToList(), rec.EventHashes);
                selfCheck["checksums"] = Compare(replay.checksumData.Select(ChecksumKey).ToList(), rec.ChecksumKeys);
                // The game anonymizes this same replay on the next line of WriteReplay; doing it one
                // call earlier makes the same Rng.Chaotic draws in the same order (IdAnonymizer caches).
                byte[] mcr = Packet(replay.Anonymized());
                selfCheck["replay_mcr_sha256"] = Sha(mcr);
                selfCheck["replay_mcr_bytes"] = mcr.Length;
                Writer.WriteRaw(Path.Combine(rec.Dir, "replay.mcr"), mcr);
            }
            catch (Exception ex)
            {
                rec.Faults.Add($"close:{ex.GetType().Name}");
                selfCheck["error"] = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        var outcome = rec.LossOutcome ?? Outcome();
        Emit("record_close", new JsonObject
        {
            ["reason"] = reason,
            ["outcome"] = outcome,
            ["game_replay_captured"] = replay != null,
            ["journal"] = new JsonObject
            {
                ["game_events"] = rec.EventHashes.Count,
                ["checksums"] = rec.ChecksumKeys.Count,
                ["backfilled"] = rec.Backfilled,
                ["last_seq"] = rec.Seq,
            },
            ["self_check"] = selfCheck,
            ["sentry_captures"] = _sentryCaptures - rec.SentryAtOpen,
            ["main_thread_us"] = Timing(rec.HookMicros),
            ["faults"] = new JsonArray(rec.Faults.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
        });
        Writer.CloseFile(rec.PublicPath);
        Writer.CloseFile(rec.HiddenPath);
        _rec = null;
    }

    /// <summary>The journal against the game's own list at close, position by position.</summary>
    private static JsonObject Compare(List<string> game, List<string> journal)
    {
        int? firstMismatch = null;
        for (int i = 0; i < Math.Max(game.Count, journal.Count); i++)
        {
            if (i >= game.Count || i >= journal.Count || game[i] != journal[i])
            {
                firstMismatch = i;
                break;
            }
        }
        return new JsonObject
        {
            ["game"] = game.Count,
            ["journal"] = journal.Count,
            ["first_mismatch"] = firstMismatch,
            ["complete"] = firstMismatch == null,
        };
    }

    private static JsonObject Timing(List<double> micros)
    {
        if (micros.Count == 0)
            return new JsonObject { ["count"] = 0 };
        var sorted = micros.OrderBy(x => x).ToList();
        double P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1)];
        return new JsonObject
        {
            ["count"] = sorted.Count,
            ["p50"] = Math.Round(P(0.50), 1),
            ["p99"] = Math.Round(P(0.99), 1),
            ["max"] = Math.Round(sorted[^1], 1),
            ["total_ms"] = Math.Round(sorted.Sum() / 1000.0, 2),
        };
    }

    private static (int number, int attempt) NextNumbers(string runDir, int act, int floor)
    {
        int max = 0, same = 0;
        string suffix = $"-a{act}f{floor:D2}";
        try
        {
            if (Directory.Exists(runDir))
            {
                foreach (var d in Directory.GetDirectories(runDir))
                {
                    string n = Path.GetFileName(d);
                    int dash = n.IndexOf('-');
                    if (dash > 0 && int.TryParse(n[..dash], out int k))
                        max = Math.Max(max, k);
                    if (n.EndsWith(suffix, StringComparison.Ordinal))
                        same++;
                }
            }
        }
        catch (Exception ex) { NoteFault("next_numbers", ex); }
        return (max + 1, same + 1);
    }

    // ========================================================================================
    // Emit helpers (all called with Gate held)
    // ========================================================================================

    /// <param name="noAt">For entries copied after the fact (backfill): "at" would be the
    /// moment of copying, not of the entry, so it is left out.</param>
    private static long Emit(string kind, JsonObject? data, bool hidden = false, bool noAt = false)
    {
        var rec = _rec;
        if (rec == null)
            return -1;
        var line = new JsonObject
        {
            ["seq"] = rec.Seq++,
            ["kind"] = kind,
            ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs,
            ["at"] = noAt ? null : SafeAt(),
        };
        if (hidden)
            line["hidden"] = true;
        MoveInto(line, data);
        Writer.AppendLine(rec.PublicPath, line);
        return line["seq"]!.GetValue<long>();
    }

    private static void EmitHidden(long seq, string kind, JsonObject data)
    {
        var rec = _rec;
        if (rec == null || seq < 0)
            return;
        var line = new JsonObject { ["seq"] = seq, ["kind"] = kind };
        MoveInto(line, data);
        Writer.AppendLine(rec.HiddenPath, line);
    }

    private static void EmitWithObservation(string kind, JsonObject? data, bool hidden)
    {
        var obs = SafeObserve();
        data ??= new JsonObject();
        if (obs is { } o)
            data["observation"] = o.pub;
        bool withHidden = hidden && obs != null;
        long seq = Emit(kind, data, hidden: withHidden);
        if (withHidden)
            EmitHidden(seq, "observation", new JsonObject { ["observation"] = obs!.Value.hidden });
    }

    /// <summary>For lines produced off the main thread (HTTP): no "at", no game reads.</summary>
    private static void EmitNoGameRead(string kind, JsonObject data)
    {
        var rec = _rec;
        if (rec == null)
            return;
        var line = new JsonObject { ["seq"] = rec.Seq++, ["kind"] = kind, ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs };
        MoveInto(line, data);
        Writer.AppendLine(rec.PublicPath, line);
    }

    private static void MoveInto(JsonObject line, JsonObject? data)
    {
        if (data == null)
            return;
        foreach (var (k, v) in data.ToList())
        {
            data.Remove(k);
            line[k] = v;
        }
    }

    private static void Fault(OpenRecord rec, string fault, string message)
    {
        rec.Faults.Add(fault);
        Emit("recorder_fault", new JsonObject { ["where"] = fault, ["message"] = message });
    }

    private static JsonNode? SafeAt()
    {
        try { return RecordObserve.At(); }
        catch (Exception ex) { NoteFault("at", ex); return null; }
    }

    private static (JsonObject pub, JsonObject hidden)? SafeObserve()
    {
        try { return RecordObserve.Observe(SlotOf); }
        catch (Exception ex) { NoteFault("observe", ex); return null; }
    }

    /// <summary>
    /// The one door into the recorder from the game. Catches everything (the game's own handlers
    /// around ActionResumed and PlayerChoiceReceived have no try/catch, and ActionEnqueued's sends
    /// what it catches to Sentry), and times the work done on the game's thread.
    /// </summary>
    private static void Guard(string where, Action body)
    {
        long start = Stopwatch.GetTimestamp();
        try { body(); }
        catch (Exception ex) { NoteFault(where, ex); }
        finally
        {
            try
            {
                var rec = _rec;
                if (rec != null && rec.HookMicros.Count < 200_000)
                    rec.HookMicros.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
            }
            catch { /* timing only */ }
        }
    }

    private static void NoteFault(string where, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Interlocked.Increment(ref _faultsTotal);
                _lastFault = $"{where}: {ex.GetType().Name}: {ex.Message}";
                var rec = _rec;
                if (rec == null)
                    return;
                rec.Faults.Add(where);
                Writer.AppendLine(rec.PublicPath, new JsonObject
                {
                    ["seq"] = rec.Seq++,
                    ["kind"] = "recorder_fault",
                    ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs,
                    ["where"] = where,
                    ["exception"] = ex.GetType().Name,
                    ["message"] = ex.Message,
                });
            }
        }
        catch { /* never throw into the game */ }
    }

    // ========================================================================================
    // Small readers
    // ========================================================================================

    private static readonly PacketWriter PacketWriterInstance = new() { WarnOnGrow = false };

    /// <summary>The game's own serializer (a writer of our own), into a fresh array.</summary>
    private static byte[] Packet<T>(T value) where T : IPacketSerializable
    {
        lock (PacketWriterInstance)
        {
            var w = PacketWriterInstance;
            w.Reset();
            w.Write(value);
            w.ZeroByteRemainder();
            return w.Buffer.AsSpan(0, w.BytePosition).ToArray();
        }
    }

    /// <summary>The replay's header: the same fields, events and checksums left empty. A data copy.</summary>
    private static CombatReplay HeaderOf(CombatReplay replay) => new()
    {
        version = replay.version,
        gitCommit = replay.gitCommit,
        modelIdHash = replay.modelIdHash,
        choiceIds = replay.choiceIds.ToList(),
        rewardIds = replay.rewardIds.ToList(),
        nextActionId = replay.nextActionId,
        nextChecksumId = replay.nextChecksumId,
        nextHookId = replay.nextHookId,
        serializableRun = replay.serializableRun,
    };

    private static string ChecksumKey(ReplayChecksumData c) => $"{c.checksumData.id}:{c.checksumData.checksum}:{c.context}";

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonObject IdsOf(CombatReplay replay) => new()
    {
        ["next_action_id"] = replay.nextActionId,
        ["next_hook_id"] = replay.nextHookId,
        ["next_checksum_id"] = replay.nextChecksumId,
        ["choice_ids"] = new JsonArray(replay.choiceIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
        ["reward_ids"] = new JsonArray(replay.rewardIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
    };

    private static JsonObject IdsNow()
    {
        var rm = RunManager.Instance;
        return new JsonObject
        {
            ["next_action_id"] = rm.ActionQueueSet?.NextActionId,
            ["next_hook_id"] = rm.ActionQueueSynchronizer?.NextHookId,
            ["next_checksum_id"] = rm.ChecksumTracker?.NextId,
            ["choice_ids"] = rm.PlayerChoiceSynchronizer == null ? null
                : new JsonArray(rm.PlayerChoiceSynchronizer.ChoiceIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
        };
    }

    private static JsonObject RoomInfo(CombatState state)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        var room = run?.CurrentRoom;
        return new JsonObject
        {
            ["encounter"] = state.Encounter?.Id.Entry,
            ["room_type"] = room?.RoomType.ToString(),
            ["room_class"] = room?.GetType().Name,
            ["parent_event"] = (room as CombatRoom)?.ParentEventId?.Entry,
            ["room_count"] = run?.CurrentRoomCount,
            ["enemies"] = new JsonArray(state.Enemies.Select(e => (JsonNode)new JsonObject
            {
                ["combat_id"] = e.CombatId,
                ["monster"] = e.Monster?.Id.Entry,
            }).ToArray()),
        };
    }

    private static JsonObject? Outcome()
    {
        try
        {
            var combat = CombatManager.Instance.DebugOnlyGetState();
            var me = RecordObserve.LocalPlayer();
            if (combat == null || me == null)
                return null;
            return new JsonObject
            {
                ["player_hp"] = me.Creature.CurrentHp,
                ["player_alive"] = me.Creature.IsAlive,
                ["enemies_alive"] = combat.Enemies.Count(e => e.IsAlive),
                ["round"] = combat.RoundNumber,
                ["turn"] = me.PlayerCombatState?.TurnNumber,
            };
        }
        catch (Exception ex) { NoteFault("outcome", ex); return null; }
    }

    private static JsonObject RunInfo(RunState? run, SerializableRun? save)
    {
        var player = run == null ? null : RecordObserve.LocalPlayer();
        return new JsonObject
        {
            ["start_time"] = save?.StartTime ?? (run == null ? null : RecorderPatches.RunStartTime()),
            ["character"] = player?.Character.Id.Entry,
            ["ascension"] = run?.AscensionLevel ?? save?.Ascension,
            ["act"] = run?.CurrentActIndex ?? save?.CurrentActIndex,
            ["act_floor"] = run?.ActFloor,
            ["total_floor"] = run?.TotalFloor,
            ["map_coord"] = run?.CurrentMapCoord is { } c ? new JsonObject { ["col"] = c.col, ["row"] = c.row } : null,
        };
    }

    private static uint? AsUInt(JsonNode? node)
    {
        try { return node is JsonValue v && v.TryGetValue(out uint u) ? u : null; }
        catch { return null; }
    }

    private static uint? KeyOf(GameAction action) =>
        ActionKeys.TryGetValue(action, out var box) ? box.Value : action.Id;

    private static JsonArray PendingChoices(OpenRecord rec, int? slot) =>
        new((rec.OpenChoices.TryGetValue(slot ?? -1, out var open) ? open : new SortedSet<uint>())
            .Select(c => (JsonNode)JsonValue.Create(c)).ToArray());

    private static int? PlayerSlot(Player player)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null)
            return null;
        for (int i = 0; i < run.Players.Count; i++)
            if (ReferenceEquals(run.Players[i], player))
                return i;
        return null;
    }

    /// <summary>A platform player id as the player's slot in the run (never the id itself).</summary>
    internal static int? SlotOf(ulong? netId)
    {
        if (netId is not { } id)
            return null;
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null)
            return null;
        for (int i = 0; i < run.Players.Count; i++)
            if (run.Players[i].NetId == id)
                return i;
        return -1;
    }

    private static int? OwnerSlot(GameAction action)
    {
        try { return SlotOf(action.OwnerId); }
        catch { return null; }
    }

    private static int? SafeProfileId()
    {
        try { return SaveManager.Instance.CurrentProfileId; }
        catch { return null; }
    }
}
