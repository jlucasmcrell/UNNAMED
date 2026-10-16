// UNNAMED Application - the journal's notes: what the player writes down for themselves (PJ10:250; WP4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Domain;

namespace UNNAMED.Application;

/// <summary>One note: the words, and the world tick they were written at.</summary>
public sealed record JournalNote(
    [property: JsonPropertyName("tick")] long Tick,
    [property: JsonPropertyName("text")] string Text);

/// <summary>
/// The notes one character's journal holds. They are the player's own words, not world state: nothing in the simulation reads them, so they
/// live beside the saves in <c>journal_notes.json</c>, keyed by character, rather than in a save - a note survives a restore from backup.
/// Each change is written at once, through a temporary file renamed over the old one.
/// </summary>
public sealed class JournalNotes
{
    public const string FileName = "journal_notes.json";

    /// <summary>The longest note, in characters.</summary>
    public const int MaxLength = 500;

    private readonly string _path;
    private readonly string _character;

    public JournalNotes(string profileRoot, EntityId character)
    {
        _path = Path.Combine(profileRoot, FileName);
        _character = character.Value;
        All = ReadAll().GetValueOrDefault(_character, ImmutableArray<JournalNote>.Empty);
    }

    /// <summary>The notes, oldest first.</summary>
    public ImmutableArray<JournalNote> All { get; private set; }

    /// <summary>Write a note down; the reason it was refused, or null.</summary>
    public string? Add(string text, long tick)
    {
        text = text.Trim();
        if (text.Length == 0)
            return "a note has words";
        if (text.Length > MaxLength)
            return $"a note is at most {MaxLength} characters";
        Write(All.Add(new JournalNote(tick, text)));
        return null;
    }

    /// <summary>Cross a note out; the reason it was refused, or null.</summary>
    public string? Remove(int index)
    {
        if (index < 0 || index >= All.Length)
            return $"there is no note {index}";
        Write(All.RemoveAt(index));
        return null;
    }

    private void Write(ImmutableArray<JournalNote> notes)
    {
        var all = ReadAll();
        if (notes.IsEmpty)
            all.Remove(_character);
        else
            all[_character] = notes;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, overwrite: true);
        All = notes;
    }

    private SortedDictionary<string, ImmutableArray<JournalNote>> ReadAll()
    {
        var empty = new SortedDictionary<string, ImmutableArray<JournalNote>>(StringComparer.Ordinal);
        if (!File.Exists(_path))
            return empty;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ImmutableArray<JournalNote>>>(File.ReadAllBytes(_path));
            return read is null ? empty : new SortedDictionary<string, ImmutableArray<JournalNote>>(read, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return empty;   // an unreadable notes file loses only notes, never the game
        }
    }
}
