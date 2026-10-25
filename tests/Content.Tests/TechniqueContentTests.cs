using UNNAMED.Domain.Combat;

namespace UNNAMED.Content.Tests;

public class TechniqueContentTests
{
    [Fact]
    public void TheSevenSliceTechniques_HaveWeaponContextAndDiscoverableSources()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var techniques = TeachingContent.BuildTechniques(loader);
        var slice = techniques.Values.Where(t => t.Discovery != TechniqueDiscovery.None).ToArray();
        Assert.Equal(7, slice.Length);
        Assert.Equal(7, slice.Select(t => t.Kind).Distinct().Count());
        Assert.All(slice, t => Assert.NotEmpty(t.Requirements));
        var quiet = techniques["ability.blade.quiet_blade"];
        Assert.Equal(2, quiet.Requirements.Count(r => r.Kind == TechniqueRequirementKind.Skill));
        Assert.Equal("ability.blade.gap_thrust", Assert.Single(quiet.Refines));
        Assert.True(quiet.RequiresDagger);
    }

    [Fact]
    public void TechniqueWeb_RejectsCyclesAndUnknownRefinements()
    {
        var first = new TechniqueDefinition("ability.test.first", "skill.bow", 1, 1, 0, 0, 0) { Refines = ["ability.test.second"] };
        var second = first with { Id = "ability.test.second", Refines = [first.Id] };
        Assert.Throws<FormatException>(() => CombatContent.ValidateTechniqueWeb(new Dictionary<string, TechniqueDefinition> { [first.Id] = first, [second.Id] = second }));
        Assert.Throws<FormatException>(() => CombatContent.ValidateTechniqueWeb(new Dictionary<string, TechniqueDefinition> { [first.Id] = first }));
    }

    [Fact]
    public void WeaponScaling_RefusesMalformedWeights_InsteadOfFallingBack()
    {
        Assert.Throws<FormatException>(() => CombatContent.WeaponScaling(new() { ["scaling"] = "might" }, "skill.bow", false, false));
        Assert.Throws<FormatException>(() => CombatContent.WeaponScaling(new()
        {
            ["scaling"] = new Dictionary<object, object> { ["might"] = "0.8", ["agility"] = "0.8" },
        }, "skill.bow", false, false));
    }
}
