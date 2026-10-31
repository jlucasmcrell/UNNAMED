using UNNAMED.Domain.Economy;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// GEN-04 in the world: every movement of the purse writes itself into the ledger where it happens, with the reason and the balance it left, so
/// the record cannot drift from the fact.
/// </summary>
public class CoinLedgerTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Hide = "item.material.wolf_hide";
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    private static Arena At(GameSession session, long currency, params InventoryEntry[] carried) =>
        Arena.OpenCreatures(session, session.Setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), r => With(r, currency, carried));

    private static PlayerRecord With(PlayerRecord r, long currency, params InventoryEntry[] carried) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(carried), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, currency, r.Effects, r.Relationships, r.Conversations);

    [Fact]
    public void SellingAWare_IsWrittenDownAsCoinIn_WithItsReasonAndTheBalanceItLeft()
    {
        using var profile = new TempProfile();
        var hide = new InventoryEntry(UNNAMED.Domain.EntityId.NewId(UNNAMED.Domain.EntityKind.Item), Hide, 1);
        var arena = At(Harness.Boot(profile), 40, hide);
        var sim = arena.Simulation;

        Assert.Empty(sim.Coins().Entries);

        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, hide.ItemId, 1)));

        var entry = Assert.Single(sim.Coins().Entries);
        Assert.Equal(CoinFlow.In, entry.Flow);
        Assert.Equal(CoinReasons.TradeSell, entry.Reason);
        Assert.True(entry.Amount > 0);
        Assert.Equal(sim.Player.Currency, entry.Balance);
        Assert.True(sim.CoinsAgree);
    }

    [Fact]
    public void BuyingAWare_IsWrittenDownAsCoinOut()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), 400);
        var sim = arena.Simulation;

        var ware = sim.Wares(Kera)!.Wares.First(w => w.Price > 0);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, ware.Ref, 1)));

        var entry = Assert.Single(sim.Coins().Entries);
        Assert.Equal(CoinFlow.Out, entry.Flow);
        Assert.Equal(CoinReasons.TradeBuy, entry.Reason);
        Assert.Equal(ware.Price, entry.Amount);
        Assert.Equal(sim.Player.Currency, entry.Balance);
        Assert.True(sim.CoinsAgree);
    }

    [Fact]
    public void APriceThePurseRefuses_WritesNothing_SoTheLedgerNeverInventsAMovement()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), 0);
        var sim = arena.Simulation;

        var ware = sim.Wares(Kera)!.Wares.First(w => w.Price > 0);
        Assert.Contains("not enough coin", arena.Submit(new BuyCommand(arena.Player, Kera, ware.Ref, 1)));
        Assert.Empty(sim.Coins().Entries);
        Assert.True(sim.CoinsAgree);
    }

}
