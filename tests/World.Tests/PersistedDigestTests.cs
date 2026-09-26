using System.Collections.Immutable;
using System.Reflection;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
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

    private static readonly EntityId PlayerId = EntityId.Create(EntityKind.Character, 1_700_000_000_000, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7 });

    private static FactionLedger Ledger() => new(4,
        ImmutableArray.Create(
            new ActRecord(1, ActKinds.CreatureKilled, "creature.beast.wolf_grey", "r_0_0:c_00_02", 20_000, 250_000, 4_100),
            new ActRecord(2, ActKinds.SwitchSet, "world.lever.mill_gate", "r_0_0:c_00_07", 50_000, 750_000, 4_200)),
        ImmutableArray.Create(
            new FactionKnowledge("faction.fixture.diggers", 1, Identities.Identified, KnowledgeSources.Reported, "npc.fixture.smith", 4_300, -100)),
        ImmutableArray.Create(new FactionStanding("faction.fixture.diggers", -100)));

    private static CompanionRecord Companion() =>
        new("npc.fixture.warden_sera", CompanionOrder.Follow, CompanionCondition.Up, 148_750, -41_500, 45_000, 64)
        {
            StuckTicks = 2,
            LastCombatTick = 4_950,
            Trail = ImmutableArray.Create(new TrailMark(149_250, -41_000)),
            Route = Route(),
        };

    private static string PlayerDigestOf(FactionLedger ledger, CompanionRecord companion) =>
        new PlayerRecord(PlayerId, "Aelin", 150_250, 12_000, -40_125, 1, Array.Empty<InventoryEntry>(), companions: new[] { companion })
        {
            Factions = ledger,
        }.Digest;

    /// <summary>G12's player half: the ledger's rows and a companion's fields, its route among them, move the player's digest.</summary>
    [Fact]
    public void EveryPersistedPlayerField_MovesThePlayerDigest()
    {
        string baseline = PlayerDigestOf(Ledger(), Companion());
        var unmoved = new List<string>();
        var covered = new List<string>();
        var ledger = Ledger();
        var act = ledger.Acts[1];
        var known = ledger.Knowledge[0];

        var acts = new Dictionary<string, ActRecord>(StringComparer.Ordinal)
        {
            ["Seq"] = act with { Seq = 3 },
            ["Kind"] = act with { Kind = ActKinds.CreatureKilled },
            ["Subject"] = act with { Subject = "world.lever.mill_race" },
            ["CellKey"] = act with { XMm = 150_000, CellKey = "r_0_0:c_01_07" },   // the cell of where it was done: it moves with the position
            ["XMm"] = act with { XMm = 50_001 },
            ["ZMm"] = act with { ZMm = 750_001 },
            ["Tick"] = act with { Tick = 4_201 },
        };
        foreach (var property in Persisted(typeof(ActRecord)))
        {
            string name = $"ActRecord.{property.Name}";
            Assert.True(acts.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (PlayerDigestOf(ledger with { Acts = ledger.Acts.SetItem(1, acts[property.Name]) }, Companion()) == baseline)
                unmoved.Add(name);
        }

        var knowledge = new Dictionary<string, FactionKnowledge>(StringComparer.Ordinal)
        {
            ["Knower"] = known with { Knower = "faction.fixture.keepers" },
            ["Act"] = known with { Act = 2 },
            ["Identity"] = known with { Identity = Identities.Unidentified, Delta = 0 },
            ["Source"] = known with { Source = KnowledgeSources.Witnessed },
            ["Via"] = known with { Via = null },
            ["Tick"] = known with { Tick = 4_301 },
            ["Delta"] = known with { Delta = -99 },
        };
        foreach (var property in Persisted(typeof(FactionKnowledge)))
        {
            string name = $"FactionKnowledge.{property.Name}";
            Assert.True(knowledge.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (PlayerDigestOf(ledger with { Knowledge = ImmutableArray.Create(knowledge[property.Name]) }, Companion()) == baseline)
                unmoved.Add(name);
        }
        if (PlayerDigestOf(ledger with { Knowledge = ImmutableArray.Create(known with { Delta = 0 }) }, Companion())
            == PlayerDigestOf(ledger with { Knowledge = ImmutableArray.Create(known with { Identity = Identities.Unidentified, Delta = 0 }) }, Companion()))
            unmoved.Add("FactionKnowledge.Identity (at delta 0)");

        var standing = new Dictionary<string, FactionStanding>(StringComparer.Ordinal)
        {
            ["FactionId"] = new("faction.fixture.keepers", -100),
            ["Points"] = new("faction.fixture.diggers", -101),
        };
        foreach (var property in Persisted(typeof(FactionStanding)))
        {
            string name = $"FactionStanding.{property.Name}";
            Assert.True(standing.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (PlayerDigestOf(ledger with { Standing = ImmutableArray.Create(standing[property.Name]) }, Companion()) == baseline)
                unmoved.Add(name);
        }

        var ledgers = new Dictionary<string, FactionLedger>(StringComparer.Ordinal)
        {
            ["NextActSeq"] = ledger with { NextActSeq = 5 },
            ["Acts"] = ledger with { Acts = ledger.Acts.RemoveAt(1) },
            ["Knowledge"] = ledger with { Knowledge = ImmutableArray<FactionKnowledge>.Empty },
            ["Standing"] = ledger with { Standing = ImmutableArray<FactionStanding>.Empty },
        };
        foreach (var property in Persisted(typeof(FactionLedger)))
        {
            string name = $"FactionLedger.{property.Name}";
            Assert.True(ledgers.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            if (PlayerDigestOf(ledgers[property.Name], Companion()) == baseline)
                unmoved.Add(name);
        }

        var companion = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["NpcId"] = "npc.fixture.scout",
            ["Order"] = CompanionOrder.Wait,
            ["Condition"] = CompanionCondition.Up,
            ["XMm"] = 148_751L,
            ["ZMm"] = -41_501L,
            ["FacingMdeg"] = 45_001,
            ["Health"] = 63,
            ["DownedTick"] = 0L,
            ["StuckTicks"] = 3,
            ["LastCombatTick"] = 4_951L,
            ["Trail"] = ImmutableArray.Create(new TrailMark(149_250, -41_001)),
            ["Route"] = NavRoute.None,
        };
        foreach (var property in Persisted(typeof(CompanionRecord)))
        {
            string name = $"CompanionRecord.{property.Name}";
            Assert.True(companion.ContainsKey(property.Name), $"{name} has no change in this test: add one, or name it exempt");
            covered.Add(name);
            // The record ties a condition to its health and downed tick, so it changes with them; a downed tick moves from one downed record.
            var downed = Companion() with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 4_990 };
            bool moved = property.Name switch
            {
                "Condition" => PlayerDigestOf(Ledger(), downed) != baseline,
                "DownedTick" => PlayerDigestOf(Ledger(), downed with { DownedTick = 4_991 }) != PlayerDigestOf(Ledger(), downed),
                _ => PlayerDigestOf(Ledger(), With(Companion(), property.Name, companion[property.Name])) != baseline,
            };
            if (!moved)
                unmoved.Add(name);
        }

        Assert.True(covered.Count >= 30, $"only {covered.Count} fields were changed");
        Assert.True(unmoved.Count == 0, "these fields change without moving the player's digest:\n" + string.Join("\n", unmoved));
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
