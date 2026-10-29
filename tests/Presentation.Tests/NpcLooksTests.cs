using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// Every NPC's look (F1 Gate C): a town's residents are all different from each other, the same person is the same person in any save or
/// any world, and every generated look already keeps the rule its people's canon sets.
/// </summary>
public class NpcLooksTests
{
    /// <summary>The creator's catalogue and its people rules, read from the shipped file as the game reads it.</summary>
    private static (CreatorCatalogue Catalogue, HumGenCreatorData Data) Catalogue()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Presentation"))) directory = directory.Parent;
        string json = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Presentation", "Art", "humgen_creator.json"));
        return (CreatorCatalogue.Read(json), new HumGenCreatorData(json));
    }

    private static readonly (CreatorCatalogue Catalogue, HumGenCreatorData Data) Shipped = Catalogue();

    private static CharacterAppearance Resident(string slot, string people, string sex, int position, int generation = 0,
        string settlement = "ashen_hollow") =>
        NpcLooks.Resident(slot, settlement, generation, people, sex, Shipped.Catalogue, Shipped.Data, position);

    /// <summary>Six plus six slots, one people: twelve residents of one town wear twelve different faces.</summary>
    [Fact]
    public void ATownsSlots_WearNoTwoTheSameFace()
    {
        var slots = Enumerable.Range(0, 12).Select(i => ($"routine.ashen_hollow.slot{i}", "people.veth", "f")).ToArray();
        var roster = NpcLooks.Residents(slots, "ashen_hollow", Shipped.Catalogue, Shipped.Data);

        Assert.Equal(12, roster.Count);
        Assert.Equal(12, roster.Values.Select(look => look.Face).Distinct(StringComparer.Ordinal).Count());
        // And the same roster read in another order is the same roster: the assignment is the town's, not the caller's.
        var reversed = NpcLooks.Residents(slots.Reverse(), "ashen_hollow", Shipped.Catalogue, Shipped.Data);
        Assert.All(roster, entry => Assert.Equal(entry.Value, reversed[entry.Key]));
    }

    /// <summary>A resident's look is a pure function of who they are: the same call twice, and the same person in another town's roster.</summary>
    [Fact]
    public void TheSameResident_IsTheSameLook_EveryTime()
    {
        Assert.Equal(Resident("routine.ashen_hollow.cook", "people.veth", "f", 3), Resident("routine.ashen_hollow.cook", "people.veth", "f", 3));
        // Different people, slots and towns are different people.
        Assert.NotEqual(Resident("routine.ashen_hollow.cook", "people.veth", "f", 3), Resident("routine.ashen_hollow.cook", "people.veth", "f", 4));
        Assert.NotEqual(Resident("routine.ashen_hollow.cook", "people.veth", "f", 3), Resident("routine.ashen_hollow.cook", "people.veth", "f", 3, settlement: "cairnwold"));
        Assert.NotEqual(Resident("routine.ashen_hollow.cook", "people.veth", "f", 3),
            Resident("routine.ashen_hollow.cook", "people.veth", "f", 3, generation: 1));
    }

    /// <summary>An arrival is someone new in the same slot: a different face from the resident who held it before.</summary>
    [Fact]
    public void AnArrival_TakesAFaceNoOtherHolderOfTheSlotWore()
    {
        var holders = Enumerable.Range(0, 4).Select(generation => Resident("routine.ashen_hollow.cook", "people.veth", "f", 0, generation).Face).ToArray();

        Assert.Equal(4, holders.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Every generated look already keeps its people's rule, so nothing is taken off it when it is drawn.</summary>
    [Fact]
    public void EveryGeneratedLook_SurvivesItsPeoplesRule()
    {
        foreach (string people in new[] { "people.veth", "people.orenth", "people.kal", "people.siann" })
        foreach (string sex in new[] { "f", "m" })
        {
            var look = Resident("routine.ashen_hollow.slot", people, sex, 1);
            Assert.Null(look.Problem());
            Assert.Equal(look, Shipped.Data.Constrain(look, Shipped.Catalogue, people));
            Assert.Equal("outfit.casual.smart_casual", look.Outfit);
            Assert.Equal("footwear.outdoor.garden_boot", look.Footwear);
            Assert.InRange(look.StatureMm, Shipped.Catalogue.Race(people)!.MinStatureMm, Shipped.Catalogue.Race(people)!.MaxStatureMm);
        }
    }

    /// <summary>A Kal grows stone and never hair or a beard; a Siann's hair is long and her age is her people's floor to thirty.</summary>
    [Fact]
    public void AKalIsNeverHaired_AndASiannIsYoungAndLongHaired()
    {
        foreach (string sex in new[] { "f", "m" })
        {
            var kal = Resident("routine.ashen_hollow.miner", "people.kal", sex, 4);
            Assert.Null(kal.Hair);
            Assert.Null(kal.FacialHair);

            var siann = Resident("routine.ashen_hollow.fletcher", "people.siann", sex, 1);
            Assert.StartsWith("hair.long.", siann.Hair, StringComparison.Ordinal);
            Assert.Null(siann.FacialHair);
            Assert.InRange(siann.Sliders["face.age"], 18, 30);
            Assert.Contains(siann.Face, Shipped.Catalogue.Options(CreatorChoice.Face, sex).Select(o => o.Id));
        }
    }
}
