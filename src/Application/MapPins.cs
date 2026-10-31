// UNNAMED Application - the map pins: places the player marks for themselves (GEN-01)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Domain;

namespace UNNAMED.Application;

/// <summary>
/// A place the player marked: where it is, the few words they left on it, and the tick they wrote it. The game never places one - a pin is the
/// player's own note about ground, which is exactly what GEN-01 asks for and what the map's discovered places are not.
/// </summary>
public sealed record MapPin(
    [property: JsonPropertyName("x_mm")] long XMm,
    [property: JsonPropertyName("z_mm")] long ZMm,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("tick")] long Tick);

/// <summary>
/// One character's pins, in the order they were marked. Like the journal's notes they are the player's own words about the world rather than
/// world state - nothing in the simulation reads them, no rule depends on one - so they live beside the saves in <c>map_pins.json</c>, keyed by
/// character, and every save and load leaves them alone. Each change is written at once, through a temporary file renamed over the old one, so a
/// crash cannot leave half a pin behind.
/// </summary>
public sealed class MapPins
{
    public const string FileName = "map_pins.json";

    /// <summary>The most pins one character may keep: enough to mark a region's worth of things, few enough to stay a map.</summary>
    public const int Max = 24;

    /// <summary>The longest label, in characters.</summary>
    public const int MaxLabel = 60;

    private readonly string _path;
    private readonly string _character;

    public MapPins(string profileRoot, EntityId character)
    {
        _path = Path.Combine(profileRoot, FileName);
        _character = character.Value;
        All = ReadAll().GetValueOrDefault(_character, ImmutableArray<MapPin>.Empty);
    }

    /// <summary>This character's pins, in the order they were marked.</summary>
    public ImmutableArray<MapPin> All { get; private set; }

    /// <summary>Mark a place, with a label; the reason it was refused, or null.</summary>
    public string? Add(long xMm, long zMm, string label, long tick)
    {
        label = label.Trim();
        if (label.Length == 0)
            return "a pin has a few words on it";
        if (label.Length > MaxLabel)
            return $"a pin's words are at most {MaxLabel} characters";
        if (All.Length >= Max)
            return $"a map holds at most {Max} pins";
        if (All.Any(p => p.XMm == xMm && p.ZMm == zMm))
            return "there is already a pin on that ground";

        All = All.Add(new MapPin(xMm, zMm, label, tick));
        return Write();
    }

    /// <summary>Take a pin off the map: the one marked at this place, if there is one.</summary>
    public string? Remove(long xMm, long zMm)
    {
        if (!All.Any(p => p.XMm == xMm && p.ZMm == zMm))
            return "there is no pin on that ground";
        All = All.RemoveAll(p => p.XMm == xMm && p.ZMm == zMm);
        return Write();
    }

    /// <summary>The reason the file could not be written, or null.</summary>
    private string? Write()
    {
        try
        {
            var all = ReadAll();
            all[_character] = All;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".writing";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, Options));
            File.Move(temporary, _path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"the map's pins could not be written: {e.Message}";
        }
    }

    private Dictionary<string, ImmutableArray<MapPin>> ReadAll()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, ImmutableArray<MapPin>>(StringComparer.Ordinal);
            var read = JsonSerializer.Deserialize<Dictionary<string, ImmutableArray<MapPin>>>(File.ReadAllText(_path), Options);
            return read is null
                ? new Dictionary<string, ImmutableArray<MapPin>>(StringComparer.Ordinal)
                : new Dictionary<string, ImmutableArray<MapPin>>(read, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, ImmutableArray<MapPin>>(StringComparer.Ordinal);
        }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
