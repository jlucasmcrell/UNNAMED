using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
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
    private const string Survival = "skill.survival";

    // Where the player stands to work each place (content/regions/ashen_hollow.yaml): south of the seam rock, in the ash
    // stand, and at the forge shed's hearth and its anvil - which are too far apart to reach both from one spot.
    private static readonly (double X, double Z) AtSeam = (64, 177.9);
    private static readonly (double X, double Z) AtStand = (180, 137);
    private static readonly (double X, double Z) AtHearth = (66.5, 35);
    private static readonly (double X, double Z) AtAnvil = (65.2, 33.4);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null, long startTick = 0,
        ICellBaselineGenerator? generator = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change, startTick: startTick,
            generator: generator);

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

    private static Arena Reload(GameSession session, TempProfile profile, string slot) =>
        Arena.Resume(session.Setup, new SaveStore(profile.Root).Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry())));

    // ── the nodes ───────────────────────────────────────────────────────────

    [Fact]
    public void TheHollow_HasItsSeamAndItsStand_InTheWorldsBaseline()
    {
        using var profile = new TempProfile();
        var simulation = Harness.Boot(profile).NewGame("Tester", seed: 42);

        var nodes = simulation.Nodes.OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ash_stand", "iron_seam" }, nodes.Select(n => n.Name));
        Assert.All(nodes, n => Assert.True(n.Ready));
        Assert.Equal(("node.wood.ash_stand", Haft, 180_000L, 138_000L), (nodes[0].NodeDefId, nodes[0].ItemId, nodes[0].XMm, nodes[0].ZMm));
        Assert.Equal(("node.ore.iron_seam", Ore, 64_000L, 178_900L), (nodes[1].NodeDefId, nodes[1].ItemId, nodes[1].XMm, nodes[1].ZMm));
        foreach (var node in nodes)
        {
            // The generator's, so a save proves the node's cell against it like any other baseline.
            var cell = CellKey.OfWorld(node.XMm / 1000.0, node.ZMm / 1000.0);
            Assert.Equal(node.Key, Assert.Single(simulation.World.Baseline(cell).Nodes).NodeKey);
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

        Assert.All(gathered, g => Assert.InRange(g.Count, 1, 2));   // [1, 2] a strike, below survival 3
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
    public void ASurvivalistTakesOneMore_FromLevelThree()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int Strike(int survival)
        {
            var arena = At(session, AtSeam, r => Skilled(r, Survival, survival));
            var gathered = arena.Record<NodeGathered>();
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, Node(arena, "iron_seam").Key)));
            return Assert.Single(gathered).Count;
        }

        // The same strike of the same seam each time: the roll is the node's and its harvest's, not the gatherer's.
        Assert.Equal(Strike(0), Strike(2));
        Assert.Equal(Strike(2) + 1, Strike(3));   // PROTOTYPE.md §4.1: survival 3 takes one more
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
        var struck = Reload(session, profile, "struck");
        Assert.Equal(seam.Simulation.StateDigest(), struck.Simulation.StateDigest());
        Assert.Equal(seam.Simulation.Nodes.ToList(), struck.Simulation.Nodes.ToList());
        Assert.Null(struck.Submit(new GatherCommand(struck.Player, seamKey)));
        Assert.Equal("it is worked out", struck.Submit(new GatherCommand(struck.Player, seamKey)));

        // Today's haft taken: still taken after a load, until the day turns.
        var stand = At(session, AtStand, startTick: session.Setup.Progression.TicksPerWorldDay - 20);
        string standKey = Node(stand, "ash_stand").Key;
        Assert.Null(stand.Submit(new GatherCommand(stand.Player, standKey)));
        Save(session, profile, stand, "cut");
        var cut = Reload(session, profile, "cut");
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
        var untouched = hearth.Simulation.Player.Inventory.Where(e => e.DefId != Ore).ToList();
        var crafted = hearth.Record<ItemCrafted>();

        Assert.Null(hearth.Submit(new CraftCommand(hearth.Player, BilletRecipe)));

        Assert.Equal((2, 1), (Carried(hearth, Ore), Carried(hearth, Billet)));   // PROTOTYPE.md C13
        var made = Assert.Single(crafted);
        Assert.Equal((BilletRecipe, Billet, 1), (made.RecipeId, made.ItemId, made.Count));
        Assert.Equal(untouched, hearth.Simulation.Player.Inventory.Where(e => e.DefId is not (Ore or Billet)).ToList());

        var anvil = At(session, AtAnvil, r => Carrying(r, Stack(Billet, 1), Stack(Haft, 2)));
        Assert.Null(anvil.Submit(new CraftCommand(anvil.Player, SpearRecipe)));
        Assert.Equal((0, 1, 1), (Carried(anvil, Billet), Carried(anvil, Haft), Carried(anvil, Spear)));
        Assert.Equal($"needs 1 {Billet}", anvil.Submit(new CraftCommand(anvil.Player, SpearRecipe)));
        Assert.Equal((0, 1, 1), (Carried(anvil, Billet), Carried(anvil, Haft), Carried(anvil, Spear)));   // a refused craft spends nothing
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
    /// door, smelt a billet at the hearth, make the spear at the anvil, take it in hand, and kill a wolf with it.
    /// </summary>
    [Fact]
    public void TheLoop_PlaysEndToEnd_GatherSmeltForgeEquipFight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf asleep in the outpost's south-west corner, out of earshot of the way to the forge, to try the spear on.
        var arena = Arena.OpenCreatures(session, session.Setup, AtSeam, 0, new[] { (Arena.Wolf, 44.0, 30.0, "sleeper") });
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

        Walk((57, 70), (55, 62), (56, 36), (58.8, 34));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.forge_shed")));
        Walk((61.5, 34), AtHearth);
        Craft(arena, BilletRecipe);
        Walk(AtAnvil);
        Craft(arena, SpearRecipe);
        Assert.Equal(new[] { Billet, Spear }, crafted.Select(c => c.ItemId));

        var spear = arena.Simulation.Player.Inventory.Single(e => e.DefId == Spear);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, spear.ItemId)));
        Assert.Equal(Spear, arena.Simulation.Combat.Weapon.Source);
        Assert.Equal(10 + 2 * spear.Quality, arena.Simulation.Combat.Weapon.DamageMin);   // the spear's own quality

        Walk((61.5, 34), (58.8, 34), (50, 32));
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
        Assert.Equal(3, xp.Count(x => x.Source == XpSource.Production));
        var first = Assert.Single(xp, x => x.Awarded > 0);   // AG-7: the first billet, once
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
        Assert.True(Assert.Single(learning, p => p.SkillId == Smithing).Xp > novelty);

        // Twenty points past a complexity-0 billet is past the margin: the first one made teaches its novelty, the rest nothing.
        var master = At(session, AtHearth, r => Skilled(Carrying(r, Stack(Ore, 5)), Smithing, 20));
        var practised = master.Record<SkillPracticed>();
        for (int i = 0; i < 5; i++)
            Craft(master, BilletRecipe);
        Assert.Equal(new[] { novelty, 0, 0, 0, 0 }, practised.Where(p => p.SkillId == Smithing).Select(p => p.Xp));
        Assert.Equal(20, ProgressionEngine.SkillLevel(master.Simulation.Player.Progression, Smithing));

        // So with gathering: the seam is difficulty 5.
        var survivalist = At(session, AtSeam, r => Skilled(r, Survival, 25));
        var gathering = survivalist.Record<SkillPracticed>();
        for (int i = 0; i < 3; i++)
            Assert.Null(survivalist.Submit(new GatherCommand(survivalist.Player, Node(survivalist, "iron_seam").Key)));
        Assert.Equal(new[] { novelty, 0, 0 }, gathering.Where(p => p.SkillId == Survival).Select(p => p.Xp));
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
        string seamCell = CellKey.OfWorld(64, 178.9).ToString();

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
}
