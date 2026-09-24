using UNNAMED.Domain.Magic;

namespace UNNAMED.Content.Tests;

/// <summary>Magic from content (M3e): the three formulas build in whole ticks, and the MAG lint refuses what Phase 1 cannot cast.</summary>
public class MagicContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-mag-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "MAG001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesMagic_IsThreeFormulasOfThreeDomains_InWholeTicks()
    {
        var magic = MagicContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        Assert.Equal(new[] { "spell.force.impulse_bolt", "spell.vital.mending_thread", "spell.warding.brace_ward" }, magic.Formulas.Keys);
        Assert.Equal(3, magic.Formulas.Values.Select(f => f.DomainSkillId).Distinct().Count());

        var bolt = magic.Formulas["spell.force.impulse_bolt"];
        Assert.Equal(("skill.force", 4, 10, 8, 12, Targeting.Projectile), (bolt.DomainSkillId, bolt.Complexity, bolt.FocusCost, bolt.StrainCost, bolt.CastTicks, bolt.Targeting));
        Assert.Equal(("physical_blunt", 9, 13, 20_000L, true, "skill.force"), (bolt.Blow!.DamageType, bolt.Blow.DamageMin, bolt.Blow.DamageMax, bolt.Blow.ReachMm,
            bolt.Blow.Magic, bolt.Blow.SkillId));

        var ward = magic.Formulas["spell.warding.brace_ward"];
        Assert.Equal((8, Targeting.Self), (ward.CastTicks, ward.Targeting));
        Assert.Equal(new[] { "effect.braced" }, ward.Applies);
        var thread = magic.Formulas["spell.vital.mending_thread"];
        Assert.Equal(new[] { "effect.mending" }, thread.Applies);
        Assert.Equal(new[] { "effect.bleeding", "effect.venom" }, thread.Removes);

        // config.magic in ticks: Focus returns 2 s after a working, Strain ebbs after 3 s; a release takes 0.3 s to recover from.
        Assert.Equal((40, 60, 6, 75), (magic.Constants.FocusRegenDelayTicks, magic.Constants.StrainRecoveryDelayTicks, magic.Constants.RecoveryTicks,
            magic.Constants.StrainedPercent));
        Assert.Equal(new[] { "spell.force.impulse_bolt", "spell.warding.brace_ward", "spell.vital.mending_thread" }, magic.Teaches["item.tome.resonance_primer"]);
    }

    [Fact]
    public void AFormulaCostingMana_IsRefused() =>
        AssertRefused("spells/force/impulse_bolt.yaml", "cost: { focus: 10, strain: 8 }", "cost: { focus: 10, strain: 8, mana: 5 }", "there is no mana");

    [Fact]
    public void AFormulaOfANonMagicDomain_IsRefused() =>
        AssertRefused("spells/force/impulse_bolt.yaml", "domain: skill.force", "domain: skill.athletics", "is not a magic-domain skill");

    [Fact]
    public void AProjectileWithoutItsDamage_IsRefused() =>
        AssertRefused("spells/force/impulse_bolt.yaml", "  - { type: damage, damage_type: physical_blunt, amount: [9, 13] }",
            "  - { type: apply_effect, effect_ref: effect.braced }", "is not built for a projectile formula");

    [Fact]
    public void ATargetingPhaseOneDoesNotBuild_IsRefused() =>
        AssertRefused("spells/warding/brace_ward.yaml", "targeting: self", "targeting: aoe_cone", "targeting 'aoe_cone' is not built");

    [Fact]
    public void ABookTeachingSomethingOtherThanAFormula_IsRefused() =>
        AssertRefused("items/tome/resonance_primer.yaml", "    - { kind: spell, ref: spell.vital.mending_thread }",
            "    - { kind: recipe, ref: spell.vital.mending_thread }", "Phase 1 books teach formulas");

    [Fact]
    public void ABookReadMoreThanOnce_IsRefused() =>
        AssertRefused("items/tome/resonance_primer.yaml", "  consume: true", "  consume: false", "a book that teaches is read once");
}
