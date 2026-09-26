// UNNAMED Content - the region layout, places, world flags and movement rules from content
// (WORLD_ARCHITECTURE.md §4, DATA_MODEL.md §1, §4.18, §4.19; PROTOTYPE.md §3, §4.4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="RegionLayout"/>, <see cref="MovementRules"/> and <see cref="TierRules"/> from
/// <c>region</c>, <c>location</c> and <c>world_flag</c> definitions and <c>config.base_speeds</c> /
/// <c>config.simulation_tiers</c>, and lints them (WLD codes). A pack with no region is valid; building a layout
/// from it is not.
/// </summary>
public static class WorldContent
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    /// <summary>Lint: every region builds, every place and door resolves, every world flag is declared soundly.</summary>
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        foreach (var flag in loader.GetByKind("world_flag").Values)
            CheckFlag(flag, errors);
        var regions = loader.GetByKind("region");
        foreach (var location in loader.GetByKind("location").Values)
        {
            var map = Read(location.YamlSource);
            if (map.GetValueOrDefault("region_ref") is not string regionRef || !regions.ContainsKey(regionRef))
                errors.Add(Error("WLD005", $"{location.Id} names region '{map.GetValueOrDefault("region_ref")}', which is not a defined region", location.SourceFile));
        }
        foreach (var region in regions.Keys.OrderBy(k => k, StringComparer.Ordinal))
            TryBuildLayout(loader, region, errors);
        if (regions.Count > 0)
        {
            TryBuild(() => BuildMovement(loader), "config.base_speeds", loader, errors);
            TryBuild(() => BuildTiers(loader), "config.simulation_tiers", loader, errors);
        }
        return errors;
    }

    /// <summary>The layout of one region. Throws with every problem listed when the region is missing or invalid.</summary>
    public static RegionLayout BuildLayout(ContentLoader loader, string regionId)
    {
        var errors = new List<ValidationError>();
        var layout = TryBuildLayout(loader, regionId, errors);
        if (layout is null || errors.Count > 0)
            throw new InvalidOperationException($"Region {regionId} is invalid:\n  " + string.Join("\n  ", errors.Select(e => $"{e.Code}: {e.Message}")));
        return layout;
    }

    public static MovementRules BuildMovement(ContentLoader loader)
    {
        var map = Config(loader, "config.base_speeds");
        var rules = new MovementRules(
            Mm(map, "base_m_s"), Int(map, "walk_percent"), Int(map, "sprint_percent"), Mm(map, "body_radius_m"), Mm(map, "interact_reach_m"));
        if (rules.BaseSpeedMmPerSecond <= 0 || rules.WalkPercent is <= 0 or > 100 || rules.SprintPercent < 100
            || rules.BodyRadiusMm <= 0 || rules.InteractReachMm <= 0)
            throw new FormatException("config.base_speeds needs a positive speed, walk_percent in 1..100, sprint_percent of at least 100, and a positive body radius and reach");
        // The jump and the crouch (the owner's M6 playtest); a pack from before them keeps the defaults.
        if (map.ContainsKey("jump_apex_m"))
            rules = rules with
            {
                JumpApexMm = Mm(map, "jump_apex_m"), JumpRiseMs = (int)Math.Round(Number(map, "jump_rise_s") * 1000),
                JumpTuckRadiusMm = Mm(map, "jump_tuck_radius_m"),
            };
        if (map.ContainsKey("stand_height_m"))
        {
            rules = rules with
            {
                StandHeightMm = Mm(map, "stand_height_m"), CrouchHeightMm = Mm(map, "crouch_height_m"), CrouchPercent = Int(map, "crouch_percent"),
            };
        }
        if (rules.JumpApexMm <= 0 || rules.JumpRiseMs <= 0 || rules.JumpTuckRadiusMm <= 0 || rules.JumpTuckRadiusMm > rules.BodyRadiusMm
            || rules.CrouchHeightMm <= 0 || rules.CrouchHeightMm >= rules.StandHeightMm
            || rules.CrouchPercent is <= 0 or > 100)
            throw new FormatException("config.base_speeds needs a positive jump apex and rise, a tuck radius no wider than the body, a crouched height below the standing one, and crouch_percent in 1..100");
        return rules;
    }

    public static TierRules BuildTiers(ContentLoader loader)
    {
        var map = Config(loader, "config.simulation_tiers");
        var rules = new TierRules(Mm(map, "full_radius_m"), Mm(map, "regional_radius_m"), Mm(map, "abstract_radius_m"), Mm(map, "hysteresis_m"));
        if (!(0 < rules.FullRadiusMm && rules.FullRadiusMm < rules.RegionalRadiusMm && rules.RegionalRadiusMm < rules.AbstractRadiusMm)
            || rules.HysteresisMm < 0 || rules.HysteresisMm * 2 >= rules.FullRadiusMm)
            throw new FormatException("config.simulation_tiers needs 0 < full < regional < abstract radii and a hysteresis under half the full radius");
        return rules;
    }

    /// <summary>Display names: every definition's <c>name</c> field, where it has one (DATA_MODEL.md §4.18 places do).</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames(ContentLoader loader) =>
        loader.Definitions.Values
            .Select(d => (d.Id, Name: Read(d.YamlSource).GetValueOrDefault("name") as string))
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .ToDictionary(d => d.Id, d => d.Name!, StringComparer.Ordinal);

    /// <summary>The fixed domain tick in milliseconds, from <c>config.time</c>'s <c>ticks_per_second</c>.</summary>
    public static int TickMilliseconds(ContentLoader loader)
    {
        var map = Config(loader, "config.time");
        int perSecond = Int(map, "ticks_per_second");
        if (perSecond <= 0 || 1000 % perSecond != 0)
            throw new FormatException($"config.time ticks_per_second {perSecond} must divide 1000 ms exactly");
        return 1000 / perSecond;
    }

    private static RegionLayout? TryBuildLayout(ContentLoader loader, string regionId, List<ValidationError> errors)
    {
        if (!loader.Definitions.TryGetValue(regionId, out var region) || region.Kind != "region")
        {
            errors.Add(Error("WLD001", $"{regionId} is not a defined region", null));
            return null;
        }
        string? file = region.SourceFile;
        int before = errors.Count;
        void Check(bool ok, string code, string message)
        {
            if (!ok)
                errors.Add(Error(code, $"{regionId}: {message}", file));
        }

        try
        {
            var map = Read(region.YamlSource);
            var regionKey = RegionKey.Parse(Text(map, "region_key"));
            var cells = List(map, "cells").Select(c => CellKey.Parse(c as string ?? throw new FormatException("a cell key must be text"))).ToList();
            Check(cells.Count > 0, "WLD002", "a region covers at least one cell");
            Check(cells.All(c => c.Region == regionKey), "WLD002", $"every cell must be in {regionKey}");
            Check(cells.Distinct().Count() == cells.Count, "WLD002", "a cell is listed twice");

            var bounds = Map(map, "bounds_m");
            var (minX, minZ) = Pair(bounds, "min");
            var (maxX, maxZ) = Pair(bounds, "max");
            Check(minX < maxX && minZ < maxZ, "WLD002", "bounds_m min must be below max");
            Check(Covered(cells, minX, minZ, maxX, maxZ), "WLD002", "the walkable bounds must lie inside the region's cells");

            var terrain = BuildTerrain(Map(map, "terrain"));
            Check(terrain.OriginXMm <= minX && terrain.OriginZMm <= minZ && terrain.MaxXMm >= maxX && terrain.MaxZMm >= maxZ,
                "WLD003", "the terrain grid must cover the walkable bounds");

            var structures = List(map, "structures").Select((s, i) => Blocker(s as Dictionary<object, object>
                ?? throw new FormatException($"structures[{i}] must be a map"))).ToImmutableArray();
            Check(structures.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == structures.Length, "WLD002", "two structures share an id");

            var flags = loader.GetByKind("world_flag");
            var doors = ImmutableArray.CreateBuilder<DoorSite>();
            foreach (var (entry, i) in List(map, "doors").Select((d, i) => (d, i)))
            {
                var door = entry as Dictionary<object, object> ?? throw new FormatException($"doors[{i}] must be a map");
                string key = Text(door, "key");
                string flag = Text(door, "flag_ref");
                var footprint = Blocker(new Dictionary<object, object>(door) { ["id"] = key }) as BoxBlocker
                    ?? throw new FormatException($"door {key} needs a box_m footprint");
                Check(key.StartsWith("door.", StringComparison.Ordinal), "WLD004", $"door key '{key}' must start with 'door.'");
                Check(flags.ContainsKey(flag), "WLD004", $"door {key} names world flag '{flag}', which is not a declared world_flag");
                doors.Add(new DoorSite(key, flag, footprint));
            }
            Check(doors.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() == doors.Count, "WLD004", "two doors share a key");
            Check(doors.Select(d => d.FlagId).Distinct(StringComparer.Ordinal).Count() == doors.Count, "WLD004", "two doors share a world flag");

            var generation = Map(Map(map, "generation"), "terrain");
            var regionGeneration = new RegionGeneration(Int(generation, "base_height_mm"), Int(generation, "amplitude_mm"), Int(generation, "samples_per_axis"));
            Check(regionGeneration.TerrainSamplesPerAxis >= 2 && regionGeneration.TerrainAmplitudeMm >= 0, "WLD003",
                "generation.terrain needs at least 2 samples per axis and a non-negative amplitude");

            var locations = ImmutableArray.CreateBuilder<LocationSite>();
            foreach (var location in loader.GetByKind("location").Values.OrderBy(l => l.Id, StringComparer.Ordinal))
            {
                var place = Read(location.YamlSource);
                if (place.GetValueOrDefault("region_ref") as string != regionId)
                    continue;
                var anchor = List(Map(place, "anchor"), "world");
                if (anchor.Count != 3)
                    throw new FormatException($"{location.Id} anchor.world must be [x, y, z]");
                var discovery = Map(place, "discovery");
                var site = new LocationSite(location.Id, ToMm(anchor[0], "anchor.world"), ToMm(anchor[2], "anchor.world"),
                    Mm(discovery, "radius_m"), Long(discovery, "xp"));
                Check(site.XMm >= minX && site.XMm <= maxX && site.ZMm >= minZ && site.ZMm <= maxZ, "WLD005", $"{location.Id} lies outside the walkable bounds");
                Check(site.DiscoveryRadiusMm > 0 && site.DiscoveryXp >= 0, "WLD005", $"{location.Id} needs a positive discovery radius and non-negative XP");
                locations.Add(site);
            }

            var containers = ImmutableArray.CreateBuilder<ContainerSite>();
            foreach (var (entry, i) in (map.ContainsKey("containers") ? List(map, "containers") : new List<object>()).Select((c, i) => (c, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"containers[{i}] must be a map");
                var (cx, cz) = Pair(site, "position_m");
                var container = new ContainerSite(Text(site, "key"), Text(site, "loot_ref"), cx, cz, Int(site, "stack_slots"));
                Check(container.Key.StartsWith("container.", StringComparison.Ordinal), "WLD009", $"container key '{container.Key}' must start with 'container.'");
                Check(container.StackSlots >= 1, "WLD009", $"{container.Key} needs at least one stack slot");
                Check(cx >= minX && cx <= maxX && cz >= minZ && cz <= maxZ, "WLD009", $"{container.Key} lies outside the walkable bounds");
                Check(loader.GetByKind("loot").ContainsKey(container.LootTableId), "WLD009", $"{container.Key} names loot table '{container.LootTableId}', which is not defined");
                containers.Add(container);
            }
            Check(containers.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() == containers.Count, "WLD009", "two containers share a key");

            // Authored resource nodes and crafting stations (M3f).
            var nodes = ImmutableArray.CreateBuilder<NodeSite>();
            foreach (var (entry, i) in (map.ContainsKey("nodes") ? List(map, "nodes") : new List<object>()).Select((n, i) => (n, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"nodes[{i}] must be a map");
                var (nx, nz) = Pair(site, "position_m");
                var node = new NodeSite(Text(site, "name"), Text(site, "node_ref"), nx, nz);
                Check(node.Name.Length > 0 && node.Name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'), "WLD010",
                    $"node name '{node.Name}' must be lowercase snake_case");
                Check(nx >= minX && nx <= maxX && nz >= minZ && nz <= maxZ, "WLD010", $"node {node.Name} lies outside the walkable bounds");
                Check(loader.GetByKind("node").ContainsKey(node.NodeDefId), "WLD010", $"node {node.Name} names '{node.NodeDefId}', which is not a node");
                nodes.Add(node);
            }
            Check(nodes.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count() == nodes.Count, "WLD010", "two nodes share a name");
            var stations = ImmutableArray.CreateBuilder<StationSite>();
            foreach (var (entry, i) in (map.ContainsKey("stations") ? List(map, "stations") : new List<object>()).Select((s, i) => (s, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"stations[{i}] must be a map");
                var (sx, sz) = Pair(site, "position_m");
                var station = new StationSite(Text(site, "key"), Text(site, "kind"), sx, sz);
                Check(station.Key.StartsWith("station.", StringComparison.Ordinal), "WLD011", $"station key '{station.Key}' must start with 'station.'");
                Check(sx >= minX && sx <= maxX && sz >= minZ && sz <= maxZ, "WLD011", $"{station.Key} lies outside the walkable bounds");
                stations.Add(station);
            }
            Check(stations.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() == stations.Count, "WLD011", "two stations share a key");

            // The named NPCs (M4): each where they stand and which way they face; a named NPC stands in one place.
            var npcs = ImmutableArray.CreateBuilder<NpcSite>();
            foreach (var (entry, i) in (map.ContainsKey("npcs") ? List(map, "npcs") : new List<object>()).Select((n, i) => (n, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"npcs[{i}] must be a map");
                var (px, pz) = Pair(site, "position_m");
                long npcFacing = (long)Math.Round(Number(site, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
                var npc = new NpcSite(Text(site, "npc_ref"), px, pz, (int)Math.Clamp(npcFacing, 0, MoveIntent.FullTurnMdeg - 1));
                Check(loader.GetByKind("npc").ContainsKey(npc.NpcId), "WLD012", $"npcs[{i}] names '{npc.NpcId}', which is not an NPC");
                Check(npcFacing is >= 0 and < MoveIntent.FullTurnMdeg, "WLD012", $"{npc.NpcId}'s facing_deg must be in [0, 360)");
                Check(px >= minX && px <= maxX && pz >= minZ && pz <= maxZ, "WLD012", $"{npc.NpcId} stands outside the walkable bounds");
                npcs.Add(npc);
            }
            Check(npcs.Select(n => n.NpcId).Distinct(StringComparer.Ordinal).Count() == npcs.Count, "WLD012", "a named NPC stands in one place only");

            // Switches and barriers (M6): a switch stands on a structure and sets a flag in its cell; a barrier blocks until a flag
            // in its cell is set.
            var switches = ImmutableArray.CreateBuilder<SwitchSite>();
            foreach (var (entry, i) in (map.ContainsKey("switches") ? List(map, "switches") : new List<object>()).Select((s, i) => (s, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"switches[{i}] must be a map");
                string key = Text(site, "key");
                string structure = Text(site, "structure");
                var body = structures.FirstOrDefault(s => s.Id == structure);
                Check(body is not null, "WLD013", $"{key} stands on structure '{structure}', which the region does not have");
                var requires = (site.ContainsKey("requires") ? List(site, "requires") : new List<object>()).Select(r => r as string ?? "").ToImmutableArray();
                var sw = new SwitchSite(key, Text(site, "flag_ref"), body ?? new CircleBlocker(structure, 0, 0, 1, 0), requires, Text(site, "name"),
                    Text(site, "verb"), Text(site, "done_text"), site.GetValueOrDefault("locked_text") as string);
                Check(key.StartsWith("switch.", StringComparison.Ordinal), "WLD013", $"switch key '{key}' must start with 'switch.'");
                foreach (string flag in requires.Prepend(sw.FlagId))
                    Check(flags.ContainsKey(flag), "WLD013", $"{key} names world flag '{flag}', which is not a declared world_flag");
                Check(requires.IsEmpty || !string.IsNullOrWhiteSpace(sw.LockedText), "WLD013", $"{key} has requirements, so it says why it will not work (locked_text)");
                switches.Add(sw);
            }
            Check(switches.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() == switches.Count, "WLD013", "two switches share a key");
            Check(switches.Select(s => s.FlagId).Concat(doors.Select(d => d.FlagId)).Distinct(StringComparer.Ordinal).Count() == switches.Count + doors.Count,
                "WLD013", "two doors or switches share a world flag");
            // A requirement is read in the switch's cell, so something in that cell must set it - or the switch could never work.
            foreach (var sw in switches)
            {
                foreach (string flag in sw.Requires.Where(f => !switches.Any(o => o.FlagId == f && CellOf(o.Body) == CellOf(sw.Body))))
                    Check(false, "WLD013", $"{sw.Key} requires {flag}, which no switch in its cell sets");
            }

            var barriers = ImmutableArray.CreateBuilder<BarrierSite>();
            foreach (var (entry, i) in (map.ContainsKey("barriers") ? List(map, "barriers") : new List<object>()).Select((b, i) => (b, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"barriers[{i}] must be a map");
                string key = Text(site, "key");
                var barrier = new BarrierSite(key, Text(site, "flag_ref"), Blocker(new Dictionary<object, object>(site) { ["id"] = key }), Text(site, "prompt"));
                Check(key.StartsWith("barrier.", StringComparison.Ordinal), "WLD014", $"barrier key '{key}' must start with 'barrier.'");
                Check(flags.ContainsKey(barrier.FlagId), "WLD014", $"{key} names world flag '{barrier.FlagId}', which is not a declared world_flag");
                // Only a switch can lift a barrier in Phase 1 - a door is toggled by hand and would lift it both ways.
                Check(switches.Any(s => s.FlagId == barrier.FlagId && CellOf(s.Body) == CellOf(barrier.Footprint)), "WLD014",
                    $"{key} is lifted by {barrier.FlagId}, which no switch in its cell sets");
                barriers.Add(barrier);
            }
            Check(barriers.Select(b => b.Key).Distinct(StringComparer.Ordinal).Count() == barriers.Count, "WLD014", "two barriers share a key");

            // Build areas (M7): where the player may build. Their placement against the lattice and what they must keep clear of is
            // BLD007's; here, their shape.
            var areas = ImmutableArray.CreateBuilder<BuildAreaSite>();
            foreach (var (entry, i) in (map.ContainsKey("build_areas") ? List(map, "build_areas") : new List<object>()).Select((a, i) => (a, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"build_areas[{i}] must be a map");
                string key = Text(site, "key");
                var box = List(site, "box_m");
                if (box.Count != 4)
                    throw new FormatException($"{key}: box_m is [min_x, min_z, max_x, max_z]");
                var area = new BuildAreaSite(key, ToMm(box[0], key), ToMm(box[1], key), ToMm(box[2], key), ToMm(box[3], key), Int(site, "max_pieces"));
                Check(key.StartsWith("build_area.", StringComparison.Ordinal), "WLD015", $"build area key '{key}' must start with 'build_area.'");
                Check(area.MinXMm < area.MaxXMm && area.MinZMm < area.MaxZMm, "WLD015", $"{key}: box_m min must be below max");
                Check(area.MinXMm >= minX && area.MinZMm >= minZ && area.MaxXMm <= maxX && area.MaxZMm <= maxZ
                      && Covered(cells, area.MinXMm, area.MinZMm, area.MaxXMm, area.MaxZMm), "WLD015",
                    $"{key} must lie inside the walkable bounds and the region's cells");
                Check(area.MaxPieces >= 1, "WLD015", $"{key} must allow at least one piece (max_pieces)");
                areas.Add(area);
            }
            Check(areas.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count() == areas.Count, "WLD015", "two build areas share a key");

            var spawnMap = Map(map, "spawn");
            var (spawnX, spawnZ) = Pair(spawnMap, "position_m");
            long facingMdeg = (long)Math.Round(Number(spawnMap, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
            Check(facingMdeg is >= 0 and < MoveIntent.FullTurnMdeg, "WLD007", "spawn facing_deg must be in [0, 360)");

            var space = new WalkSpace(minX, minZ, maxX, maxZ, terrain, structures);
            var layout = new RegionLayout(regionId, cells.Select(c => c.ToString()).ToImmutableArray(), space, doors.ToImmutable(),
                locations.ToImmutable(), new Body(spawnX, terrain.HeightAtMm(spawnX, spawnZ), spawnZ, (int)facingMdeg), regionGeneration)
            {
                Containers = containers.ToImmutable(),
                Nodes = nodes.ToImmutable(),
                Stations = stations.ToImmutable(),
                Npcs = npcs.ToImmutable(),
                Switches = switches.ToImmutable(),
                Barriers = barriers.ToImmutable(),
                BuildAreas = areas.ToImmutable(),
            };

            // The spawn must stand clear with every door shut and every barrier standing, the harshest case. An NPC may stand
            // behind a barrier - that is what the fold is for - but not in a wall or a doorway.
            var closed = layout.ClosedDoors(_ => false);
            long radius = loader.Definitions.ContainsKey("config.base_speeds") ? BuildMovement(loader).BodyRadiusMm : 0;
            Check(Kinematics.IsClear(spawnX, spawnZ, radius, space, closed), "WLD007", "the spawn point must be inside the bounds and clear of every structure, door and barrier");
            var shut = layout.ClosedDoors(_ => false, _ => true);
            foreach (var npc in layout.Npcs)
                Check(Kinematics.IsClear(npc.XMm, npc.ZMm, radius, space, shut), "WLD012", $"{npc.NpcId} must stand clear of every structure and door");
            return errors.Count == before ? layout : null;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("WLD001", $"{regionId} is malformed: {e.Message}", file));
            return null;
        }
    }

    /// <summary>The cell a footprint stands in: a switch's or a barrier's flag lives in that cell's delta.</summary>
    private static CellKey CellOf(Blocker blocker)
    {
        var (x, z) = Footprints.Center(blocker);
        return CellKey.OfWorld(x / 1000.0, z / 1000.0);
    }

    private static TerrainGrid BuildTerrain(Dictionary<object, object> terrain)
    {
        var (originX, originZ) = Pair(terrain, "origin_m");
        long spacing = Mm(terrain, "spacing_m");
        int columns = Int(terrain, "columns"), rows = Int(terrain, "rows");
        var heightRows = List(terrain, "heights_m");
        if (heightRows.Count != rows)
            throw new FormatException($"terrain.heights_m has {heightRows.Count} rows; rows says {rows}");
        var heights = new List<long>(rows * columns);
        foreach (var (row, j) in heightRows.Select((r, j) => (r, j)))
        {
            var values = row as List<object> ?? throw new FormatException($"terrain.heights_m[{j}] must be a list");
            if (values.Count != columns)
                throw new FormatException($"terrain.heights_m[{j}] has {values.Count} values; columns says {columns}");
            heights.AddRange(values.Select(v => ToMm(v, $"terrain.heights_m[{j}]")));
        }
        return new TerrainGrid(originX, originZ, spacing, columns, rows, heights);
    }

    private static Blocker Blocker(Dictionary<object, object> map)
    {
        string id = Text(map, "id");
        long height = Mm(map, "height_m");
        // An overhang begins above the ground (the owner's M6 playtest): a crouched body passes under it.
        long clearance = map.ContainsKey("clearance_m") ? Mm(map, "clearance_m") : 0;
        if (clearance < 0 || (clearance > 0 && clearance >= height))
            throw new FormatException($"structure {id}: clearance_m must be at least 0, and below height_m");
        if (map.ContainsKey("box_m"))
        {
            var b = List(map, "box_m");
            if (b.Count != 4)
                throw new FormatException($"structure {id}: box_m is [min_x, min_z, max_x, max_z]");
            var box = new BoxBlocker(id, ToMm(b[0], id), ToMm(b[1], id), ToMm(b[2], id), ToMm(b[3], id), height) { ClearanceMm = clearance };
            if (box.MinXMm >= box.MaxXMm || box.MinZMm >= box.MaxZMm)
                throw new FormatException($"structure {id}: box_m min must be below max");
            return box;
        }
        var c = List(map, "circle_m");
        if (c.Count != 3)
            throw new FormatException($"structure {id}: circle_m is [x, z, radius]");
        var circle = new CircleBlocker(id, ToMm(c[0], id), ToMm(c[1], id), ToMm(c[2], id), height) { ClearanceMm = clearance };
        if (circle.RadiusMm <= 0)
            throw new FormatException($"structure {id}: circle_m radius must be positive");
        return circle;
    }

    private static void CheckFlag(ContentEnvelope flag, List<ValidationError> errors)
    {
        var map = Read(flag.YamlSource);
        string? type = map.GetValueOrDefault("type") as string;
        // The cell delta stores a flag's divergence from 0 (PERSISTENCE.md §5.2), so a flag starts at 0.
        string? initial = map.GetValueOrDefault("default") as string;
        if (type is not ("bool" or "int"))
            errors.Add(Error("WLD006", $"{flag.Id} must declare type bool or int; string flags have no cell-delta encoding yet", flag.SourceFile));
        if (initial is not ("0" or "false"))
            errors.Add(Error("WLD006", $"{flag.Id} must default to 0 (false): the cell delta records divergence from 0", flag.SourceFile));
    }

    private static bool Covered(List<CellKey> cells, long minX, long minZ, long maxX, long maxZ)
    {
        const long size = WorldMath.CellSizeMeters * 1000L;
        // Every 1 mm-inset corner of every cell-sized tile of the bounds must fall in a listed cell.
        for (long x = minX; x < maxX; x += size)
        for (long z = minZ; z < maxZ; z += size)
        {
            foreach (var (px, pz) in new[] { (x, z), (Math.Min(x + size, maxX) - 1, z), (x, Math.Min(z + size, maxZ) - 1), (Math.Min(x + size, maxX) - 1, Math.Min(z + size, maxZ) - 1) })
            {
                if (!cells.Contains(CellKey.OfWorld(px / 1000.0, pz / 1000.0)))
                    return false;
            }
        }
        return true;
    }

    private static void TryBuild(Func<object> build, string id, ContentLoader loader, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("WLD008", $"{id}: {e.Message}", loader.Definitions.GetValueOrDefault(id)?.SourceFile));
        }
    }

    private static Dictionary<object, object> Config(ContentLoader loader, string id) =>
        loader.Definitions.TryGetValue(id, out var definition)
            ? Read(definition.YamlSource)
            : throw new KeyNotFoundException($"{id} is missing (DATA_MODEL.md §4.19)");

    private static Dictionary<object, object> Read(string? yaml) =>
        string.IsNullOrEmpty(yaml) ? new Dictionary<object, object>() : Yaml.Deserialize<Dictionary<object, object>>(yaml) ?? new Dictionary<object, object>();

    private static Dictionary<object, object> Map(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is Dictionary<object, object> inner
            ? inner
            : throw new KeyNotFoundException($"'{key}' must be a map");

    private static List<object> List(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value)
            ? value as List<object> ?? (value is null ? new List<object>() : throw new FormatException($"'{key}' must be a list"))
            : throw new KeyNotFoundException($"'{key}' is missing");

    private static string Text(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : throw new KeyNotFoundException($"'{key}' is missing");

    private static (long X, long Z) Pair(Dictionary<object, object> map, string key)
    {
        var values = List(map, key);
        return values.Count == 2 ? (ToMm(values[0], key), ToMm(values[1], key)) : throw new FormatException($"'{key}' must be [x, z]");
    }

    private static int Int(Dictionary<object, object> map, string key) =>
        int.TryParse(Scalar(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"'{key}' must be a whole number");

    private static long Long(Dictionary<object, object> map, string key) =>
        long.TryParse(Scalar(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FormatException($"'{key}' must be a whole number");

    private static double Number(Dictionary<object, object> map, string key) =>
        double.TryParse(Scalar(map, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{key}' must be a number");

    /// <summary>Metres in content, whole millimetres in the domain.</summary>
    private static long Mm(Dictionary<object, object> map, string key) => (long)Math.Round(Number(map, key) * 1000, MidpointRounding.AwayFromZero);

    private static long ToMm(object value, string what) =>
        value is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres)
            ? (long)Math.Round(metres * 1000, MidpointRounding.AwayFromZero)
            : throw new FormatException($"'{what}' must hold numbers");

    private static string Scalar(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string text ? text : throw new KeyNotFoundException($"'{key}' is missing");

    private static ValidationError Error(string code, string message, string? file) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = file ?? string.Empty,
    };
}
