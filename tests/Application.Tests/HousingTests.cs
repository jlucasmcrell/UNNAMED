// UNNAMED Application tests - a home in the world: claiming the plot, building it, furnishing it, and its chest reaching the shared stash
// (HSG-; GAPS_WORK_QUEUE #6; owner ruling 2026-10-31 on storage points).

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Housing;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The plot a player can own, the goods building it takes, the furniture they set down, and the chest that is one more door on the ONE stash the
/// economy lane built. The chest is the point of the slice: nothing here keeps a store of its own, so what goes in at home comes out at a bank.
/// </summary>
public class HousingTests
{
    private const string Plot = "home.ashen_hollow.waystation_plot";
    private const string Timber = "item.material.timber";
    private const string Ore = "item.material.iron_ore";

    /// <summary>The plot stands at (476, 736) m in the frontier region's own metres.</summary>
    private static readonly (double X, double Z) AtPlot = (76.0, 136.0);   // the plot at (476, 736) m of the region, from the hollow at (400, 600)

    private static InventoryEntry Stack(string defId, int count) =>
        new(EntityId.NewId(EntityKind.Item), defId, count);

    private static PlayerRecord With(PlayerRecord r, long currency, params InventoryEntry[] carried) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(carried), r.Progression, r.FacingMdeg,
            r.Discoveries, r.Equipment, currency, r.Effects, r.Relationships, r.Conversations);

    private static Arena At(GameSession session, long currency, params InventoryEntry[] carried) =>
        Arena.OpenCreatures(session, session.Setup, AtPlot, 0, Array.Empty<(string, double, double, string)>(),
            r => With(r, currency, carried));

    [Fact]
    public void APlotIsClaimed_BuiltFromGoods_AndFurnished()
    {
        using var profile = new TempProfile();
        var timber = Stack(Timber, 6);
        var ore = Stack(Ore, 4);
        var stool = Stack("item.tool.lantern", 1);
        var arena = At(Harness.Boot(profile), 300, timber, ore, stool);
        var sim = arena.Simulation;

        var definition = sim.Homes[Plot];
        Assert.Equal(250, definition.PriceCoin);
        Assert.Empty(sim.Owned);

        // the claim costs its price
        Assert.Null(arena.Submit(new ClaimHomeCommand(arena.Player, Plot)));
        var claimed = Assert.Single(sim.Owned);
        Assert.False(claimed.Built);
        Assert.Equal(50, sim.Player.Currency);

        // building takes the goods its definition names, all of them
        Assert.Null(arena.Submit(new BuildHomeCommand(arena.Player, Plot)));
        var built = Assert.Single(sim.Owned);
        Assert.True(built.Built);
        Assert.DoesNotContain(sim.Player.Inventory, e => e.DefId == Timber);

        // a piece goes down inside the plot, and comes back up again
        long x = definition.XMm, z = definition.ZMm;
        Assert.Null(arena.Submit(new PlaceFurnitureCommand(arena.Player, Plot, stool.ItemId, x, z)));
        Assert.Single(Assert.Single(sim.Owned).Furniture);
        Assert.DoesNotContain(sim.Player.Inventory, e => e.ItemId == stool.ItemId);

        Assert.Null(arena.Submit(new RemoveFurnitureCommand(arena.Player, Plot, 0)));
        Assert.Empty(Assert.Single(sim.Owned).Furniture);
        Assert.Contains(sim.Player.Inventory, e => e.DefId == "item.tool.lantern");
    }

    [Fact]
    public void BuildingIsRefusedWithoutTheGoods_AndClaimingTwiceIsRefused()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), 300, Stack(Timber, 2));   // not enough of either
        Assert.Null(arena.Submit(new ClaimHomeCommand(arena.Player, Plot)));

        string? refused = arena.Submit(new BuildHomeCommand(arena.Player, Plot));
        Assert.NotNull(refused);
        Assert.Contains("takes 6", refused);
        Assert.False(Assert.Single(arena.Simulation.Owned).Built);

        Assert.Contains("already yours", arena.Submit(new ClaimHomeCommand(arena.Player, Plot)));
    }

    [Fact]
    public void AHomeChest_IsOneMoreDoorOnTheSharedStash()
    {
        using var profile = new TempProfile();
        var hide = Stack("item.material.wolf_hide", 2);
        var arena = At(Harness.Boot(profile), 300, Stack(Timber, 6), Stack(Ore, 4), hide);
        var sim = arena.Simulation;

        Assert.Null(arena.Submit(new ClaimHomeCommand(arena.Player, Plot)));

        // before the house is up, the chest will not open
        var point = sim.HomeStorage(Plot)!;
        Assert.Equal(StorageKind.Home, point.Kind);
        Assert.Equal("no home is built here yet", StashRules.WhyShut(point));

        Assert.Null(arena.Submit(new BuildHomeCommand(arena.Player, Plot)));
        var open = sim.HomeStorage(Plot)!;
        Assert.Null(StashRules.WhyShut(open));
        Assert.Equal(0, StashRules.Fee(open, deposit: true));   // a home is not a bank

        // goods put away at home are in the ONE stash, and come out at a town's bank
        Assert.Null(arena.Submit(new DepositToStashCommand(arena.Player, open.Key, StorageKind.Home, false, true, false,
            hide.ItemId, 2, 0)));
        Assert.Equal(2, sim.Stash().Count("item.material.wolf_hide"));
        Assert.Null(arena.Submit(new WithdrawFromStashCommand(arena.Player, "bank.ashen_hollow", StorageKind.Bank, false, false,
            false, "item.material.wolf_hide", 2, 0)));
        Assert.Equal(0, sim.Stash().Count("item.material.wolf_hide"));
        Assert.Contains(sim.Player.Inventory, e => e.DefId == "item.material.wolf_hide" && e.Count == 2);
    }

    [Fact]
    public void AHomeSurvivesASaveAndLoad()
    {
        using var profile = new TempProfile();
        var stool = Stack("item.tool.lantern", 1);
        var session = Harness.Boot(profile);
        var arena = At(session, 300, Stack(Timber, 6), Stack(Ore, 4), stool);
        var sim = arena.Simulation;

        Assert.Null(arena.Submit(new ClaimHomeCommand(arena.Player, Plot)));
        Assert.Null(arena.Submit(new BuildHomeCommand(arena.Player, Plot)));
        var definition = sim.Homes[Plot];
        Assert.Null(arena.Submit(new PlaceFurnitureCommand(arena.Player, Plot, stool.ItemId, definition.XMm, definition.ZMm)));

        var store = new UNNAMED.Persistence.SaveStore(profile.Root);
        store.Save(UNNAMED.Persistence.SaveSlots.Manual("home"), UNNAMED.Persistence.SaveDocuments.Capture(
            sim.World, sim.CaptureRecord(), session.Content, sim.WorldTick, 0));
        var loaded = Arena.Resume(sim.Setup, store.Load(UNNAMED.Persistence.SaveSlots.Manual("home"),
            new UNNAMED.Persistence.LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry())));

        var home = Assert.Single(loaded.Simulation.Owned);
        Assert.Equal(Plot, home.HomeId);
        Assert.True(home.Built);
        Assert.Single(home.Furniture);
        Assert.Equal("item.tool.lantern", home.Furniture[0].ItemId);
    }
}
