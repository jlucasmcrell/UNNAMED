using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.BuildingTests;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Building across a save (schema 44, WP-1G): the pieces, the sequence and a chest's identity round-trip and navigation derives the same;
/// a layout edit under a saved piece is audited and kept; another character finds the pieces not theirs to take down or mend; prediction
/// meets a loaded wall as the authority does; creatures meet loaded pieces, and their homes never depend on them; a blow on a creature
/// spares a piece. M7's tests, on its crossing area moved with the hollow (coordinates local to <see cref="BuildingTests.Origin"/>).
/// </summary>
public class BuildingSaveTests
{
    private static PlayerRecord Carrying(PlayerRecord r, params int[] stacks) =>
        r.WithInventory(r.Inventory.Concat(stacks.Select(n => Arena.Stack(Timber, n))));

    private static readonly (string, double, double, string)[] None = Array.Empty<(string, double, double, string)>();

    private static LoadResult SaveAndLoad(TempProfile profile, GameSession session, Arena arena, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        return store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry()));
    }

    private static (Arena Arena, string Key) WithAChest(GameSession session)
    {
        var arena = Builder(session, (100.5, 102.6));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        return (arena, Pieces(arena).Single(p => p.DefId == Chest).ContainerKey!);
    }

    private static string? Store(Arena arena, string key, int count)
    {
        var stack = arena.Simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).First(e => e.Count >= count);
        return arena.Submit(new MoveItemCommand(arena.Player, stack.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), count));
    }

    private static int Health(Arena arena, EntityId piece) => arena.Simulation.Pieces.Single(p => p.Id == piece).HealthCurrent;

    private static string RepoRoot() => Harness.RepoRoot();

    /// <summary>The same across a save: the emptied record is kept, loads as the chest's, and takes the next store.</summary>
    [Fact]
    public void APieceChest_EmptiedAndRefilled_AcrossASave_KeepsOneIdentity()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        Assert.Null(Store(arena, key, 2));
        var identity = arena.Simulation.World.Container(key)!.InstanceId;
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, key)));
        var loaded = Arena.Resume(session.Setup, SaveAndLoad(profile, session, arena, "emptied"));
        Assert.Equal((identity, 0), (loaded.Simulation.World.Container(key)!.InstanceId, loaded.Simulation.World.Container(key)!.Items.Length));
        Assert.Null(Store(loaded, key, 2));
        Assert.Equal(identity, loaded.Simulation.World.Container(key)!.InstanceId);
    }

    /// <summary>A swing that strikes a creature strikes no piece (M7 design §4.12): the piece's jamb on the facing line is left whole.</summary>
    [Fact]
    public void ABlowThatHitsACreature_DamagesNoPiece()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The facing line meets the doorway's west jamb 0.8 m off. Built first, then loaded with a wolf in the opening - inside the
        // swing's arc and reach - since a spawner's ground is kept clear of building.
        var built = OpenAt(session, session.Setup, (99.6, 100.0), 180, None, r => Carrying(r, 20));
        Assert.Null(Place(built, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(built, Doorway, 100_500, 99_000, 0));
        var doorway = PieceAt(built, Doorway, 100_500, 99_000);
        var wolf = new SpawnSite("spawn.test.wolf_0", 100_300 + Ox, 99_000 + Oz, 0, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "sentinel")));
        var withWolf = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(wolf) } };
        var arena = Arena.Resume(withWolf, SaveAndLoad(profile, session, built, "wolf"));
        Assert.Equal((100_300L, 99_000L), (arena.Creature().Body.XMm - Ox, arena.Creature().Body.ZMm - Oz));
        var damaged = arena.Record<PieceDamaged>();
        var hits = arena.Record<HitResolved>();
        Blows(arena, 1);
        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Target == arena.Creature().Id);
        Assert.Empty(damaged);
        Assert.Equal(200, Health(arena, doorway));

        // With the wolf gone, the same swing lands on the jamb.
        var alone = OpenAt(session, session.Setup, (99.6, 100.0), 180, None, r => Carrying(r, 20));
        Assert.Null(Place(alone, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(alone, Doorway, 100_500, 99_000, 0));
        Blows(alone, 1);
        Assert.Equal(190, Health(alone, PieceAt(alone, Doorway, 100_500, 99_000)));
    }

    [Fact]
    public void Prediction_EqualsAuthority_AcrossANewWall()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 97.0));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        SaveAndLoad(profile, session, arena, "built");
        Assert.True(session.Load(SaveSlots.Manual("built")).IsComplete);

        var motion = new PlayerMotion(session);
        motion.Resync();
        session.Subscribe<BodyMoved>(motion.OnBodyMoved);
        var north = new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0);
        Assert.True(motion.Send(north));
        session.Submit(new MoveCommand(session.Simulation!.PlayerId, north));
        session.Frame(session.TickSeconds);
        int blocked = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            // Drawn a whole tick ahead, the body is where the authority then puts it - at the wall too.
            var ahead = motion.Predict(1.0);
            session.Frame(session.TickSeconds);
            var now = session.Simulation!.Player.Body;
            Assert.Equal((ahead.XMm, ahead.ZMm), (now.XMm, now.ZMm));
            if (now.ZMm - Oz >= 98_400)
                blocked++;
        }
        Assert.True(blocked > 10, "the character never reached the wall");
        Assert.True(session.Simulation!.Player.Body.ZMm - Oz <= 98_450);
        Assert.Equal(0, session.SubscriberFailures);
    }

    [Fact]
    public void Creatures_AreBlockedByPieces()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 101.0));
        foreach (long x in new long[] { 100_500, 103_500 })
        {
            Assert.Null(Place(arena, Pad, x, 100_500, 0));
            Assert.Null(Place(arena, Wall, x, 99_000, 0));
        }
        var loaded = SaveAndLoad(profile, session, arena, "walls");

        // A roaming wolf whose route runs north through the wall line, injected after the walls went up.
        var patrol = new SpawnSite("spawn.test.patrol", 101_500 + Ox, 95_000 + Oz, 0, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "roamer")))
        {
            Route = ImmutableArray.Create((101_500L + Ox, 95_000L + Oz), (101_500L + Ox, 104_000L + Oz)),
        };
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(patrol) } };
        var world = Arena.Resume(rules, loaded);
        long radius = session.Setup.Combat.Creatures[Arena.Wolf].RadiusMm;
        var parts = world.Simulation.Pieces.SelectMany(p => p.Parts).Select(p => new BoxBlocker("part", p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm, p.HeightMm)).ToList();
        long furthest = long.MinValue;
        for (int i = 0; i < 1_000; i++)
        {
            world.Tick();
            var wolf = world.Creature().Body;
            Assert.All(parts, part => Assert.Null(part.Separation(wolf.XMm, wolf.ZMm, radius)));
            furthest = Math.Max(furthest, wolf.ZMm - Oz);
        }
        // It walked up to the wall and no further.
        Assert.InRange(furthest, 98_800 - radius - 60, 98_800 - radius);
    }

    [Fact]
    public void ForeignPieces_AreNotYoursToTakeDownOrMend_ButTheirDoorsAndChestsAreOpen()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 97.8));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Doorway, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        var loaded = SaveAndLoad(profile, session, arena, "theirs");

        // The same world, played by another character: the pad is not theirs.
        var r = loaded.Player;
        var other = loaded with
        {
            Player = new PlayerRecord(EntityId.NewId(EntityKind.Character), r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression,
                r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations),
        };
        var world = Arena.Resume(session.Setup, other);
        var pad = Pieces(world).Single(p => p.DefId == Pad && p.ZMm == 100_500);
        Assert.NotEqual(world.Player, pad.Owner);
        Assert.Equal("that is not yours to take down", Dismantle(world, pad.Id));
        Assert.Equal("that is not yours to mend", world.Submit(new RepairPieceCommand(world.Player, pad.Id)));   // E8
        // Their door and their chest are a plain record of whose they are (WP-1G): who may enter or take waits for the crime and law
        // package, so this character opens the door and stores in the chest - but takes neither down.
        var door = Pieces(world).Single(p => p.DefId == Door);
        Assert.Null(world.Submit(new InteractCommand(world.Player, door.Id.Value)));
        Assert.True(Pieces(world).Single(p => p.Id == door.Id).DoorOpen);
        Assert.Equal("that is not yours to take down", Dismantle(world, door.Id));
        var chest = Pieces(world).Single(p => p.DefId == Chest);
        Assert.True(world.WalkTo(100.5, 102.6));
        var carried = world.Simulation.Player.Inventory.First(e => e.DefId == Timber);
        Assert.Null(world.Submit(new MoveItemCommand(world.Player, carried.ItemId.Value, ItemPlace.Carried, ItemPlace.In(chest.ContainerKey!), 1)));
        Assert.Equal("that is not yours to take down", Dismantle(world, chest.Id));
        Assert.Equal(4, Pieces(world).Length);
    }

    [Fact]
    public void ALayoutEditUnderASavedPiece_IsAuditedAndKept()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (88.5, 116.0), 180, timber: new[] { 3 });
        Assert.Null(Place(arena, Pad, 88_500, 112_500, 0));
        Assert.Null(Place(arena, Wall, 87_000, 112_500, 1));
        var rows = Pieces(arena).Select(p => (p.Id, p.DefId, p.XMm, p.ZMm, p.Rotation)).ToList();
        var wall = PieceAt(arena, Wall, 87_000, 112_500);
        var loaded = SaveAndLoad(profile, session, arena, "edited");

        // Later content: an authored post under the wall, and walls retuned to 150 health.
        var setup = session.Setup;
        var edited = setup with
        {
            Layout = setup.Layout with
            {
                Space = setup.Layout.Space with { Blockers = setup.Layout.Space.Blockers.Add(new CircleBlocker("post.later", 87_000 + Ox, 112_500 + Oz, 400, 2_000)) },
            },
            Building = setup.Building with
            {
                Catalog = new BuildingCatalog(setup.Building.Catalog.Pieces.Values.Select(p => p.Id == Wall ? p with { HealthMax = 150 } : p)),
            },
        };
        var bus = new EventBus();
        var published = new List<object>();
        bus.Subscribe<PiecePlaced>(published.Add);
        bus.Subscribe<PieceRemoved>(published.Add);
        bus.Subscribe<StructuresChanged>(published.Add);
        bus.Subscribe<NavigationRebuilt>(published.Add);
        var simulation = Simulation.Start(edited, loaded.Player, loaded.World, loaded.Manifest.WorldTick, bus);

        Assert.Equal(rows, simulation.Pieces.Select(Local).Select(p => (p.Id, p.DefId, p.XMm, p.ZMm, p.Rotation)));
        Assert.Equal(150, simulation.Pieces.Single(p => p.Id == wall).HealthCurrent);
        Assert.Contains(new StructureConflict(wall.Value, "it stands over post.later"), simulation.StructureAudit);
        Assert.Contains(new StructureConflict(wall.Value, "health 200 is above Timber Wall's 150: clamped"), simulation.StructureAudit);
        Assert.Empty(published);
        // Navigation derives from the changed inputs: the edited layout and the pieces it kept.
        var inputs = NavigationLayout.AuthoredInputs(edited.Layout, edited.Navigation)
            .AddRange(simulation.StructureFootprints.Select(f => new NavInput(NavInputKind.Solid, f.Box(), null)));
        var fresh = NavGrid.Build(edited.Navigation, NavigationLayout.Bounds(edited.Layout), NavigationLayout.TileKeys(edited.Layout), inputs);
        Assert.Equal(fresh.Digest(), simulation.Navigation.Grid.Digest());
    }

    // G9
    [Fact]
    public void CreatureHomes_DoNotDependOnPlacedPieces()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var built = Builder(session, (102.0, 101.0));
        Assert.Null(Place(built, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(built, Wall, 100_500, 99_000, 0));
        var withWall = SaveAndLoad(profile, session, built, "with_wall");
        var bare = SaveAndLoad(profile, session, Builder(session, (102.0, 101.0)), "bare");

        // A spawner injected on the wall after it stood, bypassing check 11: its first home sample lies inside the wall.
        var site = new SpawnSite("spawn.test.homes", 100_500 + Ox, 99_000 + Oz, 100, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "sleeper")));
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(site) } };
        var a = Arena.Resume(rules, withWall).Simulation.Creatures.Select(c => (c.Key, c.Body)).ToList();
        var b = Arena.Resume(rules, bare).Simulation.Creatures.Select(c => (c.Key, c.Body)).ToList();
        Assert.Single(a);
        Assert.Equal(b, a);

        // And the code says so: Populate reads the authored space, never the built one.
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "World", "Runtime", "Creatures.cs")).Replace("\r\n", "\n");
        int start = source.IndexOf("public void Populate()", StringComparison.Ordinal);
        string body = source[start..source.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
        Assert.Contains("_context.Setup.SpaceFor(", body);
        Assert.DoesNotContain("_context.SpaceFor(", body);
    }

    // F-E7
    [Fact]
    public void Building_RoundTripsThroughSave_AndNavigationDerivesIdentically()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        WorkshopStepOne(arena);
        // Resumed under the arena's own rules - no spawners - so the two worlds hold the same creatures.
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray<SpawnSite>.Empty } };
        var loaded = Arena.Resume(rules, SaveAndLoad(profile, session, arena, "workshop"));

        static IEnumerable<object> Rows(Arena a) => a.Simulation.World.Pieces.Select(p =>
            (object)(p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen));
        Assert.Equal(Rows(arena), Rows(loaded));
        Assert.Equal(16, loaded.Simulation.StructureRevision);
        Assert.Equal(arena.Simulation.Navigation.Grid.Digest(), loaded.Simulation.Navigation.Grid.Digest());
        Assert.Equal(arena.Simulation.ActiveSpace.Blockers.ToList(), loaded.Simulation.ActiveSpace.Blockers.ToList());
        Assert.Equal(arena.Simulation.StructureFootprints.ToList(), loaded.Simulation.StructureFootprints.ToList());
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.Empty(loaded.Simulation.StructureAudit);
    }
}
