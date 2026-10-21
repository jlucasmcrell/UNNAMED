using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// Spec 15 (owner finding 15): one notice board, carrying the hamlet's contracts and who is out on their day. The board's words are the
/// world's views read, so what is tested here is that they are read right - open work first, a claim named, and nobody who is in the inn
/// or with the character called out.
/// </summary>
public class NoticeBoardModelTests
{
    private static readonly EntityId Player = EntityId.Create(EntityKind.Character, 1, Enumerable.Range(0, 10).Select(i => (byte)i).ToArray());
    private static readonly EntityId Someone = EntityId.Create(EntityKind.Npc, 1, Enumerable.Range(0, 10).Select(i => (byte)(i + 40)).ToArray());

    private static BountyView Bounty(string id, string title, BountyStanding standing, string? claimant = null, string? claimantName = null) =>
        new(id, title, "npc.ashen_hollow.renn_vale", true, standing) { ClaimantId = claimant, ClaimantName = claimantName };

    private static AdventurerView Adventurer(string name, string goal, string? goalRef, bool inTown, string? party = null) =>
        new("npc.adventurer.ashen_hollow.slot_1_g0", Someone, name, "ashen_hollow", "ashen_hollow", 2, 10, AdventurerCondition.Active,
            goal, goalRef, party, inTown, 0, 0, 50, ImmutableArray.Create("item.weapon.rusted_sword"));

    [Fact]
    public void TheBoard_PutsOpenWorkFirst_AndNamesWhoHasTakenWhat()
    {
        var contracts = NoticeBoardModel.Contracts(new[]
        {
            Bounty("quest.a", "Wolves on the road", BountyStanding.Claimed, Someone.Value, "Isla Vessey"),
            Bounty("quest.b", "Timber for Renn", BountyStanding.Open),
            Bounty("quest.c", "The boar in the birch", BountyStanding.Held),
        }, Player);
        Assert.Equal(new[] { "Timber for Renn", "The boar in the birch", "Wolves on the road" }, contracts.Select(c => c.Title));
        Assert.Equal("Open: first to finish it is paid", contracts[0].Standing);
        Assert.True(contracts[0].Open);
        Assert.Equal(new[] { false, false }, contracts.Skip(1).Select(c => c.Open));
        Assert.Equal("Taken by Isla Vessey", contracts[2].Standing);
        // One the character finished is on the board as done; another's is not claimed by the character at all.
        Assert.Equal("Done, and paid", NoticeBoardModel.Standing(Bounty("quest.d", "A", BountyStanding.Claimed, Player.Value, "you"), Player));
        Assert.Contains("Yours", NoticeBoardModel.Standing(Bounty("quest.e", "B", BountyStanding.Held), Player));
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
        var nothing = NoticeBoardModel.Contracts(Array.Empty<BountyView>(), Player);
        var none = NoticeBoardModel.Away(Array.Empty<AdventurerView>(), id => id);
        Assert.Equal("nothing open - nobody out", NoticeBoardModel.Hint(nothing, none));
        var one = NoticeBoardModel.Contracts(new[] { Bounty("q", "Wolves", BountyStanding.Open) }, Player);
        var two = NoticeBoardModel.Away(new[]
        {
            Adventurer("Isla Vessey", AdventurerGoals.Hunt, null, inTown: false),
            Adventurer("Tesk Marrow", AdventurerGoals.Hunt, null, inTown: false),
        }, id => id);
        Assert.Equal("1 contract open - 2 out on their day", NoticeBoardModel.Hint(one, two));
    }
}
