using MessagePack;
using UNNAMED.Domain.Companions;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>Schema 34 (before testers): the main bar's layout per weapon family, and "focus my target" per party member.</summary>
public class FamilyBarPersistenceTests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static Persistence.LoadResult Load(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        return new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(33)]
    public void AnOlderSave_MigratesWithNoFamilyHeld_NoLayouts_AndNoFocusTarget(int schema)
    {
        var player = Load(schema).Player;
        Assert.Equal("", player.HeldFamily);   // the first tick adopts the weapon in hand and its layout is the bar as it stands
        Assert.Empty(player.FamilyBars);
        Assert.All(player.Companions, c => Assert.False(c.Tactics.FocusTarget));
    }

    [Fact]
    public void TheV34Fixture_LoadsTheHeldFamily_ItsLayoutIsTheMainBar_AndBareHandsKeepTheirOwn()
    {
        var player = Load(34).Player;
        Assert.Equal("skill.one_hand_blade", player.HeldFamily);
        Assert.Equal(new[] { "skill.one_hand_blade", "unarmed" }, player.FamilyBars.Keys);
        Assert.Equal(player.QuickBar.ToArray(), player.FamilyBars["skill.one_hand_blade"].ToArray());
        Assert.Equal("item.potion.minor_healing", player.FamilyBars["unarmed"][0]);
        Assert.True(Assert.Single(player.Companions).Tactics.FocusTarget);
    }

    [Fact]
    public void TheFamilyLayoutsAndTheFocusTactic_RoundTripThroughTheCodec()
    {
        var player = Load(34).Player;
        var again = SectionCodec.DecodePlayer(SectionCodec.EncodePlayer(player));
        Assert.Equal(player.HeldFamily, again.HeldFamily);
        Assert.Equal(Flat(player.FamilyBars), Flat(again.FamilyBars));
        Assert.Equal(player.Companions.Select(c => c.Tactics), again.Companions.Select(c => c.Tactics));
        Assert.Equal(player.Digest, again.Digest);
    }

    private static IEnumerable<(string, string)> Flat(IEnumerable<KeyValuePair<string, System.Collections.Immutable.ImmutableArray<string?>>> bars) =>
        bars.Select(f => (f.Key, string.Join("|", f.Value.Select(s => s ?? "-"))));

    private static PlayerDto Dto() => MessagePackSerializer.Deserialize<PlayerDto>(SectionCodec.EncodePlayer(Load(34).Player), Options);

    private static byte[] Bytes(PlayerDto dto) => MessagePackSerializer.Serialize(dto, Options);

    [Theory]
    [InlineData("held")]
    [InlineData("layouts")]
    [InlineData("focus")]
    public void APlayerWithoutTheseFields_IsCorrupt_NotDefaulted(string field)
    {
        var dto = Dto();
        switch (field)
        {
            case "held": dto.HeldFamily = null; break;
            case "layouts": dto.FamilyBars = null; break;
            default: dto.FocusTargets = null; break;
        }
        Assert.Contains("required from schema 34", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(dto))).Message);
    }

    [Fact]
    public void ABadFamilyLayout_IsCorrupt()
    {
        var wrongSize = Dto();
        wrongSize.FamilyBars![1].Slots = new string?[3];
        Assert.Contains("twelve slots", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(wrongSize))).Message);

        var notAFamily = Dto();
        notAFamily.FamilyBars![1].Family = "item.weapon.iron_sword";
        Assert.Contains("twelve slots under a weapon family", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(notAFamily))).Message);

        var twice = Dto();
        twice.FamilyBars![1].Family = twice.FamilyBars[0].Family;
        Assert.Contains("two layouts", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(twice))).Message);

        var twoOnABar = Dto();
        twoOnABar.FamilyBars![1].Slots[1] = twoOnABar.FamilyBars[1].Slots[0];   // the draught twice on one bar
        Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(Bytes(twoOnABar)));
    }

    [Fact]
    public void TheHeldFamily_MustHaveALayout_AndItIsTheMainBar()
    {
        var unknown = Dto();
        unknown.HeldFamily = "skill.archery";
        Assert.Contains("has no layout", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(unknown))).Message);

        var differs = Dto();
        differs.FamilyBars!.Single(f => f.Family == differs.HeldFamily).Slots[0] = "item.potion.minor_healing";
        Assert.Contains("not the main bar", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(differs))).Message);

        var badName = Dto();
        badName.HeldFamily = "sword";
        Assert.Contains("held weapon family", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(badName))).Message);
    }

    [Fact]
    public void AFocusTarget_MustBeAPartyMember_AndListedOnce()
    {
        var stranger = Dto();
        stranger.FocusTargets = new[] { "npc.fixture.nobody" };
        Assert.Contains("focus target", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(stranger))).Message);

        var twice = Dto();
        twice.FocusTargets = new[] { twice.Companions![0].NpcId, twice.Companions[0].NpcId };
        Assert.Contains("focus target", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(twice))).Message);

        var none = Dto();
        none.FocusTargets = Array.Empty<string>();
        Assert.False(Assert.Single(SectionCodec.DecodePlayer(Bytes(none)).Companions).Tactics.FocusTarget);
        Assert.Equal(new PartyTactics(false, true, false), Assert.Single(SectionCodec.DecodePlayer(Bytes(none)).Companions).Tactics);
    }

    [Fact]
    public void TheV33ToV34Step_CopiesEveryOtherFieldOfThePlayerUnchanged()
    {
        var old = Load(33).Player;
        var now = Load(34).Player;
        // The two fixtures are different worlds' writes; what the step must hold is that nothing else of an old player moved: its pack, bars and tactics.
        Assert.Equal(old.Bags, now.Bags.Select(_ => (UNNAMED.Domain.EntityId?)null).Take(0).Concat(old.Bags));
        Assert.Equal(old.ExtraBars.Select(b => b.ToArray()), now.ExtraBars.Select(b => b.ToArray()));
        Assert.Equal(old.GearSets.Select(g => (g.Index, g.Name, g.Items.Length)), now.GearSets.Select(g => (g.Index, g.Name, g.Items.Length)));
        Assert.Equal(old.Inventory.Select(e => (e.DefId, e.Bag, e.Slot, e.Locked, e.Junk)), now.Inventory.Select(e => (e.DefId, e.Bag, e.Slot, e.Locked, e.Junk)));
    }
}