using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP3a over the game's own content: experimenting at a station, recipe books, masterwork at an upgraded station with the maker's mark,
/// armour moved by quality, the off-hand torch and shield, and a notable instance's record - travelling with it and growing familiar.
/// </summary>
public class ItemizationTests
{
    private const string Gloves = "item.armor.smith_gloves";
    private const string GlovesRecipe = "recipe.leatherwork.smith_gloves";
    private const string Cloak = "recipe.leatherwork.cloak";
    private const string Shield = "recipe.leatherwork.timber_shield";
    private const string Torch = "recipe.smithing.torch";

    // Where the player stands to reach each station (content/regions/starting_frontier.yaml), in the hollow's metres.
    private static readonly (double X, double Z) AtBench = (37.5, 143.5);
    private static readonly (double X, double Z) AtHearth = (59.5, 143);
    private const string Bench = "station.hollow.workbench";
    private const string Hearth = "station.forge_hearth";

    private static InventoryEntry Stack(string defId, int count, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality };

    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, r.Currency, r.Effects);

    private static PlayerRecord Knowing(PlayerRecord r, params string[] recipes) =>
        r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItems(recipes.Select(id => KeyValuePair.Create(id, new KnownTechnique(LearningSource.Teacher, "npc.test", 0)))),
        });

    private static PlayerRecord Skilled(PlayerRecord r, string skill, int level) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord> change, SimulationSetup? setup = null) =>
        Arena.OpenCreatures(session, setup ?? session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static bool Knows(Arena arena, string id) => ProgressionEngine.Knows(arena.Simulation.Player.Progression, id);

    [Fact]
    public void Experimenting_AtTheBench_FindsItsSetOneByOne_FromWhatIsCarried()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var empty = At(session, AtBench, r => r);
        Assert.Equal((0, 2), empty.Simulation.Experiments(Bench));
        // Trying with nothing to try closes the question once, with a Finding; the same attempt again is refused with the same words.
        Assert.Null(empty.Submit(new ExperimentCommand(empty.Player, Bench)));
        Assert.Equal("nothing comes of it: what you carry suggests nothing new here", empty.Submit(new ExperimentCommand(empty.Player, Bench)));

        var arena = At(session, AtBench, r => Carrying(r, Stack("item.material.wolf_hide", 1), Stack("item.material.bog_cotton", 2),
            Stack("item.material.timber", 2), Stack("item.material.small_hide", 1)));
        var found = arena.Record<RecipeDiscovered>();
        var before = arena.Simulation.Player.Inventory;

        Assert.Null(arena.Submit(new ExperimentCommand(arena.Player, Bench)));
        Assert.Null(arena.Submit(new ExperimentCommand(arena.Player, Bench)));
        Assert.Equal("everything this workbench has to teach is known", arena.Submit(new ExperimentCommand(arena.Player, Bench)));

        Assert.Equal(new[] { Cloak, Shield }, found.Select(f => f.RecipeId));
        Assert.True(Knows(arena, Cloak) && Knows(arena, Shield));
        Assert.Equal(LearningSource.Experiment, arena.Simulation.Player.Progression.Known[Cloak].Source);
        Assert.Equal(before, arena.Simulation.Player.Inventory);   // experimenting spends nothing
        Assert.Equal((2, 2), arena.Simulation.Experiments(Bench));
        Assert.Equal($"{Hearth} is out of reach", arena.Submit(new ExperimentCommand(arena.Player, Hearth)));
    }

    [Fact]
    public void AnExperiment_RecordsAFinding_OnceForEachQuestionItCloses_AndTheyAreKept()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // Nothing carried: one Finding that nothing came of it; trying again is refused and adds none.
        var none = At(session, AtBench, r => r);
        var recorded = none.Record<FindingRecorded>();
        Assert.Null(none.Submit(new ExperimentCommand(none.Player, Bench)));
        var nothing = Assert.Single(none.Simulation.Findings);
        Assert.Equal((FindingKind.Nothing, Bench, "workbench", null), (nothing.Kind, nothing.StationKey, nothing.StationKind, nothing.RecipeId));
        Assert.Empty(nothing.Materials);
        Assert.NotNull(none.Submit(new ExperimentCommand(none.Player, Bench)));
        Assert.Single(none.Simulation.Findings);
        Assert.Single(recorded);

        // Part of a recipe's makings: a hint at that recipe, naming what bore on it; the same hint is not recorded twice.
        var part = At(session, AtBench, r => Carrying(r, Stack("item.material.wolf_hide", 1)));
        Assert.Null(part.Submit(new ExperimentCommand(part.Player, Bench)));
        var hint = Assert.Single(part.Simulation.Findings);
        Assert.Equal((FindingKind.Hint, Cloak), (hint.Kind, hint.RecipeId));
        Assert.Equal(new[] { "item.material.wolf_hide" }, hint.Materials);
        Assert.False(Knows(part, Cloak));
        Assert.Equal("there is more to this than what you carry, and you have learned all it will say", part.Submit(new ExperimentCommand(part.Player, Bench)));
        Assert.Single(part.Simulation.Findings);

        // All of the makings: the recipe is found, and the Finding says so.
        var all = At(session, AtBench, r => Carrying(r, Stack("item.material.wolf_hide", 1), Stack("item.material.bog_cotton", 2)));
        Assert.Null(all.Submit(new ExperimentCommand(all.Player, Bench)));
        var found = Assert.Single(all.Simulation.Findings);
        Assert.Equal((FindingKind.Found, Cloak), (found.Kind, found.RecipeId));
        Assert.True(Knows(all, Cloak));
        Assert.Equal(new[] { "item.material.bog_cotton", "item.material.wolf_hide" }, found.Materials);
    }

    [Fact]
    public void TheForge_TeachesTheTorch_WhichIsWornInTheOffHand_AndShowsTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtHearth, r => Carrying(r, Stack("item.material.timber", 2), Stack("item.material.bog_cotton", 2)));

        Assert.Null(arena.Submit(new ExperimentCommand(arena.Player, Hearth)));
        Assert.True(Knows(arena, Torch));
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, Torch)));
        var torch = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.tool.torch");
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, torch.ItemId)));
        Assert.Equal(torch.ItemId, arena.Simulation.Player.Equipment[EquipSlot.OffHand]);
    }

    [Fact]
    public void ARecipeBook_IsReadOnce_AndTeachesItsRecipes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var book = Stack("item.tome.bench_patterns", 1);
        var arena = At(session, AtBench, r => Carrying(r, book));

        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, book.ItemId)));

        Assert.True(Knows(arena, GlovesRecipe) && Knows(arena, "recipe.leatherwork.wool_trousers") && Knows(arena, "recipe.leatherwork.hide_boots"));
        Assert.Equal(LearningSource.Book, arena.Simulation.Player.Progression.Known[GlovesRecipe].Source);
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == "item.tome.bench_patterns");
    }

    /// <summary>Gloves made one after another by a skilled hand; their qualities, and the records the masterworks carry.</summary>
    private static List<InventoryEntry> MakeGloves(GameSession session, int grade, int count)
    {
        var rules = session.Setup with
        {
            Layout = session.Setup.Layout with { Stations = session.Setup.Layout.Stations.Select(s => s with { Grade = grade }).ToImmutableArray() },
            Crafting = session.Setup.Crafting with { Constants = session.Setup.Crafting.Constants with { MasterworkPercentOfFine = 100 } },
        };
        var arena = At(session, AtBench, r => Skilled(Knowing(Carrying(r, Stack("item.material.small_hide", count)), GlovesRecipe), "skill.survival", 60), rules);
        for (int i = 0; i < count; i++)
        {
            Assert.Null(arena.Submit(new CraftCommand(arena.Player, GlovesRecipe)));
            arena.Tick();
        }
        return arena.Simulation.Player.Inventory.Where(e => e.DefId == Gloves).ToList();
    }

    [Fact]
    public void Masterwork_ComesOnlyFromAnUpgradedStation_AndCarriesTheMakersMark()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var common = MakeGloves(session, grade: 0, count: 10);
        Assert.DoesNotContain(common, g => g.Quality == Quality.Masterwork);
        Assert.Contains(common, g => g.Quality == Quality.Fine);
        Assert.All(common, g => Assert.Null(g.Notable));

        var upgraded = MakeGloves(session, grade: 1, count: 10);
        Assert.DoesNotContain(upgraded, g => g.Quality == Quality.Fine);   // every fine roll became masterwork at 100%
        var masterworks = upgraded.Where(g => g.Quality == Quality.Masterwork).ToList();
        Assert.NotEmpty(masterworks);
        Assert.All(masterworks, g =>
        {
            Assert.Equal((ProvenanceKind.Crafted, GlovesRecipe, "Wanderer", 0), (g.Notable!.Provenance, g.Notable.SourceRef, g.Notable.Maker, g.Notable.Familiarity));
            Assert.True(g.Notable.IsValid);
        });
        Assert.All(upgraded.Where(g => g.Quality != Quality.Masterwork), g => Assert.Null(g.Notable));
    }

    [Fact]
    public void ArmourQuality_MovesTheArmourWorn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int Worn(int quality)
        {
            var vest = Stack("item.armor.hide_vest", 1, quality);
            var arena = At(session, AtBench, r => Carrying(r, vest));
            int bare = arena.Simulation.Player.Armor;
            Assert.Null(arena.Submit(new EquipCommand(arena.Player, vest.ItemId)));
            return arena.Simulation.Player.Armor - bare;
        }
        Assert.Equal(new[] { 5, 6, 7, 8 }, new[] { Quality.Crude, Quality.Standard, Quality.Fine, Quality.Masterwork }.Select(Worn));
    }

    [Fact]
    public void AShield_TakesTheOffHand_AndATwoHanderDisplacesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var shield = Stack("item.armor.timber_shield", 1);
        var bow = Stack("item.weapon.hunting_bow", 1);
        var arena = At(session, AtBench, r => Carrying(r, shield, bow));
        int bare = arena.Simulation.Player.Armor;

        Assert.Null(arena.Submit(new EquipCommand(arena.Player, shield.ItemId)));
        Assert.Equal(shield.ItemId, arena.Simulation.Player.Equipment[EquipSlot.OffHand]);
        Assert.Equal(bare + 2, arena.Simulation.Player.Armor);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, bow.ItemId)));
        Assert.False(arena.Simulation.Player.Equipment.ContainsValue(shield.ItemId));
    }

    [Fact]
    public void ANotableRecord_TravelsWithItsItem_ToTheGroundAndBack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var bow = Stack("item.weapon.haldas_bow", 1) with { Notable = new NotableRecord("item.weapon.haldas_bow", ProvenanceKind.Given, "quest.test", 5) { Familiarity = 2, Uses = 1 } };
        var arena = At(session, AtBench, r => Carrying(r, bow));

        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, bow.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        var lying = Assert.Single(arena.Simulation.WorldItems, w => w.DefId == "item.weapon.haldas_bow");
        Assert.Equal(bow.Notable, lying.Notable);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, lying.Id.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
        Assert.Equal(bow.Notable, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.haldas_bow").Notable);
    }

    [Fact]
    public void ANotableFound_WithoutARecord_GetsOneWhenTaken()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var bow = Stack("item.weapon.haldas_bow", 1);   // a save from before records were kept
        var arena = At(session, AtBench, r => Carrying(r, bow));

        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, bow.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        var lying = Assert.Single(arena.Simulation.WorldItems, w => w.DefId == "item.weapon.haldas_bow");
        Assert.NotNull(lying.Notable);
        Assert.Equal(ProvenanceKind.Given, lying.Notable!.Provenance);
    }

    [Fact]
    public void Kills_MakeAnEquippedNotableMoreFamiliar()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var record = new NotableRecord("item.weapon.rusted_sword", ProvenanceKind.Kit, "kit", 0);
        var arena = Arena.Open(session, (120, 60), 0, new[] { (120.0, 62.0) }, r =>
        {
            var sword = r.Equipment[EquipSlot.MainHand];
            return r.WithInventory(r.Inventory.Select(e => e.ItemId == sword ? e with { Notable = record } : e));
        });
        var killed = arena.Record<CreatureKilled>();

        arena.Fight(arena.Creature(), 400);

        Assert.Single(killed);
        var sword = arena.Simulation.Player.Inventory.Single(e => e.ItemId == arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
        Assert.Equal((0, 1), (sword.Notable!.Familiarity, sword.Notable.Uses));
    }

    [Fact]
    public void ABodysDrops_RollTheirQualityByTheCreaturesLevel_TheSameOnEveryLook()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, (120, 60), 0, new[] { (120.0, 62.0) });
        arena.Fight(arena.Creature(), 400);

        var body = arena.Simulation.Containers.Single(c => c.Site.DropLevel is not null);
        Assert.Equal(2, body.Site.DropLevel);
        Assert.All(body.Items, i => Assert.InRange(i.Quality, Quality.Crude, Quality.Standard));   // a level-2 wolf never drops fine
        Assert.Equal(body.Items.AsEnumerable(), arena.Simulation.Containers.Single(c => c.Site.DropLevel is not null).Items.AsEnumerable());
    }
}
