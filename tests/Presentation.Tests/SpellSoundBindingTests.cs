using System.Text.Json;
using System.Text.RegularExpressions;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// Every working the game teaches has a voice: a formula bound to a magic sound stem, and - where the audio is on this machine - that
/// stem's cast sound delivered. The nine P2 workings are the reason this exists: a spell added to content without one is a working that
/// casts in silence, and nothing else in the game would say so.
/// </summary>
public class SpellSoundBindingTests
{
    private static string? ArtRoot()
    {
        string? root = Environment.GetEnvironmentVariable("OTHERREACH_ART_ROOT") ?? @"D:\Otherreach_Shared\art_root";
        return Directory.Exists(Path.Combine(root, "manifests")) ? root : null;
    }

    /// <summary>The formula sounds the bindings name: <c>spell.force.cinder_dart</c> -> <c>cinder_dart</c>.</summary>
    private static Dictionary<string, string> Bound()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_bindings.json")));
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("audio", out var audio) || !audio.TryGetProperty("formulas", out var formulas))
            return bound;
        foreach (var formula in formulas.EnumerateObject())
            bound[formula.Name] = formula.Value.GetString() ?? "";
        return bound;
    }

    /// <summary>Every spell the content defines, read off the files rather than a list that would drift.</summary>
    private static List<string> Spells()
    {
        var spells = new List<string>();
        foreach (string path in Directory.GetFiles(Repository.File("content", "spells"), "*.yaml", SearchOption.AllDirectories))
        {
            var id = Regex.Match(File.ReadAllText(path), @"^id:\s*(spell\.[a-z0-9_.]+)\s*$", RegexOptions.Multiline);
            if (id.Success)
                spells.Add(id.Groups[1].Value);
        }
        return spells;
    }

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
    public void EverySpell_HasACastSoundBound()
    {
        var spells = Spells();
        Assert.True(spells.Count >= 9, $"only {spells.Count} spells were read from content/spells: the sweep is looking in the wrong place");
        var bound = Bound();
        var silent = spells.Where(spell => !bound.ContainsKey(spell) || bound[spell].Length == 0).ToList();
        Assert.True(silent.Count == 0, "spells with no formula sound bound in art_bindings.json audio.formulas: " + string.Join(", ", silent));
    }

    [Fact]
    public void EveryBindingsEntry_NamesASpellTheContentDefines()
    {
        var spells = Spells().ToHashSet(StringComparer.Ordinal);
        var stale = Bound().Keys.Where(formula => !spells.Contains(formula)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "formula sounds bound to spells the content does not define: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryBoundWorking_IsHeardAtItsCastOrItsActivation()
    {
        if (ArtRoot() is not { } artRoot)
            return;   // no audio assets on this machine: nothing to judge
        var delivered = Delivered(artRoot);
        var absent = new List<string>();
        foreach (var (formula, stem) in Bound())
        {
            // The ward answers when it settles, not when it starts - SoundEvents finds no cast for it and plays activate instead - so a
            // family may be heard at either moment. A family that answers at neither is a working that casts in silence.
            if (!delivered.Contains($"sfx.magic.{stem}.cast") && !delivered.Contains($"sfx.magic.{stem}.activate"))
                absent.Add($"{formula} -> sfx.magic.{stem}.cast or .activate");
        }
        Assert.True(absent.Count == 0,
            "bound working sounds with nothing delivered to hear: " + string.Join(", ", absent.OrderBy(id => id, StringComparer.Ordinal)));
    }
}
