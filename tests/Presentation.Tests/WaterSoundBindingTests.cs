using System.Text.Json;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The player in water: every movement state the swimming code names has a sound bound to it, a wade is heard by its depth, and the bed
/// under the surface exists. The state names are the contract (<c>wade, swim, tread, enter_water, exit_water</c>), so this fails if one is
/// added to the movement and not to the bindings - or removed from the bindings while the movement still asks for it.
/// </summary>
public class WaterSoundBindingTests
{
    /// <summary>The movement states the swimming code names, and nothing else: the table is this list, exactly.</summary>
    private static readonly string[] States = ["wade", "swim", "tread", "enter_water", "exit_water"];

    private static string? ArtRoot()
    {
        string? root = Environment.GetEnvironmentVariable("OTHERREACH_ART_ROOT") ?? @"D:\Otherreach_Shared\art_root";
        return Directory.Exists(Path.Combine(root, "manifests")) ? root : null;
    }

    private static JsonElement Water()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_bindings.json")));
        return document.RootElement.TryGetProperty("audio", out var audio) && audio.TryGetProperty("water", out var water)
            ? water.Clone()
            : default;
    }

    private static Dictionary<string, string> Map(JsonElement water, string name) =>
        water.ValueKind == JsonValueKind.Object && water.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static HashSet<string> Delivered(string artRoot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(artRoot, "manifests", "playable_prototype_audio_v3.json")));
        foreach (var sound in document.RootElement.GetProperty("sounds").EnumerateArray())
        {
            string id = sound.GetProperty("audio_id").GetString()!;
            ids.Add(id);
            int dot = id.LastIndexOf('.');
            if (dot > 0 && id[(dot + 1)..].All(char.IsDigit))
                ids.Add(id[..dot]);
        }
        return ids;
    }

    [Fact]
    public void EveryMovementStateInWater_IsBound()
    {
        var states = Map(Water(), "states");
        var missing = States.Where(state => !states.ContainsKey(state) || states[state].Length == 0).ToList();
        Assert.True(missing.Count == 0, "water movement states with no sound bound in art_bindings.json audio.water.states: " + string.Join(", ", missing));
        var extra = states.Keys.Where(state => !States.Contains(state, StringComparer.Ordinal)).ToList();
        Assert.True(extra.Count == 0, "sound bound to a water state the movement does not name: " + string.Join(", ", extra));
    }

    [Fact]
    public void A_Wade_IsHeardAtEachDepth()
    {
        var depths = Map(Water(), "wade_depths");
        var missing = new[] { "shallow", "knee_deep" }.Where(depth => !depths.ContainsKey(depth) || depths[depth].Length == 0).ToList();
        Assert.True(missing.Count == 0, "wade depths with no sound bound in art_bindings.json audio.water.wade_depths: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryBoundWaterSound_IsDelivered()
    {
        if (ArtRoot() is not { } artRoot)
            return;   // no audio assets on this machine: nothing to judge
        var delivered = Delivered(artRoot);
        var water = Water();
        var bound = Map(water, "states").Concat(Map(water, "wade_depths"))
            .Select(b => $"{b.Key} -> {b.Value}");
        if (water.ValueKind == JsonValueKind.Object && water.TryGetProperty("underwater_bed", out var bed) && bed.GetString() is { Length: > 0 } bedId)
            bound = bound.Append($"underwater_bed -> {bedId}");
        var absent = bound.Where(entry => !delivered.Contains(entry.Split(" -> ")[1])).OrderBy(entry => entry, StringComparer.Ordinal).ToList();
        Assert.True(absent.Count == 0, "water sounds bound with nothing delivered: " + string.Join(", ", absent));
    }

    [Fact]
    public void TheUnderwaterBed_IsDeliveredAsALoop()
    {
        if (ArtRoot() is not { } artRoot)
            return;
        var water = Water();
        if (water.ValueKind != JsonValueKind.Object || !water.TryGetProperty("underwater_bed", out var bed) || bed.GetString() is not { Length: > 0 } id)
        {
            Assert.Fail("no underwater_bed is bound in art_bindings.json audio.water");
            return;
        }
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(artRoot, "manifests", "playable_prototype_audio_v3.json")));
        var entry = document.RootElement.GetProperty("sounds").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("audio_id").GetString() == id + ".01" || s.GetProperty("audio_id").GetString() == id);
        Assert.True(entry.ValueKind == JsonValueKind.Object, $"{id} has no manifest entry");
        Assert.True(entry.GetProperty("loop").GetBoolean(), $"{id} is not marked as a loop");
        Assert.True(entry.GetProperty("seconds").GetDouble() > 2.0, $"{id} is too short for a bed");
    }
}
