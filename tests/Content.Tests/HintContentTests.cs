using UNNAMED.Domain.Guidance;

namespace UNNAMED.Content.Tests;

/// <summary>Hints from content (WP10, §2.10 O1): the game's hints build, cover the new systems' moments, and HNT001 refuses what cannot show.</summary>
public class HintContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    [Fact]
    public void TheGamesHints_Build_AboutTwenty_EachToItsOwnMoment()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.DoesNotContain(loader.Errors, e => e.Code == "HNT001");
        var hints = HintContent.Build(loader);

        Assert.InRange(hints.Count, 18, 30);
        Assert.All(hints.Values, h => Assert.Contains(h.Trigger, HintTriggers.All));
        Assert.All(hints.Values, h => Assert.Null(HintRules.Problem(h.Text)));
        // The new systems' moments (§2.10): the away sheet, hiring, party commands and prices moving.
        foreach (string trigger in new[] { HintTriggers.AwaySheet, HintTriggers.HireOffered, HintTriggers.CompanionRecruited, HintTriggers.PriceChanged })
            Assert.Contains(hints.Values, h => h.Trigger == trigger);
        Assert.Contains("companion_order", HintRules.Actions(hints["hint.party"].Text));
        // What a new game shows first: how to move, then where every key is listed.
        Assert.Equal(new[] { "hint.moving", "hint.help" },
            hints.Values.Where(h => h.Trigger == HintTriggers.NewGame).OrderBy(h => h.Order).Select(h => h.Id));
    }

    [Theory]
    [InlineData("trigger: hire_offered", "trigger: hint_of_lore", "not a moment the game names")]
    [InlineData("order: 0", "order: first", "order must be a whole number")]
    [InlineData("order: 0", "order: 1", "at the same order 1")]   // hint.hiring onto hint.hireling_risk's place
    [InlineData("text: \"A hireling", "text: \"{wage} A hireling", "brace that is not a {key:action} token")]
    public void AHintThatCannotShow_IsRefused(string find, string replace, string mentions)
    {
        string source = Path.Combine(RepoPaths.Root(), "content");
        string root = Path.Combine(Path.GetTempPath(), "unnamed-hnt-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(root, "hints", "hiring.yaml");
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));

            var loader = Load(root);
            Assert.Contains(loader.Errors, e => e.Code == "HNT001" && e.Message.Contains(mentions, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
