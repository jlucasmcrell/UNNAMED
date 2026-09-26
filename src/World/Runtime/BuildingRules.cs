// UNNAMED World - the placement rules (M7 design §4.5, §4.6)
// No Godot references - pure C#. Read-only: nothing here dispatches, publishes or writes (G7).

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// What a placement is judged with: the world, navigation with the scratch and counter sink to plan on (the authoritative ones for a
/// command, the preview's own and none for the ghost), who the player is, who asks, and whether navigability is to be checked.
/// </summary>
internal sealed record PlacementContext(SystemContext Context, NavigationSystem Navigation, EntityId Player, EntityId Actor, NavScratch Scratch,
    NavCounterSink? Counters, bool CheckNavigability);

/// <summary>A placement judged: allowed or the first rule it fails and why, the piece's bounds and parts, the stacks it would spend, and navigability.</summary>
internal sealed record PlacementCheck(bool Allowed, PlacementRule? Failed, string? Reason, BoundsMm Bounds, ImmutableArray<BoxBlocker> Parts,
    ImmutableArray<StackTake> Takes, NavVerdict Navigability);

/// <summary>
/// The fifteen placement checks (M7 design §4.5), in order; the first failure is returned. One function for the command and the ghost,
/// so a preview at equal state answers as the command would (§4.6). Check 15, navigability (§3.13), runs only for a piece with a solid
/// part or a door: <see cref="NavVerdict.NotApplicable"/> for pads and roofs, <see cref="NavVerdict.NotChecked"/> when the ghost does
/// not ask it, otherwise proven or refused.
/// </summary>
internal static class BuildingRules
{
    public static PlacementCheck Validate(PlacementContext ctx, string defId, long xMm, long zMm, int rotation)
    {
        var context = ctx.Context;
        var state = context.State;
        var setup = context.Setup;
        var building = setup.Building;
        var none = ImmutableArray<BoxBlocker>.Empty;
        PlacementCheck Refuse(PlacementRule rule, string reason, BoundsMm bounds, ImmutableArray<BoxBlocker> parts, NavVerdict verdict) =>
            new(false, rule, reason, bounds, parts, ImmutableArray<StackTake>.Empty, verdict);

        // 1-3: who asks, what, and which way.
        if (ctx.Actor != ctx.Player)
            return Refuse(PlacementRule.Actor, $"unknown actor {ctx.Actor}", default, none, NavVerdict.NotChecked);
        if (state.PlayerCombat.Defeated)
            return Refuse(PlacementRule.Actor, "dead", default, none, NavVerdict.NotChecked);
        if (building.Catalog.Find(defId) is not { } piece)
            return Refuse(PlacementRule.Definition, $"{defId} is not a piece this build knows", default, none, NavVerdict.NotChecked);
        var verdict = piece.Parts.IsEmpty ? NavVerdict.NotApplicable : NavVerdict.NotChecked;
        if (rotation is < 0 or > 3 || !piece.Rotations.Contains(rotation))
            return Refuse(PlacementRule.Rotation, $"{piece.Name} cannot be turned that way", default, none, verdict);
        var bounds = BuildingMath.WorldBounds(piece, xMm, zMm, rotation);
        var parts = BuildingMath.WorldParts(piece, xMm, zMm, rotation, "candidate");
        var index = state.StructureIndex;

        // 4. On the lattice; a door only where a doorway stands.
        if (!Lattice.Fits(piece.Slot, xMm, zMm, rotation)
            || (piece.Slot == PieceSlot.Door && Occupant(context, Lattice.EdgeKey(xMm, zMm, rotation))?.Family != PieceFamily.Doorway))
            return Refuse(PlacementRule.Lattice, "that is not on the building grid", bounds, parts, verdict);

        // 5. Inside a build area, closed intervals.
        var ground = Lattice.Footprint(piece.Slot, xMm, zMm, rotation);
        if (setup.Layout.BuildAreas.FirstOrDefault(a => Box(a).Contains(ground)) is not { } area)
            return Refuse(PlacementRule.BuildArea, "you may only build inside a build area", bounds, parts, verdict);

        // 6. Within reach of the body.
        var body = state.Body;
        long reach = building.Constants.PlaceReachMm;
        long d2 = BuildingMath.DistanceSquared(bounds, body.XMm, body.ZMm);
        if (d2 > reach * reach)
            return Refuse(PlacementRule.Reach, string.Create(CultureInfo.InvariantCulture,
                $"that is {Math.Sqrt(d2) / 1000:0.00} m away; building reach is {reach / 1000.0:0.00} m"), bounds, parts, verdict);

        // 7. The slot is free.
        if (index.Slots.TryGetValue(Lattice.SlotKey(piece.Slot, xMm, zMm, rotation), out var holder))
        {
            return Refuse(PlacementRule.Slot, piece.Slot == PieceSlot.Door
                ? "that doorway already has a door"
                : $"a {Definition(context, holder)?.Name ?? "piece"} already stands there", bounds, parts, verdict);
        }

        // 8. Supported: every mount on a provider; a roof on a wall, or beside a roof that is on one (depth one, evaluated now).
        foreach (var mount in BuildingMath.WorldSockets(piece, xMm, zMm, rotation).Where(s => BuildingMath.IsMount(s.Type)))
        {
            if (index.Providers.GetValueOrDefault((BuildingMath.ProviderFor(mount.Type), mount.XMm, mount.ZMm, mount.Axis)) == 0)
                return Refuse(PlacementRule.Support, Unsupported(piece, mount.Type), bounds, parts, verdict);
        }
        if (piece.Family == PieceFamily.Roof && !Lattice.RoofSupported(xMm, zMm,
                edge => index.Slots.TryGetValue(edge, out var id) && Definition(context, id)?.SupportsRoof == true, index.Slots.ContainsKey))
            return Refuse(PlacementRule.Support, "a roof needs a wall under one of its edges, or a roofed neighbour that has one", bounds, parts, verdict);

        // 9. A pad needs even ground.
        if (piece.Family == PieceFamily.Pad)
        {
            long relief = BuildingMath.Relief(setup.Layout.Space.Terrain, xMm, zMm);
            long allowed = building.Constants.PadMaxReliefMm;
            if (relief > allowed)
                return Refuse(PlacementRule.Terrain, string.Create(CultureInfo.InvariantCulture,
                    $"the ground here is too uneven for a pad: {relief / 1000.0:0.00} m of rise, {allowed / 1000.0:0.00} m allowed"), bounds, parts, verdict);
        }

        // 10-11. Clear of what the region authored, and of the ground it keeps clear.
        if (Authored(setup.Layout).Any(b => BuildingMath.Overlaps(bounds, b)))
            return Refuse(PlacementRule.Authored, "that would build over something already standing there", bounds, parts, verdict);
        if (ProtectedZones.Met(Zones(context), bounds) is { } zone)
            return Refuse(PlacementRule.Protected, $"that ground is kept clear ({zone.What})", bounds, parts, verdict);

        // 12. Nobody stands where a part would.
        if (parts.Any(context.BodyIn))
            return Refuse(PlacementRule.Bodies, "someone is standing there", bounds, parts, verdict);

        // 13. The area has room.
        int standing = state.World.Pieces.Count(p => p.XMm >= area.MinXMm && p.XMm <= area.MaxXMm && p.ZMm >= area.MinZMm && p.ZMm <= area.MaxZMm);
        if (standing >= area.MaxPieces)
            return Refuse(PlacementRule.PieceCap, $"this build area already holds {standing} pieces", bounds, parts, verdict);

        // 14. The materials are carried: worst quality first, then the smallest stack, then the ID.
        var takes = Takes(context, piece.Cost, out string? missing);
        if (missing is not null)
            return Refuse(PlacementRule.Materials, missing, bounds, parts, verdict);

        // 15. Navigability: the edit leaves no newly sealed pocket and cuts off no protected point (§3.13).
        if (verdict == NavVerdict.NotApplicable || !ctx.CheckNavigability)
            return new PlacementCheck(true, null, null, bounds, parts, takes, verdict);
        var solids = ImmutableArray.CreateBuilder<NavInput>();
        var doors = ImmutableArray.CreateBuilder<NavInput>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (piece.Parts[i].Traversal == TraversalClass.Solid)
                solids.Add(new NavInput(NavInputKind.Solid, parts[i], null));
            else
                doors.Add(new NavInput(NavInputKind.Door, parts[i], parts[i].Id));
        }
        var edit = ctx.Navigation.CheckEdit(solids.ToImmutable(), doors.ToImmutable(), ProtectedPoints(context, (piece, xMm, zMm, rotation)), ctx.Scratch,
            ctx.Counters);
        return edit.Ok
            ? new PlacementCheck(true, null, null, bounds, parts, takes, NavVerdict.Proven)
            : Refuse(PlacementRule.Navigability, edit.Reason!, bounds, parts, NavVerdict.Refused);
    }

    /// <summary>A body's circle and the reach within which a walkable node must stay (§3.13's table).</summary>
    public const long BodyRadiusMm = 350, SiteReachMm = 1_600;

    /// <summary>How near a walkable node must lie to a work anchor (M7 design §4.17, V-N3).</summary>
    public const long AnchorReachMm = 250;

    /// <summary>
    /// What an edit must not cut off (M7 design §3.13), in the canonical order that names a sealed pocket: the character and their companions,
    /// every authored NPC's place, the spawn, everything worked by reaching it, and both approaches of every door. A candidate's own sites
    /// (chests and stations, E8) and the places NPCs work at (E9) join the list with their pieces and errands.
    /// </summary>
    public static ImmutableArray<NavProtectedPoint> ProtectedPoints(SystemContext context,
        (PieceDefinition Piece, long XMm, long ZMm, int Rotation)? candidate = null)
    {
        var state = context.State;
        var setup = context.Setup;
        var layout = setup.Layout;
        var points = ImmutableArray.CreateBuilder<NavProtectedPoint>();
        static NavRect At(long x, long z) => new(x, z, x, z);
        string Name(string npcId) => setup.Social.Npcs.TryGetValue(npcId, out var npc) ? npc.Name : npcId;

        // The candidate's own site: a chest's, which lies inside its own box and must be reached; a bench's work anchor, which must be
        // stood at (E8).
        if (candidate is { Piece.Container: { } container } placing)
        {
            var (dx, dz) = QuarterTurn.Apply(container.XMm, container.ZMm, placing.Rotation);
            points.Add(new NavProtectedPoint(placing.Piece.Name, NavPointKind.NewSite, At(placing.XMm + dx, placing.ZMm + dz), 0, SiteReachMm));
        }
        if (candidate is { Piece.Station: { } station } working)
        {
            var (dx, dz) = QuarterTurn.Apply(station.AnchorXMm, station.AnchorZMm, working.Rotation);
            points.Add(new NavProtectedPoint(working.Piece.Name, NavPointKind.NewSite, At(working.XMm + dx, working.ZMm + dz), BodyRadiusMm, AnchorReachMm));
        }

        // Where each NPC who works here stands to work (E9): cut off, they could not get to it.
        foreach (var errand in state.World.NpcErrands.Where(e => e.Phase is NpcErrandPhase.ToWork or NpcErrandPhase.AtWork))
        {
            if (state.World.Piece(errand.PieceId!) is { } row && setup.Building.Catalog.Find(row.DefId)?.Station is { } workStation)
            {
                var (ax, az, _) = WorkAnchors.Of(workStation, row);
                points.Add(new NavProtectedPoint(Name(errand.NpcId), NavPointKind.WorkAnchor, At(ax, az), BodyRadiusMm, AnchorReachMm));
            }
        }

        points.Add(new NavProtectedPoint("you", NavPointKind.Body, At(state.Body.XMm, state.Body.ZMm), BodyRadiusMm, SiteReachMm));
        foreach (string npcId in state.Companions.Keys)
        {
            if (state.Npcs.TryGetValue(npcId, out var npc))
                points.Add(new NavProtectedPoint(Name(npcId), NavPointKind.Body, At(npc.Body.XMm, npc.Body.ZMm), BodyRadiusMm, SiteReachMm));
        }
        foreach (var errand in state.World.NpcErrands)
        {
            if (state.Npcs.TryGetValue(errand.NpcId, out var npc))
                points.Add(new NavProtectedPoint(Name(errand.NpcId), NavPointKind.Body, At(npc.Body.XMm, npc.Body.ZMm), BodyRadiusMm, SiteReachMm));
        }
        foreach (var site in layout.Npcs.OrderBy(n => n.NpcId, StringComparer.Ordinal))
            points.Add(new NavProtectedPoint(Name(site.NpcId), NavPointKind.NpcSite, At(site.XMm, site.ZMm), BodyRadiusMm, SiteReachMm));
        points.Add(new NavProtectedPoint("the waystone", NavPointKind.Spawn, At(layout.Spawn.XMm, layout.Spawn.ZMm), BodyRadiusMm, SiteReachMm));

        NavProtectedPoint Reach(string label, long x, long z) => new(label, NavPointKind.Reach, At(x, z), 0, SiteReachMm);
        foreach (var c in layout.Containers.OrderBy(c => c.Key, StringComparer.Ordinal))
            points.Add(Reach(Words(c.Key), c.XMm, c.ZMm));
        foreach (var c in context.CorpseSites().OrderBy(c => c.Key, StringComparer.Ordinal))
            points.Add(Reach("the remains", c.XMm, c.ZMm));
        foreach (var c in context.PieceChestSites())
            points.Add(Reach("the chest", c.XMm, c.ZMm));
        foreach (var s in layout.Stations.OrderBy(s => s.Key, StringComparer.Ordinal))
            points.Add(Reach(Words(s.Key), s.XMm, s.ZMm));
        foreach (var s in context.PieceStationSites().OrderBy(s => s.Key, StringComparer.Ordinal))
            points.Add(Reach("the bench", s.XMm, s.ZMm));
        foreach (var n in layout.Nodes.OrderBy(n => n.Name, StringComparer.Ordinal))
            points.Add(Reach(Words(n.Name), n.XMm, n.ZMm));
        foreach (var s in layout.Switches.OrderBy(s => s.Key, StringComparer.Ordinal))
            points.Add(new NavProtectedPoint(s.Name.Length > 0 ? $"the {s.Name}" : Words(s.Key), NavPointKind.Reach, NavGeometry.Aabb(s.Body), 0, SiteReachMm));
        var stacks = layout.CellKeys.Select(CellKey.Parse)
            .SelectMany(cell => state.World.CreatedIn(cell).Select(c => (Position: InventorySystem.WorldPosition(cell, c.XCm, c.ZCm), c.DefId, c.Count)))
            .OrderBy(s => s.Position.X).ThenBy(s => s.Position.Z).ThenBy(s => s.DefId, StringComparer.Ordinal).ThenBy(s => s.Count);
        foreach (var (position, defId, _) in stacks)
            points.Add(Reach(defId, position.X, position.Z));

        foreach (var door in layout.Doors.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            foreach (var (x, z) in NavEditCheck.Approaches(NavGeometry.Aabb(door.ClosedFootprint)))
                points.Add(new NavProtectedPoint(Words(door.Key), NavPointKind.DoorApproach, At(x, z), 0, NavEditCheck.DoorApproachReachMm));
        }
        foreach (var leaf in context.StructureFootprints.Where(f => f.Class == TraversalClass.Door))
        {
            string label = state.World.Piece(leaf.PieceId) is { } row && setup.Building.Catalog.Find(row.DefId) is { } piece ? $"the {piece.Name}" : "the door";
            foreach (var (x, z) in NavEditCheck.Approaches(new NavRect(leaf.MinXMm, leaf.MinZMm, leaf.MaxXMm, leaf.MaxZMm)))
                points.Add(new NavProtectedPoint(label, NavPointKind.DoorApproach, At(x, z), 0, NavEditCheck.DoorApproachReachMm));
        }
        return points.ToImmutable();
    }

    /// <summary>A key read as words for a refusal: <c>container.timber_stack</c> is "the timber stack", <c>door.forge_shed</c> "the forge shed door".</summary>
    private static string Words(string key)
    {
        int dot = key.IndexOf('.');
        string tail = (dot >= 0 ? key[(dot + 1)..] : key).Replace('_', ' ').Replace('.', ' ');
        return key.StartsWith("door.", StringComparison.Ordinal) ? $"the {tail} door" : $"the {tail}";
    }

    /// <summary>
    /// Who may work a placed door (M7 design §4.9): its owner; the owner's companion when the owner is the player; or an NPC on an errand
    /// for the owner - to work, at work, or walking home from it (E9). Creatures never work doors.
    /// </summary>
    public static bool CanOperate(EntityId operatorId, string? operatorNpcId, PieceRecord door, RuntimeState state, EntityId player) =>
        operatorId == door.Owner
        || (operatorNpcId is { } npc && state.Companions.ContainsKey(npc) && door.Owner == player)
        || (operatorNpcId is { } worker && state.World.NpcErrand(worker)?.WorkOwner is { } owner && owner == door.Owner);

    /// <summary>The ghost's reading of a check: its bounds, parts, and each cost line against what is carried.</summary>
    public static PlacementPreview Preview(SystemContext context, PlacementCheck check, string defId)
    {
        var cost = context.Setup.Building.Catalog.Find(defId) is { } piece
            ? piece.Cost.Select(c => new CostView(c.ItemId, c.Count, Unequipped(context, c.ItemId).Sum(e => e.Count))).ToImmutableArray()
            : ImmutableArray<CostView>.Empty;
        var b = check.Bounds;
        return new PlacementPreview(check.Allowed, check.Failed, check.Reason, b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, check.Parts, cost, check.Navigability);
    }

    /// <summary>The stacks a cost takes, in check 14's order; <paramref name="missing"/> names the first line not covered.</summary>
    public static ImmutableArray<StackTake> Takes(SystemContext context, ImmutableArray<PieceCost> cost, out string? missing)
    {
        missing = null;
        var takes = ImmutableArray.CreateBuilder<StackTake>();
        var used = new Dictionary<EntityId, int>();
        foreach (var line in cost)
        {
            int left = line.Count;
            foreach (var stack in Unequipped(context, line.ItemId).OrderBy(e => e.Quality).ThenBy(e => e.Count).ThenBy(e => e.ItemId.Value, StringComparer.Ordinal))
            {
                int free = stack.Count - used.GetValueOrDefault(stack.ItemId);
                int take = Math.Min(left, free);
                if (take <= 0)
                    continue;
                takes.Add(new StackTake(stack.ItemId, take));
                used[stack.ItemId] = used.GetValueOrDefault(stack.ItemId) + take;
                left -= take;
                if (left == 0)
                    break;
            }
            if (left > 0)
            {
                missing = $"needs {line.Count} {line.ItemId}";
                return ImmutableArray<StackTake>.Empty;
            }
        }
        return takes.ToImmutable();
    }

    /// <summary>What a piece's mount needs, in words.</summary>
    private static string Unsupported(PieceDefinition piece, SocketType mount) => mount switch
    {
        SocketType.EdgeMount => $"a {BuildingKeys.Key(piece.Family)} needs a floor pad on one side",
        SocketType.SquareMount => $"a {piece.Name} needs a floor pad under it",
        _ => "a door needs a doorway",
    };

    /// <summary>What the region authored that a piece may not strictly overlap, whatever its state: statics, door boxes and barriers.</summary>
    public static IEnumerable<Blocker> Authored(RegionLayout layout) =>
        layout.Space.Blockers.Concat(layout.Doors.Select(d => (Blocker)d.ClosedFootprint)).Concat(layout.Barriers.Select(b => b.Footprint));

    /// <summary>The region's protected zones, with each NPC's place named by the NPC.</summary>
    public static ImmutableArray<ProtectedZone> Zones(SystemContext context)
    {
        var setup = context.Setup;
        return ProtectedZones.Of(setup.Layout, setup.Combat.Spawns,
            id => setup.Combat.Creatures.TryGetValue(id, out var creature) ? creature.RadiusMm : 0, setup.Building.Constants.Protection,
            id => setup.Social.Npcs.TryGetValue(id, out var npc) ? npc.Name : id);
    }

    public static BoundsMm Box(BuildAreaSite area) => new(area.MinXMm, area.MinZMm, area.MaxXMm, area.MaxZMm);

    private static IEnumerable<InventoryEntry> Unequipped(SystemContext context, string itemId) =>
        context.State.Inventory.Where(e => e.DefId == itemId && !context.State.Equipment.ContainsValue(e.ItemId));

    private static PieceDefinition? Definition(SystemContext context, EntityId pieceId) =>
        context.State.World.Piece(pieceId) is { } row ? context.Setup.Building.Catalog.Find(row.DefId) : null;

    private static PieceDefinition? Occupant(SystemContext context, string slotKey) =>
        context.State.StructureIndex.Slots.TryGetValue(slotKey, out var id) ? Definition(context, id) : null;
}
