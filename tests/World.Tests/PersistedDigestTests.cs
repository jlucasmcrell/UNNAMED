using System.Collections.Immutable;
using System.Reflection;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Tests;

/// <summary>
/// G12 (M7 design §6.4): every persisted field of an M7 record moves the digest that covers it, so a hand-written digest cannot miss a
/// field and leave two different states "equal". Its only exemptions are named here.
/// </summary>
public class PersistedDigestTests
{
    /// <summary>The proof stamps: set by <c>TakeSnapshot</c> and at decode, outside the digest as a created instance's stamp is.</summary>
    private static readonly string[] Exempt = { "PieceRecord.BaselineHash", "NpcErrandRecord.BaselineHash" };

    private static readonly CellKey Cell = CellKey.Parse("r_0_0:c_00_04");
    private static readonly EntityId Owner = EntityId.Create(EntityKind.Character, 1_700_000_000_100, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
    private static readonly EntityId OtherOwner = EntityId.Create(EntityKind.Character, 1_700_000_000_200, new byte[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 });

    private static PieceRecord Piece() =>
        new(EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v1", Owner.Value), "piece.fixture.pad", Cell.ToString(), 49_500, 450_500, 0, Owner, 200);

    private static NavRoute Route() => NavRoute.Active(new NavPoint(40_000, 440_000),
        ImmutableArray.Create(new NavPoint(45_000, 445_000), new NavPoint(40_000, 440_000)), 90, 0xFEDCBA9876543210, new NavRect(20_000, 420_000, 60_000, 470_000), false);

    private static NpcErrandRecord Errand() =>
        new("npc.fixture.smith", Cell.ToString(), NpcErrandPhase.ToHome, null, Owner, 41_000, 441_000, 180_000) { Route = Route(), StuckTicks = 3 };

    private static string DigestOf(PieceRecord piece, NpcErrandRecord errand)
    {
        var world = TestWorlds.NewWorld();
        world.PlacePiece(piece, 5);
        world.SetNpcErrand(errand);
        return world.EffectiveCellDigest(Cell);
    }

    /// <summary>A copy of a record with one property set, through its init accessor.</summary>
    private static T With<T>(T record, string property, object? value) where T : class
    {
        var copy = (T)typeof(T).GetMethod("<Clone>$")!.Invoke(record, null)!;
        typeof(T).GetProperty(property)!.SetValue(copy, value);
        return copy;
    }

    private static IEnumerable<PropertyInfo> Persisted(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract");

    [Fact]
    public void EveryPersistedField_MovesItsDigest()
    {
        string baseline = DigestOf(Piece(), Errand());
        var unmoved = new List<string>();
        var covered = new List<string>();

        var piece = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["InstanceId"] = EntityId.Derived(EntityKind.Piece, 2, "unnamed.piece/v1", Owner.Value),
            ["DefId"] = "piece.fixture.wall",
            ["HostCell"] = "r_0_0:c_00_05",
            ["XMm"] = 49_501L,
            ["ZMm"] = 450_501L,
            ["Rotation"] = 1,
            ["Owner"] = OtherOwner,
            ["HealthCurrent"] = 150,
            ["DoorOpen"] = true,
        };
        foreach (var property in Persisted(typeof(PieceRecord)))
        {
            string name = $"PieceRecord.{property.Name}";
            if (Exempt.Contains(name))
                continue;
            Assert.True(piece.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (DigestOf(With(Piece(), property.Name, piece[property.Name]), Errand()) == baseline)
                unmoved.Add(name);
        }

        var errand = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["NpcId"] = "npc.fixture.other",
            ["HostCell"] = "r_0_0:c_00_05",
            ["Phase"] = NpcErrandPhase.ToWork,
            ["PieceId"] = Piece().InstanceId,
            ["WorkOwner"] = OtherOwner,
            ["XMm"] = 41_001L,
            ["ZMm"] = 441_001L,
            ["FacingMdeg"] = 180_001,
            ["StuckTicks"] = 4,
            ["Route"] = NavRoute.None,
        };
        foreach (var property in Persisted(typeof(NpcErrandRecord)))
        {
            string name = $"NpcErrandRecord.{property.Name}";
            if (Exempt.Contains(name))
                continue;
            Assert.True(errand.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (DigestOf(Piece(), With(Errand(), property.Name, errand[property.Name])) == baseline)
                unmoved.Add(name);
        }

        // A route's fields, one at a time, each through a factory: a route cannot be built any other way.
        var goal = new NavPoint(40_000, 440_000);
        var corners = Route().Corners;
        var watch = Route().Watch;
        var routes = new Dictionary<string, NavRoute>(StringComparer.Ordinal)
        {
            ["Status"] = NavRoute.Unreachable(goal, 90, 0xFEDCBA9876543210, watch),
            ["GoalXMm"] = NavRoute.Active(goal with { XMm = 40_001 }, corners, 90, 0xFEDCBA9876543210, watch, false),
            ["GoalZMm"] = NavRoute.Active(goal with { ZMm = 440_001 }, corners, 90, 0xFEDCBA9876543210, watch, false),
            ["Corners"] = NavRoute.Active(goal, corners.SetItem(0, new NavPoint(45_000, 445_001)), 90, 0xFEDCBA9876543210, watch, false),
            ["PlannedTick"] = NavRoute.Active(goal, corners, 91, 0xFEDCBA9876543210, watch, false),
            ["Stamp"] = NavRoute.Active(goal, corners, 90, 0xFEDCBA9876543211, watch, false),
            ["Watch"] = NavRoute.Active(goal, corners, 90, 0xFEDCBA9876543210, watch with { MaxZMm = 470_001 }, false),
            ["Partial"] = NavRoute.Active(goal, corners, 90, 0xFEDCBA9876543210, watch, true),
        };
        foreach (var property in Persisted(typeof(NavRoute)))
        {
            string name = $"NavRoute.{property.Name}";
            Assert.True(routes.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (DigestOf(Piece(), Errand() with { Route = routes[property.Name] }) == baseline)
                unmoved.Add(name);
        }
        foreach (var (x, z, maxX, maxZ) in new[] { (1L, 0L, 0L, 0L), (0L, 1L, 0L, 0L), (0L, 0L, 1L, 0L) })
        {
            var moved = watch with { MinXMm = watch.MinXMm - x, MinZMm = watch.MinZMm - z, MaxXMm = watch.MaxXMm + maxX, MaxZMm = watch.MaxZMm + maxZ };
            if (DigestOf(Piece(), Errand() with { Route = NavRoute.Active(goal, corners, 90, 0xFEDCBA9876543210, moved, false) }) == baseline)
                unmoved.Add($"NavRoute.Watch ({x}, {z}, {maxX}, {maxZ})");
        }

        Assert.True(covered.Count >= 27, $"only {covered.Count} fields were changed");
        Assert.True(unmoved.Count == 0, "these fields change without moving the digest:\n" + string.Join("\n", unmoved));
    }

    [Fact]
    public void PiecesAndErrands_SurviveASnapshot_AndARebuild()
    {
        var world = TestWorlds.NewWorld();
        var piece = Piece() with { DoorOpen = true };
        world.PlacePiece(piece, 5);
        world.SetNpcErrand(Errand());
        var snapshot = world.TakeSnapshot();

        Assert.Equal(5, snapshot.StructureSequence);
        var saved = Assert.Single(snapshot.Pieces);
        Assert.Equal(world.Baseline(Cell).Digest, saved.BaselineHash);
        Assert.Equal(Errand() with { BaselineHash = world.Baseline(Cell).Digest }, Assert.Single(snapshot.NpcErrands));

        var registry = new UNNAMED.EntityRegistry.EntityRegistry();
        var again = WorldDelta.FromSnapshot(world.Generator, world.WorldSeed, registry, snapshot, out var rejected);
        Assert.Empty(rejected);
        Assert.Equal(5, again.StructureSequence);
        Assert.Equal(saved, again.Piece(piece.InstanceId));
        Assert.True(registry.Exists(piece.InstanceId));
        Assert.Equal(Errand() with { BaselineHash = world.Baseline(Cell).Digest }, again.NpcErrand("npc.fixture.smith"));
        Assert.Equal(world.EffectiveCellDigest(Cell), again.EffectiveCellDigest(Cell));
    }
}
