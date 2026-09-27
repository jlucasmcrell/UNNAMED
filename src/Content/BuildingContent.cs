// UNNAMED Content - building pieces, config.building and the build areas (M7 design §4.3, §4.10, §4.16, §4.20)
// Validates and builds the piece catalogue and building's numbers; lints BLD001-BLD004 and BLD006-BLD009.

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Building (M7): the pieces a player places, <c>config.building</c>, and the build areas the regions declare. A piece's fields and
/// keys are a closed set (BLD002), its sockets and parts are consistent with the 3 m lattice (BLD003), it costs defined items (BLD004);
/// config.building exists wherever anything can be built, and its numbers are sane and the module save-locked (BLD006); a build area sits on the lattice clear of everything authored and every
/// protected zone (BLD007); and no cell or region may hold more pieces than the ceilings (BLD009). Build-area shape is WLD015's.
/// </summary>
public static class BuildingContent
{
    public const string ConfigId = "config.building";

    private static readonly ImmutableHashSet<string> Envelope =
        ImmutableHashSet.Create(StringComparer.Ordinal, "id", "kind", "schema", "display_key", "tags", "notes", "defines", "deprecated");

    private static readonly ImmutableHashSet<string> PieceFields = ImmutableHashSet.Create(StringComparer.Ordinal,
        "name", "family", "slot", "rotations", "bounds_m", "parts", "sockets", "cost", "health_max", "supports_roof", "container", "station");

    private static readonly ImmutableHashSet<string> ConfigFields = ImmutableHashSet.Create(StringComparer.Ordinal,
        "module_m", "rotation_step_deg", "place_reach_m", "pad_max_relief_m", "refund_percent", "repair_cost_percent", "protection", "damage");

    private static readonly ImmutableHashSet<string> PartFields = ImmutableHashSet.Create(StringComparer.Ordinal, "box_m", "height_m", "traversal");
    private static readonly ImmutableHashSet<string> SocketFields = ImmutableHashSet.Create(StringComparer.Ordinal, "type", "at_m", "axis");
    private static readonly ImmutableHashSet<string> CostFields = ImmutableHashSet.Create(StringComparer.Ordinal, "item_ref", "count");

    private static readonly BoundsMm SquareBounds = new(-Lattice.HalfMm, -Lattice.HalfMm, Lattice.HalfMm, Lattice.HalfMm);
    private static readonly BoundsMm EdgeLimit = new(-1_700, -200, 1_700, 200);
    private static readonly BoundsMm FurnitureLimit = new(-1_300, -1_300, 1_300, 1_300);

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        var pieces = loader.GetByKind("piece");
        string? configFile = loader.Definitions.GetValueOrDefault(ConfigId)?.SourceFile;
        var layouts = loader.GetByKind("region").Keys.OrderBy(r => r, StringComparer.Ordinal).Select(r => Layout(loader, r)).OfType<RegionLayout>().ToList();
        bool anyArea = layouts.Any(l => !l.BuildAreas.IsEmpty);
        if (pieces.Count == 0 && configFile is null && !anyArea)
            return errors;

        // BLD006 (the owner's ruling of 2026-09-26): config.building is required when there is anything to build, and whenever it is
        // present it is validated in full - a valid one with nothing to build is allowed.
        BuildingConstants? constants = null;
        if (configFile is null)
        {
            errors.Add(Error("BLD006", $"{ConfigId} is missing, but the content has pieces or build areas", null));
        }
        else
        {
            foreach (string key in Read(loader.Definitions[ConfigId].YamlSource).Keys.OfType<string>().Where(k => !Envelope.Contains(k) && !ConfigFields.Contains(k)))
                errors.Add(Error("BLD006", $"{ConfigId}: '{key}' is not a building setting", configFile));
            try
            {
                constants = ParseConfig(loader);
                string? problem = constants.Problem();
                if (problem is null && NodeMm(loader) is { } node && constants.ModuleMm % node != 0)
                    problem = string.Create(CultureInfo.InvariantCulture, $"module_m must be a whole multiple of config.navigation's node_m ({node / 1000.0:0.###} m)");
                if (problem is not null)
                {
                    errors.Add(Error("BLD006", $"{ConfigId}: {problem}", configFile));
                    constants = null;
                }
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
            {
                errors.Add(Error("BLD006", $"{ConfigId}: {e.Message}", configFile));
            }
        }

        foreach (var envelope in pieces.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
            errors.AddRange(PieceProblems(loader, envelope));
        errors.AddRange(WorksAtProblems(loader, layouts));

        foreach (var layout in layouts.Where(l => !l.BuildAreas.IsEmpty))
        {
            string? file = loader.Definitions[layout.Id].SourceFile;
            foreach (string problem in AreaProblems(loader, layout, constants))
                errors.Add(Error("BLD007", problem, file));
            foreach (string problem in CeilingProblems(layout))
                errors.Add(Error("BLD009", problem, file));
            foreach (string problem in SealProblems(loader, layout))
                errors.Add(Error("BLD008", problem, file));
        }
        return errors;
    }

    /// <summary>Building as the running world uses it; <see cref="BuildingSetup.Empty"/> when the pack has no pieces and no config.</summary>
    public static BuildingSetup Build(ContentLoader loader)
    {
        var pieces = loader.GetByKind("piece");
        if (pieces.Count == 0 && !loader.Definitions.ContainsKey(ConfigId))
            return BuildingSetup.Empty;
        var constants = loader.Definitions.ContainsKey(ConfigId) ? ParseConfig(loader) : BuildingConstants.Default;
        return new BuildingSetup(new BuildingCatalog(pieces.Values.Select(ParsePiece)), constants);
    }

    // ── config.building ─────────────────────────────────────────────────────────

    private static BuildingConstants ParseConfig(ContentLoader loader)
    {
        var map = Config(loader, ConfigId);
        var p = Map(map, "protection");
        var damage = Map(map, "damage");
        return new BuildingConstants(Mm(map, "module_m"), Int(map, "rotation_step_deg"), Mm(map, "place_reach_m"), Mm(map, "pad_max_relief_m"),
            Int(map, "refund_percent"), Int(map, "repair_cost_percent"),
            new BuildingProtection(Mm(p, "spawn_point_m"), Mm(p, "npc_site_m"), Mm(p, "site_m"), Mm(p, "structure_margin_m"), Mm(p, "spawner_margin_m")),
            damage.Keys.Select(k => k as string ?? throw new FormatException("a damage source is named by text"))
                .ToImmutableSortedDictionary(k => k, k => Int(damage, k), StringComparer.Ordinal));
    }

    private static long? NodeMm(ContentLoader loader)
    {
        try
        {
            return NavigationContent.Build(loader).NodeMm;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return null;   // the NAV codes report it
        }
    }

    // ── pieces ──────────────────────────────────────────────────────────────────

    private static IEnumerable<ValidationError> PieceProblems(ContentLoader loader, ContentEnvelope envelope)
    {
        string id = envelope.Id, file = envelope.SourceFile ?? "";
        var map = Read(envelope.YamlSource);

        // BLD002, first on the raw file: a closed field set and closed keys.
        var shape = ShapeProblems(map).ToList();
        foreach (string problem in shape)
            yield return Error("BLD002", $"{id}: {problem}", file);
        if (shape.Count > 0)
            yield break;

        // BLD001: it builds.
        PieceDefinition piece;
        string? failure = null;
        try
        {
            piece = ParsePiece(envelope);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            piece = null!;
            failure = e.Message;
        }
        if (failure is not null)
        {
            yield return Error("BLD001", $"{id} does not build: {failure}", file);
            yield break;
        }

        // BLD002, on what it means.
        if (piece.Slot != PieceDefinition.SlotOf(piece.Family))
            yield return Error("BLD002", $"{id}: a {BuildingKeys.Key(piece.Family)} goes in the {Slot(PieceDefinition.SlotOf(piece.Family))} slot, not {Slot(piece.Slot)}", file);
        if (piece.Rotations.IsEmpty || piece.Rotations.Distinct().Count() != piece.Rotations.Length || piece.Rotations.Any(r => r is < 0 or > 3))
            yield return Error("BLD002", $"{id}: rotations must be distinct quarter turns from 0 to 3, at least one", file);
        if (piece.HealthMax < 1)
            yield return Error("BLD002", $"{id}: health_max must be at least 1", file);

        foreach (string problem in LatticeProblems(piece))
            yield return Error("BLD003", $"{id}: {problem}", file);

        // BLD005: a chest's container, on storage alone, its site inside the piece.
        if (piece.Container is { } container)
        {
            if (piece.Family != PieceFamily.Storage)
                yield return Error("BLD005", $"{id}: only storage has a container", file);
            if (container.StackSlots < 1)
                yield return Error("BLD005", $"{id}: a container holds at least one stack (stack_slots)", file);
            if (!piece.Bounds.Contains(new BoundsMm(container.XMm, container.ZMm, container.XMm, container.ZMm)))
                yield return Error("BLD005", $"{id}: the container's site at_m lies outside the piece's bounds", file);
        }
        else if (piece.Family == PieceFamily.Storage)
            yield return Error("BLD005", $"{id}: storage has a container", file);

        // BLD005: a bench's station, on stations alone: a kind some recipe is worked at, and room for a worker at its anchor.
        if (piece.Station is { } station)
        {
            if (piece.Family != PieceFamily.Station)
                yield return Error("BLD005", $"{id}: only a station has a station", file);
            if (!RecipeStations(loader).Contains(station.Kind))
                yield return Error("BLD005", $"{id}: no recipe is worked at a '{station.Kind}'", file);
            if (PersonRadiusMm(loader) is { } radius && AnchorProblem(piece, station, radius + AnchorSpareMm) is { } problem)
                yield return Error("BLD005", $"{id}: {problem}", file);
        }
        else if (piece.Family == PieceFamily.Station)
            yield return Error("BLD005", $"{id}: a station has a station", file);

        // BLD004: it costs defined items.
        if (piece.Cost.IsEmpty)
            yield return Error("BLD004", $"{id}: a piece costs something (cost)", file);
        foreach (var line in piece.Cost)
        {
            if (!loader.Definitions.TryGetValue(line.ItemId, out var item) || item.Kind != "item")
                yield return Error("BLD004", $"{id}: cost names '{line.ItemId}', which is not an item", file);
            if (line.Count < 1)
                yield return Error("BLD004", $"{id}: every cost line is at least 1", file);
        }
    }

    /// <summary>
    /// BLD005 (E9): what an NPC works at is a kind some recipe is worked at, and every corner of every build area in their region lies within
    /// the planner's reach of their place on each axis - <c>window_max_m</c> less twice <c>window_margin_m</c> and both snaps (85 m shipped) -
    /// so a walk to any bench that can be built is always a plan the window holds.
    /// </summary>
    private static IEnumerable<ValidationError> WorksAtProblems(ContentLoader loader, IReadOnlyList<RegionLayout> layouts)
    {
        var stations = RecipeStations(loader);
        long? span = WindowSpanMm(loader);
        foreach (var npc in loader.GetByKind("npc").Values.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            if (Read(npc.YamlSource).GetValueOrDefault("works_at") is not List<object> kinds)
                continue;
            string file = npc.SourceFile ?? "";
            foreach (string kind in kinds.OfType<string>().Where(k => !stations.Contains(k)))
                yield return Error("BLD005", $"{npc.Id}: works_at names '{kind}', and no recipe is worked at one", file);
            if (span is not { } limit)
                continue;
            foreach (var layout in layouts)
            {
                foreach (var site in layout.Npcs.Where(s => s.NpcId == npc.Id))
                {
                    foreach (var area in layout.BuildAreas)
                    {
                        long far = Math.Max(Math.Max(Math.Abs(area.MinXMm - site.XMm), Math.Abs(area.MaxXMm - site.XMm)),
                            Math.Max(Math.Abs(area.MinZMm - site.ZMm), Math.Abs(area.MaxZMm - site.ZMm)));
                        if (far > limit)
                            yield return Error("BLD005", string.Create(CultureInfo.InvariantCulture,
                                $"{npc.Id}: {area.Key} reaches {far / 1000.0:0.###} m from their place on an axis; a walk to work plans within {limit / 1000.0:0.###} m"), file);
                    }
                }
            }
        }
    }

    /// <summary>The planner's reach on each axis from a start: the window less its margins and both snaps; null when the config does not build.</summary>
    private static long? WindowSpanMm(ContentLoader loader)
    {
        try
        {
            var l = NavigationContent.Build(loader).Limits;
            return l.WindowMaxMm - 2 * l.WindowMarginMm - l.StartSnapMm - l.GoalSnapMm;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return null;   // the NAV codes report it
        }
    }

    /// <summary>The room a work anchor keeps beyond a person's radius, from every part and from the furniture limit (§4.20: 350 + 300).</summary>
    private const long AnchorSpareMm = 300;

    /// <summary>The station kinds the recipes are worked at.</summary>
    private static HashSet<string> RecipeStations(ContentLoader loader) =>
        loader.GetByKind("recipe").Values.Select(r => Read(r.YamlSource).GetValueOrDefault("station") as string).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The person class's radius from <c>config.navigation</c>, or null when it does not build (the NAV codes report it).</summary>
    private static long? PersonRadiusMm(ContentLoader loader)
    {
        try
        {
            return NavigationContent.Build(loader).Classes.FirstOrDefault(c => c.Id == "person")?.RadiusMm;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A work anchor a body cannot stand at: nearer than <paramref name="clearMm"/> to a part or to the furniture limit.</summary>
    private static string? AnchorProblem(PieceDefinition piece, PieceStation station, long clearMm)
    {
        long x = station.AnchorXMm, z = station.AnchorZMm;
        string metres = (clearMm / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        if (FurnitureLimit.MaxXMm - Math.Abs(x) < clearMm || FurnitureLimit.MaxZMm - Math.Abs(z) < clearMm)
            return $"the work anchor lies within {metres} m of the square's edge (work_anchor_m)";
        foreach (var part in piece.Parts)
        {
            long dx = Math.Max(0, Math.Max(part.Box.MinXMm - x, x - part.Box.MaxXMm));
            long dz = Math.Max(0, Math.Max(part.Box.MinZMm - z, z - part.Box.MaxZMm));
            if (dx * dx + dz * dz < clearMm * clearMm)
                return $"the work anchor lies within {metres} m of a part (work_anchor_m)";
        }
        return null;
    }

    private static IEnumerable<string> ShapeProblems(Dictionary<object, object> map)
    {
        foreach (string key in map.Keys.OfType<string>().Where(k => !Envelope.Contains(k) && !PieceFields.Contains(k)))
            yield return $"'{key}' is not a piece field";
        if (map.GetValueOrDefault("family") is string family && !BuildingKeys.Families.ContainsKey(family))
            yield return $"family '{family}' is not one of {string.Join(", ", BuildingKeys.Families.Keys)}";
        if (map.GetValueOrDefault("slot") is string slot && !BuildingKeys.Slots.ContainsKey(slot))
            yield return $"slot '{slot}' is not one of {string.Join(", ", BuildingKeys.Slots.Keys)}";
        foreach (var part in RowsOf(map, "parts"))
        {
            foreach (string key in part.Keys.OfType<string>().Where(k => !PartFields.Contains(k) && k != "clearance_m"))
                yield return $"'{key}' is not a part field";
            if (part.GetValueOrDefault("traversal") is string traversal && !BuildingKeys.Traversals.ContainsKey(traversal))
                yield return $"traversal '{traversal}' is not one of {string.Join(", ", BuildingKeys.Traversals.Keys)}";
        }
        foreach (var socket in RowsOf(map, "sockets"))
        {
            foreach (string key in socket.Keys.OfType<string>().Where(k => !SocketFields.Contains(k)))
                yield return $"'{key}' is not a socket field";
            string? type = socket.GetValueOrDefault("type") as string;
            if (type is not null && !BuildingKeys.SocketTypes.ContainsKey(type))
                yield return $"socket type '{type}' is not one of {string.Join(", ", BuildingKeys.SocketTypes.Keys)}";
            bool squareLike = type is "square" or "square_mount";
            if (socket.GetValueOrDefault("axis") is string axis ? squareLike || !BuildingKeys.Axes.ContainsKey(axis) : !squareLike)
                yield return $"a {type} socket's axis is {(squareLike ? "absent" : "x or z")}";
        }
        foreach (var line in RowsOf(map, "cost"))
        {
            foreach (string key in line.Keys.OfType<string>().Where(k => !CostFields.Contains(k)))
                yield return $"'{key}' is not a cost field";
        }
    }

    private static IEnumerable<string> LatticeProblems(PieceDefinition piece)
    {
        // Sockets: providers where the lattice puts them; every mount at the anchor.
        foreach (var s in piece.Sockets)
        {
            bool ok = s.Type switch
            {
                SocketType.Edge => (s.XMm == 0 && Math.Abs(s.ZMm) == Lattice.HalfMm && s.Axis == SocketAxis.X)
                                   || (s.ZMm == 0 && Math.Abs(s.XMm) == Lattice.HalfMm && s.Axis == SocketAxis.Z),
                SocketType.Square or SocketType.SquareMount => (s.XMm, s.ZMm) == (0, 0),
                _ => (s.XMm, s.ZMm) == (0, 0) && s.Axis == SocketAxis.X,
            };
            if (!ok)
                yield return string.Create(CultureInfo.InvariantCulture, $"the {Key(s.Type)} socket at ({s.XMm / 1000.0:0.###}, {s.ZMm / 1000.0:0.###}) is not on the lattice ") +
                             "(edges at (0, ±1.5) along x or (±1.5, 0) along z; every other socket at (0, 0), along x where it has an axis)";
        }

        // Bounds: exactly the square, or within the slot's limit.
        switch (piece.Slot)
        {
            case PieceSlot.Square or PieceSlot.Roof when piece.Bounds != SquareBounds:
                yield return "a square or roof piece's bounds are exactly the square [-1.5, -1.5, 1.5, 1.5]";
                break;
            case PieceSlot.Edge or PieceSlot.Door when !EdgeLimit.Contains(piece.Bounds):
                yield return "an edge or door piece's bounds lie within [-1.7, -0.2, 1.7, 0.2]";
                break;
            case PieceSlot.Furniture when !FurnitureLimit.Contains(piece.Bounds):
                yield return "a furniture piece's bounds lie within [-1.3, -1.3, 1.3, 1.3]";
                break;
        }

        // Parts: none on pads and roofs; otherwise inside the bounds, which are their union; standing; a door part only on a door.
        if (piece.Family is PieceFamily.Pad or PieceFamily.Roof)
        {
            if (!piece.Parts.IsEmpty)
                yield return "a pad or a roof has no blocking parts";
            yield break;
        }
        if (piece.Parts.IsEmpty)
        {
            yield return "a piece other than a pad or a roof has at least one part";
            yield break;
        }
        foreach (var part in piece.Parts)
        {
            if (!piece.Bounds.Contains(part.Box))
                yield return "a part lies outside the piece's bounds";
            if (part.HeightMm <= 0)
                yield return "a part's height_m is above 0";
            if (part.Traversal == TraversalClass.Door && piece.Family != PieceFamily.Door)
                yield return "only a door has a door part";
        }
        var union = piece.Parts.Select(p => p.Box).Aggregate((a, b) => a.Union(b));
        if (union != piece.Bounds)
            yield return "a piece's bounds are the union of its parts";
    }

    private static PieceDefinition ParsePiece(ContentEnvelope envelope)
    {
        var map = Read(envelope.YamlSource);
        string id = envelope.Id;
        if (RowsOf(map, "parts").Any(p => p.ContainsKey("clearance_m")))
            throw new FormatException("a part stands on the ground: no clearance_m");
        var piece = new PieceDefinition(id, map.GetValueOrDefault("name") as string ?? id, BuildingKeys.Families[Text(map, "family")],
            BuildingKeys.Slots[Text(map, "slot")],
            List(map, "rotations").Select((_, i) => IntOf(List(map, "rotations"), i, "rotations")).ToImmutableArray(),
            Box(List(map, "bounds_m"), "bounds_m"),
            RowsOf(map, "parts").Select(p => new PiecePart(Box(List(p, "box_m"), "box_m"), Mm(p, "height_m"),
                BuildingKeys.Traversals[Text(p, "traversal")])).ToImmutableArray(),
            RowsOf(map, "sockets").Select(s =>
            {
                var (x, z) = Point(List(s, "at_m"), "at_m");
                return new PieceSocket(BuildingKeys.SocketTypes[Text(s, "type")], x, z,
                    s.GetValueOrDefault("axis") is string axis ? BuildingKeys.Axes[axis] : SocketAxis.None);
            }).ToImmutableArray(),
            RowsOf(map, "cost").Select(c => new PieceCost(Text(c, "item_ref"), Int(c, "count"))).ToImmutableArray(),
            Int(map, "health_max"),
            map.GetValueOrDefault("supports_roof") is string supports && bool.Parse(supports));
        if (map.ContainsKey("container"))
        {
            var c = Map(map, "container");
            var (x, z) = Point(List(c, "at_m"), "container at_m");
            piece = piece with { Container = new PieceContainer(Int(c, "stack_slots"), x, z) };
        }
        if (map.ContainsKey("station"))
        {
            var s = Map(map, "station");
            var (x, z) = Point(List(s, "work_anchor_m"), "work_anchor_m");
            piece = piece with { Station = new PieceStation(Text(s, "kind"), x, z, (int)Math.Round(Number(s, "work_facing_deg") * 1000, MidpointRounding.AwayFromZero)) };
        }
        return piece;
    }

    private static IEnumerable<Dictionary<object, object>> RowsOf(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is List<object> rows
            ? rows.Select((row, i) => row as Dictionary<object, object> ?? throw new FormatException($"{key}[{i}] must be a map"))
            : Enumerable.Empty<Dictionary<object, object>>();

    private static BoundsMm Box(List<object> values, string what)
    {
        if (values.Count != 4)
            throw new FormatException($"{what} is [min_x, min_z, max_x, max_z]");
        var box = new BoundsMm(MmOf(values[0], what), MmOf(values[1], what), MmOf(values[2], what), MmOf(values[3], what));
        return box.MinXMm < box.MaxXMm && box.MinZMm < box.MaxZMm ? box : throw new FormatException($"{what}: min must be below max");
    }

    private static (long X, long Z) Point(List<object> values, string what) =>
        values.Count == 2 ? (MmOf(values[0], what), MmOf(values[1], what)) : throw new FormatException($"{what} is [x, z]");

    /// <summary>Metres in content, whole millimetres in the domain, rounded half away from zero.</summary>
    private static long MmOf(object value, string what) =>
        double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres)
            ? (long)Math.Round(metres * 1000, MidpointRounding.AwayFromZero)
            : throw new FormatException($"'{what}' must be numbers");

    private static string Key(SocketType type) => BuildingKeys.SocketTypes.First(t => t.Value == type).Key;

    private static string Slot(PieceSlot slot) => BuildingKeys.Slots.First(s => s.Value == slot).Key;

    // ── build areas ─────────────────────────────────────────────────────────────

    /// <summary>
    /// BLD007: each area's corners on the lattice; the area grown by 200 mm overlaps no authored structure, door box or barrier, and meets
    /// no protected zone.
    /// </summary>
    private static IEnumerable<string> AreaProblems(ContentLoader loader, RegionLayout layout, BuildingConstants? constants)
    {
        var zones = constants is null ? (ImmutableArray<ProtectedZone>?)null : Zones(loader, layout, constants);
        foreach (var area in layout.BuildAreas)
        {
            if (new[] { area.MinXMm, area.MinZMm, area.MaxXMm, area.MaxZMm }.Any(v => v % Lattice.ModuleMm != 0))
                yield return $"{area.Key}'s corners are not on the {Lattice.ModuleMm / 1000} m building lattice";
            var grown = new BoundsMm(area.MinXMm - 200, area.MinZMm - 200, area.MaxXMm + 200, area.MaxZMm + 200);
            var authored = layout.Space.Blockers.Concat(layout.Doors.Select(d => (Blocker)d.ClosedFootprint)).Concat(layout.Barriers.Select(b => b.Footprint));
            foreach (var blocker in authored.Where(b => BuildingMath.Overlaps(grown, b)))
                yield return $"{area.Key} overlaps {blocker.Id}";
            if (zones is { } z && ProtectedZones.Met(z, grown) is { } met)
                yield return $"{area.Key} meets ground kept clear ({met.What})";
        }
    }

    private static ImmutableArray<ProtectedZone>? Zones(ContentLoader loader, RegionLayout layout, BuildingConstants constants)
    {
        try
        {
            int tickMs = WorldContent.TickMilliseconds(loader);
            var creatures = BuildCreatures(loader, tickMs);
            return ProtectedZones.Of(layout, BuildSpawns(loader, layout.Id), id => creatures.TryGetValue(id, out var c) ? c.RadiusMm : 0,
                constants.Protection, id => id);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            return null;   // the CMB and WLD codes report it
        }
    }

    /// <summary>
    /// BLD008 (M7 design §3.13): what the placement check's flood must be able to decide. (a) Each area grown by the 200 mm overhang
    /// holds at most <c>seal_limit_nodes</c> nodes, so a pocket inside it is always found before the flood gives up. (b) Filled solid, it
    /// cuts off no node outside it from the spawn - every gate passable, for a person - so a pocket can only ever lie inside an area.
    /// </summary>
    private static IEnumerable<string> SealProblems(ContentLoader loader, RegionLayout layout)
    {
        NavConfig config;
        try
        {
            config = NavigationContent.Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            yield break;   // the NAV codes report it
        }
        long node = config.NodeMm, half = node / 2;
        NavGrid? before = null;
        NavReach? reachBefore = null;
        foreach (var area in layout.BuildAreas)
        {
            var grown = new NavRect(area.MinXMm - 200, area.MinZMm - 200, area.MaxXMm + 200, area.MaxZMm + 200);
            long columns = NavGeometry.FloorDiv(grown.MaxXMm - half, node) - NavGeometry.CeilDiv(grown.MinXMm - half, node) + 1;
            long rows = NavGeometry.FloorDiv(grown.MaxZMm - half, node) - NavGeometry.CeilDiv(grown.MinZMm - half, node) + 1;
            if (columns * rows > config.Limits.SealLimitNodes)
            {
                yield return $"{area.Key} holds {columns * rows} nodes grown by 200 mm; the placement check floods at most {config.Limits.SealLimitNodes} (seal_limit_nodes)";
                continue;
            }
            before ??= NavigationLayout.Build(layout, config);
            var spawn = new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm);
            reachBefore ??= NavReach.FromSpawn(before, config, spawn);
            var filled = before.With(grown, NavigationLayout.AuthoredInputs(layout, config)
                .Add(new NavInput(NavInputKind.Solid, new BoxBlocker(area.Key, grown.MinXMm, grown.MinZMm, grown.MaxXMm, grown.MaxZMm, 0), null)));
            var reachAfter = NavReach.FromSpawn(filled, config, spawn);
            int person = config.Classes.Select((c, k) => (c, k)).Single(p => p.c.Id == "person").k;
            (long I, long J)? cut = null;
            for (long j = filled.NodeMinJ; j <= filled.NodeMaxJ && cut is null; j++)
            {
                for (long i = filled.NodeMinI; i <= filled.NodeMaxI; i++)
                {
                    if (reachBefore.Reached(i, j) && !reachAfter.Reached(i, j) && filled.SolidFitAt(i, j) > person)
                    {
                        cut = (i, j);
                        break;
                    }
                }
            }
            if (cut is { } n)
                yield return $"{area.Key}, filled solid, cuts off ({filled.CentreOf(n.I)}, {filled.CentreOf(n.J)}) from the spawn";
        }
    }

    /// <summary>BLD009: the areas meeting any one cell, and all of a region's, may hold at most the ceilings between them.</summary>
    private static IEnumerable<string> CeilingProblems(RegionLayout layout)
    {
        int total = layout.BuildAreas.Sum(a => a.MaxPieces);
        if (total > BuildingConstants.PiecesPerRegionCeiling)
            yield return $"{layout.Id}'s build areas allow {total} pieces; a region holds at most {BuildingConstants.PiecesPerRegionCeiling}";
        foreach (string key in layout.CellKeys)
        {
            var tile = NavigationLayout.TileOf(CellKey.Parse(key));
            var square = new BoundsMm(tile.Tx * NavConfig.TileMm, tile.Tz * NavConfig.TileMm, (tile.Tx + 1) * NavConfig.TileMm, (tile.Tz + 1) * NavConfig.TileMm);
            int inCell = layout.BuildAreas.Where(a => BuildingMath.Overlaps(square, new BoundsMm(a.MinXMm, a.MinZMm, a.MaxXMm, a.MaxZMm))).Sum(a => a.MaxPieces);
            if (inCell > BuildingConstants.PiecesPerCellCeiling)
                yield return $"the build areas meeting {key} allow {inCell} pieces; a cell holds at most {BuildingConstants.PiecesPerCellCeiling}";
        }
    }

    private static RegionLayout? Layout(ContentLoader loader, string regionId)
    {
        try
        {
            return WorldContent.BuildLayout(loader, regionId);
        }
        catch (InvalidOperationException)
        {
            return null;   // the WLD codes report it
        }
    }

    private static ValidationError Error(string code, string message, string? file) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = file ?? string.Empty,
    };
}
