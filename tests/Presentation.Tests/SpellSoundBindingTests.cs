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

    /// <summary>Each spell and how it reaches its target, which decides the moments it must be heard at.</summary>
    private static List<(string Id, string Targeting)> Workings()
    {
        var workings = new List<(string, string)>();
        foreach (string path in Directory.GetFiles(Repository.File("content", "spells"), "*.yaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            var id = Regex.Match(text, @"^id:\s*(spell\.[a-z0-9_.]+)\s*$", RegexOptions.Multiline);
            var targeting = Regex.Match(text, @"^targeting:\s*([a-z_]+)\s*$", RegexOptions.Multiline);
            if (id.Success)
                workings.Add((id.Groups[1].Value, targeting.Success ? targeting.Groups[1].Value : ""));
        }
        return workings;
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
        Assert.True(spells.Count >= 16, $"only {spells.Count} spells were read from content/spells: the sweep is looking in the wrong place");
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

    /// <summary>
    /// A working that throws something is heard three times: gathering in the hand, crossing the gap, and landing. A working kept on the
    /// caster is heard once as it opens - cast, or activation for the ward that has no cast - and once as it settles.
    /// </summary>
    [Fact]
    public void EveryWorking_IsHeardAtEachMomentItsTargetingHas()
    {
        if (ArtRoot() is not { } artRoot)
            return;
        var delivered = Delivered(artRoot);
        var bound = Bound();
        var absent = new List<string>();
        foreach (var (spell, targeting) in Workings())
        {
            if (!bound.TryGetValue(spell, out string? stem))
                continue;   // EverySpell_HasACastSoundBound says so once, in its own words
            bool Heard(string moment) => delivered.Contains($"sfx.magic.{stem}.{moment}");
            var missing = targeting switch
            {
                "projectile" => new[] { "cast", "travel", "impact" }.Where(moment => !Heard(moment)).ToList(),
                "self" => MissingForASelfWorking(Heard),
                _ => [],
            };
            if (missing.Count > 0)
                absent.Add($"{spell} ({targeting}) -> {string.Join(", ", missing)} of sfx.magic.{stem}");
        }
        Assert.True(absent.Count == 0, "workings heard at only some of their moments: " + string.Join("; ", absent));
    }

    private static List<string> MissingForASelfWorking(Func<string, bool> heard)
    {
        var missing = new List<string>();
        if (!heard("cast") && !heard("activate"))
            missing.Add("one of cast/activate");
        if (!new[] { "activate", "resolve", "end" }.Any(heard))
            missing.Add("one of activate/resolve/end");
        return missing;
    }
}
