using System;
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
            ["events"] = events,
            ["checksums"] = checksums,
        };
    }
}
