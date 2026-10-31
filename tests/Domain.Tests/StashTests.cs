// UNNAMED Domain tests - the shared stash and its doors (owner ruling 2026-10-31)
using UNNAMED.Domain.Economy;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The ruling made testable: one stash behind four kinds of door, a rented-room chest that locks when the rent lapses, and a bank that charges a
/// fee for being a bank. Deterministic: the door either opens or it says why not.
/// </summary>
public class StashTests
{
    [Fact]
    public void ABankAndACampOpen_AndSayNothingAboutIt()
    {
        Assert.True(StashRules.Opens(new StoragePoint(StorageKind.Bank, "bank.ashen_hollow")));
        Assert.Null(StashRules.WhyShut(new StoragePoint(StorageKind.Bank, "bank.ashen_hollow")));
        Assert.True(StashRules.Opens(new StoragePoint(StorageKind.Camp, "camp.foldscar", CampStanding: true)));
        Assert.False(StashRules.Opens(new StoragePoint(StorageKind.Camp, "camp.foldscar", CampStanding: false)));
    }

    [Fact]
    public void AnInnRoomChest_IsOpenWhileTheRoomIsRented_AndLocksWhenTheRentLapses()
    {
        var rented = new StoragePoint(StorageKind.InnRoomChest, "inn.ashen_hollow.room_2", RoomRented: true);
        var lapsed = new StoragePoint(StorageKind.InnRoomChest, "inn.ashen_hollow.room_2", RoomRented: false);

        Assert.True(StashRules.Opens(rented));
        Assert.False(StashRules.Opens(lapsed));
        Assert.Equal("the room is not rented: the chest is locked", StashRules.WhyShut(lapsed));
    }

    [Fact]
    public void ABuiltHomeOpens_AHypotheticalOneDoesNot()
    {
        Assert.True(StashRules.Opens(new StoragePoint(StorageKind.Home, "home.ashen_hollow", HomeBuilt: true)));
        Assert.Equal("no home is built here yet", StashRules.WhyShut(new StoragePoint(StorageKind.Home, "home.ashen_hollow")));
    }

    [Fact]
    public void OnlyABankCharges_AndItChargesBothWays()
    {
        var bank = new StoragePoint(StorageKind.Bank, "bank.ashen_hollow");
        var room = new StoragePoint(StorageKind.InnRoomChest, "inn.room", RoomRented: true);
        var camp = new StoragePoint(StorageKind.Camp, "camp", CampStanding: true);

        Assert.Equal(StashRules.BankDepositFee, StashRules.Fee(bank, deposit: true));
        Assert.Equal(StashRules.BankWithdrawFee, StashRules.Fee(bank, deposit: false));
        Assert.Equal(0, StashRules.Fee(room, deposit: true));
        Assert.Equal(0, StashRules.Fee(camp, deposit: false));
    }

    [Fact]
    public void OneStashHoldsWhatEveryDoorPutInIt()
    {
        // The ruling's own point: the stash is per character, so town A's deposit is town B's withdrawal.
        var stash = new Stash();
        stash.Put("item.material.wolf_hide", 4);
        stash.PutCoin(120);

        Assert.Equal(4, stash.Count("item.material.wolf_hide"));
        Assert.Equal(120, stash.Coin);
        Assert.True(stash.Take("item.material.wolf_hide", 4));
        Assert.True(stash.TakeCoin(120));
        Assert.Empty(stash.Goods);
        Assert.Equal(0, stash.Coin);
    }

    [Fact]
    public void TheStashRefusesToGiveWhatItDoesNotHold()
    {
        var stash = new Stash();
        stash.Put("item.material.wolf_hide", 2);
        stash.PutCoin(5);
        stash.Put("item.material.wolf_hide", 0);   // nothing is nothing
        stash.Put("", 3);                          // and a nameless good is not a good

        Assert.False(stash.Take("item.material.wolf_hide", 3));
        Assert.Equal(2, stash.Count("item.material.wolf_hide"));
        Assert.False(stash.TakeCoin(6));
        Assert.Equal(5, stash.Coin);
        Assert.Single(stash.Goods);
    }
}
