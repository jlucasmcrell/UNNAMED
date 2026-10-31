using UNNAMED.Domain;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The owner's ruling in the world (2026-10-31): one stash per character, reachable from a storage point in any town; a rented room's chest that
/// locks when the rent lapses; and a bank's fee that reaches the GEN-04 ledger as a bank fee rather than vanishing.
/// </summary>
public class StashTests
{
    private const string Hide = "item.material.wolf_hide";
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    private static Arena At(GameSession session, long currency, params InventoryEntry[] carried) =>
        Arena.OpenCreatures(session, session.Setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), r => With(r, currency, carried));

    private static PlayerRecord With(PlayerRecord r, long currency, params InventoryEntry[] carried) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(carried), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, currency, r.Effects, r.Relationships, r.Conversations);

    private static InventoryEntry Stack(string defId, int count) =>
        new(EntityId.NewId(EntityKind.Item), defId, count);

    [Fact]
    public void GoodsPutAwayInOneTown_AreTakenOutInAnother()
    {
        using var profile = new TempProfile();
        var hide = Stack(Hide, 3);
        var arena = At(Harness.Boot(profile), 12, hide);
        var sim = arena.Simulation;

        // Town A's bank: the goods go into the one stash, and the bank takes its fee.
        Assert.Null(arena.Submit(new DepositToStashCommand(arena.Player, "bank.ashen_hollow", StorageKind.Bank, false, false, false, hide.ItemId, 3, 0)));
        Assert.Equal(3, sim.Stash().Count(Hide));

        // Town B's bank: the same stash, so the same goods come out, and the fee is paid again.
        Assert.Null(arena.Submit(new WithdrawFromStashCommand(arena.Player, "bank.cairnwold", StorageKind.Bank, false, false, false, Hide, 3, 0)));
        Assert.Equal(0, sim.Stash().Count(Hide));
        Assert.Contains(sim.Player.Inventory, e => e.DefId == Hide && e.Count == 3);

        // Both fees are in the ledger, as bank, and the ledger still agrees with the purse.
        var fees = sim.Coins().Entries.Where(e => e.Reason == CoinReasons.Bank).ToArray();
        Assert.Equal(2, fees.Length);
        Assert.All(fees, f => Assert.Equal(CoinFlow.Out, f.Flow));
        Assert.Equal(12 - StashRules.BankDepositFee - StashRules.BankWithdrawFee, sim.Player.Currency);
        Assert.True(sim.CoinsAgree);
    }

    [Fact]
    public void ARentedRoomChestHolds_AndLocksTheMomentTheRentLapses()
    {
        using var profile = new TempProfile();
        var hide = Stack(Hide, 1);
        var arena = At(Harness.Boot(profile), 5, hide);
        var sim = arena.Simulation;

        // Rented: the chest opens and the goods go into the same one stash.
        Assert.Null(arena.Submit(new DepositToStashCommand(arena.Player, "inn.ashen_hollow.room_2", StorageKind.InnRoomChest, RoomRented: true, false, false, hide.ItemId, 1, 0)));
        Assert.Equal(1, sim.Stash().Count(Hide));

        // The rent lapses: the chest is shut, and it says so.
        Assert.Equal("the room is not rented: the chest is locked",
            arena.Submit(new WithdrawFromStashCommand(arena.Player, "inn.ashen_hollow.room_2", StorageKind.InnRoomChest, RoomRented: false, false, false, Hide, 1, 0)));

        // Nothing moved, and no fee was taken for a door that did not open.
        Assert.Equal(1, sim.Stash().Count(Hide));
        Assert.Equal(5, sim.Player.Currency);
        Assert.Empty(sim.Coins().Entries);
    }

    [Fact]
    public void CoinPutAway_ComesBackOut_AndTheBankFeeIsWhatTheLedgerShows()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), 100);
        var sim = arena.Simulation;

        Assert.Null(arena.Submit(new DepositToStashCommand(arena.Player, "bank.ashen_hollow", StorageKind.Bank, false, false, false, null, 0, 50)));
        Assert.Equal(50, sim.Stash().Coin);

        Assert.Null(arena.Submit(new WithdrawFromStashCommand(arena.Player, "bank.cairnwold", StorageKind.Bank, false, false, false, null, 0, 50)));
        Assert.Equal(0, sim.Stash().Coin);
        Assert.Equal(100 - StashRules.BankDepositFee - StashRules.BankWithdrawFee, sim.Player.Currency);
        // Four bank movements: the fee and the coin for the deposit, the fee and the coin for the withdrawal.
        var bankLines = sim.Coins().Entries.Where(e => e.Reason == CoinReasons.Bank).ToArray();
        Assert.Equal(4, bankLines.Length);
        Assert.Contains(bankLines, e => e.Flow == CoinFlow.Out && e.Amount == 50);
        Assert.Contains(bankLines, e => e.Flow == CoinFlow.In && e.Amount == 50);
        Assert.True(sim.CoinsAgree);
    }
}
