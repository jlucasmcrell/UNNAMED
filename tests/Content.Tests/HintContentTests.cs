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
    public void TheGamesHints_Build_TheOnboardingPack_EachToItsOwnMoment()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.DoesNotContain(loader.Errors, e => e.Code == "HNT001");
        var hints = HintContent.Build(loader);

        Assert.InRange(hints.Count, 43, 60);   // DeepSeek's pack: 43 hints (25 existing, 18 proposed), plus the owner's auto-run hint
        Assert.All(hints.Values, h => Assert.Contains(h.Trigger, HintTriggers.All));
        Assert.All(hints.Values, h => Assert.Null(HintRules.Problem(h.Text)));
        // The new systems' moments (§2.10): the away sheet, hiring, party commands and prices moving.
        foreach (string trigger in new[] { HintTriggers.AwaySheet, HintTriggers.HireOffered, HintTriggers.CompanionRecruited, HintTriggers.PriceChanged })
            Assert.Contains(hints.Values, h => h.Trigger == trigger);
        Assert.Contains("companion_order", HintRules.Actions(hints["hint.party"].Text));
        // What a new game shows first: how to move, then where every key is listed - everyone's hints, in order.
        Assert.Equal(new[] { "hint.moving", "hint.help", "hint.calls", "hint.auto_run" },
            hints.Values.Where(h => h.Trigger == HintTriggers.NewGame && h.People.IsDefaultOrEmpty).OrderBy(h => h.Order).Select(h => h.Id));
        // And a people's own hint waits with them (note 3): the Kal are told how to open the wings only they have.
        var wings = hints["hint.kal_wings"];
        Assert.Equal(HintTriggers.NewGame, wings.Trigger);
        Assert.Equal(new[] { "people.kal" }, wings.People);
        Assert.Contains("{key:jump}", wings.Text);
    }

    [Fact]
    public void TheSixNewMoments_HaveHints_AndTheThreeNewCategoriesExist()
    {
        var hints = HintContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));
        foreach (string trigger in new[] { HintTriggers.BarOpened, HintTriggers.StashOpened, HintTriggers.TrainerOpened, HintTriggers.ItemWorn, HintTriggers.ChatOpened,
                     HintTriggers.ReputationOpened })
        {
            Assert.Contains(trigger, HintTriggers.All);
            Assert.Contains(hints.Values, h => h.Trigger == trigger);
        }
        foreach (string category in new[] { "social", "hiring", "identity" })
            Assert.Contains(hints.Values, h => h.Category == category);
        Assert.Equal(34, HintTriggers.All.Count);   // 31 existing moments plus C3's three first-hour cues.
        foreach (string trigger in new[] { HintTriggers.DarkSpaceEntered, HintTriggers.RawMeatTaken, HintTriggers.ForgeOpened })
            Assert.Contains(hints.Values, h => h.Trigger == trigger);
        Assert.Contains(hints.Values, h => h.Trigger == HintTriggers.PackFull && h.Category == "bags");
    }

    [Fact]
    public void LightGuidance_ShowsTheOrderZeroHintOfEachCategory_AndNoOtherOfTheSameCategory()
    {
        var hints = HintContent.Build(Load(Path.Combine(RepoPaths.Root(), "content"))).Values.ToList();
        // Play through every moment in turn on Light: what is shown is the first hint of each category that comes round, then no more of that category.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shown = new List<HintDefinition>();
        for (int round = 0; round < 6; round++)
        {
            foreach (string trigger in HintTriggers.All)
            {
                while (HintRules.Pick(hints, trigger, seen, HintDepth.FirstOfEachCategory) is { } hint)
                {
                    seen.Add(hint.Id);
                    shown.Add(hint);
                }
            }
        }
        Assert.Equal(shown.Select(h => h.Category).Distinct().Count(), shown.Count);   // one a category
        Assert.Equal(hints.Select(h => h.Category).Distinct().Count(), shown.Count);   // every category has one
        // The movement hint a new game opens with is the movement category's first, so Light keeps it.
        Assert.Contains(shown, h => h.Id == "hint.moving");
        Assert.DoesNotContain(shown, h => h.Id == "hint.auto_run");   // the auto-run hint is Full only
    }

    [Fact]
    public void EveryHint_FitsTheBrief_AndTheAutoRunHintNamesItsKey()
    {
        var hints = HintContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));
        Assert.All(hints.Values, h => Assert.True(h.Text.Length <= 140, $"{h.Id} is {h.Text.Length} characters"));
        Assert.Contains("auto_run", HintRules.Actions(hints["hint.auto_run"].Text));
        Assert.Equal("movement", hints["hint.auto_run"].Category);
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

    // ---- the Field Guide (1I): F1 searches this content, so every hint has to be reachable from it ---------------------------

    [Fact]
    public void TheFieldGuide_CanFindEveryHintByAWordFromItsOwnText()
    {
        var hints = HintContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));
        Assert.NotEmpty(hints);
        var entries = hints.Values.Select(h => FieldGuide.Of(h, action => action == "interact" ? "E" : action)).ToArray();
        Assert.All(entries, e =>
        {
            Assert.Equal("Guidance", e.Section);
            Assert.DoesNotContain('{', e.Title);      // the key tokens are spelled out before the guide shows them
            Assert.Contains("waits until", e.Body);
        });
        foreach (var pair in hints.Keys.Zip(entries))
        {
            string word = pair.Second.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .First(w => w.Length >= 4 && w.All(c => char.IsLetter(c) || c == '-'));
            Assert.True(FieldGuide.Search(entries, word).Contains(pair.Second), $"{pair.First} cannot be found by '{word}'");
        }
    }

    [Fact]
    public void TheStuckMoment_HasItsOwnHint_AndAPlayerWhoIsStuckSeesItFirst()
    {
        var hints = HintContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));
        var stuck = Assert.Single(hints.Values.Where(h => h.Trigger == HintTriggers.Stuck));
        Assert.Equal("help", stuck.Category);
        Assert.Contains("help", HintRules.Actions(stuck.Text));
        Assert.Equal(stuck.Id, StuckHints.Candidates(hints.Values, new HashSet<string>(StringComparer.Ordinal))[0].Id);
    }
}
