// UNNAMED Domain tests - the technique requirement record (PRG-29)
using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// PRG-29's record: one gate vocabulary for a technique, whatever path it arrives by - skill, attribute, another technique known, gear in
/// hand, the body, a knowledge record, and standing - with a reason in the player's words for every refusal.
/// </summary>
public class TechniqueRequirementsTests
{
    private static TechniqueCandidate Alive(
        int blade = 12, int might = 4, int presence = 3,
        bool knowsStrike = false, bool carriesBlade = false, string body = "people.veth",
        bool readTheMeasure = false, int standingKera = 0) => new(
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).SetItem("skill.one_hand_blade", blade),
        ImmutableSortedDictionary.Create<CharacterAttribute, int>().SetItem(CharacterAttribute.Might, might).SetItem(CharacterAttribute.Presence, presence),
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal).Add(knowsStrike ? "ability.blade.power_strike" : "ability.blade.other"),
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal).Add(carriesBlade ? "item.weapon.rusted_sword" : "item.tool.pick"),
        body,
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal).Add(readTheMeasure ? "knowledge.the_measure" : "knowledge.something_else"),
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).SetItem("npc.ashen_hollow.kera_voss", standingKera));

    private static readonly ImmutableArray<TechniqueRequirement> NoRequirements = ImmutableArray<TechniqueRequirement>.Empty;

    [Fact]
    public void ATechniqueThatAsksNothing_IsOpenToAnyone()
    {
        Assert.True(TechniqueRequirements.Met(NoRequirements, Alive()));
        Assert.Empty(TechniqueRequirements.Check(NoRequirements, Alive()));
        Assert.Null(TechniqueRequirements.FirstUnmet(NoRequirements, Alive()));
    }

    [Fact]
    public void ASkillIsAskedForByItsLevel()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Skill, "skill.one_hand_blade", 10));
        Assert.True(TechniqueRequirements.Met(needs, Alive(blade: 12)));

        var short_ = TechniqueRequirements.FirstUnmet(needs, Alive(blade: 4));
        Assert.NotNull(short_);
        Assert.Equal("Short of one hand blade: 4 of 10", short_!.Why);
    }

    [Fact]
    public void AnAttributeIsAskedForByNameAndReadsInThePlayersWords()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Attribute, "Might", 5));
        Assert.True(TechniqueRequirements.Met(needs, Alive(might: 5)));

        var refusal = TechniqueRequirements.FirstUnmet(needs, Alive(might: 3));
        Assert.Equal("Short of Might: 3 of 5", refusal!.Why);

        // An attribute the game does not have is a content error, not a silently unmet gate.
        var missing = TechniqueRequirements.FirstUnmet(
            ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Attribute, "Luck", 1)), Alive());
        Assert.Equal("'Luck' is not an attribute", missing!.Why);
    }

    [Fact]
    public void AnotherTechniqueMustBeKnownFirst()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Known, "ability.blade.power_strike", 1));
        Assert.True(TechniqueRequirements.Met(needs, Alive(knowsStrike: true)));
        Assert.Equal("Needs power strike first", TechniqueRequirements.FirstUnmet(needs, Alive())!.Why);
    }

    [Fact]
    public void GearIsAskedForByWhatIsCarried()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Equipment, "item.weapon.rusted_sword", 1));
        Assert.True(TechniqueRequirements.Met(needs, Alive(carriesBlade: true)));
        Assert.Equal("Needs rusted sword in hand", TechniqueRequirements.FirstUnmet(needs, Alive())!.Why);
    }

    [Fact]
    public void ABodyCannotBeArguedWith()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Body, "people.veth", 1));
        Assert.True(TechniqueRequirements.Met(needs, Alive(body: "people.veth")));
        Assert.Equal("Body cannot: veth only", TechniqueRequirements.FirstUnmet(needs, Alive(body: "people.kal"))!.Why);
    }

    [Fact]
    public void KnowledgeIsARequirementLikeAnyOther()
    {
        var needs = ImmutableArray.Create(new TechniqueRequirement(TechniqueRequirementKind.Knowledge, "knowledge.the_measure", 1));
        Assert.True(TechniqueRequirements.Met(needs, Alive(readTheMeasure: true)));
        Assert.Equal("Needs the knowledge: the measure", TechniqueRequirements.FirstUnmet(needs, Alive())!.Why);
    }

    [Fact]
    public void StandingIsARequirementToo_AndTheFirstUnmetIsTheOneThePlayerHears()
    {
        var needs = ImmutableArray.Create(
            new TechniqueRequirement(TechniqueRequirementKind.Skill, "skill.one_hand_blade", 10),
            new TechniqueRequirement(TechniqueRequirementKind.Standing, "npc.ashen_hollow.kera_voss", 2));

        Assert.True(TechniqueRequirements.Met(needs, Alive(blade: 20, standingKera: 5)));

        // The skill is met and the standing is not: the standing is what is said.
        Assert.Equal("Not in standing with kera voss: 0 of 2", TechniqueRequirements.FirstUnmet(needs, Alive(blade: 20))!.Why);

        // Both unmet: the first in content order is the one said, so a list reads top to bottom.
        Assert.StartsWith("Short of one hand blade", TechniqueRequirements.FirstUnmet(needs, Alive(blade: 1))!.Why);
    }

    [Fact]
    public void EveryRequirementIsReported_SoAWhyCantITableCanShowThemAll()
    {
        var needs = ImmutableArray.Create(
            new TechniqueRequirement(TechniqueRequirementKind.Skill, "skill.one_hand_blade", 10),
            new TechniqueRequirement(TechniqueRequirementKind.Attribute, "Might", 5),
            new TechniqueRequirement(TechniqueRequirementKind.Equipment, "item.weapon.rusted_sword", 1));

        var verdicts = TechniqueRequirements.Check(needs, Alive(blade: 12, might: 4, carriesBlade: false));
        Assert.Equal(3, verdicts.Length);
        Assert.True(verdicts[0].Met);
        Assert.False(verdicts[1].Met);
        Assert.False(verdicts[2].Met);
    }

    [Fact]
    public void ARequirementWithNoSubject_IsContentErrorSaidPlainly()
    {
        var nameless = new TechniqueRequirement(TechniqueRequirementKind.Attribute, null, 1);
        Assert.NotNull(nameless.Problem());
        Assert.Equal(nameless.Problem(), TechniqueRequirements.FirstUnmet(ImmutableArray.Create(nameless), Alive())!.Why);
    }
}
