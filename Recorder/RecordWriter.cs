using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace STS2_MCP.Recorder;

/// <summary>
/// Appends record lines on a thread of its own, so the game's main thread only builds the
/// line (reads game objects, runs the game's own packet serializer) and never waits on disk.
///
/// Every chained file carries a hash chain: each line's "prev" is the SHA-256 of the previous
/// line's bytes in the same file. A line that goes missing or is edited breaks the chain, so
/// a reader can tell a gap from a record that simply ended.
/// </summary>
internal sealed class RecordWriter
{
    private abstract record Item;
    private sealed record Line(string Path, JsonObject Json) : Item;
    private sealed record Raw(string Path, byte[] Bytes) : Item;
    private sealed record Close(string Path) : Item;
    private sealed record Barrier(ManualResetEventSlim Done) : Item;

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly BlockingCollection<Item> _queue = new(new ConcurrentQueue<Item>());
    private readonly Dictionary<string, string> _prevHash = new();
    private readonly Dictionary<string, FileStream> _open = new();
    private readonly Thread _thread;

    private int _failures;
    private string? _lastError;

    /// <summary>Write failures so far (disk full, permissions...). Surfaced on the status endpoint.</summary>
    internal int Failures => Volatile.Read(ref _failures);
    internal string? LastError => Volatile.Read(ref _lastError);
    internal int Pending => _queue.Count;

    internal RecordWriter()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "STS2_MCP_Recorder" };
        _thread.Start();
    }

    internal void AppendLine(string path, JsonObject json) => _queue.Add(new Line(path, json));

    internal void WriteRaw(string path, byte[] bytes) => _queue.Add(new Raw(path, bytes));

    internal void CloseFile(string path) => _queue.Add(new Close(path));

    /// <summary>Blocks until everything queued so far is on disk (or <paramref name="timeoutMs"/> passes).</summary>
    internal bool Drain(int timeoutMs)
    {
        using var done = new ManualResetEventSlim(false);
        _queue.Add(new Barrier(done));
        return done.Wait(timeoutMs);
    }

    private void Run()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            try
            {
                switch (item)
                {
                    case Line line:
                        WriteLine(line);
                        break;
                    case Raw raw:
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(raw.Path)!);
                        File.WriteAllBytes(raw.Path, raw.Bytes);
                        break;
                    case Close close:
                        if (_open.Remove(close.Path, out var fs))
                            fs.Dispose();
                        _prevHash.Remove(close.Path);
                        break;
                    case Barrier barrier:
                        foreach (var stream in _open.Values)
                            stream.Flush(flushToDisk: false);
                        barrier.Done.Set();
                        break;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failures);
                Volatile.Write(ref _lastError, $"{ex.GetType().Name}: {ex.Message}");
                if (item is Barrier b)
                    b.Done.Set();
            }
        }
    }

    private void WriteLine(Line line)
    {
        _prevHash.TryGetValue(line.Path, out var prev);
        line.Json["prev"] = prev;
        byte[] bytes = Encoding.UTF8.GetBytes(line.Json.ToJsonString(LineOptions));
        _prevHash[line.Path] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!_open.TryGetValue(line.Path, out var fs))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(line.Path)!);
            fs = new FileStream(line.Path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _open[line.Path] = fs;
        }
        fs.Write(bytes);
        fs.WriteByte((byte)'\n');
        // Flushed per line: a crash loses at most what the OS had not yet written, and a record
        // cut short that way reads as "no record_close", which is exactly what happened.
        fs.Flush(flushToDisk: false);
    }
}
