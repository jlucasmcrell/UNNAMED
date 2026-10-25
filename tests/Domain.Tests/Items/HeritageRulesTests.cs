using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>Heritage (owner, 2026-10-24): an item of a people's heritage is for that people alone; anything else is for anyone.</summary>
public class HeritageRulesTests
{
    private static readonly ItemDefinition Roll =
        new("item.heritage.kept_roll", "misc", 1, 100, 8, "common", true, false, null, Requirements.None, null, 0) { Heritage = "people.veth" };

    private static readonly ItemDefinition Flask =
        new("item.tool.water_flask", "tool", 1, 500, 6, "common", false, false, null, Requirements.None, null, 0);

    [Theory]
    [InlineData("people.veth", true)]
    [InlineData("people.ondrek", false)]
    [InlineData(null, false)]
    public void AHeritageItem_IsForItsOwnPeopleOnly(string? people, bool allowed)
    {
        Assert.Equal(allowed, HeritageRules.Allows(Roll, people));
        Assert.Equal(allowed ? null : "that is Veth heritage: only their own people use or trade it", HeritageRules.Refusal(Roll, people));
    }

    [Theory]
    [InlineData("people.veth")]
    [InlineData("people.ondrek")]
    [InlineData(null)]
    public void AnythingElse_IsForAnyone(string? people) => Assert.True(HeritageRules.Allows(Flask, people));
}

/// <summary>A title reward names the character by their surname: the last word of their name.</summary>
public class TitleRewardTests
{
    [Theory]
    [InlineData("Wenna Lind", "Keeper of the Lind roll")]
    [InlineData("Kester Vale-of-Stones", "Keeper of the Vale-of-Stones roll")]
    [InlineData("Ada", "Keeper of the Ada roll")]
    public void TheSurname_IsTheLastWordOfTheName(string name, string title) =>
        Assert.Equal(title, new UNNAMED.Domain.Quests.TitleReward("Keeper of the {surname} roll").For(name));
}
