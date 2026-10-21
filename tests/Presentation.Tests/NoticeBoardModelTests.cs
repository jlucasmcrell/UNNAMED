using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// Spec 15 (owner finding 15): one notice board, carrying the hamlet's contracts and who is out on their day. The board's words are the
/// world's views read, so what is tested here is that they are read right: open work first, each line carrying the coin, the place and who
/// posted it, a claim named in the line that has it, and nobody who is in the inn or with the character called out.
/// </summary>
public class NoticeBoardModelTests
{
    private static readonly EntityId Player = EntityId.Create(EntityKind.Character, 1, Enumerable.Range(0, 10).Select(i => (byte)i).ToArray());
    private static readonly EntityId Someone = EntityId.Create(EntityKind.Npc, 1, Enumerable.Range(0, 10).Select(i => (byte)(i + 40)).ToArray());
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";

    private static BountyView Bounty(string id, string title, BountyStanding standing, string? claimant = null, string? claimantName = null) =>
        new(id, title, Kera, true, standing) { ClaimantId = claimant, ClaimantName = claimantName };

    private static QuestDefinition Quest(string id, long coin, string? place) =>
        new(id, id, "", Kera, "",
            place is null ? ImmutableArray<ObjectiveDefinition>.Empty
                : ImmutableArray.Create(new ObjectiveDefinition("o", "", new ExploreLocation(place, 5_000), ImmutableArray<string>.Empty)),
            ImmutableArray<ObjectiveCondition>.Empty,
            coin > 0 ? ImmutableArray.Create<QuestReward>(new CurrencyReward(coin)) : ImmutableArray<QuestReward>.Empty);

    private static AdventurerView Adventurer(string name, string goal, string? goalRef, bool inTown, string? party = null) =>
        new("npc.adventurer.ashen_hollow.slot_1_g0", Someone, name, "ashen_hollow", "ashen_hollow", 2, 10, AdventurerCondition.Active,
            goal, goalRef, party, inTown, 0, 0, 50, ImmutableArray.Create("item.weapon.rusted_sword"));

    private static string PlaceNames(string id) => id == Renn ? "Renn Vale" : id == Kera ? "Kera Voss"
        : id == "location.old_east_road" ? "the Old East Road" : id;

    [Fact]
    public void EachContract_SaysWhatItPays_Where_AndWhoPostedIt_AndWhoHasTakenIt()
    {
        var quests = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal)
        {
            ["quest.a"] = Quest("quest.a", 12, "location.old_east_road"),
            ["quest.b"] = Quest("quest.b", 30, null),
            ["quest.c"] = Quest("quest.c", 5, null),
        };
        var contracts = NoticeBoardModel.Contracts(new[]
        {
            Bounty("quest.a", "Wolves on the road", BountyStanding.Claimed, Someone.Value, "Isla Vessey"),
            Bounty("quest.b", "Timber for Renn", BountyStanding.Open),
            Bounty("quest.c", "The boar in the birch", BountyStanding.Held),
        }, Player, id => quests.GetValueOrDefault(id), PlaceNames);
        Assert.Equal(new[] { "Timber for Renn", "The boar in the birch", "Wolves on the road" }, contracts.Select(c => c.Title));
        // Open first, and each line carries its own facts rather than the board's rules.
        Assert.True(contracts[0].Open);
        Assert.Equal("30 coin - posted by Kera Voss", contracts[0].Facts);
        Assert.Null(contracts[0].Taken);
        Assert.Equal("5 coin - posted by Kera Voss", contracts[1].Facts);
        Assert.Equal("Yours", contracts[1].Taken);
        Assert.Equal("12 coin - at the Old East Road - posted by Kera Voss", contracts[2].Facts);
        Assert.Equal("Taken by Isla Vessey", contracts[2].Taken);
        Assert.False(contracts[2].Open);
        // The character's own finished contract reads done.
        var done = NoticeBoardModel.Contracts(new[] { Bounty("quest.a", "Wolves on the road", BountyStanding.Claimed, Player.Value, "you") },
            Player, id => quests.GetValueOrDefault(id), PlaceNames);
        Assert.Equal("Done, and paid", Assert.Single(done).Taken);
    }

    [Fact]
    public void WhoIsOut_IsEveryoneNeitherInTheInnNorWithTheCharacter_AndWhatTheyWentFor()
    {
        var away = NoticeBoardModel.Away(new[]
        {
            Adventurer("Isla Vessey", AdventurerGoals.Hunt, "spawn.hollow.den_pack", inTown: false),
            Adventurer("Tesk Marrow", AdventurerGoals.Bounty, "quest.bounty.road_wolves", inTown: false),
            Adventurer("Garret Lindmoor", AdventurerGoals.Travel, "cairnwold", inTown: false),
            Adventurer("Tobin Ashcombe", AdventurerGoals.Rest, null, inTown: true),
            Adventurer("With you", AdventurerGoals.Hunt, "spawn.hollow.hares", inTown: false, party: "temporary_ally"),
        }, id => id == "cairnwold" ? "Cairnwold" : id);
        Assert.Equal(new[] { "Garret Lindmoor", "Isla Vessey", "Tesk Marrow" }, away.Select(a => a.Name));
        Assert.Equal(new[] { "on the road to Cairnwold", "out hunting, back before dark", "out on a bounty, and not back till it is finished" },
            away.Select(a => a.Doing));
    }

    [Fact]
    public void TheLineUnderThePrompt_CountsBothHalves()
    {
        var nothing = NoticeBoardModel.Contracts(Array.Empty<BountyView>(), Player, _ => null, PlaceNames);
        var none = NoticeBoardModel.Away(Array.Empty<AdventurerView>(), id => id);
        Assert.Equal("nothing open - nobody out", NoticeBoardModel.Hint(nothing, none));
        var one = NoticeBoardModel.Contracts(new[] { Bounty("q", "Wolves", BountyStanding.Open) }, Player, _ => null, PlaceNames);
        var two = NoticeBoardModel.Away(new[]
        {
            Adventurer("Isla Vessey", AdventurerGoals.Hunt, null, inTown: false),
            Adventurer("Tesk Marrow", AdventurerGoals.Hunt, null, inTown: false),
        }, id => id);
        Assert.Equal("1 contract open - 2 out on their day", NoticeBoardModel.Hint(one, two));
    }
}
