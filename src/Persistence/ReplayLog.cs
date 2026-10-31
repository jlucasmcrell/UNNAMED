// UNNAMED Persistence - command_log.jsonl, the replay enabler (PERSISTENCE.md §5.7, §4.2; CMB-126)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Persistence;

/// <summary>
/// What a log's session began from: the replay tuple's world seed and content identity, and the character creation that exists nowhere
/// else in a save. A session resumed from a save names that slot instead of a character (<see cref="Spec"/> is then null): replaying it
/// loads the slot and continues. Every part repeats the start, because a part is read on its own.
/// </summary>
public sealed record ReplayLogStart(
    ulong Seed,
    CharacterSpec? Spec,
    string ContentHash,
    string WorldgenFingerprint,
    string? ResumedFrom = null);

/// <summary>
/// One command of a log: the tick it was dispatched on, its place in dispatch order, the command as it was written, and the refusal it
/// got when it was live. Instance IDs are runtime identity and never derived from a seed (D-04), so the dispatching session's own player
/// is written as the token <c>"player"</c> and <see cref="Data"/> is resolved against the replaying session's player (<see cref="Resolve"/>).
/// </summary>
public sealed record ReplayLogEntry(long Tick, long Sequence, string Type, JsonNode? Data, string? Rejected = null, string? Why = null);

/// <summary>What a log holds: the session it describes, its commands in dispatch order, and why it cannot be replayed, if it cannot.</summary>
public sealed record ReplayLogContents(ReplayLogStart? Start, ImmutableArray<ReplayLogEntry> Entries, string? Refusal)
{
    public bool Complete => Refusal is null && Start is not null;
}

/// <summary>
/// <c>command_log.jsonl</c>: every command that went into the simulation, one JSON line each, append-only, in dispatch order
/// (§5.7). It is not a debug artifact - it is the fourth member of the replay tuple, so a save can be replayed into a fresh world and
/// compared field by field.
/// </summary>
/// <remarks>
/// A log is kept under a size cap and rotated (§5.7; CMB-126): at most <see cref="MaxParts"/> parts of at most
/// <see cref="MaxPartBytes"/>, the current one <c>command_log.jsonl</c> and older ones
/// <c>command_log.&lt;first tick&gt;_&lt;first sequence&gt;.jsonl</c>. A part that begins at sequence 0 begins a session; a load rotates
/// the part it found, so the file always holds the session being played and the folder always keeps the ones before it. A log rotated
/// past its session's start can no longer replay, and says so rather than pretending.
/// </remarks>
public static class ReplayLog
{
    /// <summary>The current part, beside the character's save slots (PERSISTENCE.md §3.1).</summary>
    public const string FileName = "command_log.jsonl";

    /// <summary>How big one part may grow before it is rotated: eight megabytes is hours of dispatch at a few hundred bytes a line.</summary>
    public const int MaxPartBytes = 8 * 1024 * 1024;

    /// <summary>How many parts one folder keeps, current included. Past that the oldest goes, and replaying the missing start refuses.</summary>
    public const int MaxParts = 4;

    /// <summary>The format this build writes.</summary>
    public const int Format = 1;

    internal static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static readonly Lazy<ImmutableDictionary<string, Type>> Commands = new(() =>
        typeof(GameCommand).Assembly.GetTypes()
            .Where(t => typeof(GameCommand).IsAssignableFrom(t) && !t.IsAbstract)
            .ToImmutableDictionary(t => t.Name, t => t, StringComparer.Ordinal));

    /// <summary>Where the current part of a profile root's log is.</summary>
    public static string PathOf(string root) => System.IO.Path.Combine(root, FileName);

    /// <summary>
    /// The parts of a log, oldest first: rotated ones by the tick and sequence they begin at, then the current one. A folder with no
    /// current part has nothing playable.
    /// </summary>
    public static ImmutableArray<string> Parts(string root)
    {
        if (!Directory.Exists(root))
            return ImmutableArray<string>.Empty;
        var rotated = Directory.EnumerateFiles(root, "command_log.*.jsonl")
            .Select(path => (Path: path, Start: StartOfFileName(path)))
            .Where(p => p.Start is not null)
            .OrderBy(p => p.Start!.Value.Tick).ThenBy(p => p.Start!.Value.Sequence)
            .Select(p => p.Path);
        return rotated.Append(PathOf(root)).Where(System.IO.File.Exists).ToImmutableArray();
    }

    /// <summary>The parts of the newest session, oldest first: the last part that begins at sequence 0, and everything after it.</summary>
    public static ImmutableArray<string> SessionParts(string root)
    {
        var parts = Parts(root);
        int from = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (HeaderOf(parts[i])?["from_sequence"]?.GetValue<long>() == 0)
                from = i;
        }
        return parts.Skip(from).ToImmutableArray();
    }

    /// <summary>Reads the newest session's parts in order. A part whose header disagrees with the session's, or a line that cannot be read, ends the log.</summary>
    public static ReplayLogContents Read(string root)
    {
        var parts = SessionParts(root);
        if (parts.IsEmpty)
            return new ReplayLogContents(null, ImmutableArray<ReplayLogEntry>.Empty, "this character has no command log: replay is unavailable");
        ReplayLogStart? start = null;
        var entries = ImmutableArray.CreateBuilder<ReplayLogEntry>();
        foreach (string part in parts)
        {
            string? refusal = ReadPart(part, start, entries, out ReplayLogStart? partStart);
            if (refusal is not null)
                return new ReplayLogContents(start, entries.ToImmutable(), refusal);
            start ??= partStart;
        }
        if (start is null)
            return new ReplayLogContents(null, entries.ToImmutable(), "the command log holds no header: replay is unavailable");
        if ((HeaderOf(parts[0])?["from_sequence"]?.GetValue<long>() ?? 0) != 0)
        {
            return new ReplayLogContents(start, entries.ToImmutable(),
                $"this log was rotated past its start ({HeaderOf(parts[0])?["from_sequence"]} commands dropped): replay is unavailable");
        }
        if (start.Spec is null && start.ResumedFrom is null)
            return new ReplayLogContents(start, entries.ToImmutable(), "the log names neither a character to make nor a save to load: replay is unavailable");
        return new ReplayLogContents(start, entries.ToImmutable(), null);
    }

    private static string? ReadPart(string part, ReplayLogStart? known, ImmutableArray<ReplayLogEntry>.Builder entries, out ReplayLogStart? start)
    {
        start = null;
        string[] lines = System.IO.File.ReadAllLines(part, Encoding.UTF8);
        if (lines.Length == 0)
            return $"{System.IO.Path.GetFileName(part)} is empty: the log is cut";
        JsonNode? header;
        try
        {
            header = JsonNode.Parse(lines[0]);
        }
        catch (JsonException e)
        {
            return $"{System.IO.Path.GetFileName(part)}: its header cannot be read ({e.Message})";
        }
        if (header is null || header["kind"]?.GetValue<string>() != "log")
            return $"{System.IO.Path.GetFileName(part)}: it does not begin with a log header";
        start = StartOf(header);
        if (known is not null && (start.Seed != known.Seed || start.ContentHash != known.ContentHash))
            return $"{System.IO.Path.GetFileName(part)}: it belongs to another session (seed or content differs)";
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            JsonNode? line;
            try
            {
                line = JsonNode.Parse(lines[i]);
            }
            catch (JsonException e)
            {
                return $"{System.IO.Path.GetFileName(part)} line {i + 1}: it cannot be read ({e.Message})";
            }
            string? kind = line?["kind"]?.GetValue<string>();
            long tick = line?["tick"]?.GetValue<long>() ?? 0;
            long sequence = line?["sequence"]?.GetValue<long>() ?? 0;
            if (kind == "log-cut")
                return $"{System.IO.Path.GetFileName(part)} line {i + 1}: the log was cut ({line?["why"]?.GetValue<string>()})";
            if (kind != "command")
                return $"{System.IO.Path.GetFileName(part)} line {i + 1}: '{kind}' is not a log line";
            string type = line?["command"]?.GetValue<string>() ?? "";
            string? rejected = line?["rejected"]?.GetValue<string>();
            JsonNode? data = line?["data"];
            entries.Add(new ReplayLogEntry(tick, sequence, type, data?.DeepClone(), rejected,
                data is null || !Commands.Value.ContainsKey(type) ? $"the command '{type}' is not one this build knows" : null));
        }
        return null;
    }

    private static JsonNode? HeaderOf(string part)
    {
        try
        {
            using var reader = new StreamReader(part, Encoding.UTF8);
            string? first = reader.ReadLine();
            return first is null ? null : JsonNode.Parse(first);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReplayLogStart StartOf(JsonNode header) => new(
        WorldSeed.Parse(header["seed"]!.GetValue<string>()),
        header["spec"]?.ToJsonString() is { } spec ? JsonSerializer.Deserialize<CharacterSpec>(spec, Json) : null,
        header["content_hash"]!.GetValue<string>(),
        header["worldgen_fingerprint"]?.GetValue<string>() ?? "",
        header["resumed_from"]?.GetValue<string>());

    /// <summary>
    /// The digest a save records (§5.7): SHA-256 over the log's bytes exactly as written, but only the commands dispatched before
    /// <paramref name="beforeTick"/> - a command dispatched on the saved tick applies on the one after it. It is read from the files, so a
    /// truncated, reordered or edited log is detectable, and rotating a part does not change it.
    /// </summary>
    public static string? Digest(string root, long beforeTick)
    {
        using var sha = SHA256.Create();
        bool any = false;
        foreach (string part in SessionParts(root))
        {
            string[] lines = System.IO.File.ReadAllLines(part, Encoding.UTF8);
            int last = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                if (TickOf(lines[i]) >= beforeTick)
                    break;
                last = i;
            }
            if (last < 0)
                continue;   // a part none of whose commands reach the capture adds nothing to it
            any = true;
            for (int i = 0; i <= last; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(lines[i] + "\n");
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
        }
        if (!any)
            return null;
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static long TickOf(string line)
    {
        try
        {
            return JsonNode.Parse(line)?["tick"]?.GetValue<long>() ?? 0;
        }
        catch (JsonException)
        {
            return long.MinValue;
        }
    }

    private static (long Tick, long Sequence)? StartOfFileName(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (!name.StartsWith("command_log.", StringComparison.Ordinal) || !name.EndsWith(".jsonl", StringComparison.Ordinal))
            return null;
        string middle = name["command_log.".Length..^".jsonl".Length];
        string[] pieces = middle.Split('_');
        if (pieces.Length != 2
            || !long.TryParse(pieces[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long tick)
            || !long.TryParse(pieces[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long sequence))
            return null;
        return (tick, sequence);
    }

    internal static string HeaderLine(ReplayLogStart start, long fromSequence, long fromTick) => new JsonObject
    {
        ["kind"] = "log",
        ["format"] = Format,
        ["from_sequence"] = fromSequence,
        ["from_tick"] = fromTick,
        ["seed"] = WorldSeed.Format(start.Seed),
        ["content_hash"] = start.ContentHash,
        ["worldgen_fingerprint"] = start.WorldgenFingerprint,
        ["resumed_from"] = start.ResumedFrom,
        ["spec"] = start.Spec is null ? null : JsonSerializer.SerializeToNode(start.Spec, Json),
    }.ToJsonString();

    /// <summary>
    /// The command a line carries, with the token <c>"player"</c> read as <paramref name="player"/>: the replaying session's own player.
    /// A command whose JSON this build cannot read comes back null with the reason.
    /// </summary>
    public static (GameCommand? Command, string? Why) Resolve(ReplayLogEntry entry, EntityId player)
    {
        if (entry.Data is null || entry.Why is not null)
            return (null, entry.Why ?? "the line carries no command");
        if (!Commands.Value.TryGetValue(entry.Type, out var clr))
            return (null, $"the command '{entry.Type}' is not one this build knows");
        JsonNode data = entry.Data.DeepClone();
        Substitute(data, "player", player.Value);
        try
        {
            return (JsonSerializer.Deserialize(data, clr, Json) as GameCommand,
                null);
        }
        catch (JsonException e)
        {
            return (null, $"'{entry.Type}' cannot be rebuilt ({e.Message})");
        }
    }

    /// <summary>Writes the session's own player id where it appears as <paramref name="from"/>, so a replay can read it as this player.</summary>
    private static void Substitute(JsonNode node, string from, string to)
    {
        switch (node)
        {
            case JsonObject map:
                foreach (string key in map.Select(kv => kv.Key).ToList())
                {
                    if (map[key] is JsonObject or JsonArray)
                        Substitute(map[key]!, from, to);
                    else if (map[key] is JsonValue value && value.TryGetValue(out string? text) && text == from)
                        map[key] = to;
                }
                break;
            case JsonArray list:
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is JsonObject or JsonArray)
                        Substitute(list[i]!, from, to);
                    else if (list[i] is JsonValue value && value.TryGetValue(out string? text) && text == from)
                        list[i] = to;
                }
                break;
        }
    }

    internal static string Line(LoggedCommand logged, EntityId? player) => new JsonObject
    {
        ["kind"] = "command",
        ["tick"] = logged.Tick,
        ["sequence"] = logged.Sequence,
        ["command"] = logged.Command.GetType().Name,
        ["rejected"] = logged.RejectedReason,
        ["data"] = Data(logged.Command, player),
    }.ToJsonString();

    private static JsonNode Data(GameCommand command, EntityId? player)
    {
        JsonNode data = JsonSerializer.SerializeToNode(command, command.GetType(), Json) ?? new JsonObject();
        if (player is not null)
            Substitute(data, player.Value, "player");
        return data;
    }

    internal static string CutLine(GameCommand command, long tick, long sequence, string why) => new JsonObject
    {
        ["kind"] = "log-cut",
        ["tick"] = tick,
        ["sequence"] = sequence,
        ["why"] = why,
    }.ToJsonString();

    /// <summary>The current part's own start, so a session boundary can rotate it under the name it should have had.</summary>
    internal static (long Tick, long Sequence) StartOfPart(string path)
    {
        JsonNode? header = HeaderOf(path);
        return header is null ? (0, 0)
            : (header["from_tick"]?.GetValue<long>() ?? 0,
               header["from_sequence"]?.GetValue<long>() ?? 0);
    }
}

/// <summary>
/// Appends the simulation's dispatched commands to a character's <c>command_log.jsonl</c>, rotating it at
/// <see cref="ReplayLog.MaxPartBytes"/> and keeping <see cref="ReplayLog.MaxParts"/> parts. Append-only: a part is never rewritten, so a
/// crash leaves every line it wrote intact, and each session begins a part at sequence 0.
/// </summary>
public sealed class ReplayLogWriter : IDisposable
{
    private readonly string _root;
    private readonly ReplayLogStart _start;
    private readonly EntityId? _player;
    private readonly int _maxPartBytes;
    private readonly int _maxParts;
    private int _written;
    private bool _disposed;

    /// <param name="root">The character's save root: the log sits beside its slots.</param>
    /// <param name="start">What the session began from.</param>
    /// <param name="player">The session's player, whose instance id is written as the token <c>"player"</c>.</param>
    /// <param name="resume">A session is beginning in a folder that already holds one: rotate that session's part aside first.</param>
    public ReplayLogWriter(string root, ReplayLogStart start, EntityId? player, bool resume = false,
        int maxPartBytes = ReplayLog.MaxPartBytes, int maxParts = ReplayLog.MaxParts)
    {
        if (maxPartBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPartBytes), maxPartBytes, "a part is at least a byte");
        if (maxParts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxParts), maxParts, "a log keeps at least one part");
        _root = root;
        _start = start;
        _player = player;
        _maxPartBytes = maxPartBytes;
        _maxParts = maxParts;
        Directory.CreateDirectory(root);
        string current = ReplayLog.PathOf(root);
        if (File.Exists(current) && new FileInfo(current).Length > 0)
        {
            // A session boundary rotates the part the folder holds; a rotation of a full part does the same later.
            if (resume)
                Rotate(current);
        }
    }

    /// <summary>How many of the commands handed over are on disk.</summary>
    public int Written => _written;

    /// <summary>Writes any of <paramref name="commands"/> not yet on disk, in dispatch order.</summary>
    public void Sync(IReadOnlyList<LoggedCommand> commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (; _written < commands.Count; _written++)
        {
            var logged = commands[_written];
            string line;
            try
            {
                line = ReplayLog.Line(logged, _player);
            }
            catch (Exception error) when (error is NotSupportedException or JsonException or InvalidOperationException)
            {
                // A command the log cannot carry makes the log useless for replay, and saying so is the only honest thing to write.
                line = ReplayLog.CutLine(logged.Command, logged.Tick, logged.Sequence,
                    $"the command '{logged.Command.GetType().Name}' could not be written ({error.Message})");
            }
            Append(line, logged.Tick, logged.Sequence);
        }
    }

    // The part is opened, appended to and closed for every batch of commands: no handle outlives a frame, so a save folder can be
    // copied, deleted or reopened while the game is running, and a crash loses nothing that was written.
    private void Append(string line, long tick, long sequence)
    {
        int bytes = Encoding.UTF8.GetByteCount(line) + 1;
        string path = ReplayLog.PathOf(_root);
        long length = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (length > 0 && length + bytes > _maxPartBytes)
        {
            Rotate(path);
            length = 0;
        }
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        if (length == 0)
        {
            writer.Write(ReplayLog.HeaderLine(_start, sequence, tick));
            writer.Write('\n');
        }
        writer.Write(line);
        writer.Write('\n');
    }

    private void Rotate(string path)
    {
        var (fromTick, fromSequence) = ReplayLog.StartOfPart(path);
        string rotated = System.IO.Path.Combine(_root,
            $"command_log.{fromTick.ToString(CultureInfo.InvariantCulture)}_{fromSequence.ToString(CultureInfo.InvariantCulture)}.jsonl");
        File.Move(path, rotated, overwrite: true);
        // The cap counts the part about to begin too, so keep one fewer than the folder may hold.
        var parts = ReplayLog.Parts(_root);
        foreach (string stale in parts.Take(Math.Max(0, parts.Length - (_maxParts - 1))))
            File.Delete(stale);
    }

    public void Dispose() => _disposed = true;
}
