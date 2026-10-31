using UNNAMED.Domain.Magic;
using UNNAMED.World.Runtime;

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

    /// <summary>The game's own pack, built once for the tests that only read it: the content is 1,370-odd definitions and re-parsing it
    /// per case would make a theory over eight workings cost eight loads.</summary>
    private static readonly Lazy<MagicSetup> Game =
        new(() => MagicContent.Build(Load(Path.Combine(RepoPaths.Root(), "content"))));

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

    private static void AssertRefused(string relativePath, string find, string replace, string mentions, string code = "MAG001")
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesMagic_IsThreeFormulasOfThreeDomains_InWholeTicks()
    {
        var magic = Game.Value;

        // Playtest 2 (data-only): the three original workings, the six first-hour formulas the spell lane added, two to a school, and the
        // eight this pass adds - three of force, two of vital, three of warding - each with its own cooldown, its own skill floor and the
        // art it borrows until Codex's VFX pass gives it its own.
        Assert.Equal(new[]
        {
            "spell.force.cinder_dart", "spell.force.impulse_bolt", "spell.force.keen_note", "spell.force.rime_pin",
            "spell.force.salt_arc", "spell.force.shiver_spike",
            "spell.vital.clear_the_dead_weight", "spell.vital.ember_mend", "spell.vital.mending_thread", "spell.vital.set_the_bone",
            "spell.vital.wake_the_nerve",
            "spell.warding.brace_ward", "spell.warding.holdfast", "spell.warding.hush_the_sting", "spell.warding.loose_the_root",
            "spell.warding.quiet_seal", "spell.warding.tripwire_sigil",
        }, magic.Formulas.Keys);
        Assert.Equal(new[] { "skill.force", "skill.vital", "skill.warding" },
            magic.Formulas.Values.Select(f => f.DomainSkillId).Distinct().OrderBy(d => d, StringComparer.Ordinal));

        // Every formula but the three the game shipped with sits inside the band a first-hour caster can hold: a rung of a ladder that
        // runs from a one-complexity note to a four-complexity nerve working, never past what one Focus pool pays for.
        foreach (var early in magic.Formulas.Values.Where(f => f.Id is not ("spell.force.impulse_bolt" or "spell.vital.mending_thread" or "spell.warding.brace_ward")))
        {
            Assert.InRange(early.Complexity, 1, 4);
            Assert.InRange(early.FocusCost, 1, 15);
            Assert.InRange(early.StrainCost, 1, 12);
            Assert.True(early.CastTicks > 0);
        }

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

        // The first-hour additions: the schools' own damage types and effects, and one source for all of them - the primer the survey
        // scholar already hands out, so nothing new has to be found for Playtest 2.
        Assert.Equal("fire", magic.Formulas["spell.force.cinder_dart"].Blow!.DamageType);
        Assert.Equal("frost", magic.Formulas["spell.force.shiver_spike"].Blow!.DamageType);
        Assert.Equal(new[] { "effect.holdfast" }, magic.Formulas["spell.warding.holdfast"].Applies);
        // In the order the formula lifts them: the venom first, then the wound.
        Assert.Equal(new[] { "effect.venom", "effect.bleeding" }, magic.Formulas["spell.warding.hush_the_sting"].Removes);
        Assert.Equal(new[] { "effect.ember_mending" }, magic.Formulas["spell.vital.ember_mend"].Applies);
        Assert.Equal(new[] { "effect.weakened" }, magic.Formulas["spell.vital.clear_the_dead_weight"].Removes);
        // The primer teaches all nine, in whatever order the file lists them: what matters is the set, not the line order.
        Assert.Equal(
            new[]
            {
                "spell.force.cinder_dart", "spell.force.impulse_bolt", "spell.force.shiver_spike",
                "spell.vital.clear_the_dead_weight", "spell.vital.ember_mend", "spell.vital.mending_thread",
                "spell.warding.brace_ward", "spell.warding.holdfast", "spell.warding.hush_the_sting",
            }.OrderBy(id => id, StringComparer.Ordinal),
            magic.Teaches["item.tome.resonance_primer"].OrderBy(id => id, StringComparer.Ordinal));
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
            "    - { kind: quest, ref: spell.vital.mending_thread }", "a book teaches formulas or recipes");

    [Fact]
    public void ABookReadMoreThanOnce_IsRefused() =>
        AssertRefused("items/tome/resonance_primer.yaml", "  consume: true", "  consume: false", "a book that teaches is read once");

    // ── the eight first-hour workings, one test each ───────────────────────────────────────────────────────────────────────────
    // Each row is that formula's data: the school it works, what it costs, its own cooldown in ticks (config.time's 50 ms), the skill
    // floor a lesson keeps, and the formula whose art it borrows while it has none of its own.

    [Theory]
    [InlineData("spell.force.keen_note", "skill.force", 1, 4, 3, 20, 0, "spell.force.impulse_bolt")]
    [InlineData("spell.force.rime_pin", "skill.force", 1, 5, 4, 24, 0, "spell.force.impulse_bolt")]
    [InlineData("spell.force.salt_arc", "skill.force", 3, 9, 7, 70, 15, "spell.force.impulse_bolt")]
    [InlineData("spell.vital.set_the_bone", "skill.vital", 2, 8, 6, 80, 0, "spell.vital.mending_thread")]
    [InlineData("spell.vital.wake_the_nerve", "skill.vital", 4, 12, 9, 120, 15, "spell.vital.mending_thread")]
    [InlineData("spell.warding.quiet_seal", "skill.warding", 2, 8, 6, 60, 0, "spell.warding.brace_ward")]
    [InlineData("spell.warding.tripwire_sigil", "skill.warding", 2, 10, 7, 100, 15, "spell.warding.brace_ward")]
    [InlineData("spell.warding.loose_the_root", "skill.warding", 3, 9, 7, 80, 15, "spell.warding.brace_ward")]
    public void AFirstHourWorking_IsTheWorkingItsContentSays(string id, string domain, int complexity, int focus, int strain, int cooldownTicks,
        int requires, string art)
    {
        var magic = Game.Value;
        var formula = magic.Formulas[id];

        Assert.Equal((domain, complexity, focus, strain, cooldownTicks, requires),
            (formula.DomainSkillId, formula.Complexity, formula.FocusCost, formula.StrainCost, formula.CooldownTicks, formula.RequiresSkillLevel));
        // Its art is another formula's, and that formula is one the pack builds - never an id invented for a working that has no art yet.
        Assert.Equal(art, formula.Art);
        Assert.Contains(formula.Art, magic.Formulas.Keys);
        // A cooldown is a wait a person can feel, and never longer than a first-hour fight.
        Assert.InRange(formula.CooldownTicks, 1, 200);
    }

    [Theory]
    [InlineData("spell.force.keen_note", "damage:physical_blunt:4:7")]
    [InlineData("spell.force.rime_pin", "damage:frost:4:7")]
    [InlineData("spell.force.salt_arc", "damage:shock:8:12")]
    [InlineData("spell.vital.set_the_bone", "applies:effect.mending")]
    [InlineData("spell.vital.wake_the_nerve", "removes:effect.sleep,effect.paralyse")]
    [InlineData("spell.warding.quiet_seal", "applies:effect.muffled")]
    [InlineData("spell.warding.tripwire_sigil", "applies:effect.ward_tripwire")]
    [InlineData("spell.warding.loose_the_root", "removes:effect.root")]
    public void AFirstHourWorking_DoesWhatItsContentSays(string id, string expected)
    {
        var magic = Game.Value;
        var formula = magic.Formulas[id];
        string read = formula.Blow is { } blow
            ? $"damage:{blow.DamageType}:{blow.DamageMin}:{blow.DamageMax}"
            : formula.Applies.Length > 0 && formula.Removes.Length == 0 ? $"applies:{string.Join(",", formula.Applies)}"
            : formula.Removes.Length > 0 && formula.Applies.Length == 0 ? $"removes:{string.Join(",", formula.Removes)}"
            : $"both:{string.Join(",", formula.Applies)}/{string.Join(",", formula.Removes)}";
        Assert.Equal(expected, read);
    }

    [Fact]
    public void AWorkingBorrowingArtThatDoesNotExist_IsRefused() =>
        AssertRefused("spells/force/keen_note.yaml", "vfx: spell.force.impulse_bolt", "vfx: vfx.force.impulse_bolt_travel",
            "is not a formula this pack builds");

    [Fact]
    public void ALessonOfferedBelowTheSkillItsWorkingNeeds_IsRefused() =>
        AssertRefused("npcs/ashen_hollow/sel_arien.yaml",
            "spell_ref: spell.force.salt_arc, fee: 18, requires: { skill: { skill_ref: skill.force, band: familiar }",
            "spell_ref: spell.force.salt_arc, fee: 18, requires: { skill: { skill_ref: skill.force, band: untrained }",
            "offers it at 0 (untrained)", "TCH001");

    [Fact]
    public void ASkillFloorThatIsNotABand_IsRefused() =>
        AssertRefused("spells/force/salt_arc.yaml", "requires_skill_level: 15", "requires_skill_level: 12",
            "is not a band floor config.teaching names", "TCH001");

    [Fact]
    public void ABookHandingOutAWorkingALessonWouldWithhold_IsRefused() =>
        AssertRefused("items/tome/resonance_primer.yaml", "    - { kind: spell, ref: spell.vital.mending_thread }",
            "    - { kind: spell, ref: spell.vital.mending_thread }\n    - { kind: spell, ref: spell.force.salt_arc }",
            "a book cannot teach spell.force.salt_arc", "TCH001");
}
