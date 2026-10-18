using MessagePack;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>Schema 33 (inventory organisation): where each carried stack lies, its lock and junk mark, and the four bag slots.</summary>
public class PackPersistenceTests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static Persistence.LoadResult Load(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        return new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    public void AnOlderSave_MigratesWithEveryStackUnplaced_NoneLockedOrJunk_AndFourEmptyBagSlots(int schema)
    {
        var player = Load(schema).Player;
        Assert.Equal(4, player.Bags.Length);
        Assert.All(player.Bags, b => Assert.Null(b));
        Assert.NotEmpty(player.Inventory);
        Assert.All(player.Inventory, e => Assert.Equal((0, -1, false, false), (e.Bag, e.Slot, e.Locked, e.Junk)));
    }

    [Fact]
    public void TheV33Fixture_LoadsItsPack_BagSlotsAndEveryStacksPlace()
    {
        var player = Load(33).Player;
        var sword = player.Inventory.Single(e => e.DefId == "item.weapon.iron_sword");
        var draughts = player.Inventory.Single(e => e.DefId == "item.potion.minor_healing");
        var satchel = player.Inventory.Single(e => e.DefId == "item.bag.satchel");
        Assert.Equal((0, 0, true, false), (sword.Bag, sword.Slot, sword.Locked, sword.Junk));
        Assert.Equal((0, 3, false, true), (draughts.Bag, draughts.Slot, draughts.Locked, draughts.Junk));
        Assert.Equal((-1, -1), (satchel.Bag, satchel.Slot));
        Assert.Equal(new UNNAMED.Domain.EntityId?[] { null, satchel.ItemId, null, null }, player.Bags);
    }

    [Fact]
    public void ThePack_RoundTripsThroughTheCodec()
    {
        var player = Load(33).Player;
        var again = SectionCodec.DecodePlayer(SectionCodec.EncodePlayer(player));
        Assert.Equal(player.Bags.ToArray(), again.Bags.ToArray());
        Assert.Equal(player.Inventory.Select(e => (e.ItemId, e.Bag, e.Slot, e.Locked, e.Junk)), again.Inventory.Select(e => (e.ItemId, e.Bag, e.Slot, e.Locked, e.Junk)));
        Assert.Equal(player.Digest, again.Digest);
    }

    private static PlayerDto Dto() => MessagePackSerializer.Deserialize<PlayerDto>(SectionCodec.EncodePlayer(Load(33).Player), Options);

    private static byte[] Bytes(PlayerDto dto) => MessagePackSerializer.Serialize(dto, Options);

    [Fact]
    public void APlayerWithoutItsPack_IsCorrupt_NotDefaulted()
    {
        var dto = Dto();
        dto.Pack = null;
        Assert.Contains("no pack", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(dto))).Message);
    }

    [Fact]
    public void ABagSlotNamingAStackThatLiesInABag_OrNoStackCarried_IsCorrupt()
    {
        var dto = Dto();
        dto.Pack!.Bags[0] = dto.Inventory.First(e => e.DefId == "item.weapon.iron_sword").ItemId;   // the sword lies in the backpack
        Assert.Contains("bag slots", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(dto))).Message);

        var gone = Dto();
        gone.Pack!.Bags[0] = "itm_01HF7YAT0N144GJ289144GJ28N";
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(gone)));
    }

    [Fact]
    public void AStackWithNoPlaceRecord_OrWithTwo_OrAWrongNumberOfBagSlots_IsCorrupt()
    {
        var missing = Dto();
        missing.Pack!.Stacks = missing.Pack.Stacks[1..];
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(missing)));

        var twice = Dto();
        twice.Pack!.Stacks = twice.Pack.Stacks.Append(twice.Pack.Stacks[0]).ToArray();
        Assert.Contains("more than once", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(twice))).Message);

        var five = Dto();
        five.Pack!.Bags = new string?[5];
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(five)));
    }
}
