// UNNAMED World - building v1 (M7 design §4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>Building as the running world uses it: the piece catalogue and building's numbers. Built by <c>BuildingContent.Build</c>.</summary>
public sealed record BuildingSetup(BuildingCatalog Catalog, BuildingConstants Constants)
{
    /// <summary>No pieces, and the shipped numbers.</summary>
    public static BuildingSetup Empty { get; } = new(BuildingCatalog.Empty, BuildingConstants.Default);
}

/// <summary>Ground building keeps clear (M7 design §4.16), and what it is kept clear for, as a refusal names it.</summary>
public sealed record ProtectedZone(string What, Blocker Shape);

/// <summary>
/// The protected zones of a region (M7 design §4.16), one reading shared by the placement check (check 11) and the content lint that
/// keeps build areas clear (BLD007): the spawn, each authored NPC's place, each authored container, station and node, each door, switch
/// structure and barrier grown by a margin, and each spawner's disc and patrol legs. A piece's bounds may not strictly meet one.
/// </summary>
public static class ProtectedZones
{
    /// <summary>A patrol leg is sampled this often, its ends included.</summary>
    public const long PatrolSampleMm = 500;

    /// <param name="creatureRadiusMm">A creature definition's body radius.</param>
    /// <param name="nameOf">A definition's display name, for an NPC's place.</param>
    public static ImmutableArray<ProtectedZone> Of(RegionLayout layout, IEnumerable<SpawnSite> spawns, Func<string, long> creatureRadiusMm,
        BuildingProtection protection, Func<string, string> nameOf)
    {
        var zones = new List<ProtectedZone>
        {
            new("the spawn point", Circle("zone.spawn", layout.Spawn.XMm, layout.Spawn.ZMm, protection.SpawnPointMm)),
        };
        zones.AddRange(layout.Npcs.Select(n => new ProtectedZone($"{nameOf(n.NpcId)}'s place", Circle(n.NpcId, n.XMm, n.ZMm, protection.NpcSiteMm))));
        zones.AddRange(layout.Containers.Select(c => new ProtectedZone(c.Key, Circle(c.Key, c.XMm, c.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Stations.Select(s => new ProtectedZone(s.Key, Circle(s.Key, s.XMm, s.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Nodes.Select(n => new ProtectedZone(n.Name, Circle(n.Name, n.XMm, n.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Doors.Select(d => new ProtectedZone(d.Key, Grown(d.ClosedFootprint, protection.StructureMarginMm))));
        zones.AddRange(layout.Switches.Select(s => new ProtectedZone(s.Key, Grown(s.Body, protection.StructureMarginMm))));
        zones.AddRange(layout.Barriers.Select(b => new ProtectedZone(b.Key, Grown(b.Footprint, protection.StructureMarginMm))));
        foreach (var site in spawns)
        {
            long radius = site.RadiusMm + (site.Members.IsEmpty ? 0 : site.Members.Max(m => creatureRadiusMm(m.CreatureId))) + protection.SpawnerMarginMm;
            zones.Add(new ProtectedZone(site.Key, Circle(site.Key, site.XMm, site.ZMm, radius)));
            for (int leg = 0; leg + 1 < site.Route.Length; leg++)
            {
                var (ax, az) = site.Route[leg];
                var (bx, bz) = site.Route[leg + 1];
                long dx = bx - ax, dz = bz - az;
                long steps = Math.Max(1, (long)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dz * dz) / PatrolSampleMm));
                for (long i = 0; i <= steps; i++)
                    zones.Add(new ProtectedZone(site.Key, Circle(site.Key, ax + dx * i / steps, az + dz * i / steps, radius)));
            }
        }
        return zones.ToImmutableArray();
    }

    /// <summary>The first zone a box strictly meets, or null.</summary>
    public static ProtectedZone? Met(ImmutableArray<ProtectedZone> zones, BoundsMm bounds) =>
        zones.FirstOrDefault(z => BuildingMath.Overlaps(bounds, z.Shape));

    private static CircleBlocker Circle(string id, long xMm, long zMm, long radiusMm) => new(id, xMm, zMm, radiusMm, 0);

    private static Blocker Grown(Blocker shape, long marginMm) => shape switch
    {
        BoxBlocker b => new BoxBlocker(b.Id, b.MinXMm - marginMm, b.MinZMm - marginMm, b.MaxXMm + marginMm, b.MaxZMm + marginMm, b.HeightMm),
        CircleBlocker c => new CircleBlocker(c.Id, c.CenterXMm, c.CenterZMm, c.RadiusMm + marginMm, c.HeightMm),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape.GetType().Name, "Unknown blocker shape"),
    };
}

// ── commands, events and views ──────────────────────────────────────────────

/// <summary>Place a piece at a discrete pose on the lattice (M7 design §4.5): no aim point, camera ray or tolerance.</summary>
public sealed record PlacePieceCommand(EntityId Actor, string PieceDefId, long XMm, long ZMm, int Rotation) : GameCommand(Actor);

/// <summary>Take down a piece the actor owns (M7 design §4.13), for half its cost back.</summary>
public sealed record DismantlePieceCommand(EntityId Actor, EntityId PieceId) : GameCommand(Actor);

/// <summary>Mend a piece the actor owns to full health (M7 design §4.12), for its cost scaled by the health missing.</summary>
public sealed record RepairPieceCommand(EntityId Actor, EntityId PieceId) : GameCommand(Actor);

/// <summary>A blow on a piece (M7 design §4.12): so much off its health, from the one damage rule's source. At 0 it is destroyed.</summary>
internal sealed record DamagePiece(EntityId PieceId, int Amount, string Source) : InternalCommand;

/// <summary>
/// Open or shut a placed door (M7 design §4.9): the player's toggle (<paramref name="Open"/> null), or an NPC's open. The operator is the
/// player's or the NPC's instance ID; <paramref name="OperatorNpcId"/> names the NPC, when it is one.
/// </summary>
internal sealed record OperatePieceDoor(EntityId PieceId, EntityId Operator, string? OperatorNpcId, bool? Open) : InternalCommand;

/// <summary>The fifteen placement checks (M7 design §4.5), in the order they run.</summary>
public enum PlacementRule
{
    Actor, Definition, Rotation, Lattice, BuildArea, Reach, Slot, Support, Terrain, Authored, Protected, Bodies, PieceCap, Materials, Navigability,
}

/// <summary>What check 15 said: nothing to check (pads and roofs), not checked, proven, or refused.</summary>
public enum NavVerdict { NotApplicable, NotChecked, Proven, Refused }

/// <summary>How the structures changed.</summary>
public enum StructureChangeKind { Placed, Dismantled, Destroyed }

/// <summary>One cost line as the ghost shows it: so many of an item needed, and how many are carried.</summary>
public sealed record CostView(string ItemId, int Count, int Carried);

/// <summary>
/// What placing a piece here would do (M7 design §4.6): allowed, or the first rule it fails and why; the piece's bounds and parts; its
/// cost against what is carried; and navigability. Advisory - the command may still refuse.
/// </summary>
public sealed record PlacementPreview(bool Allowed, PlacementRule? Failed, string? Reason, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm,
    ImmutableArray<BoxBlocker> Parts, ImmutableArray<CostView> Cost, NavVerdict Navigability);

/// <summary>A blocking part of a placed piece, in world millimetres.</summary>
public sealed record PiecePartView(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long HeightMm, TraversalClass Traversal);

/// <summary>
/// A placed piece as presentation reads it (M7 design §4.23): its definition and pose, owner, health, door, worker, bounds and parts,
/// and its chest and station keys where it has them.
/// </summary>
public sealed record PieceView(EntityId Id, string DefId, PieceFamily Family, long XMm, long ZMm, int Rotation, EntityId Owner, int HealthCurrent,
    int HealthMax, bool DoorOpen, string? WorkerNpcId, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, ImmutableArray<PiecePartView> Parts,
    string? ContainerKey, string? StationKey);

/// <summary>A line of the load audit (M7 design §4.19): a piece, or an errand, and what is wrong with it now.</summary>
public sealed record StructureConflict(string Subject, string Problem);

/// <summary>A piece was placed.</summary>
public sealed record PiecePlaced(EntityId PieceId, string DefId, long XMm, long ZMm, int Rotation, EntityId Owner, long Revision, long Tick);

/// <summary>A piece was taken down, and what came back.</summary>
public sealed record PieceRemoved(EntityId PieceId, string DefId, EntityId Actor, ImmutableArray<CostView> Refund, long Revision, long Tick);

/// <summary>A piece lost health to a blow and stands; <see cref="HealthNow"/> is what it has left.</summary>
public sealed record PieceDamaged(EntityId PieceId, string DefId, int Amount, int HealthNow, string Source, long Tick);

/// <summary>A piece's health reached 0 and it is gone, with nothing back.</summary>
public sealed record PieceDestroyed(EntityId PieceId, string DefId, string Source, long Revision, long Tick);

/// <summary>A piece was mended from one health to its maximum.</summary>
public sealed record PieceRepaired(EntityId PieceId, int From, int To, long Tick);

/// <summary>The structures changed over this rectangle; <see cref="Revision"/> is the structure sequence now.</summary>
public sealed record StructuresChanged(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, StructureChangeKind Kind, long Revision, long Tick);

/// <summary>
/// The socket index (M7 design §4.4): which intact piece holds each slot key, and how many intact providers offer each socket. Asked
/// only for a count, so no iteration order decides anything.
/// </summary>
internal sealed record StructureIndex(ImmutableSortedDictionary<string, EntityId> Slots,
    ImmutableDictionary<(SocketType Type, long XMm, long ZMm, SocketAxis Axis), int> Providers)
{
    public static StructureIndex Empty { get; } = new(ImmutableSortedDictionary.Create<string, EntityId>(StringComparer.Ordinal),
        ImmutableDictionary<(SocketType, long, long, SocketAxis), int>.Empty);
}

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Structures"/> - the pieces the player places (M7 design §4): their rows and the structure sequence, and
/// what is derived from them (the collision space, the closed door leaves, the socket index, the footprints navigation reads, and the
/// load audit). It has no tick. Placing and taking down commit at a tick boundary; every change bumps the sequence by one. It records
/// no faction act and reads no faction state.
/// </summary>
internal sealed class BuildingSystem
{
    private const string PieceTag = "unnamed.piece/v1";

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly NavigationSystem _navigation;

    public BuildingSystem(SystemContext context, SliceOwner owner, EntityId player, NavigationSystem navigation)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _navigation = navigation;
    }

    private RuntimeState State => _context.State;
    private BuildingCatalog Catalog => _context.Setup.Building.Catalog;

    public string? Handle(PlacePieceCommand command, long tick)
    {
        var check = BuildingRules.Validate(new PlacementContext(_context, _navigation, _player, command.Actor, _navigation.Scratch, _navigation.Counters, true),
            command.PieceDefId, command.XMm, command.ZMm, command.Rotation);
        if (!check.Allowed)
            return check.Reason;
        // The spend is the one step that can still refuse, so it goes first: nothing is built on a failed spend.
        if (_context.Dispatch(new ExchangeItems(check.Takes, null, 0, 0)) is { } refused)
            return refused;
        var piece = Catalog.Find(command.PieceDefId)!;
        long sequence = State.World.StructureSequence + 1;
        var id = EntityId.Derived(EntityKind.Piece, sequence, PieceTag, command.Actor.Value);
        State.PlacePiece(_owner, new PieceRecord(id, piece.Id, HostOf(command.XMm, command.ZMm), command.XMm, command.ZMm, command.Rotation,
            command.Actor, piece.HealthMax), sequence);
        Rebuild();
        if (!check.Parts.IsEmpty)
            _context.Dispatch(new RebuildNavigation(UnionOf(check.Parts), StructureChangeKind.Placed, sequence));
        _context.Events.Publish(new PiecePlaced(id, piece.Id, command.XMm, command.ZMm, command.Rotation, command.Actor, sequence, tick));
        var b = check.Bounds;
        _context.Events.Publish(new StructuresChanged(b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, StructureChangeKind.Placed, sequence, tick));
        return null;
    }

    public string? Handle(DismantlePieceCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.World.Piece(command.PieceId) is not { } row || Catalog.Find(row.DefId) is not { } piece)
            return "there is no such piece";
        if (row.Owner != command.Actor)
            return "that is not yours to take down";
        if (OutOfReach(row, piece) is { } far)
            return far;
        if (Dependents(row, piece) is { } dependents)
            return dependents;
        if (piece.Container is not null && State.World.Container(WorldDelta.PieceChestKey(row.InstanceId)) is { Items.IsEmpty: false })
            return "empty the chest first";
        RemoveCore(row, piece, StructureChangeKind.Dismantled, command.Actor, null, tick);
        return null;
    }

    /// <summary>
    /// A piece mended to full health (M7 design §4.12), checked in order: the actor; an intact piece; theirs; within building reach; not
    /// whole already; and the materials, each cost line scaled by the health missing. One command, one cost: no partial repair.
    /// </summary>
    public string? Handle(RepairPieceCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.World.Piece(command.PieceId) is not { } row || Catalog.Find(row.DefId) is not { } piece)
            return "there is no such piece";
        if (row.Owner != command.Actor)
            return "that is not yours to mend";
        if (OutOfReach(row, piece) is { } far)
            return far;
        if (row.HealthCurrent >= piece.HealthMax)
            return "it needs no repair";
        int missing = piece.HealthMax - row.HealthCurrent, percent = _context.Setup.Building.Constants.RepairCostPercent;
        var cost = piece.Cost.Select(c => c with { Count = BuildingMath.RepairCost(c.Count, missing, piece.HealthMax, percent) })
            .Where(c => c.Count > 0).ToImmutableArray();
        var takes = BuildingRules.Takes(_context, cost, out string? lacking);
        if (lacking is not null)
            return lacking;
        if (!takes.IsEmpty && _context.Dispatch(new ExchangeItems(takes, null, 0, 0)) is { } refused)
            return refused;
        State.SetPiece(_owner, row with { HealthCurrent = piece.HealthMax });
        _context.Events.Publish(new PieceRepaired(row.InstanceId, row.HealthCurrent, piece.HealthMax, tick));
        return null;
    }

    /// <summary>
    /// A blow on a piece (M7 design §4.12): its amount off the piece's health; at 0 the piece is destroyed (§4.13). Damage that leaves it
    /// standing changes no footprint: the sequence and navigation stay as they are.
    /// </summary>
    public string? Handle(DamagePiece command, long tick)
    {
        if (State.World.Piece(command.PieceId) is not { } row || Catalog.Find(row.DefId) is not { } piece)
            return "there is no such piece";
        if (command.Amount < 1)
            return "no damage";
        int to = Math.Max(0, row.HealthCurrent - command.Amount);
        if (to > 0)
        {
            State.SetPiece(_owner, row with { HealthCurrent = to });
            _context.Events.Publish(new PieceDamaged(row.InstanceId, row.DefId, command.Amount, to, command.Source, tick));
            return null;
        }
        // A destroyed doorway destroys its door first (L9), each with a removal of its own. No other cascade exists.
        if (piece.Family == PieceFamily.Doorway
            && State.StructureIndex.Slots.TryGetValue(Lattice.SlotKey(PieceSlot.Door, row.XMm, row.ZMm, row.Rotation), out var doorId)
            && State.World.Piece(doorId) is { } door && Catalog.Find(door.DefId) is { } doorPiece)
            RemoveCore(door, doorPiece, StructureChangeKind.Destroyed, null, command.Source, tick);
        RemoveCore(row, piece, StructureChangeKind.Destroyed, null, command.Source, tick);
        return null;
    }

    /// <summary>Building reach (check 6's rule) from the body to a piece's bounds, or null when it is within it.</summary>
    private string? OutOfReach(PieceRecord row, PieceDefinition piece)
    {
        var bounds = BuildingMath.WorldBounds(piece, row.XMm, row.ZMm, row.Rotation);
        long reach = _context.Setup.Building.Constants.PlaceReachMm;
        long d2 = BuildingMath.DistanceSquared(bounds, State.Body.XMm, State.Body.ZMm);
        return d2 > reach * reach
            ? string.Create(CultureInfo.InvariantCulture, $"that is {Math.Sqrt(d2) / 1000:0.00} m away; building reach is {reach / 1000.0:0.00} m")
            : null;
    }

    /// <summary>
    /// A door opened or shut (M7 design §4.9), checked in order: an intact door; one the operator may work; within reach of their body;
    /// never shut on a body in the doorway; and asking for the state it is in already changes nothing. Only the closed-leaf cache follows
    /// it: navigation reads a door's state when it plans, and the structure sequence does not move.
    /// </summary>
    public string? Handle(OperatePieceDoor command, long tick)
    {
        if (State.World.Piece(command.PieceId) is not { } row || Catalog.Find(row.DefId) is not { Family: PieceFamily.Door } piece)
            return "there is no such door";
        if (!BuildingRules.CanOperate(command.Operator, command.OperatorNpcId, row, State, _player))
            return "that door is not yours";
        var body = command.OperatorNpcId is { } npc && State.Npcs.TryGetValue(npc, out var npcState) ? npcState.Body : State.Body;
        var closed = BuildingMath.WorldBounds(piece, row.XMm, row.ZMm, row.Rotation);
        var box = new BoxBlocker(row.InstanceId.Value, closed.MinXMm, closed.MinZMm, closed.MaxXMm, closed.MaxZMm, 0);
        long reach = _context.Setup.Movement.InteractReachMm;
        double distance = box.DistanceTo(body.XMm, body.ZMm);
        if (distance > reach)
            return string.Create(CultureInfo.InvariantCulture, $"the door is {distance / 1000:0.00} m away; reach is {reach / 1000.0:0.00} m");
        bool open = command.Open ?? !row.DoorOpen;
        if (!open && _context.BodyIn(box))
            return "the door cannot close: something is in the doorway";
        if (open == row.DoorOpen)
            return null;
        State.SetPiece(_owner, row with { DoorOpen = open });
        State.SetClosedPieceLeaves(_owner, ClosedLeaves());
        _context.Events.Publish(new DoorToggled(command.Operator, row.InstanceId.Value, open, tick));
        return null;
    }

    /// <summary>
    /// Why a piece cannot come down yet, or null: a doorway holds its door; a pad holds the furniture on its square and every wall or
    /// doorway on its edges with no pad on the other side. Removal only ever opens the way, so nothing else depends on anything.
    /// </summary>
    private string? Dependents(PieceRecord row, PieceDefinition piece)
    {
        var slots = State.StructureIndex.Slots;
        if (piece.Family == PieceFamily.Doorway)
            return slots.ContainsKey(Lattice.SlotKey(PieceSlot.Door, row.XMm, row.ZMm, row.Rotation)) ? "take the door down first" : null;
        if (piece.Family != PieceFamily.Pad)
            return null;
        if (slots.ContainsKey(Lattice.SlotKey(PieceSlot.Furniture, row.XMm, row.ZMm, 0)))
            return "take down what stands on it first";
        foreach (var edge in Lattice.EdgesOfSquare(row.XMm, row.ZMm))
        {
            if (!slots.ContainsKey(Lattice.EdgeKey(edge.XMm, edge.ZMm, edge.Rotation)))
                continue;
            var other = Lattice.SquaresBesideEdge(edge.XMm, edge.ZMm, edge.Rotation).Single(s => s != (row.XMm, row.ZMm));
            if (!slots.ContainsKey(Lattice.SlotKey(PieceSlot.Square, other.XMm, other.ZMm, 0)))
                return "take down what stands on it first";
        }
        return null;
    }

    /// <summary>
    /// A piece leaves the world (M7 design §4.13), taken down by <paramref name="actor"/> or destroyed by <paramref name="source"/>: the
    /// row goes and its identity retires, the sequence moves on, what is derived is rebuilt, and - taken down - half its cost comes back.
    /// </summary>
    private void RemoveCore(PieceRecord row, PieceDefinition piece, StructureChangeKind kind, EntityId? actor, string? source, long tick)
    {
        // A chest's record goes with it: taken down, it is empty by then and is discarded; destroyed, what it held falls where it stood.
        string chest = WorldDelta.PieceChestKey(row.InstanceId);
        if (piece.Container is not null && State.World.Container(chest) is not null)
        {
            if (kind == StructureChangeKind.Destroyed)
                _context.Dispatch(new SpillContainer(chest));
            else
                _context.Dispatch(new DiscardContainer(chest));
        }
        long sequence = State.World.StructureSequence + 1;
        State.RemovePiece(_owner, row.InstanceId, sequence);
        Rebuild();
        var parts = BuildingMath.WorldParts(piece, row.XMm, row.ZMm, row.Rotation, row.InstanceId.Value);
        if (!parts.IsEmpty)
            _context.Dispatch(new RebuildNavigation(UnionOf(parts), kind, sequence));
        int percent = _context.Setup.Building.Constants.RefundPercent;
        var refund = kind == StructureChangeKind.Dismantled
            ? piece.Cost.Select(c => new CostView(c.ItemId, BuildingMath.Refund(c.Count, percent), 0)).Where(c => c.Count > 0).ToImmutableArray()
            : ImmutableArray<CostView>.Empty;
        if (kind == StructureChangeKind.Destroyed)
            _context.Events.Publish(new PieceDestroyed(row.InstanceId, row.DefId, source!, sequence, tick));
        else
            _context.Events.Publish(new PieceRemoved(row.InstanceId, row.DefId, actor!, refund, sequence, tick));
        var b = BuildingMath.WorldBounds(piece, row.XMm, row.ZMm, row.Rotation);
        _context.Events.Publish(new StructuresChanged(b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, kind, sequence, tick));
        foreach (var line in refund)
            _context.Dispatch(new GrantItem(line.ItemId, line.Count, Quality.Standard));
    }

    /// <summary>
    /// World start: what the saved pieces derive, and the load audit - pieces that no longer fit the lattice, an area, the authored
    /// ground or their support are kept and reported, never silently dropped; health above a retuned maximum is clamped. Publishes nothing.
    /// </summary>
    public void Populate()
    {
        var lines = new List<StructureConflict>();
        foreach (var row in State.World.Pieces.OrderBy(p => p.InstanceId.Value, StringComparer.Ordinal))
        {
            if (Catalog.Find(row.DefId) is not { } piece)
            {
                lines.Add(new StructureConflict(row.InstanceId.Value, $"{row.DefId} is not a piece this build knows"));
                continue;
            }
            if (row.HealthCurrent > piece.HealthMax)
            {
                lines.Add(new StructureConflict(row.InstanceId.Value, $"health {row.HealthCurrent} is above {piece.Name}'s {piece.HealthMax}: clamped"));
                State.SetPiece(_owner, row with { HealthCurrent = piece.HealthMax });
            }
            if (row.DoorOpen && piece.Family != PieceFamily.Door)
                lines.Add(new StructureConflict(row.InstanceId.Value, $"a {BuildingKeys.Key(piece.Family)} is not a door: its door_open is read as shut"));
        }
        Rebuild();

        var layout = _context.Setup.Layout;
        var zones = BuildingRules.Zones(_context);
        var index = State.StructureIndex;
        foreach (var row in State.World.Pieces.OrderBy(p => p.InstanceId.Value, StringComparer.Ordinal))
        {
            if (Catalog.Find(row.DefId) is not { } piece)
                continue;
            string subject = row.InstanceId.Value;
            var bounds = BuildingMath.WorldBounds(piece, row.XMm, row.ZMm, row.Rotation);
            var ground = Lattice.Footprint(piece.Slot, row.XMm, row.ZMm, row.Rotation);
            if (!Lattice.Fits(piece.Slot, row.XMm, row.ZMm, row.Rotation))
                lines.Add(new StructureConflict(subject, "it is off the building grid"));
            if (!layout.BuildAreas.Any(a => BuildingRules.Box(a).Contains(ground)))
                lines.Add(new StructureConflict(subject, "it stands outside every build area"));
            if (BuildingRules.Authored(layout).FirstOrDefault(b => BuildingMath.Overlaps(bounds, b)) is { } authored)
                lines.Add(new StructureConflict(subject, $"it stands over {authored.Id}"));
            if (ProtectedZones.Met(zones, bounds) is { } zone)
                lines.Add(new StructureConflict(subject, $"it stands on ground kept clear ({zone.What})"));
            bool supported = BuildingMath.WorldSockets(piece, row.XMm, row.ZMm, row.Rotation).Where(s => BuildingMath.IsMount(s.Type))
                .All(m => index.Providers.GetValueOrDefault((BuildingMath.ProviderFor(m.Type), m.XMm, m.ZMm, m.Axis)) > 0);
            if (piece.Family == PieceFamily.Roof)
                supported = Lattice.RoofSupported(row.XMm, row.ZMm,
                    edge => index.Slots.TryGetValue(edge, out var id) && State.World.Piece(id) is { } r && Catalog.Find(r.DefId)?.SupportsRoof == true,
                    index.Slots.ContainsKey);
            if (!supported)
                lines.Add(new StructureConflict(subject, "nothing holds it up now"));
        }
        State.SetStructureAudit(_owner, lines.ToImmutableArray());
    }

    /// <summary>
    /// Everything derived from the rows, in <see cref="StructureOrder"/> so it depends on which pieces stand, never on the order they
    /// went up (G10): the collision space (authored blockers, then every solid part), the closed door leaves, the socket index and the
    /// footprints navigation reads.
    /// </summary>
    private void Rebuild()
    {
        var solids = new List<BoxBlocker>();
        var footprints = new List<NavFootprint>();
        var slots = ImmutableSortedDictionary.CreateBuilder<string, EntityId>(StringComparer.Ordinal);
        var providers = new Dictionary<(SocketType, long, long, SocketAxis), int>();
        foreach (var row in State.World.Pieces)
        {
            if (Catalog.Find(row.DefId) is not { } piece)
                continue;
            slots[Lattice.SlotKey(piece.Slot, row.XMm, row.ZMm, row.Rotation)] = row.InstanceId;
            foreach (var socket in BuildingMath.WorldSockets(piece, row.XMm, row.ZMm, row.Rotation).Where(s => !BuildingMath.IsMount(s.Type)))
            {
                var key = (socket.Type, socket.XMm, socket.ZMm, socket.Axis);
                providers[key] = providers.GetValueOrDefault(key) + 1;
            }
            var parts = BuildingMath.WorldParts(piece, row.XMm, row.ZMm, row.Rotation, row.InstanceId.Value);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var traversal = piece.Parts[i].Traversal;
                footprints.Add(new NavFootprint(row.InstanceId, i, part.MinXMm, part.MinZMm, part.MaxXMm, part.MaxZMm, part.HeightMm, traversal, row.Owner));
                if (traversal == TraversalClass.Solid)
                    solids.Add(part);
            }
        }
        solids.Sort(StructureOrder.Instance);
        footprints.Sort(StructureOrder.Instance);
        var authored = _context.Setup.Layout.Space;
        var space = solids.Count == 0 ? authored : authored with { Blockers = authored.Blockers.AddRange(solids) };
        State.SetStructureDerived(_owner, space, ClosedLeaves(),
            new StructureIndex(slots.ToImmutable(), providers.ToImmutableDictionary()), footprints.ToImmutableArray());
    }

    /// <summary>The door parts of the doors standing shut, in <see cref="StructureOrder"/>: what <c>ClosedDoors()</c> appends.</summary>
    private ImmutableArray<Blocker> ClosedLeaves()
    {
        var leaves = new List<BoxBlocker>();
        foreach (var row in State.World.Pieces.Where(p => !p.DoorOpen))
        {
            if (Catalog.Find(row.DefId) is not { } piece)
                continue;
            var parts = BuildingMath.WorldParts(piece, row.XMm, row.ZMm, row.Rotation, row.InstanceId.Value);
            for (int i = 0; i < parts.Length; i++)
            {
                if (piece.Parts[i].Traversal == TraversalClass.Door)
                    leaves.Add(parts[i]);
            }
        }
        leaves.Sort(StructureOrder.Instance);
        return leaves.ToImmutableArray<Blocker>();
    }

    /// <summary>Every placed piece, by ID.</summary>
    public ImmutableArray<PieceView> Views() =>
        State.World.Pieces.OrderBy(p => p.InstanceId.Value, StringComparer.Ordinal).Select(row =>
        {
            var piece = Catalog.Find(row.DefId);
            var bounds = piece is null ? new BoundsMm(row.XMm, row.ZMm, row.XMm, row.ZMm) : BuildingMath.WorldBounds(piece, row.XMm, row.ZMm, row.Rotation);
            var parts = piece is null
                ? ImmutableArray<PiecePartView>.Empty
                : BuildingMath.WorldParts(piece, row.XMm, row.ZMm, row.Rotation, "part")
                    .Select((p, i) => new PiecePartView(p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm, p.HeightMm, piece.Parts[i].Traversal)).ToImmutableArray();
            return new PieceView(row.InstanceId, row.DefId, piece?.Family ?? PieceFamily.Pad, row.XMm, row.ZMm, row.Rotation, row.Owner, row.HealthCurrent,
                piece?.HealthMax ?? row.HealthCurrent, row.DoorOpen && piece?.Family == PieceFamily.Door, null, bounds.MinXMm, bounds.MinZMm, bounds.MaxXMm,
                bounds.MaxZMm, parts, piece?.Container is null ? null : WorldDelta.PieceChestKey(row.InstanceId),
                piece?.Station is null ? null : SystemContext.PieceStationKey(row.InstanceId));
        }).ToImmutableArray();

    /// <summary>The union of a piece's parts, not inflated: what navigation restamps around.</summary>
    private static NavRect UnionOf(ImmutableArray<BoxBlocker> parts) =>
        new(parts.Min(p => p.MinXMm), parts.Min(p => p.MinZMm), parts.Max(p => p.MaxXMm), parts.Max(p => p.MaxZMm));

    /// <summary>The cell a piece's anchor stands in: its row's host.</summary>
    private static string HostOf(long xMm, long zMm) => CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0).ToString();
}
