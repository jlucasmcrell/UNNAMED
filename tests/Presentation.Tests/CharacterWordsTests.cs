using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>The Character sheet's Call line, the nine-way source label and the Skills list's default roster (UI progression items A, H, N).</summary>
public class CharacterWordsTests
{
    private static string Name(string id) => id switch
    {
        "call.wayfarer" => "Wayfarer",
        "skill.athletics" => "Athletics",
        "skill.one_hand_blade" => "One-hand blade",
        "npc.ashen_hollow.renn_vale" => "Renn Vale",
        "npc.ashen_hollow.kera_voss" => "Kera Voss",
        "ability.blade.power_strike" => "Power Strike",
        _ => Names.Words(id),
    };

    private static CallDefinition Wayfarer() => new("call.wayfarer", "The road kept going, so you did.", "…",
        new StartingPackage(
            ImmutableSortedDictionary<CharacterAttribute, int>.Empty
                .Add(CharacterAttribute.Endurance, 2).Add(CharacterAttribute.Might, 1).Add(CharacterAttribute.Agility, 1),
            ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).Add("skill.athletics", 15).Add("skill.one_hand_blade", 15),
            ImmutableArray<string>.Empty),
        ImmutableArray<StartingItem>.Empty, new CallContact("npc.ashen_hollow.renn_vale", 5));

    [Fact]
    public void TheCallLine_NamesTheCall_ItsPackageInOneLine_AndItsContact()
    {
        var calls = new Dictionary<string, CallDefinition> { ["call.wayfarer"] = Wayfarer() };
        var line = CharacterWords.Call("call.wayfarer", calls, Name)!;
        Assert.Equal("Wayfarer", line.Name);
        Assert.Equal("The road kept going, so you did.", line.Tagline);
        Assert.Equal("Leans Endurance +2 · Agility +1 · Might +1 · starts Athletics 15, One-hand blade 15", line.Package);
        Assert.Equal("Renn Vale, who already trusts you a little", line.Contact);
    }

    [Fact]
    public void ACharacterWithNoCall_OrOneTheContentDropped_HasNoCallLine()
    {
        var calls = new Dictionary<string, CallDefinition> { ["call.wayfarer"] = Wayfarer() };
        Assert.Null(CharacterWords.Call(null, calls, Name));
        Assert.Null(CharacterWords.Call("call.gone", calls, Name));
    }

    [Theory]
    [InlineData(LearningSource.StartingPackage, "Culture")]
    [InlineData(LearningSource.Culture, "Culture")]
    [InlineData(LearningSource.Teacher, "Learned")]
    [InlineData(LearningSource.Book, "Learned")]
    [InlineData(LearningSource.Experiment, "Learned")]
    [InlineData(LearningSource.Discovery, "Learned")]
    [InlineData(LearningSource.Artifact, "Gear")]
    public void EverySource_HasOneOfTheCanonLabels(LearningSource source, string label) => Assert.Equal(label, CharacterWords.SourceLabel(source));

    [Fact]
    public void TheLabel_SitsBesideTheSourceLine()
    {
        var taught = new KnownTechnique(LearningSource.Teacher, "npc.ashen_hollow.kera_voss", 10);
        Assert.Equal("Learned: taught by Kera Voss", CharacterWords.Labelled(taught, Name));
        Assert.Equal("Culture: known from the start", CharacterWords.Labelled(new KnownTechnique(LearningSource.StartingPackage, null, 0), Name));
        Assert.Equal("Learned: worked out at the bench", CharacterWords.Labelled(new KnownTechnique(LearningSource.Experiment, null, 5), Name));
    }

    private static readonly IReadOnlyList<SkillEntry> Roster = new[]
    {
        new SkillEntry("skill.athletics", "world"), new SkillEntry("skill.bow", "combat"), new SkillEntry("skill.force", "magic"),
        new SkillEntry("skill.smithing", "crafting"),
    };

    [Fact]
    public void ByDefault_OnlyThePractisedAndTheLinkedAreListed()
    {
        var linked = new HashSet<string> { "skill.force" };
        var shown = DisciplineRoster.Shown(Roster, id => id == "skill.athletics", linked, showAll: false);
        Assert.Equal(new[] { "skill.athletics", "skill.force" }, shown.Select(s => s.Id));
        Assert.Equal(2, DisciplineRoster.Hidden(Roster, shown));
        Assert.Equal("Show all disciplines (2 more)", DisciplineRoster.Toggle(false, 2));
    }

    [Fact]
    public void ShowAll_ListsEveryDiscipline_AndOffersTheWayBack()
    {
        var shown = DisciplineRoster.Shown(Roster, _ => false, new HashSet<string>(), showAll: true);
        Assert.Equal(Roster.Count, shown.Count);
        Assert.Equal("Show only what you practise", DisciplineRoster.Toggle(true, 0));
    }

    [Fact]
    public void TheChosenDiscipline_StaysListed_EvenUnpractised()
    {
        var shown = DisciplineRoster.Shown(Roster, _ => false, new HashSet<string>(), showAll: false, keep: "skill.bow");
        Assert.Equal("skill.bow", Assert.Single(shown).Id);
    }
}
