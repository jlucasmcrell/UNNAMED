using UNNAMED.Content;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The shipped progression config (content/config) builds the rules PROGRESSION.md specifies, and the lint
/// catches a broken one (PRG codes). DATA_MODEL.md §4.19, §4.21.
/// </summary>
public class ProgressionContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "unnamed-progression-" + Guid.NewGuid().ToString("N"));

    public ProgressionContentTests()
    {
        // A copy of the game's progression content: its config and skills. The inventory and damage configs name items
        // and effects, which this copy does not carry, so they stay behind.
        foreach (string dir in new[] { "config", "skills" })
        {
            Directory.CreateDirectory(Path.Combine(_root, dir));
            foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "content", dir))
                         .Where(f => Path.GetFileName(f) is not ("inventory.yaml" or "damage_constants.yaml")))
                File.Copy(file, Path.Combine(_root, dir, Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private void Edit(string relative, string from, string to)
    {
        string path = Path.Combine(_root, relative);
        string text = File.ReadAllText(path).Replace("\r\n", "\n");   // checked out with CRLF on Windows
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
    }

    [Fact]
    public void TheGameContent_BuildsTheRulesProgressionMdSpecifies()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);

        var rules = ProgressionContent.BuildRules(loader);

        Assert.Equal(2_448_025, rules.Curve.TotalTo(50));            // PROGRESSION.md §3.2
        Assert.Equal((5, 50), (rules.LevelCap, rules.SoftCap));       // PROTOTYPE.md's cap while it is in force
        Assert.Equal(1, rules.AttributePointsPerLevel);               // §3.1: and nothing else
        Assert.Equal(57_600, rules.TicksPerWorldDay);                 // config.time: 1440 game minutes x 2 s x 20 Hz
        Assert.Equal((15, 60), (rules.Skills.DifficultyMargin, rules.Skills.CommonCeiling));
        Assert.Equal(0.60, rules.Guards.LevelBandMultiplier(-3));     // AG-1
        Assert.Equal(1.00, rules.Guards.LevelBandMultiplier(0));
        Assert.Equal(1.30, rules.Guards.LevelBandMultiplier(7));
        Assert.Equal(0.00, rules.Guards.LevelBandMultiplier(-40));
        Assert.Equal(3, rules.AttributeGrantBudget);                  // §11.3
        Assert.Equal(5, rules.TierTargets.Length);
        Assert.Equal(CharacterProgression.Empty.Digest, ProgressionEngine.Create(rules).Digest);   // Phase 1: no starting package yet
    }

    [Fact]
    public void TheGameContent_DefinesThePrototypesSkills_WithFamilies()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));

        Assert.Equal(
            new[] { "skill.athletics", "skill.one_hand_blade", "skill.survival" },
            loader.GetByKind("skill").Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void ACopyOfTheGameConfig_Lints()
    {
        Assert.Empty(Load(_root).Errors);
    }

    [Fact]
    public void ASkillWithoutAValidFamily_FailsTheLint()
    {
        Edit(Path.Combine("skills", "athletics.yaml"), "family: world", "family: sporty");

        Assert.Contains(Load(_root).Errors, e => e.Code == "PRG001" && e.Message.Contains("skill.athletics"));
    }

    [Fact]
    public void ACurveWhoseTotalDrifts_FailsTheLint()
    {
        // The recorded total is derived from the formula; editing one without the other is the 28.8% bug again.
        Edit(Path.Combine("config", "xp_curve.yaml"), "exponent: 1.85", "exponent: 1.80");

        Assert.Contains(Load(_root).Errors, e => e.Code == "PRG003");
    }

    [Fact]
    public void ALevelBandWithAGap_FailsTheLint()
    {
        Edit(Path.Combine("config", "progression.yaml"), "    - [-5, -1, 0.60]\n", "");

        Assert.Contains(Load(_root).Errors, e => e.Code == "PRG005" && e.Message.Contains("AG-1"));
    }

    [Fact]
    public void AStartingPackageNamingAnUndefinedSkill_FailsTheLint()
    {
        Edit(Path.Combine("config", "progression.yaml"), "  skills: {}", "  skills: { skill.juggling: 5 }");

        Assert.Contains(Load(_root).Errors, e => e.Code == "PRG005" && e.Message.Contains("skill.juggling"));
    }

    [Fact]
    public void AMalformedValue_FailsTheLint_AndBuildingRulesSaysWhy()
    {
        Edit(Path.Combine("config", "progression.yaml"), "attribute_base: 10", "attribute_base: ten");

        var loader = Load(_root);
        Assert.Contains(loader.Errors, e => e.Code == "PRG004");
        var error = Assert.Throws<InvalidOperationException>(() => ProgressionContent.BuildRules(loader));
        Assert.Contains("attribute_base", error.Message);
    }

    [Fact]
    public void APackWithoutProgressionConfig_Lints_ButCannotBuildRules()
    {
        // Test fixture packs carry skills but no progression config; they are content, not a game.
        Directory.Delete(Path.Combine(_root, "config"), recursive: true);

        var loader = Load(_root);
        Assert.Empty(loader.Errors);
        var error = Assert.Throws<InvalidOperationException>(() => ProgressionContent.BuildRules(loader));
        Assert.Contains("config.progression is missing", error.Message);
    }
}
