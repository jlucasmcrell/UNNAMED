// UNNAMED Persistence - journal.jsonl, the append-only session log the save layout names (SYS-113; PERSISTENCE.md:116, 137)
// No Godot references - persistence only

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace UNNAMED.Persistence;

/// <summary>
/// One line of the session journal: what happened, when in the world, and the sentence a player would read. It is prose, not state - nothing
/// replays it and nothing loads from it - which is why the layout keeps it plain text beside the saves it belongs to.
/// </summary>
public sealed record SessionJournalEntry(long Tick, string Kind, string Text);

/// <summary>
/// Writes and reads <c>journal.jsonl</c> (SYS-113; PERSISTENCE.md §3.2 and §3.3): one JSON object per line, appended, never rewritten in place.
/// The only rule that matters on the way back in is the layout's own: a partial tail line is not corruption, so a reader stops at the last
/// complete line and says where it stopped - a session that died mid-write costs the player a sentence, not the file.
/// </summary>
public static class SessionJournal
{
    /// <summary>The file's name in a save's directory.</summary>
    public const string FileName = "journal.jsonl";

    /// <summary>The journal as bytes: UTF-8 without a byte-order mark, one JSON object per line, each ending in a newline.</summary>
    public static byte[] Encode(IEnumerable<SessionJournalEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
            text.Append(Line(entry)).Append('\n');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
    }

    /// <summary>Append entries to a journal file, creating it and its directory when they are not there. Returns the bytes written.</summary>
    public static int Append(string path, IEnumerable<SessionJournalEntry> entries)
    {
        var bytes = Encode(entries);
        if (bytes.Length == 0)
            return 0;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
        return bytes.Length;
    }

    /// <summary>
    /// Read a journal back: every complete line, in order, and how many bytes of a partial tail were left aside. A line that is complete but not
    /// a journal object is reported in <paramref name="malformed"/> rather than dropped, because a tool should be able to say the file is odd.
    /// </summary>
    public static ImmutableArray<SessionJournalEntry> Read(string path, out long partialTailBytes, out ImmutableArray<string> malformed)
    {
        partialTailBytes = 0;
        var bad = ImmutableArray.CreateBuilder<string>();
        if (!File.Exists(path))
        {
            malformed = bad.ToImmutable();
            return ImmutableArray<SessionJournalEntry>.Empty;
        }

        var bytes = File.ReadAllBytes(path);
        int end = LastCompleteLineEnd(bytes);
        partialTailBytes = bytes.Length - end;
        if (end == 0)
        {
            malformed = bad.ToImmutable();
            return ImmutableArray<SessionJournalEntry>.Empty;
        }

        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes, 0, end);
        var entries = ImmutableArray.CreateBuilder<SessionJournalEntry>();
        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (TryParse(line, out var entry))
                entries.Add(entry);
            else
                bad.Add(line);
        }
        malformed = bad.ToImmutable();
        return entries.ToImmutable();
    }

    /// <summary>
    /// The layout's repair for an append-only file (§3.3): cut a torn tail off so the next append starts a clean line. Returns how many bytes were
    /// removed; nothing is written when the file ends where it should.
    /// </summary>
    public static long TruncatePartial(string path)
    {
        if (!File.Exists(path))
            return 0;
        var bytes = File.ReadAllBytes(path);
        int end = LastCompleteLineEnd(bytes);
        if (end == bytes.Length)
            return 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(end);
        stream.Flush(flushToDisk: true);
        return bytes.Length - end;
    }

    /// <summary>
    /// Where the complete part of the file ends: the byte after the last newline. A tail with no newline is a line that was still being written,
    /// so it is not read (and not counted as corruption) - the layout's rule for an append-only file.
    /// </summary>
    public static int LastCompleteLineEnd(ReadOnlySpan<byte> bytes)
    {
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] == (byte)'\n')
                return i + 1;
        }
        return 0;
    }

    /// <summary>One line's JSON: fixed field names and invariant number formatting, so the file is diffable and a reader never guesses.</summary>
    private static string Line(SessionJournalEntry entry)
    {
        var text = new StringBuilder("{\"tick\":");
        text.Append(entry.Tick.ToString(CultureInfo.InvariantCulture));
        text.Append(",\"kind\":").Append(Quoted(entry.Kind));
        text.Append(",\"text\":").Append(Quoted(entry.Text)).Append('}');
        return text.ToString();
    }

    private static string Quoted(string value)
    {
        var escaped = new StringBuilder("\"");
        foreach (char c in value)
        {
            escaped.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }
        return escaped.Append('"').ToString();
    }

    private static bool TryParse(string line, out SessionJournalEntry entry)
    {
        entry = new SessionJournalEntry(0, "", "");
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!document.RootElement.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Number)
                return false;
            if (!document.RootElement.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
                return false;
            if (!document.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                return false;
            entry = new SessionJournalEntry(tick.GetInt64(), kind.GetString() ?? "", text.GetString() ?? "");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
