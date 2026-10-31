using System.Text.Json;
using System.Text.RegularExpressions;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// Every sound id the game can ask for resolves to a delivered file - or is on the reviewed list of what is still silence.
///
/// The audio root is the shared art root, not the repo, so this test skips when it is not present (a machine without the assets). When it
/// is present it reads the same manifest the game loads, resolves a base id the way the bank does (families are keyed without their take
/// number, so `sfx.weapon.sword.draw` resolves to `sfx.weapon.sword.draw.01`), and fails on anything the game asks for that neither
/// resolves nor appears in `content/audio_gaps.yaml`.
/// </summary>
public class AudioIdCoverageTests
{
    private static string? ArtRoot()
    {
        string? root = Environment.GetEnvironmentVariable("OTHERREACH_ART_ROOT") ?? @"D:\Otherreach_Shared\art_root";
        return Directory.Exists(Path.Combine(root, "manifests")) ? root : null;
    }

    private static HashSet<string> Delivered(string artRoot)
    {
        string manifest = Path.Combine(artRoot, "manifests", "playable_prototype_audio_v3.json");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        foreach (var sound in document.RootElement.GetProperty("sounds").EnumerateArray())
        {
            string id = sound.GetProperty("audio_id").GetString()!;
            ids.Add(id);
            // the loader keys a family without its take number
            int dot = id.LastIndexOf('.');
            if (dot > 0 && id[(dot + 1)..].All(char.IsDigit))
                ids.Add(id[..dot]);
        }
        return ids;
    }

    /// <summary>Ids the code asks for: the literals and the families it builds, read from the source because they are not data.</summary>
    private static IEnumerable<string> CodeIds()
    {
        foreach (string path in Directory.GetFiles(Repository.File("src"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(text, "\"((?:sfx|mus|amb)\\.[a-z0-9_.]+)\""))
                yield return match.Groups[1].Value;
        }
    }

    /// <summary>Ids content names, which is where creature, station and quest audio is chosen.</summary>
    private static IEnumerable<string> ContentIds()
    {
        foreach (string path in Directory.GetFiles(Repository.File("content"), "*.yaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(text, "\\b((?:sfx|mus|amb)\\.[a-z0-9_.]+)\\b"))
                yield return match.Groups[1].Value;
        }
    }

    /// <summary>The reviewed list of what is still silence, and why.</summary>
    private static HashSet<string> KnownGaps()
    {
        string text = File.ReadAllText(Repository.File("content", "_audio_gaps.yaml"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, "\\b((?:sfx|mus|amb)\\.[a-z0-9_.]+)\\b"))
            ids.Add(match.Groups[1].Value);
        return ids;
    }

    [Fact]
    public void EverySoundIdTheGameAsksFor_ResolvesOrIsOnTheGapsList()
    {
        if (ArtRoot() is not { } artRoot)
            return;   // no audio assets on this machine: nothing to judge
        var delivered = Delivered(artRoot);
        var gaps = KnownGaps();

        var missing = CodeIds().Concat(ContentIds())
            .Distinct(StringComparer.Ordinal)
            .Where(id => !delivered.Contains(id) && !gaps.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "sound ids asked for with no delivered file and not on content/audio_gaps.yaml: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheGapsList_NamesOnlyIdsThatAreActuallyMissing()
    {
        if (ArtRoot() is not { } artRoot)
            return;
        var delivered = Delivered(artRoot);
        var staleGlaps = KnownGaps().Where(delivered.Contains).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(staleGlaps.Count == 0,
            "these are on the gaps list but HAVE been delivered - take them off it: " + string.Join(", ", staleGlaps));
    }

    [Fact]
    public void EveryDeliveredEntry_HasItsFileOnDisk()
    {
        if (ArtRoot() is not { } artRoot)
            return;
        string manifest = Path.Combine(artRoot, "manifests", "playable_prototype_audio_v3.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var absent = new List<string>();
        foreach (var sound in document.RootElement.GetProperty("sounds").EnumerateArray())
        {
            string id = sound.GetProperty("audio_id").GetString()!;
            string delivered = sound.TryGetProperty("delivered", out var path) ? path.GetString() ?? "" : "";
            if (delivered.Length == 0 || !File.Exists(Path.Combine(artRoot, delivered)))
                absent.Add(id);
        }
        Assert.True(absent.Count == 0, "manifest entries whose file is not on disk: " + string.Join(", ", absent));
    }

    [Fact]
    public void EveryCreatureThatIsVoiced_HasAllFourCallsHurtAndDeath()
    {
        if (ArtRoot() is not { } artRoot)
            return;
        var delivered = Delivered(artRoot);

        string bindings = File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_bindings.json"));
        using var document = JsonDocument.Parse(bindings);
        var silent = new List<string>();
        var incomplete = new List<string>();
        foreach (var creature in document.RootElement.GetProperty("creatures").EnumerateObject())
        {
            // a creature with no voice property has no recordings of its own; the gaps list is where that is recorded
            if (!creature.Value.TryGetProperty("voice", out var voiceProperty))
                continue;
            string? voice = voiceProperty.GetString();
            if (string.IsNullOrEmpty(voice))
                continue;
            if (!delivered.Contains($"sfx.creature.{voice}.idle"))
                silent.Add($"{creature.Name} ({voice})");
            foreach (string role in new[] { "attack", "alert", "hurt", "death", "idle" })
                if (!delivered.Contains($"sfx.creature.{voice}.{role}"))
                    incomplete.Add($"sfx.creature.{voice}.{role}");
        }
        Assert.True(silent.Count == 0, "voiced creatures whose recordings are not delivered: " + string.Join(", ", silent));
        Assert.True(incomplete.Count == 0, "voiced creatures missing a role: " + string.Join(", ", incomplete));
    }
}
