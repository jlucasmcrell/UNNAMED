using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

public class WardrobeTests
{
    private const string Shirt = "outfit.office.new_intern", Boots = "footwear.outdoor.garden_boot";
    private static readonly CharacterAppearance Original = new(1810, "face.m.caucasian_2", "hair.short.short_combed", null, null, null)
    {
        Sliders = ImmutableSortedDictionary<string, int>.Empty.Add("body.stomach", 27).Add("look.skin_tone", 1),
    };

    private static PlayerRecord Carry(PlayerRecord r, params InventoryEntry[] items) =>
        r.WithInventory(r.Inventory.Concat(items).Select((item, index) => r.Equipment.Values.Contains(item.ItemId)
            ? item with { Bag = -1, Slot = -1 } : item with { Bag = 0, Slot = index }));

    private static Arena Open(GameSession session, CharacterAppearance? look, params InventoryEntry[] items) =>
        Arena.OpenCreatures(session, session.Setup, (120, 60), 0, [],
            r => Carry(r, items) with { Identity = r.Identity with { Appearance = look } });

    [Fact]
    public void ClothesAndDyesChangeOnlyAppearance_KeepPowerAndKeepsakes_AndSurviveSaveLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var dye = Arena.Stack("item.material.dye_moss", 2);
        var keepsake = Arena.Stack(session.Characters.Peoples[CharacterSpec.DefaultPeople].Heritage[0].ItemId, 1);
        var arena = Open(session, Original, dye, keepsake);
        var before = arena.Simulation.CaptureRecord();
        var power = arena.Simulation.Combat;
        Assert.Null(arena.Submit(new ChangeWardrobeCommand(arena.Player, Shirt, Boots, true)));
        Assert.Null(arena.Submit(new ApplyClothDyeCommand(arena.Player, dye.ItemId, ClothRegion.Shirt)));
        var after = arena.Simulation.CaptureRecord();
        var look = after.Identity.Appearance!;
        Assert.Equal(Original with { Outfit = Shirt, Footwear = Boots, Sliders = Original.Sliders
            .Add(WardrobeLook.OverGear, 1).Add(WardrobeLook.Shirt, 2) }, look);
        Assert.Equal(before.Identity, after.Identity with { Appearance = before.Identity.Appearance });
        Assert.Equal(before.Equipment, after.Equipment);
        Assert.Equal(before.Currency, after.Currency);
        Assert.Equal(before.Progression.Digest, after.Progression.Digest);
        Assert.Equal(power, arena.Simulation.Combat);
        Assert.Equal(before.Inventory.Where(i => i.ItemId != dye.ItemId), after.Inventory.Where(i => i.ItemId != dye.ItemId));
        Assert.Equal(1, after.Inventory.Single(i => i.ItemId == dye.ItemId).Count);
        Assert.True(WardrobeLook.CoversGear(arena.Simulation.Player.Identity.Appearance));
        new SaveStore(profile.Root).Save(SaveSlots.Manual("clothes"), SaveDocuments.Capture(arena.Simulation.World, after,
            session.Content, arena.Simulation.WorldTick, 0));
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Manual("clothes"), new LoadContext(session.Generator, session.Content, new Registry()));
        var resumed = Arena.Resume(session.Setup, loaded);
        Assert.Equal(after.Digest, resumed.Simulation.CaptureRecord().Digest);
        Assert.Equal(look, resumed.Simulation.Player.Identity.Appearance);
        Assert.Null(resumed.Submit(new ChangeWardrobeCommand(resumed.Player, Shirt, Boots, false)));
        Assert.False(WardrobeLook.CoversGear(resumed.Simulation.Player.Identity.Appearance));
        Assert.Equal(2, resumed.Simulation.Player.Identity.Appearance!.Sliders[WardrobeLook.Shirt]);
    }

    [Fact]
    public void InvalidClothesOrDyeDoNotSpendAnything_AndAnUnchangedColourDoesNotWasteDye()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var dye = Arena.Stack("item.material.dye_ochre", 2);
        var arena = Open(session, Original, dye);
        var before = arena.Simulation.StateDigest();
        Assert.NotNull(arena.Submit(new ChangeWardrobeCommand(arena.Player, "outfit.missing", Boots, true)));
        Assert.NotNull(arena.Submit(new ChangeWardrobeCommand(arena.Player, "outfit.casual.relaxed_weekday", Boots, true))); // female only
        Assert.NotNull(arena.Submit(new ApplyClothDyeCommand(arena.Player, dye.ItemId, (ClothRegion)99)));
        Assert.NotNull(arena.Submit(new ApplyClothDyeCommand(arena.Player, EntityId.NewId(EntityKind.Item), ClothRegion.Shirt)));
        Assert.NotNull(arena.Submit(new ChangeWardrobeCommand(EntityId.NewId(EntityKind.Character), Shirt, Boots, true)));
        Assert.Equal(before, arena.Simulation.StateDigest());
        Assert.Null(arena.Submit(new ChangeWardrobeCommand(arena.Player, "outfit.extra_outfits_pack.pirate", Boots, true)));
        Assert.NotNull(arena.Submit(new ApplyClothDyeCommand(arena.Player, dye.ItemId, ClothRegion.Shirt)));
        Assert.Equal(2, arena.Simulation.Player.Inventory.Single(i => i.ItemId == dye.ItemId).Count);
        Assert.Null(arena.Submit(new ChangeWardrobeCommand(arena.Player, Shirt, Boots, true)));
        Assert.Null(arena.Submit(new ApplyClothDyeCommand(arena.Player, dye.ItemId, ClothRegion.Trousers)));
        before = arena.Simulation.StateDigest();
        Assert.NotNull(arena.Submit(new ApplyClothDyeCommand(arena.Player, dye.ItemId, ClothRegion.Trousers)));
        Assert.Equal(before, arena.Simulation.StateDigest());
    }

    [Fact]
    public void APreCreatorCharacterKeepsItsPhysicalDefaults_WhenChangingClothes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, null);
        var identity = arena.Simulation.Player.Identity;
        Assert.Null(arena.Submit(new ChangeWardrobeCommand(arena.Player, Shirt, Boots, true)));
        var after = arena.Simulation.Player.Identity;
        Assert.Equal(identity, after with { Appearance = null });
        Assert.Equal(session.Characters.Wardrobe.DefaultStatures[identity.PeopleId], after.Appearance!.StatureMm);
        Assert.Null(after.Appearance.Face);
        Assert.Single(after.Appearance.Sliders);
    }

    [Theory]
    [InlineData("body.default")]
    [InlineData("body.veth_m.average")]
    public void NativeFaceSelectedBodiesUseTheirActualGarmentFit_WithoutChangingPhysicalIdentity(string preset)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, [], r => r with
        {
            Identity = r.Identity with { BodyPresetId = preset, Appearance = Original with { Face = "face.f.caucasian_2" } },
        });
        var before = arena.Simulation.Player.Identity;
        Assert.Null(arena.Submit(new ChangeWardrobeCommand(arena.Player, "outfit.casual.relaxed_weekday", Boots, true)));
        var after = arena.Simulation.Player.Identity;
        Assert.Equal(before, after with { Appearance = before.Appearance });
        Assert.Equal(before.Appearance!.StatureMm, after.Appearance!.StatureMm);
    }

    [Theory]
    [InlineData("oxblood", "cinder_sac")]
    [InlineData("moss", "herb_woundmoss")]
    [InlineData("ochre", "herb_ashbloom")]
    [InlineData("slate", "boneleaf")]
    public void ARealManualTeachesEachDye_AndCraftingSpendsGatheredMaterialsAtACookfire(string colour, string material)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var site = session.Setup.Layout.Stations.First(s => s.Kind == "cookfire");
        string recipe = "recipe.herbal.dye_" + colour;
        var book = Arena.Stack("item.tome.herbal_remedies", 1);
        var arena = Arena.OpenCreatures(session, session.Setup, (site.XMm / 1000.0, site.ZMm / 1000.0), 0, [],
            r => Carry(r, book, Arena.Stack("item.material." + material, 2), Arena.Stack("item.material.bog_cotton", 1),
                Arena.Stack("item.material.boiled_water", 1)), origin: (0, 0));
        Assert.NotNull(arena.Submit(new CraftCommand(arena.Player, recipe)));
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, book.ItemId)));
        Assert.Contains(recipe, arena.Simulation.Player.Progression.Known.Keys);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, recipe)));
        Assert.Contains(arena.Simulation.Player.Inventory, i => i.DefId == "item.material.dye_" + colour && i.Count >= 1);
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, i => i.DefId == "item.material." + material || i.DefId == "item.material.bog_cotton" || i.DefId == "item.material.boiled_water");
    }
}
