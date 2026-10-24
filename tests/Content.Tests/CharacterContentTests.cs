using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Peoples, Calls and bodies from content (WP2, WP17): the Veth, the four CC-0 Calls and the Veth's five builds of each body type build,
/// and the CHR lint refuses what the creator cannot offer.
/// </summary>
public class CharacterContentTests
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
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-chr-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "CHR001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesCreator_OffersTheFourStartingPeoples_AndFourCalls_BuiltFromWhatExists()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.DoesNotContain(loader.Errors, e => e.Code == "CHR001");
        var setup = CharacterContent.Build(loader);

        // Owner, 2026-10-24: Veth, Kal, Siann and Orenth to start; all eight peoples are defined, with their traits and cards.
        Assert.Equal(new[] { "people.kal", "people.orenth", "people.siann", "people.veth" }, setup.PlayablePeoples.Select(p => p.Id));
        Assert.Equal(8, setup.Peoples.Count);
        Assert.All(setup.Peoples.Values, p =>
        {
            Assert.NotNull(p.Card);
            Assert.NotEqual(PeopleTraits.None, p.Traits);
            Assert.Equal(2, p.Traits.GrantTotal);
            Assert.NotEmpty(p.Abilities);
        });
        Assert.Contains("Wenna Lind", setup.Peoples["people.veth"].Names.Names);
        Assert.Equal(CharacterSpec.DefaultOrigin, Assert.Single(setup.Origins.Keys));

        Assert.Equal(new[] { "call.hedge_healer", "call.scholar", "call.smiths_hand", "call.wayfarer" }, setup.Calls.Keys);
        var smith = setup.Calls["call.smiths_hand"];
        Assert.Equal(new[] { (CharacterAttribute.Might, 2), (CharacterAttribute.Endurance, 2) }.OrderBy(a => a.Item1),
            smith.Package.AttributeBias.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(new[] { "skill.polearm", "skill.smithing" }, smith.Package.Skills.Keys);
        Assert.Equal(new CallContact("npc.ashen_hollow.kera_voss", 5), smith.Contact);
        Assert.All(setup.Calls.Values, call =>
        {
            Assert.Equal(CallDefinition.BiasPoints, call.Package.AttributeBias.Values.Sum());
            Assert.Equal(CallDefinition.SkillCount, call.Package.Skills.Count);
            Assert.Empty(call.Package.Techniques);
            Assert.NotEmpty(call.Kit);
            Assert.NotEmpty(call.Tagline);
            Assert.DoesNotContain("class", call.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// RACE_ABILITIES_PROPOSAL.md §4.2's guardrails, on the shipped numbers: no people is strictly stronger. Health against physical blows
    /// stays within 0.80-1.65 of the usual, and the time to the common ceiling within 0.70-1.35 of it.
    /// </summary>
    [Fact]
    public void ThePeoples_StayInsideTheApprovedGuardrails()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        var setup = CharacterContent.Build(loader);
        var model = ProgressionContent.BuildRules(loader).Skills;
        double usual = Enumerable.Range(0, model.CommonCeiling).Sum(level => (double)model.ToNext(level));
        foreach (var people in setup.Peoples.Values)
        {
            var traits = people.Traits;
            double taken = traits.DamageTakenPercent.GetValueOrDefault("physical_slash", 100) / 100.0;
            double effective = traits.HealthPercent / 100.0 / taken;
            Assert.InRange(effective, 0.80, 1.65);
            double time = Enumerable.Range(0, model.CommonCeiling).Sum(level => model.ToNext(level) * 100.0 / traits.Learning.RatePercent("skill.athletics", level));
            Assert.InRange(time / usual, 0.70, 1.35);
        }
        // The Veth come fastest to the common ceiling, the Siann slowest (RACES.md §1, §3).
        double Time(string id) => Enumerable.Range(0, model.CommonCeiling).Sum(level => model.ToNext(level) * 100.0 / setup.Peoples[id].Traits.Learning.RatePercent("skill.athletics", level));
        Assert.True(setup.Peoples.Keys.All(id => Time("people.veth") <= Time(id) && Time("people.siann") >= Time(id)));
    }

    [Fact]
    public void ACallSpendingTooManyPoints_IsRefused() =>
        AssertRefused("calls/wayfarer.yaml", "endurance: 2, might: 1", "endurance: 3, might: 1", "a Call spends exactly 4");

    [Fact]
    public void ACallStartingASkillAboveFifteen_IsRefused() =>
        AssertRefused("calls/scholar.yaml", "skill.force: 15", "skill.force: 20", "each at 15");

    [Fact]
    public void ACallStartingAnUndefinedSkill_IsRefused() =>
        AssertRefused("calls/scholar.yaml", "skill.force: 15", "skill.astronomy: 15", "skill.astronomy is not a defined skill");

    [Fact]
    public void ACallWearingOverTheCommonKit_IsRefused() =>
        AssertRefused("calls/smiths_hand.yaml", "item.armor.hide_cap, count: 1, equip: true", "item.weapon.march_spear, count: 1, equip: true",
            "cannot be worn over the common starting kit");

    [Fact]
    public void ACallWhoseContactIsNoNpc_IsRefused() =>
        AssertRefused("calls/hedge_healer.yaml", "contact: { npc_ref: npc.ashen_hollow.renn_vale", "contact: { npc_ref: npc.cairnwold.nobody", "is not a defined npc");

    [Fact]
    public void APeopleWithAnUnusableName_IsRefused() =>
        AssertRefused("people/veth.yaml", "  - Teagan Peel", "  - Teagan Peel of the Long Road Beyond the Hills", "A name is at most 24");

    [Fact]
    public void APeopleListingANameTwice_IsRefused() =>
        AssertRefused("people/veth.yaml", "  - Teagan Peel", "  - Teagan Peel\n  - Wenna Lind", "lists a name twice");

    [Fact]
    public void TheVeth_HaveFiveBuildsOfEachBodyType_LeanAndAverageOffered_AsContent()
    {
        var setup = CharacterContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        Assert.Equal(10, setup.Bodies.Values.Count(b => b.PeopleId == "people.veth"));
        foreach (var type in new[] { ("veth_m", "Masculine"), ("veth_f", "Feminine") })
        {
            var builds = setup.Bodies.Values.Where(b => b.Type == type.Item1).OrderBy(b => b.Order).ToList();
            Assert.Equal(new[] { "very_lean", "lean", "average", "heavy", "very_heavy" }, builds.Select(b => b.Build));
            Assert.All(builds, b => Assert.Equal(("people.veth", type.Item2, $"body.{type.Item1}.{b.Build}"), (b.PeopleId, b.TypeLabel, b.Id)));
            // Heavy and the extremes are held until the clipping gate passes them (owner 2026-10-17)
            Assert.All(builds.Where(b => b.Build is "lean" or "average"), b => Assert.True(b.Offered));
            Assert.All(builds, b => Assert.Equal(new[] { "chest", "hips", "thigh", "waist" }, b.MeasurementsMm.Keys));
            // heavier builds measure more round the waist, and swing their arms wider of their own sides
            Assert.True(builds.Zip(builds.Skip(1)).All(p => p.First.MeasurementsMm["waist"] < p.Second.MeasurementsMm["waist"]
                                                              && p.First.ArmSplayDegrees <= p.Second.ArmSplayDegrees));
        }
        Assert.Equal(setup.Bodies.Values.Where(b => b.Offered && b.PeopleId == "people.veth").Select(b => b.Id).OrderBy(i => i, StringComparer.Ordinal),
            setup.OfferedBodies("people.veth").Select(b => b.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void ABodyOfAnUndefinedPeople_IsRefused() =>
        AssertRefused("bodies/veth_m_lean.yaml", "people_ref: people.veth", "people_ref: people.nobody", "people.nobody is not a defined people");

    [Fact]
    public void ABodyNamedForAnotherBuild_IsRefused() =>
        AssertRefused("bodies/veth_m_lean.yaml", "build: lean", "build: slight", "must be named body.<type>.<build>");

    [Fact]
    public void TwoBuildsAtOneOrder_AreRefused() =>
        AssertRefused("bodies/veth_m_lean.yaml", "order: 2", "order: 3", "share build order 3");

    [Fact]
    public void AnArmSplayOutOfRange_IsRefused() =>
        AssertRefused("bodies/veth_f_heavy.yaml", "arm_splay_deg: 3", "arm_splay_deg: 45", "arm_splay_deg must be a whole number of degrees, 0 to 30");
}
