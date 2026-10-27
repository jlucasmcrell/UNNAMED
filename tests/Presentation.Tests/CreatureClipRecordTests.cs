using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The creature clip records are data, and data can be wrong in ways the game only shows on screen: a record whose id does not match its file
/// name, a duration of zero, a malformed event. This reads every one of them through the same parser the game uses, when the art root is on
/// the machine (OTHERREACH_ART_ROOT, or the private copy this workstation keeps), so a bad record fails here instead of in a fight.
/// </summary>
public class CreatureClipRecordTests
{
    private static string? ArtRoot()
    {
        foreach (string? candidate in new[]
                 {
                     Environment.GetEnvironmentVariable("OTHERREACH_ART_ROOT"),
                     @"D:\Otherreach_Scratch\ds_art\primary",
                 })
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(Path.Combine(candidate, "animation", "clips")))
                return candidate;
        }
        return null;
    }

    [Fact]
    public void EveryCreatureClipRecord_Parses_AndNamesItself()
    {
        string? root = ArtRoot();
        if (root is null)
            return;   // no art root on this machine: nothing to check, and no reason to fail the build

        string[] records = Directory.GetFiles(Path.Combine(root, "animation", "clips"), "anim.creature.*.json");
        Assert.NotEmpty(records);
        var problems = new List<string>();
        int states = 0;
        foreach (string path in records)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            ClipRecord? clip = ArtRecords.Clip(File.ReadAllText(path), out string? problem);
            if (clip is null)
            {
                problems.Add($"{name}: {problem}");
                continue;
            }
            states++;
            if (clip.Seconds <= 0)
                problems.Add($"{name}: duration {clip.Seconds}");
            string? id = ClipId(File.ReadAllText(path));
            if (id is not null && id != name)
                problems.Add($"{name}: animation_id is {id}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(states >= 100, $"only {states} creature clip records were read");
    }

    private static string? ClipId(string json)
    {
        int at = json.IndexOf("\"animation_id\"", StringComparison.Ordinal);
        if (at < 0)
            return null;
        int open = json.IndexOf('"', json.IndexOf(':', at) + 1);
        int close = json.IndexOf('"', open + 1);
        return open < 0 || close < 0 ? null : json[(open + 1)..close];
    }
}
