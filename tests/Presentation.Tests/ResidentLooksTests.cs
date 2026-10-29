using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation.Tests;

public class ResidentLooksTests
{
    [Theory]
    [InlineData("people.kal", "f", "humgen_race_kal_f_compact")]
    [InlineData("people.kal", "m", "humgen_race_kal_m_compact")]
    [InlineData("people.siann", "f", "humgen_race_siann_f_long")]
    [InlineData("people.orenth", "m", "humgen_race_orenth_m_frontier")]
    [InlineData("people.mor", "m", null)]
    [InlineData("people.ondrek", "m", null)]
    [InlineData("people.constructed", "f", null)]
    [InlineData("people.vaskaal", "f", null)]
    public void OnlyDrawnResidentPeoplesUseTheirOwnSexedRaceBody(string people, string sex, string? expected)
    {
        var bodies = new PlayerBodies(new[] {
            new BodyModels("kal_f", new[]{"humgen_race_kal_f_compact"}), new BodyModels("kal_m", new[]{"humgen_race_kal_m_compact"}),
            new BodyModels("siann_f", new[]{"humgen_race_siann_f_long"}), new BodyModels("orenth_m", new[]{"humgen_race_orenth_m_frontier"}),
            new BodyModels("mor_m", new[]{"humgen_pool_01"}),
        }, null, "average");
        Assert.Equal(expected, bodies.ResidentModel(people, sex));
        Assert.Null(PlayerBodies.None.ResidentModel(people, sex));
    }

    [Theory]
    [InlineData("people.kal")]
    [InlineData("people.siann")]
    [InlineData("people.veth")]
    public void ResidentColoursAreStableVariedAndStayInTheirPeoplesPalettes(string people)
    {
        var choices=Enumerable.Range(0,48).Select(i=>LookSwatches.ResidentColours(people,"resident-instance-"+i)).ToArray();
        Assert.Equal(choices,Enumerable.Range(0,48).Select(i=>LookSwatches.ResidentColours(people,"resident-instance-"+i)).ToArray());
        Assert.True(choices.Distinct().Count()>=8);
        Assert.All(choices,c=>{Assert.InRange(c.Skin,0,LookSwatches.Skin(people).Count-1);Assert.InRange(c.Hair,0,LookSwatches.Hair(people).Count-1);});
        if(people=="people.siann")Assert.All(choices,c=>Assert.Contains(LookSwatches.Hair(people)[c.Hair].Label,new[]{"Platinum white","Pearl white","White"}));
    }
}
