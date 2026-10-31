// UNNAMED Domain tests - a home: claiming it, building it, furnishing it, and the chest that is a door to the shared stash (HSG-; queue #6).

using System.Collections.Immutable;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Housing;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// A home is a plot claimed, a house built from goods, furniture set down inside it, and a chest that opens the ONE shared stash once the house
/// stands. Every rule here is pure, so what a home may become is testable without a world.
/// </summary>
public class HomeTests
{
    private static HomeDefinition Plot() => new(
        "home.test.plot", "starting_frontier", 500_000, 700_000, 250, "The test plot")
    {
        Materials = ImmutableArray.Create(new HomeMaterial("item.material.timber", 6)),
        RadiusMm = 9_000,
    };

    private static FurniturePlacement At(long x, long z, int facing = 0) => new("item.misc.stool", x, z, facing);

    [Fact]
    public void APlotIsClaimedOnce_AndBuiltOnlyAfterItIsClaimed()
    {
        var plot = Plot();
        Assert.Null(HomeRules.WhyNotClaim(plot, null));
        Assert.Equal("that plot is not yours to build on", HomeRules.WhyNotBuild(null));

        var claimed = Homes.Claim(plot);
        Assert.Equal(plot.Id, claimed.HomeId);
        Assert.False(claimed.Built);
        Assert.NotNull(HomeRules.WhyNotClaim(plot, claimed));

        var built = Homes.Build(claimed);
        Assert.True(built.Built);
        Assert.Equal("the house is already built", HomeRules.WhyNotBuild(built));
    }

    [Fact]
    public void TheChestOfAHomeOpensOnlyOnceTheHouseStands()
    {
        var plot = Plot();
        var claimed = Homes.Claim(plot);

        Assert.False(Homes.ChestOpens(null, plot));
        Assert.False(Homes.ChestOpens(claimed, plot));
        Assert.Equal("no home is built here yet", StashRules.WhyShut(claimed.Storage(plot)));

        var built = Homes.Build(claimed);
        Assert.True(Homes.ChestOpens(built, plot));
        Assert.Null(StashRules.WhyShut(built.Storage(plot)));
        Assert.Equal(plot.ChestKey, built.Storage(plot).Key);
        Assert.Equal(0, StashRules.Fee(built.Storage(plot), deposit: true));   // a home is not a bank
    }

    [Fact]
    public void FurnitureStandsInsideThePlot_OneToAPatchOfGround()
    {
        var plot = Plot();
        var home = Homes.Build(Homes.Claim(plot));

        Assert.Null(HomeRules.WhyNotPlace(plot, home, At(501_000, 700_000)));
        Assert.Equal($"that is outside the plot at {plot.Name}", HomeRules.WhyNotPlace(plot, home, At(560_000, 700_000)));

        var furnished = Homes.Place(home, At(501_000, 700_000));
        Assert.Single(furnished.Furniture);
        Assert.Equal("something already stands there", HomeRules.WhyNotPlace(plot, furnished, At(501_200, 700_200)));
        Assert.Null(HomeRules.WhyNotPlace(plot, furnished, At(501_000 + HomeRules.ClearanceMm, 700_000)));
    }

    [Fact]
    public void AFreshPlotCannotBeFurnishedUntilItIsBuilt()
    {
        var plot = Plot();
        var claimed = Homes.Claim(plot);
        Assert.Equal("the house is not built yet", HomeRules.WhyNotPlace(plot, claimed, At(500_500, 700_000)));
    }

    [Fact]
    public void AHomeHoldsWhatItCanHold_AndNoMore()
    {
        var plot = Plot() with { RadiusMm = 200_000 };
        var home = Homes.Build(Homes.Claim(plot));
        for (int i = 0; i < HomeRules.MaxFurniture; i++)
            home = Homes.Place(home, At(400_000 + i * HomeRules.ClearanceMm, 700_000));
        Assert.Equal(HomeRules.MaxFurniture, home.Furniture.Length);
        Assert.Equal($"a home holds at most {HomeRules.MaxFurniture} pieces",
            HomeRules.WhyNotPlace(plot, home, At(900_000, 700_000)));
    }

    [Fact]
    public void FacingIsNormalised_AndAPieceCanBeTakenUpAgain()
    {
        var plot = Plot();
        var home = Homes.Place(Homes.Build(Homes.Claim(plot)), At(500_500, 700_000, facing: -90));
        Assert.Equal(270, home.Furniture[0].FacingMdeg);
        Assert.Equal(0, HomeRules.NormaliseFacing(720));
        Assert.Equal(359, HomeRules.NormaliseFacing(-1));

        var emptied = Homes.Remove(home, 0);
        Assert.Empty(emptied.Furniture);
        Assert.Equal("there is no such piece in this home", HomeRules.WhyNotRemove(emptied, 0));
    }

    [Fact]
    public void PlacedPiecesKeepTheOrderTheyWereSetDownIn()
    {
        var plot = Plot();
        var home = Homes.Build(Homes.Claim(plot));
        home = Homes.Place(home, new FurniturePlacement("item.misc.stool", 500_000, 700_000, 0));
        home = Homes.Place(home, new FurniturePlacement("item.misc.bench", 502_000, 700_000, 0));
        Assert.Equal(new[] { "item.misc.stool", "item.misc.bench" }, home.Furniture.Select(f => f.ItemId));
        Assert.Equal("item.misc.bench", Homes.Remove(home, 0).Furniture[0].ItemId);
    }
}
