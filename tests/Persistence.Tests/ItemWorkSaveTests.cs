using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// Schema 44 (queue #5): a reforged and inscribed piece comes back from the save exactly as it went in, a broken record is refused rather than
/// guessed at, and every save from before schema 44 loads with every piece at tier 0 and nothing cut into it.
/// </summary>
public class ItemWorkSaveTests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static PlayerRecord With(PlayerRecord player, ItemWork? work)
    {
        var sword = player.Inventory.Single(e => e.DefId == "item.weapon.iron_sword");
        return player.WithInventory(player.Inventory.Replace(sword, sword with { Work = work }));
    }

    [Fact]
    public void AReforgedAndInscribedPiece_RoundTrips_ByteForByte()
    {
        var player = With(M2Fixtures.Historical.Player(), new ItemWork(2, new Inscription("working.ember_anchor", 4)));

        byte[] bytes = SectionCodec.EncodePlayer(player);
        var decoded = SectionCodec.DecodePlayer(bytes);

        Assert.Equal(new ItemWork(2, new Inscription("working.ember_anchor", 4)), decoded.Inventory.Single(e => e.DefId == "item.weapon.iron_sword").Work);
        Assert.Equal(player.Digest, decoded.Digest);
        Assert.Equal(bytes, SectionCodec.EncodePlayer(decoded));
        Assert.NotEqual(player.Digest, With(player, new ItemWork(2, new Inscription("working.ember_anchor", 3))).Digest);
        Assert.NotEqual(player.Digest, With(player, null).Digest);
    }

    [Fact]
    public void ABrokenWorkRecord_IsRefused()
    {
        var player = M2Fixtures.Historical.Player();
        // Neither shape can be built through the record, so the bytes are made by hand: a fresh sword, then its work rewritten.
        byte[] good = SectionCodec.EncodePlayer(With(player, new ItemWork(1, new Inscription("working.keen_edge", 2))));
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(good, Options);
        var sword = dto.Inventory.Single(e => e.DefId == "item.weapon.iron_sword");

        sword.Work = new WorkDto { Tier = 1, Working = "working.keen_edge", Stability = 0 };
        Assert.ThrowsAny<Exception>(() => SectionCodec.DecodePlayer(MessagePackSerializer.Serialize(dto, Options)));
        sword.Work = new WorkDto { Tier = -1 };
        Assert.ThrowsAny<Exception>(() => SectionCodec.DecodePlayer(MessagePackSerializer.Serialize(dto, Options)));
        sword.Work = new WorkDto { Tier = 0, Stability = 3 };
        Assert.ThrowsAny<Exception>(() => SectionCodec.DecodePlayer(MessagePackSerializer.Serialize(dto, Options)));
    }

    public static TheoryData<int> OlderSchemas()
    {
        var data = new TheoryData<int>();
        for (int schema = SaveFormat.OldestSupportedSchema; schema < 44; schema++)
            data.Add(schema);
        return data;
    }

    [Theory]
    [MemberData(nameof(OlderSchemas))]
    public void EverySaveBeforeSchema44_LoadsAtTierZero_WithNothingCutIntoAnything(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
        var snapshot = loaded.World.TakeSnapshot();

        Assert.True(loaded.IsComplete);
        Assert.NotEmpty(loaded.Player.Inventory);
        Assert.All(loaded.Player.Inventory, e => Assert.Null(e.Work));
        Assert.All(loaded.Player.PartySheets.SelectMany(p => p.Items), e => Assert.Null(e.Work));
        Assert.All(snapshot.Created, c => Assert.Null(c.Work));
        Assert.All(snapshot.Containers.SelectMany(c => c.Items), i => Assert.Null(i.Work));
    }
}
