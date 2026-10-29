using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M3f: one gather, craft and equip loop over the game's own content - two nodes, two recipes, and quality on the thing made
/// (PROTOTYPE.md C12-C14; the content bible's §12). No rank gates a recipe; the first of each thing made earns level XP once.
/// </summary>
public class CraftingTests
{
    private const string Ore = "item.material.iron_ore";
    private const string Billet = "item.material.iron_ingot";
    private const string Haft = "item.material.ash_haft";
    private const string Spear = "item.weapon.march_spear";
    private const string BilletRecipe = "recipe.smithing.iron_billet";
    private const string SpearRecipe = "recipe.smithing.march_spear";
    private const string Smithing = "skill.smithing";
    private const string Mining = "skill.mining";
    private const string Kera = "npc.ashen_hollow.kera_voss";

    // Where the player stands to work each place (content/regions/ashen_hollow.yaml): south of the seam rock, in the ash
    // stand, and at the forge shed's hearth and its anvil - which are too far apart to reach both from one spot.
    private static readonly (double X, double Z) AtSeam = (28, 44.1);
    private static readonly (double X, double Z) AtStand = (180, 137);
    private static readonly (double X, double Z) AtHearth = (59.5, 143);
    private static readonly (double X, double Z) AtAnvil = (58.2, 141.4);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    /// <summary>A character Kera has taught - both recipes known (M4: the starting package holds none) - then any change.</summary>
    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null, long startTick = 0,
        ICellBaselineGenerator? generator = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(),
            r => Usual(change?.Invoke(Taught(r)) ?? Taught(r)), startTick: startTick, generator: generator);

    /// <summary>A smith who learns at the rules' own rate: the numbers here are the crafting rules', before any people's aptitude.</summary>
    private static PlayerRecord Usual(PlayerRecord r) => Arena.Usual(r);

    private static PlayerRecord Taught(PlayerRecord r) =>
        r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItems(new[] { BilletRecipe, SpearRecipe }
                .Select(id => KeyValuePair.Create(id, new KnownTechnique(LearningSource.Teacher, Kera, 0)))),
        });

    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, r.Currency, r.Effects);

    private static InventoryEntry Stack(string defId, int count, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality };

    private static PlayerRecord Skilled(PlayerRecord r, string skill, int level) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static NodeView Node(Arena arena, string name) => arena.Simulation.Nodes.Single(n => n.Name == name);

    private static void Craft(Arena arena, string recipe)
    {
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, recipe)));
        arena.Tick();
    }

    private static void Save(GameSession session, TempProfile profile, Arena arena, string slot) =>
        new SaveStore(profile.Root).Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));

    private static Arena Reload(GameSession session, TempProfile profile, Arena before, string slot) =>
        Arena.Resume(before.Simulation.Setup, new SaveStore(profile.Root).Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry())));

    // ── the nodes ───────────────────────────────────────────────────────────


    [Fact]
    public void AnAssembledSword_IsBuiltFromThePartsThePlayerPicked_AndNotFromTheFirstOnesOffered()
    {
        // D21 G3: the bench asks which parts, the craft carries them, and the piece that lands in the pack remembers them.
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string recipeId = "recipe.smithing.assembled_sword";
        var recipe = session.Setup.Crafting.Recipes[recipeId];
        var assembly = session.Setup.Items.Catalog.Find(recipe.OutputItemId)!.Assembly!;
        var maker = session.Setup.Gear.Makers[assembly.MakerId!];

        // The player picks the maker's second blade and the second guard - not the defaults - and leaves the rest alone.
        string blade = maker.PartSets[assembly.Family]["blade"][1].PartId;
        string guard = maker.PartSets[assembly.Family]["guard"][1].PartId;
        var picked = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        picked["blade"] = blade;
        picked["guard"] = guard;

        var arena = Arena.OpenCreatures(session, session.Setup, AtHearth, 0, Array.Empty<(string, double, double, string)>(),
            r => Usual(Taught(r)) is var taught
                ? Carrying(taught.WithProgression(taught.Progression with
                {
                    Known = taught.Progression.Known.Add(recipeId, new KnownTechnique(LearningSource.Experiment, "forge", 0)),
                }), Stack(Billet, 4), Stack(Haft, 2))
                : taught);

        Assert.Null(arena.Submit(new CraftCommand(arena.Player, recipeId) { Parts = picked.ToImmutable() }));
        arena.Tick();

        // The character also starts with a plain sword, so take the one this bench made.
        var made = Assert.Single(arena.Simulation.Player.Inventory.Where(e => e.DefId == recipe.OutputItemId && e.Built is not null));
        Assert.NotNull(made.Built);
        Assert.Equal(assembly.Family, made.Built!.Family);
        Assert.Equal(maker.Id, made.Built.MakerId);
        Assert.Equal(blade, made.Built.Parts["blade"]);
        Assert.Equal(guard, made.Built.Parts["guard"]);
        Assert.Equal(maker.PartSets[assembly.Family]["grip"][0].PartId, made.Built.Parts["grip"]);   // the default filled the rest

        // And the piece it makes is that assembly, resolved: not the plain template.
        var resolved = GearResolver.Resolve(session.Setup.Items.Catalog.Find(made.DefId),
            made.Built, session.Setup.Gear.Parts, session.Setup.Gear.Materials, maker);
        Assert.False(resolved.Clamped);
        Assert.True(resolved.DamageMax > 0);
    }

    [Fact]
    public void TheHollow_HasItsSeamAndItsStand_InTheWorldsBaseline()
    {
        using var profile = new TempProfile();
        var simulation = Harness.Boot(profile).NewGame(Harness.Named("Tester"), seed: 42);

        Assert.Equal(new[]
            {
                "ash_stand", "birch_woundmoss_1", "birch_woundmoss_2", "blackvein_spoil_1", "blackvein_spoil_2", "bog_ashbloom_1", "bog_ashbloom_2",
                "bog_ashbloom_3", "bog_ashbloom_4", "cairnwold_ash_stand", "cairnwold_ash_stand_2", "cairnwold_iron_seam", "charwood_deadfall_1",
                "charwood_deadfall_2", "crag_spoil_1", "crag_spoil_2", "foldscar_boneleaf_1", "foldscar_boneleaf_2", "foldscar_boneleaf_3",
                "halda_larder_snare_east", "halda_larder_snare_west", "halda_pelt_snare_east", "halda_pelt_snare_west", "hollow_ash_stand_2", "hollow_woundmoss_1", "hollow_woundmoss_2", "hollow_woundmoss_3", "iron_seam", "moor_deadfall_1", "moor_deadfall_2",
                "moss_cotton_1", "moss_cotton_2", "moss_cotton_3", "road_deadfall", "road_woundmoss",
            }, simulation.Nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal));
        var nodes = simulation.Nodes.Where(n => n.Name is "ash_stand" or "iron_seam").OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
        Assert.All(nodes, n => Assert.True(n.Ready));
        Assert.Equal(("node.wood.ash_stand", Haft, 580_000L, 738_000L), (nodes[0].NodeDefId, nodes[0].ItemId, nodes[0].XMm, nodes[0].ZMm));
        Assert.Equal(("node.ore.iron_seam", Ore, 428_000L, 645_100L), (nodes[1].NodeDefId, nodes[1].ItemId, nodes[1].XMm, nodes[1].ZMm));
        foreach (var node in nodes)
        {
            // The generator's, so a save proves the node's cell against it like any other baseline.
            var cell = CellKey.OfWorld(node.XMm / 1000.0, node.ZMm / 1000.0);
            Assert.Contains(node.Key, simulation.World.Baseline(cell).Nodes.Select(n => n.NodeKey));
        }
    }

    [Fact]
    public void TheSeam_GivesThreeStrikes_ThenIsWorkedOutForGood()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtSeam, startTick: session.Setup.Progression.TicksPerWorldDay - 20);
        var gathered = arena.Record<NodeGathered>();
        string seam = Node(arena, "iron_seam").Key;

        for (int strike = 0; strike < 3; strike++)
        {
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, seam)));
            arena.Tick();
        }

        Assert.All(gathered, g => Assert.InRange(g.Count, 1, 2));   // [1, 2] a strike, below mining 3
        Assert.Equal(new[] { false, false, true }, gathered.Select(g => g.Spent));
        Assert.Equal(gathered.Sum(g => g.Count), Carried(arena, Ore));
        Assert.Equal("it is worked out", arena.Submit(new GatherCommand(arena.Player, seam)));

        arena.Tick(40);   // a new world day: a seam does not grow back
        Assert.False(Node(arena, "iron_seam").Ready);
        Assert.Equal("it is worked out", arena.Submit(new GatherCommand(arena.Player, seam)));
    }

    [Fact]
    public void TheStand_GivesAHaftADay_AndGrowsBackAtTheDayBoundary()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtStand, startTick: session.Setup.Progression.TicksPerWorldDay - 20);
        string stand = Node(arena, "ash_stand").Key;

        Assert.Null(arena.Submit(new GatherCommand(arena.Player, stand)));
        Assert.Equal(1, Carried(arena, Haft));
        Assert.Equal("there is nothing to take until it grows back", arena.Submit(new GatherCommand(arena.Player, stand)));
        arena.Tick(10);
        Assert.False(Node(arena, "ash_stand").Ready);

        arena.Tick(20);   // past the boundary
        Assert.True(Node(arena, "ash_stand").Ready);
        Assert.Null(arena.Submit(new GatherCommand(arena.Player, stand)));
        Assert.Equal(2, Carried(arena, Haft));
    }

    [Fact]
    public void AMinerTakesOneMore_FromLevelThree()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int Strike(int mining)
        {
            var arena = At(session, AtSeam, r => Skilled(r, Mining, mining));
            var gathered = arena.Record<NodeGathered>();
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, Node(arena, "iron_seam").Key)));
            return Assert.Single(gathered).Count;
        }

        // The same strike of the same seam each time: the roll is the node's and its harvest's, not the gatherer's.
        Assert.Equal(Strike(0), Strike(2));
        Assert.Equal(Strike(2) + 1, Strike(3));   // PROTOTYPE.md §4.1: mining 3 takes one more
    }

    [Fact]
    public void WhatWasHarvested_SurvivesASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // Two strikes of the seam's three.
        var seam = At(session, AtSeam);
        string seamKey = Node(seam, "iron_seam").Key;
        Assert.Null(seam.Submit(new GatherCommand(seam.Player, seamKey)));
        Assert.Null(seam.Submit(new GatherCommand(seam.Player, seamKey)));
        seam.Tick(5);
        Save(session, profile, seam, "struck");
        var struck = Reload(session, profile, seam, "struck");
        Assert.Equal(seam.Simulation.StateDigest(), struck.Simulation.StateDigest());
        Assert.Equal(seam.Simulation.Nodes.ToList(), struck.Simulation.Nodes.ToList());
        Assert.Null(struck.Submit(new GatherCommand(struck.Player, seamKey)));
        Assert.Equal("it is worked out", struck.Submit(new GatherCommand(struck.Player, seamKey)));

        // Today's haft taken: still taken after a load, until the day turns.
        var stand = At(session, AtStand, startTick: session.Setup.Progression.TicksPerWorldDay - 20);
        string standKey = Node(stand, "ash_stand").Key;
        Assert.Null(stand.Submit(new GatherCommand(stand.Player, standKey)));
        Save(session, profile, stand, "cut");
        var cut = Reload(session, profile, stand, "cut");
        Assert.Equal("there is nothing to take until it grows back", cut.Submit(new GatherCommand(cut.Player, standKey)));
        cut.Tick(30);
        Assert.Null(cut.Submit(new GatherCommand(cut.Player, standKey)));
    }

    // ── the recipes ─────────────────────────────────────────────────────────

    [Fact]
    public void ARecipe_NeedsKnowing_ItsStation_AndItsMaterials()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var unlearned = At(session, AtHearth, r => Carrying(r.WithProgression(r.Progression with { Known = r.Progression.Known.Clear() }), Stack(Ore, 1)));
        Assert.Equal($"{BilletRecipe} is not known", unlearned.Submit(new CraftCommand(unlearned.Player, BilletRecipe)));

        var smith = At(session, AtHearth);
        Assert.Equal($"needs 1 {Ore}", smith.Submit(new CraftCommand(smith.Player, BilletRecipe)));
        Assert.Equal("no anvil in reach", smith.Submit(new CraftCommand(smith.Player, SpearRecipe)));
        Assert.Equal("recipe.smithing.nothing is not a recipe this build knows", smith.Submit(new CraftCommand(smith.Player, "recipe.smithing.nothing")));
        Assert.Equal("that is out of reach", smith.Submit(new GatherCommand(smith.Player, Node(smith, "iron_seam").Key)));
        Assert.Equal("there is no node 'node.nowhere'", smith.Submit(new GatherCommand(smith.Player, "node.nowhere")));

        var afield = At(session, AtSeam, r => Carrying(r, Stack(Ore, 1)));
        Assert.Equal("no forge in reach", afield.Submit(new CraftCommand(afield.Player, BilletRecipe)));
    }

    [Fact]
    public void Crafting_SpendsExactlyItsInputs_AndMakesExactlyItsOutput()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var hearth = At(session, AtHearth, r => Carrying(r, Stack(Ore, 3)));
        // Where a stack lies in the pack (schema 33) is the pack's to settle: the stacks are compared as they are, wherever they lie.
        InventoryEntry Unplaced(InventoryEntry e) => e with { Bag = 0, Slot = -1 };
        var untouched = hearth.Simulation.Player.Inventory.Where(e => e.DefId != Ore).Select(Unplaced).ToList();
        var crafted = hearth.Record<ItemCrafted>();

        Assert.Null(hearth.Submit(new CraftCommand(hearth.Player, BilletRecipe)));

        Assert.Equal((2, 1), (Carried(hearth, Ore), Carried(hearth, Billet)));   // PROTOTYPE.md C13
        var made = Assert.Single(crafted);
        Assert.Equal((BilletRecipe, Billet, 1), (made.RecipeId, made.ItemId, made.Count));
        Assert.Equal(untouched, hearth.Simulation.Player.Inventory.Where(e => e.DefId is not (Ore or Billet)).Select(Unplaced).ToList());

        var anvil = At(session, AtAnvil, r => Carrying(r, Stack(Billet, 1), Stack(Haft, 2)));
        Assert.Null(anvil.Submit(new CraftCommand(anvil.Player, SpearRecipe)));
        Assert.Equal((0, 1, 1), (Carried(anvil, Billet), Carried(anvil, Haft), Carried(anvil, Spear)));
        Assert.Equal($"needs 1 {Billet}", anvil.Submit(new CraftCommand(anvil.Player, SpearRecipe)));
        Assert.Equal((0, 1, 1), (Carried(anvil, Billet), Carried(anvil, Haft), Carried(anvil, Spear)));   // a refused craft spends nothing
    }

    /// <summary>
    /// The Phase-1 technical audit, L-12: every craft sent its first-time award, and every repeat - which pays nothing - was still told
    /// as "+0 XP". Only an award that paid is news.
    /// </summary>
    [Fact]
    public void TwoBilletsMade_TellOfExperienceOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var hearth = At(session, AtHearth, r => Carrying(r, Stack(Ore, 3)));
        var gained = hearth.Record<ExperienceGained>();

        Assert.Null(hearth.Submit(new CraftCommand(hearth.Player, BilletRecipe)));
        Assert.Null(hearth.Submit(new CraftCommand(hearth.Player, BilletRecipe)));

        Assert.Equal(2, Carried(hearth, Billet));
        var first = Assert.Single(gained);
        Assert.Equal(XpSource.Production, first.Source);
        Assert.True(first.Awarded > 0);
    }

    /// <summary>Spears made one after another at the anvil by a smith of this skill, each dropped once made; their qualities.</summary>
    private static List<int> Spears(GameSession session, int smithing, int count, int billetQuality = Quality.Standard)
    {
        var arena = At(session, AtAnvil, r => Skilled(Carrying(r, Stack(Billet, count, billetQuality), Stack(Haft, count)), Smithing, smithing));
        var crafted = arena.Record<ItemCrafted>();
        for (int i = 0; i < count; i++)
        {
            Craft(arena, SpearRecipe);
            var spear = arena.Simulation.Player.Inventory.Single(e => e.DefId == Spear);
            Assert.Equal(crafted[^1].Quality, spear.Quality);   // the quality lands on the instance (C14)
            Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, spear.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        }
        return crafted.Select(c => c.Quality).ToList();
    }

    [Fact]
    public void Quality_ComesFromTheSmithsHand_CappedByTheWeakestMaterial()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // Ten points short of the spear's complexity: 30% crude and never fine - but a novice may try it.
        var novice = Spears(session, smithing: 0, count: 10);
        Assert.Contains(Quality.Crude, novice);
        Assert.DoesNotContain(Quality.Fine, novice);

        // Twenty points past it: half fine, never crude.
        var master = Spears(session, smithing: 30, count: 10);
        Assert.Contains(Quality.Fine, master);
        Assert.DoesNotContain(Quality.Crude, master);

        // A crude billet caps even a master's spear at standard.
        Assert.DoesNotContain(Quality.Fine, Spears(session, smithing: 30, count: 10, billetQuality: Quality.Crude));

        // The best of what is carried is spent first.
        var choosy = At(session, AtAnvil, r => Carrying(r, Stack(Billet, 1, Quality.Crude), Stack(Billet, 1, Quality.Fine), Stack(Haft, 1)));
        Craft(choosy, SpearRecipe);
        Assert.Equal(Quality.Crude, choosy.Simulation.Player.Inventory.Single(e => e.DefId == Billet).Quality);
    }

    [Fact]
    public void AFineSpear_HitsHarder_AndACrudeOneSofter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        (int Min, int Max, int Hit) Wield(int quality)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 62.0, "sentinel") },
                r => Carrying(r, Stack(Spear, 1, quality)));
            Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == Spear).ItemId)));
            var hits = arena.Record<HitResolved>();
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
            var weapon = arena.Simulation.Combat.Weapon;
            return (weapon.DamageMin, weapon.DamageMax, hits.Single(h => h.Attacker == arena.Player).Damage);
        }

        var crude = Wield(Quality.Crude);
        var standard = Wield(Quality.Standard);
        var fine = Wield(Quality.Fine);
        Assert.Equal((10, 14), (standard.Min, standard.Max));   // the spear as authored
        Assert.Equal((8, 12), (crude.Min, crude.Max));          // config.crafting: 2 a step
        Assert.Equal((12, 16), (fine.Min, fine.Max));
        Assert.True(crude.Hit < standard.Hit && standard.Hit < fine.Hit, $"{crude.Hit} < {standard.Hit} < {fine.Hit}");   // one thrust, one roll
    }

    // ── the whole loop ──────────────────────────────────────────────────────

    /// <summary>
    /// ROADMAP.md M3f's exit: one character in one world, every step through the commands the keys send - strike the seam
    /// until it is worked out, cut a haft at the stand, walk back through the outpost gate to the forge shed and open its
    /// door, ask Kera to teach the forge (M4: nobody starts knowing it), smelt a billet at the hearth, make the spear at the
    /// anvil, take it in hand, and kill a wolf with it.
    /// </summary>
    [Fact]
    public void TheLoop_PlaysEndToEnd_GatherSmeltForgeEquipFight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf asleep west of the smithy, out of earshot of the way to the forge, to try the spear on.
        var arena = Arena.OpenCreatures(session, session.Setup, AtSeam, 0, new[] { (Arena.Wolf, 37.0, 138.0, "sleeper") }, Usual);
        var crafted = arena.Record<ItemCrafted>();
        var hits = arena.Record<HitResolved>();
        void Walk(params (double X, double Z)[] route)
        {
            foreach (var (x, z) in route)
                Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z})");
        }

        while (Node(arena, "iron_seam").Ready)
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, Node(arena, "iron_seam").Key)));
        Assert.InRange(Carried(arena, Ore), 3, 6);

        Walk(AtStand);
        Assert.Null(arena.Submit(new GatherCommand(arena.Player, Node(arena, "ash_stand").Key)));
        Assert.Equal(1, Carried(arena, Haft));

        Walk((160, 140), (120, 140), (100, 142), (88, 145), (70, 135), (51.8, 136), (51.8, 142));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.forge_shed")));
        Walk((54.5, 142), AtKera);
        Assert.Equal($"{BilletRecipe} is not known", arena.Submit(new CraftCommand(arena.Player, BilletRecipe)));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "teach")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        Walk(AtHearth);
        Craft(arena, BilletRecipe);
        Walk(AtAnvil);
        Craft(arena, SpearRecipe);
        Assert.Equal(new[] { Billet, Spear }, crafted.Select(c => c.ItemId));

        var spear = arena.Simulation.Player.Inventory.Single(e => e.DefId == Spear);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, spear.ItemId)));
        Assert.Equal(Spear, arena.Simulation.Combat.Weapon.Source);
        Assert.Equal(10 + 2 * spear.Quality, arena.Simulation.Combat.Weapon.DamageMin);   // the spear's own quality

        Walk((54.5, 142), (51.8, 142), (43, 140));
        Assert.True(arena.Creature().Alive);
        arena.Fight(arena.Creature(), 1_200);
        Assert.False(arena.Creature().Alive);
        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Source == Spear && h.Damage > 0);
        Assert.DoesNotContain(hits, h => h.Attacker == arena.Player && h.Source != Spear);
    }

    // ── what making teaches ─────────────────────────────────────────────────

    [Fact]
    public void OnlyTheFirstOfEachThingMade_EarnsLevelXp()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtHearth, r => Carrying(r, Stack(Ore, 3)));
        var xp = arena.Record<ExperienceGained>();

        for (int i = 0; i < 3; i++)
            Craft(arena, BilletRecipe);

        Assert.Equal(3, Carried(arena, Billet));
        // AG-7: the first billet, once - and the repeats, which pay nothing, are not told as "+0 XP" (the Phase-1 technical audit, L-12).
        var first = Assert.Single(xp, x => x.Source == XpSource.Production);
        Assert.Equal(arena.Simulation.Setup.Crafting.Recipes[BilletRecipe].FirstTimeXp, first.Awarded);
    }

    [Fact]
    public void WorkFarBelowTheHand_TeachesOnlyItsFirst()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        long novelty = session.Setup.Progression.Skills.NoveltyBonusXp;

        // A novice's billet is work worth learning from.
        var novice = At(session, AtHearth, r => Carrying(r, Stack(Ore, 1)));
        var learning = novice.Record<SkillPracticed>();
        Craft(novice, BilletRecipe);
        var lesson = Assert.Single(learning, p => p.SkillId == Smithing);
        Assert.True(lesson.Xp > novelty);
        // Twenty XP takes smithing from 0 to 1, so the event says it rose: the skill-up the HUD tells (WP9).
        Assert.Equal(ProgressionEngine.SkillLevel(novice.Simulation.Player.Progression, Smithing), lesson.LevelsGained);
        Assert.True(lesson.LevelsGained > 0);

        // Twenty points past a complexity-0 billet is past the margin: the first one made teaches its novelty, the rest nothing.
        var master = At(session, AtHearth, r => Skilled(Carrying(r, Stack(Ore, 5)), Smithing, 20));
        var practised = master.Record<SkillPracticed>();
        for (int i = 0; i < 5; i++)
            Craft(master, BilletRecipe);
        Assert.Equal(new[] { novelty, 0, 0, 0, 0 }, practised.Where(p => p.SkillId == Smithing).Select(p => p.Xp));
        Assert.All(practised, p => Assert.Equal(0, p.LevelsGained));   // 25 XP is not the 100 that level 20 needs
        Assert.Equal(20, ProgressionEngine.SkillLevel(master.Simulation.Player.Progression, Smithing));

        // So with gathering: the seam is difficulty 5.
        var miner = At(session, AtSeam, r => Skilled(r, Mining, 25));
        var gathering = miner.Record<SkillPracticed>();
        for (int i = 0; i < 3; i++)
            Assert.Null(miner.Submit(new GatherCommand(miner.Player, Node(miner, "iron_seam").Key)));
        Assert.Equal(new[] { novelty, 0, 0 }, gathering.Where(p => p.SkillId == Mining).Select(p => p.Xp));
    }

    // ── saves from before the nodes ─────────────────────────────────────────

    [Fact]
    public void ASaveFromBeforeTheNodes_LoadsOntoTheBaselineThatHasThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var g = session.Setup.Layout.Generation;
        var before = new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(),
            new TerrainRule(g.TerrainBaseHeightMm, g.TerrainAmplitudeMm, g.TerrainSamplesPerAxis)));
        Assert.NotEqual(before.Fingerprint, session.Generator.Fingerprint);

        // A world from before M3f with a flask put down at the foot of the seam rock: the seam's cell has diverged.
        var old = At(session, AtSeam, generator: before);
        var flask = old.Simulation.Player.Inventory.Single(e => e.DefId == "item.tool.water_flask").ItemId;
        Assert.Null(old.Submit(new MoveItemCommand(old.Player, flask.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        Save(session, profile, old, "before");
        var hollow = Harness.HollowOf(session.Setup);
        string seamCell = CellKey.OfWorld(AtSeam.X + hollow.X, AtSeam.Z + 1 + hollow.Z).ToString();

        // Today's baseline of that cell holds the seam, so the save cannot be proven against it without the transition...
        var refused = Assert.Throws<SaveCompatibilityException>(() =>
            new SaveStore(profile.Root).Load(SaveSlots.Manual("before"), new LoadContext(session.Generator, session.Content, new Registry())));
        Assert.Contains(refused.Report.CellsMismatched, c => c.StartsWith(seamCell, StringComparison.Ordinal));

        // ... which the session registers: nothing the save holds was a node.
        var loaded = session.Load(SaveSlots.Manual("before"));
        Assert.Contains(loaded.Report.CellsRebased, c => c.StartsWith(seamCell, StringComparison.Ordinal));
        Assert.Empty(loaded.Report.Loss);
        Assert.Contains(session.Simulation!.WorldItems, i => i.DefId == "item.tool.water_flask");
        Assert.True(session.Simulation.Nodes.Single(n => n.Name == "iron_seam").Ready);
    }

    /// <summary>
    /// M6 moved the region to the content bible's four cells, and the iron seam from M3's north shelf to Blackvein Cut. A save made
    /// under M3's layout loads through the transition the session registers: the old seam's cell is rebased, a strike recorded
    /// against the old seam is declared lost (the seam it names is not there any more), and everything else carries.
    /// </summary>
    [Fact]
    public void ASaveFromTheM3Layout_LoadsOntoTheFourCells_LosingOnlyTheOldSeamsStrike()
    {
        using var profile = new TempProfile();
        // M3 and M6 played the hollow as a region of its own; the transition between their layouts is that region's.
        var session = Harness.Boot(profile, "region.ashen_hollow");
        var g = session.Setup.Layout.Generation;
        FixedNode Fixed(string name, string def, double x, double z) => new(name, def, CellKey.OfWorld(x, z),
            (int)WorldMath.FloorMod((long)(x * 1000) / 10, WorldMath.CellSizeCm), (int)WorldMath.FloorMod((long)(z * 1000) / 10, WorldMath.CellSizeCm));
        var m3 = new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(),
            new TerrainRule(g.TerrainBaseHeightMm, g.TerrainAmplitudeMm, g.TerrainSamplesPerAxis),
            new[] { Fixed("iron_seam", "node.ore.iron_seam", 64, 178.9), Fixed("ash_stand", "node.wood.ash_stand", 180, 138) }));
        var m3Rules = session.Setup with
        {
            Layout = session.Setup.Layout with
            {
                Nodes = ImmutableArray.Create(new NodeSite("iron_seam", "node.ore.iron_seam", 64_000, 178_900), new NodeSite("ash_stand", "node.wood.ash_stand", 180_000, 138_000)),
            },
        };

        // A world of M3's layout: the old seam struck once.
        var old = Arena.OpenCreatures(session, m3Rules, (64, 177.9), 0, Array.Empty<(string, double, double, string)>(), Taught, generator: m3);
        Assert.Null(old.Submit(new GatherCommand(old.Player, Node(old, "iron_seam").Key)));
        Save(session, profile, old, "m3");
        string oldSeamCell = CellKey.OfWorld(64, 178.9).ToString();

        var loaded = session.Load(SaveSlots.Manual("m3"));
        Assert.Contains(loaded.Report.CellsRebased, c => c.StartsWith(oldSeamCell, StringComparison.Ordinal) && c.Contains("four cells", StringComparison.Ordinal));
        Assert.Contains(loaded.Report.Loss, l => l.Contains("iron_seam", StringComparison.Ordinal) && l.Contains("is not in the new baseline", StringComparison.Ordinal));
        Assert.True(session.Simulation!.Nodes.Single(n => n.Name == "iron_seam").Ready);   // the seam in Blackvein Cut is untouched
        Assert.InRange(session.Simulation.Player.Inventory.Where(e => e.DefId == Ore).Sum(e => e.Count), 1, 2);   // the ore carried is kept
    }
}
