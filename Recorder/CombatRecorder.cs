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
/// a public observation at each boundary.
///
/// Rules this class keeps (docs/recording.md):
///  - It never changes what the game does. Everything reads; nothing here writes game state,
///    awaits, or calls game logic beyond property getters and the game's serializers.
///  - It never throws into the game. Every entry point catches, and a failure marks the record
///    faulted instead of being lost.
///  - Hidden information (RNG state, draw order, the game's full-state checksums, the run save)
///    goes to hidden.jsonl / replay.mcr only - never to public.jsonl and never to the state API.
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

    /// <summary>"not_installed" / "installed" / "failed". Patches are only applied when the config says so.</summary>
    internal static string InstallState { get; private set; } = "not_installed";
    internal static string? InstallError { get; private set; }

    /// <summary>Runtime switch. Off means every hook returns at its first line.</summary>
    private static volatile bool _active;
    internal static bool Active => _active;

    private static string? _dllSha256;
    private static string? _dllPath;

    // --- API attribution ---------------------------------------------------------------------

    private static long _apiSeq;
    /// <summary>The request whose handler is running on the main thread right now (0 = none).</summary>
    [ThreadStatic] private static long _handlerRequest;
    /// <summary>The request between receipt and response (HTTP thread). A time window, not a cause.</summary>
    private static long _windowRequest;
    private static readonly ConditionalWeakTable<GameAction, StrongBox<long>> ApiActions = new();

    // --- records -----------------------------------------------------------------------------

    private sealed class PendingInitial
    {
        internal required CombatReplayWriter Writer;
        internal required CombatReplay Replay;
        internal required byte[] HeaderPacket;
        internal required JsonObject Ids;
        internal required JsonObject Run;
        internal required long StartTime;
        internal required int Act;
        internal required int Floor;
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
        internal readonly List<string> EventHashes = new();
        internal readonly List<string> ChecksumHashes = new();
        internal readonly List<string> Faults = new();
        internal readonly HashSet<GameAction> JournaledActions = new(ReferenceEqualityComparer.Instance);
        internal uint? LastReservedChoice;
        internal string PublicPath => Path.Combine(Dir, "public.jsonl");
        internal string HiddenPath => Path.Combine(Dir, "hidden.jsonl");
    }

    private static PendingInitial? _pending;
    private static OpenRecord? _rec;
    private static bool _inCleanup;
    private static int _faultsTotal;
    private static string? _lastFault;
    private static int _recordsOpened;

    // ========================================================================================
    // Configuration and install
    // ========================================================================================

    private static string DefaultRoot()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "state", "sts2", "records");
    }

    /// <summary>Reads "record" / "record_dir" from STS2_MCP.conf. Missing keys keep the defaults.</summary>
    internal static void Configure(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } root)
            return;
        if (root.TryGetProperty("record", out var rec) && rec.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ConfigEnabled = rec.GetBoolean();
        if (root.TryGetProperty("record_dir", out var dir) && dir.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(dir.GetString()))
        {
            string d = dir.GetString()!;
            if (d.StartsWith("~/", StringComparison.Ordinal))
                d = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), d[2..]);
            Root = d;
        }
    }

    /// <summary>
    /// Applies the recorder's patches, all or nothing. Called at startup when the config enables
    /// recording, or later from set_recording. With "record": false in the config nothing is
    /// patched at all - the game runs exactly as with a mod that has no recorder.
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
        if (!ConfigEnabled)
            return;
        if (Install())
            _active = true;
    }

    private static void StartDllHash()
    {
        try
        {
            _dllPath = typeof(RunManager).Assembly.Location;
            string path = _dllPath;
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
        var cm = CombatManager.Instance;
        cm.CombatSetUp += s => Guard("combat_setup", () => OnCombatSetUp(s));
        cm.CombatBegan += s => Guard("combat_began", () => Emit("combat_began", null));
        cm.TurnStarted += s => Guard("turn_started", () => OnTurnBoundary("turn_started"));
        cm.TurnEnded += s => Guard("turn_ended", () => OnTurnBoundary("turn_ended"));
        cm.PlayerEndedTurn += (p, canBackOut) => Guard("player_ended_turn", () => OnPlayerEndedTurn(canBackOut));
        cm.PlayerUnendedTurn += p => Guard("player_unended_turn", () => Emit("player_unended_turn", null));
        cm.CombatWon += r => Guard("combat_won", () => Emit("combat_won", null));
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
                    if (CombatManager.Instance.IsInProgress || CombatManager.Instance.DebugOnlyGetState() != null)
                        TryOpen("recording_enabled");
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
        // The HTTP thread must not read game objects; the line is built without "at".
        EmitFromAnyThread("api_request", new JsonObject { ["request"] = id, ["action"] = action, ["args"] = args });
        return id;
    }

    internal static void ApiEnd(long id, string? status)
    {
        if (id == 0)
            return;
        Interlocked.CompareExchange(ref _windowRequest, 0, id);
        EmitFromAnyThread("api_response", new JsonObject { ["request"] = id, ["status"] = status });
    }

    /// <summary>Wraps a handler that runs on the main thread, so actions it enqueues carry the request.</summary>
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
            Guard("tag_api_action", () => ApiActions.AddOrUpdate(action, new StrongBox<long>(_handlerRequest)));
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

    [ThreadStatic] private static Type? _expectType;

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
        Guard("request_enqueue", () => ApiActions.AddOrUpdate(action, new StrongBox<long>(_handlerRequest)));
    }

    private static JsonObject OriginOf(GameAction action)
    {
        if (ApiActions.TryGetValue(action, out var box))
            return new JsonObject { ["kind"] = "mod_api", ["request"] = box.Value };
        if (action is GenericHookGameAction)
            return new JsonObject { ["kind"] = "game" };
        if (ActionQueueSet.IsGameActionPlayerDriven(action))
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
    // Game hooks (called from RecorderPatches)
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
                    Writer = writer,
                    Replay = replay,
                    HeaderPacket = Packet(replay.Anonymized()),
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
            var room = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
            var data = new JsonObject
            {
                ["encounter"] = state.Encounter?.Id.Entry,
                ["room_type"] = room?.RoomType.ToString(),
                ["room_class"] = room?.GetType().Name,
                ["parent_event"] = (room as CombatRoom)?.ParentEventId?.Entry,
                ["enemies"] = new JsonArray(state.Enemies.Select(e => (JsonNode)new JsonObject
                {
                    ["combat_id"] = e.CombatId,
                    ["monster"] = e.Monster?.Id.Entry,
                }).ToArray()),
            };
            EmitWithObservation("combat_setup", data);
        }
    }

    private static void OnTurnBoundary(string kind)
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec == null)
                return;
            EmitWithObservation(kind, null);
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
                ["running_action_id"] = running?.Id,
                ["running_action_class"] = running?.GetType().Name,
            });
        }
    }

    private static void OnCombatEnded()
    {
        if (!_active)
            return;
        lock (Gate)
        {
            if (_rec == null)
                return;
            EmitWithObservation("combat_ended", null);
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
                if (replay == null || replay.events.Count != countBefore + 1)
                    return; // the game did not record anything (not in combat, or disabled)
                if (_rec == null)
                    TryOpen("game_event");
                if (_rec == null || !ReferenceEquals(_rec.Replay, replay))
                    return;

                int index = replay.events.Count - 1;
                var ev = replay.events[index];
                byte[] packet = Packet(ev.Anonymized());
                _rec.EventHashes.Add(Sha(packet));

                var data = new JsonObject
                {
                    ["index"] = index,
                    ["type"] = ev.eventType.ToString(),
                };
                switch (ev.eventType)
                {
                    case CombatReplayEventType.GameAction:
                        if (action != null)
                        {
                            foreach (var (k, v) in RecordObserve.DescribeAction(action))
                                data[k] = v?.DeepClone();
                            data["origin"] = OriginOf(action);
                            data["cause_action_id"] = RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id;
                            _rec.JournaledActions.Add(action);
                        }
                        data["action"] = RecordObserve.DescribeNetAction(ev.action);
                        break;
                    case CombatReplayEventType.HookAction:
                        data["hook_id"] = ev.hookId;
                        data["game_action_type"] = ev.gameActionType?.ToString();
                        if (action != null)
                        {
                            data["action_id"] = action.Id;
                            data["cause_action_id"] = RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id;
                            _rec.JournaledActions.Add(action);
                        }
                        break;
                    case CombatReplayEventType.ResumeAction:
                        data["resumed_action_id"] = ev.actionId;
                        break;
                    case CombatReplayEventType.PlayerChoice:
                        data["choice_id"] = ev.choiceId;
                        data["result"] = RecordObserve.DescribeChoiceResult(ev.playerChoiceResult);
                        data["origin"] = WindowOrigin();
                        break;
                }
                long seq = Emit("game_event", data, hidden: true);
                EmitHidden(seq, "game_event", new JsonObject
                {
                    ["index"] = index,
                    ["packet"] = Convert.ToBase64String(packet),
                });
            }
        });
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
                if (replay == null || replay.checksumData.Count != countBefore + 1)
                    return;
                if (_rec == null)
                    TryOpen("checksum");
                if (_rec == null || !ReferenceEquals(_rec.Replay, replay))
                    return;
                int index = replay.checksumData.Count - 1;
                var cs = replay.checksumData[index];
                byte[] packet = Packet(cs.Anonymized());
                _rec.ChecksumHashes.Add(Sha(packet));
                long seq = Emit("checksum", new JsonObject
                {
                    ["index"] = index,
                    ["checksum_id"] = cs.checksumData.id,
                    ["context"] = cs.context,
                    ["action_id"] = RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id,
                }, hidden: true);
                EmitHidden(seq, "checksum", new JsonObject
                {
                    ["index"] = index,
                    ["checksum"] = cs.checksumData.checksum,
                    ["packet"] = Convert.ToBase64String(packet),
                });
            }
        });
    }

    /// <summary>
    /// Postfix of ActionQueueSet.EnqueueWithoutSynchronizing. The game's writer records every
    /// action enqueued while the combat is in progress; one it did not record (its handler threw,
    /// or another handler before it did) makes the replay unreproducible, so it is flagged.
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
                var data = RecordObserve.DescribeAction(action);
                data["origin"] = OriginOf(action);
                data["in_progress"] = CombatManager.Instance.IsInProgress;
                if (CombatManager.Instance.IsInProgress && _rec.Writer?.IsEnabled == true)
                {
                    _rec.Faults.Add($"enqueue_unrecorded:{action.Id}");
                    Emit("enqueue_unrecorded", data);
                }
                else
                {
                    // Before IsInProgress (combat setup) the game does not record actions; a replay
                    // regenerates them from the initial state. Listed so the stream is complete.
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
                _rec.LastReservedChoice = choiceId;
                var running = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
                Emit("choice_reserved", new JsonObject
                {
                    ["choice_id"] = choiceId,
                    ["player_slot"] = PlayerSlot(player),
                    ["running_action_id"] = running?.Id,
                    ["running_action_class"] = running?.GetType().Name,
                });
            }
        });
    }

    /// <summary>
    /// Postfix of the concrete PlayerChoiceContext.SignalPlayerChoiceBegun. Every caller of
    /// ReserveChoiceId in v0.111.0 (CardSelectCmd x3) calls SignalPlayerChoiceBegun on the very
    /// next line with nothing awaited in between, so the choice being begun is the one reserved
    /// last. The context names the action that owns the choice - for a hook's choice, the hook
    /// action it just generated.
    /// </summary>
    internal static void OnChoiceBegun(PlayerChoiceContext context, Player chooser)
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
                var data = new JsonObject
                {
                    ["choice_id"] = _rec.LastReservedChoice,
                    ["context"] = context.GetType().Name,
                    ["player_slot"] = PlayerSlot(chooser),
                };
                if (owner != null)
                    foreach (var (k, v) in RecordObserve.DescribeAction(owner))
                        data[k] = v?.DeepClone();
                _rec.LastReservedChoice = null;
                Emit("choice_begun", data);
            }
        });
    }

    internal static void OnPausedForChoice(GameAction action, PlayerChoiceOptions options)
    {
        if (!_active)
            return;
        Guard("choice_paused", () =>
        {
            lock (Gate)
            {
                if (_rec == null)
                    return;
                var data = RecordObserve.DescribeAction(action);
                data["options"] = options.ToString();
                data["state_after"] = action.State.ToString();
                EmitWithObservation("choice_paused", data);
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
                Emit("action_resumed", new JsonObject
                {
                    ["old_action_id"] = oldId,
                    // ResumeActionWithoutSynchronizing hands out exactly one new id after the event.
                    ["new_action_id"] = nextActionId - 1,
                });
            }
        });
    }

    internal static void OnCancelled(GameAction action)
    {
        if (!_active)
            return;
        Guard("action_cancelled", () =>
        {
            lock (Gate)
            {
                if (_rec == null || action.Id == null)
                    return;
                Emit("action_cancelled", RecordObserve.DescribeAction(action));
            }
        });
    }

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
                EmitWithObservation("action_finished", RecordObserve.DescribeAction(action));
            }
        });
    }

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
                // Only two callers stop the replay: CombatManager.EndCombatInternal (the combat is
                // over; CombatEnded/CombatWon fire after this) and RunManager.CleanUp.
                string reason = _inCleanup ? "cleanup" : "combat_end";
                CloseRecord(reason, replay);
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

        bool fromEntry = _pending != null && ReferenceEquals(_pending.Replay, replay) && replay != null;
        int eventsBefore = replay?.events.Count ?? 0;
        int checksBefore = replay?.checksumData.Count ?? 0;
        string boundary = fromEntry && eventsBefore == 0 && checksBefore == 0
            ? "room_entry"
            : (CombatManager.Instance.IsInProgress ? "mid_combat" : "mid_room");

        long startTime = _pending?.StartTime ?? StartTimeOf(run);
        int act = _pending?.Act ?? run.CurrentActIndex;
        int floor = _pending?.Floor ?? run.ActFloor;

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
        };
        _recordsOpened++;

        var profile = SafeProfileId();
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
            ["profile_id"] = profile,
            ["run"] = _pending?.Run.DeepClone() ?? RunInfo(run, null),
            ["start"] = new JsonObject
            {
                ["boundary"] = boundary,
                ["initial_state"] = fromEntry,
                ["game_events_before_open"] = eventsBefore,
                ["checksums_before_open"] = checksBefore,
                ["game_writer_enabled"] = writer?.IsEnabled,
                ["game_writer_recording"] = replay != null,
            },
        });

        if (fromEntry)
        {
            long seq = Emit("initial_state", new JsonObject { ["ids"] = _pending!.Ids.DeepClone() }, hidden: true);
            EmitHidden(seq, "initial_state", new JsonObject
            {
                ["format"] = "CombatReplay packet (Anonymized, events and checksums empty), game PacketWriter",
                ["packet"] = Convert.ToBase64String(_pending.HeaderPacket),
            });
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
                selfCheck["events"] = Compare(replay.events.Select(e => Packet(e.Anonymized())).ToList(),
                    rec.EventHashes, rec.GameEventsBeforeOpen);
                selfCheck["checksums"] = Compare(replay.checksumData.Select(c => Packet(c.Anonymized())).ToList(),
                    rec.ChecksumHashes, rec.ChecksumsBeforeOpen);
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

        JsonObject? outcome = null;
        try
        {
            var combat = CombatManager.Instance.DebugOnlyGetState();
            var me = RecordObserve.LocalPlayer();
            if (combat != null && me != null)
                outcome = new JsonObject
                {
                    ["player_hp"] = me.Creature.CurrentHp,
                    ["player_alive"] = me.Creature.IsAlive,
                    ["enemies_alive"] = combat.Enemies.Count(e => e.IsAlive),
                };
        }
        catch (Exception ex) { NoteFault("outcome", ex); }

        Emit("record_close", new JsonObject
        {
            ["reason"] = reason,
            ["outcome"] = outcome,
            ["game_replay_captured"] = replay != null,
            ["journal"] = new JsonObject
            {
                ["game_events"] = rec.EventHashes.Count,
                ["checksums"] = rec.ChecksumHashes.Count,
                ["last_seq"] = rec.Seq,
            },
            ["self_check"] = selfCheck,
            ["faults"] = new JsonArray(rec.Faults.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
        });
        Writer.CloseFile(rec.PublicPath);
        Writer.CloseFile(rec.HiddenPath);
        _rec = null;
    }

    /// <summary>
    /// Journal vs the game's own list at close: how many the game had, how many the journal
    /// has, whether the journal is missing a prefix, and the first index whose bytes differ.
    /// </summary>
    private static JsonObject Compare(List<byte[]> game, List<string> journal, int before)
    {
        int? firstMismatch = null;
        for (int i = 0; i < journal.Count; i++)
        {
            int gi = before + i;
            if (gi >= game.Count || Sha(game[gi]) != journal[i])
            {
                firstMismatch = gi;
                break;
            }
        }
        bool complete = before == 0 && firstMismatch == null && journal.Count == game.Count;
        return new JsonObject
        {
            ["game"] = game.Count,
            ["journal"] = journal.Count,
            ["missing_prefix"] = before,
            ["first_mismatch"] = firstMismatch,
            ["complete"] = complete,
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
    // Emit helpers
    // ========================================================================================

    private static long Emit(string kind, JsonObject? data, bool hidden = false)
    {
        var rec = _rec;
        if (rec == null)
            return -1;
        long seq = rec.Seq++;
        var line = new JsonObject
        {
            ["seq"] = seq,
            ["kind"] = kind,
            ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs,
            ["at"] = SafeAt(),
        };
        if (hidden)
            line["hidden"] = true;
        if (data != null)
        {
            foreach (var (k, v) in data.ToList())
            {
                data.Remove(k);
                line[k] = v;
            }
        }
        Writer.AppendLine(rec.PublicPath, line);
        return seq;
    }

    private static void EmitHidden(long seq, string kind, JsonObject data)
    {
        var rec = _rec;
        if (rec == null || seq < 0)
            return;
        var line = new JsonObject { ["seq"] = seq, ["kind"] = kind };
        foreach (var (k, v) in data.ToList())
        {
            data.Remove(k);
            line[k] = v;
        }
        Writer.AppendLine(rec.HiddenPath, line);
    }

    private static void EmitWithObservation(string kind, JsonObject? data)
    {
        var obs = SafeObserve();
        data ??= new JsonObject();
        if (obs is { } o)
            data["observation"] = o.pub;
        long seq = Emit(kind, data, hidden: obs != null);
        if (obs is { } h)
            EmitHidden(seq, "observation", new JsonObject { ["observation"] = h.hidden });
    }

    /// <summary>For lines produced off the main thread (HTTP): no "at", no game reads.</summary>
    private static void EmitFromAnyThread(string kind, JsonObject data)
    {
        lock (Gate)
        {
            var rec = _rec;
            if (rec == null)
                return;
            long seq = rec.Seq++;
            var line = new JsonObject { ["seq"] = seq, ["kind"] = kind, ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs };
            foreach (var (k, v) in data.ToList())
            {
                data.Remove(k);
                line[k] = v;
            }
            Writer.AppendLine(rec.PublicPath, line);
        }
    }

    private static JsonNode? SafeAt()
    {
        try { return RecordObserve.At(); }
        catch (Exception ex) { NoteFault("at", ex); return null; }
    }

    private static (JsonObject pub, JsonObject hidden)? SafeObserve()
    {
        try { return RecordObserve.Observe(); }
        catch (Exception ex) { NoteFault("observe", ex); return null; }
    }

    private static void Guard(string where, Action body)
    {
        try { body(); }
        catch (Exception ex) { NoteFault(where, ex); }
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
            if (rec != null)
            {
                rec.Faults.Add(where);
                long seq = rec.Seq++;
                Writer.AppendLine(rec.PublicPath, new JsonObject
                {
                    ["seq"] = seq,
                    ["kind"] = "recorder_fault",
                    ["t_ms"] = Clock.ElapsedMilliseconds - rec.OpenedMs,
                    ["where"] = where,
                    ["exception"] = ex.GetType().Name,
                    ["message"] = ex.Message,
                });
            }
            }
        }
        catch { /* never throw into the game */ }
    }

    // ========================================================================================
    // Small readers
    // ========================================================================================

    private static readonly PacketWriter PacketWriterInstance = new() { WarnOnGrow = false };

    /// <summary>The game's own serializer, into a fresh array. Main thread only (shared writer).</summary>
    private static byte[] Packet<T>(T value) where T : IPacketSerializable
    {
        var w = PacketWriterInstance;
        w.Reset();
        w.Write(value);
        w.ZeroByteRemainder();
        return w.Buffer.AsSpan(0, w.BytePosition).ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonObject IdsOf(CombatReplay replay) => new()
    {
        ["next_action_id"] = replay.nextActionId,
        ["next_hook_id"] = replay.nextHookId,
        ["next_checksum_id"] = replay.nextChecksumId,
        ["choice_ids"] = new JsonArray(replay.choiceIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
        ["reward_ids"] = new JsonArray(replay.rewardIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
    };

    private static JsonObject RunInfo(RunState? run, SerializableRun? save)
    {
        var player = run == null ? null : RecordObserve.LocalPlayer();
        return new JsonObject
        {
            ["start_time"] = save?.StartTime ?? (run == null ? null : StartTimeOf(run)),
            ["character"] = player?.Character.Id.Entry,
            ["ascension"] = run?.AscensionLevel ?? save?.Ascension,
            ["act"] = run?.CurrentActIndex ?? save?.CurrentActIndex,
            ["act_floor"] = run?.ActFloor,
            ["total_floor"] = run?.TotalFloor,
            ["map_coord"] = run?.CurrentMapCoord is { } c ? new JsonObject { ["col"] = c.col, ["row"] = c.row } : null,
        };
    }

    private static long StartTimeOf(RunState run)
    {
        // RunManager keeps the start time in a private field; ToSave copies it into SerializableRun.
        return RecorderPatches.RunStartTime();
    }

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

    private static int? SafeProfileId()
    {
        try { return SaveManager.Instance.CurrentProfileId; }
        catch { return null; }
    }
}
