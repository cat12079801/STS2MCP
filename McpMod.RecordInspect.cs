using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using STS2_MCP.Recorder;

namespace STS2_MCP;

public static partial class McpMod
{
    /// <summary>
    /// GET /api/v1/record/inspect?file=&lt;path&gt; - reads a .mcr (the game's CombatReplay) with the
    /// game's own PacketReader and lists its parts as digests, so two replays can be compared
    /// entry by entry without a second implementation of the format.
    ///
    /// A verification tool (docs/recording.md "A/B"), not part of the play API: its output
    /// contains the game's checksums, which are hidden information. Only files under the record
    /// directory or the current profile's replays/ directory are read.
    /// </summary>
    private static void HandleInspectReplay(HttpListenerRequest request, HttpListenerResponse response)
    {
        bool pretty = WantsPretty(request);
        string? file = request.QueryString["file"];
        if (string.IsNullOrEmpty(file) || !file.EndsWith(".mcr", StringComparison.Ordinal))
        {
            SendError(response, 400, "Pass ?file=<path to a .mcr>", pretty);
            return;
        }
        try
        {
            var result = RunOnMainThreadBlocking(() => InspectReplay(file));
            if (result["error"] is JsonNode err)
                SendError(response, 400, err.GetValue<string>(), pretty);
            else
                SendJson(response, result, pretty);
        }
        catch (MainThreadUnavailableException ex)
        {
            SendUnavailable(response, ex.Message, pretty);
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Inspect failed: {ex.GetType().Name}: {ex.Message}", pretty);
        }
    }

    /// <summary>
    /// The run's digest with only the fields that differ between two plays of the same save
    /// removed: the wall-clock fields (SaveTime, RunTime, WinTime), NumReloads (a "continue" adds
    /// one), the map drawings, and the players' ids (IdAnonymizer draws them per process; they
    /// become the player's slot). Everything else - RNG, decks, relics, map, history - stays in.
    /// Works on the copy this request just read from the file, never on the live run.
    /// </summary>
    private static string NormalizedRunDigest(SerializableRun run, Func<SerializableRun, string> digest)
    {
        var slots = new Dictionary<ulong, ulong>();
        for (int i = 0; i < run.Players.Count; i++)
        {
            slots[run.Players[i].NetId] = (ulong)i;
            run.Players[i].NetId = (ulong)i;
        }
        foreach (var act in run.MapPointHistory)
            foreach (var entry in act)
                foreach (var stat in entry.PlayerStats)
                    stat.PlayerId = slots.TryGetValue(stat.PlayerId, out var slot) ? slot : ulong.MaxValue;
        run.SaveTime = 0;
        run.RunTime = 0;
        run.WinTime = 0;
        run.NumReloads = 0;
        run.MapDrawings = null;
        return digest(run);
    }

    private static JsonObject RunParts(SerializableRun run, Func<IPacketSerializable, string> digest)
    {
        var parts = new JsonObject();
        foreach (var p in typeof(SerializableRun).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetMethod == null)
                continue;
            object? v;
            try { v = p.GetValue(run); }
            catch { continue; }
            string d = v switch
            {
                null => "null",
                IPacketSerializable ps => digest(ps),
                System.Collections.IEnumerable seq and not string => string.Join(",", seq.Cast<object?>().Select(x =>
                    x is IPacketSerializable xp ? digest(xp)[..12]
                    : x is System.Collections.IEnumerable inner and not string
                        ? "[" + string.Join(",", inner.Cast<object?>().Select(y => y is IPacketSerializable yp ? digest(yp)[..12] : y?.ToString())) + "]"
                        : x?.ToString())),
                _ => v.ToString() ?? "",
            };
            // Lists stay item by item (short digests), so the item that differs can be named.
            parts[p.Name] = d.Length > 64 && v is not System.Collections.IEnumerable
                ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(d))).ToLowerInvariant() : d;
        }
        // The history of the current act, entry by entry and field by field (the save rewrites it).
        var history = new JsonArray();
        if (run.MapPointHistory.Count > 0)
        {
            foreach (var entry in run.MapPointHistory[^1])
            {
                var e = new JsonObject
                {
                    ["type"] = entry.MapPointType.ToString(),
                    ["rooms"] = string.Join(";", entry.Rooms.Cast<object?>().Select(x => x is IPacketSerializable xp ? digest(xp)[..12] : x?.ToString())),
                };
                var stats = new JsonArray();
                foreach (var stat in entry.PlayerStats)
                {
                    var o = new JsonObject();
                    foreach (var sp in stat.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    {
                        if (sp.GetIndexParameters().Length != 0 || sp.GetMethod == null)
                            continue;
                        object? sv;
                        try { sv = sp.GetValue(stat); } catch { continue; }
                        o[sp.Name] = sv switch
                        {
                            null => null,
                            string str => str,
                            System.Collections.ICollection c => string.Join(";", c.Cast<object?>().Select(x =>
                                x is IPacketSerializable xp ? digest(xp)[..12] : x?.ToString())),
                            _ => sv.ToString(),
                        };
                    }
                    stats.Add(o);
                }
                e["player_stats"] = stats;
                history.Add(e);
            }
        }
        parts["last_act_history"] = history;
        return parts;
    }

    private static JsonObject InspectReplay(string file)
    {
        string full = Path.GetFullPath(file);
        string recordRoot = Path.GetFullPath(CombatRecorder.Root) + Path.DirectorySeparatorChar;
        string replays = Path.GetFullPath(ProjectSettings.GlobalizePath(
            SaveManager.Instance.GetProfileScopedPath("replays"))) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(recordRoot, StringComparison.Ordinal) && !full.StartsWith(replays, StringComparison.Ordinal))
            return new JsonObject { ["error"] = $"Only files under {recordRoot} or {replays} can be inspected" };
        if (!File.Exists(full))
            return new JsonObject { ["error"] = $"No such file: {full}" };

        byte[] bytes = File.ReadAllBytes(full);
        var reader = new PacketReader();
        reader.Reset(bytes);
        var replay = reader.Read<CombatReplay>();

        var writer = new PacketWriter { WarnOnGrow = false };
        string Digest<T>(T value) where T : IPacketSerializable
        {
            writer.Reset();
            writer.Write(value);
            writer.ZeroByteRemainder();
            return Convert.ToHexString(SHA256.HashData(writer.Buffer.AsSpan(0, writer.BytePosition))).ToLowerInvariant();
        }

        var events = new JsonArray();
        for (int i = 0; i < replay.events.Count; i++)
        {
            var e = replay.events[i];
            var entry = new JsonObject
            {
                ["index"] = i,
                ["type"] = e.eventType.ToString(),
                ["sha256"] = Digest(e),
                // Each .mcr anonymizes player ids with values drawn per process (IdAnonymizer), so
                // two replays of the same play differ there; this digest leaves the id out.
                ["sha256_without_player"] = Digest(e with { playerId = e.playerId.HasValue ? 0UL : null }),
            };
            switch (e.eventType)
            {
                case CombatReplayEventType.GameAction:
                    entry["action"] = RecordObserve.DescribeNetAction(e.action, CombatRecorder.SlotOf);
                    break;
                case CombatReplayEventType.HookAction:
                    entry["hook_id"] = e.hookId;
                    break;
                case CombatReplayEventType.ResumeAction:
                    entry["action_id"] = e.actionId;
                    break;
                case CombatReplayEventType.PlayerChoice:
                    entry["choice_id"] = e.choiceId;
                    entry["result"] = RecordObserve.DescribeChoiceResult(e.playerChoiceResult, CombatRecorder.SlotOf);
                    break;
            }
            events.Add(entry);
        }

        var checksums = new JsonArray(replay.checksumData.Select((c, i) => (JsonNode)new JsonObject
        {
            ["index"] = i,
            ["checksum_id"] = c.checksumData.id,
            ["checksum"] = c.checksumData.checksum,
            ["context"] = c.context,
            ["sha256"] = Digest(c),
        }).ToArray());

        return new JsonObject
        {
            ["file"] = full,
            ["bytes"] = bytes.Length,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ["version"] = replay.version,
            ["git_commit"] = replay.gitCommit,
            ["model_id_hash"] = replay.modelIdHash,
            ["next_action_id"] = replay.nextActionId,
            ["next_hook_id"] = replay.nextHookId,
            ["next_checksum_id"] = replay.nextChecksumId,
            ["choice_ids"] = new JsonArray(replay.choiceIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            ["reward_ids"] = new JsonArray(replay.rewardIds.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            ["start_time"] = replay.serializableRun.StartTime,
            ["serializable_run_sha256"] = Digest(replay.serializableRun),
            ["serializable_run_sha256_normalized"] = NormalizedRunDigest(replay.serializableRun, Digest),
            // Per property of the normalized run, so a difference can be located.
            ["serializable_run_parts"] = RunParts(replay.serializableRun, x => Digest(x)),
            ["events"] = events,
            ["checksums"] = checksums,
        };
    }
}
