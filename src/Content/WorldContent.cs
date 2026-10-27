// UNNAMED Content - the region layout, places, world flags and movement rules from content
// (WORLD_ARCHITECTURE.md §4, DATA_MODEL.md §1, §4.18, §4.19; PROTOTYPE.md §3, §4.4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using UNNAMED.World;
using UNNAMED.World.Runtime;
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
            TryBuild(() => BuildTravel(loader), "config.travel", loader, errors);
            TryBuild(() => BuildOffline(loader), "config.time", loader, errors);
            TryBuild(() => BuildCalendar(loader), "config.time", loader, errors);
            TryBuild(() => BuildWeather(loader), "config.weather", loader, errors);
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
        // Standing on structure tops (owner finding 50) and what is vaulted, climbed and scrambled (BRIEF D6).
        if (map.ContainsKey("step_up_m"))
            rules = rules with
            {
                StepUpMm = Mm(map, "step_up_m"),
                MantleMm = map.ContainsKey("mantle_reach_m") ? Mm(map, "mantle_reach_m") : 0,
                VaultMm = map.ContainsKey("vault_m") ? Mm(map, "vault_m") : 0,
                ScrambleGradeMmPerM = map.ContainsKey("scramble_grade_mm_per_m") ? Mm(map, "scramble_grade_mm_per_m") : 650,
                ScramblePercent = map.ContainsKey("scramble_percent") ? Int(map, "scramble_percent") : 45,
                SheerGradeMmPerM = map.ContainsKey("sheer_grade_mm_per_m") ? Mm(map, "sheer_grade_mm_per_m") : 1_500,
                ClimbPrefixes = map.ContainsKey("climb_prefixes") ? List(map, "climb_prefixes").Select(v => v as string ?? "").Where(v => v.Length > 0).ToImmutableArray() : ImmutableArray<string>.Empty,
                UnclimbablePrefixes = map.ContainsKey("unclimbable_prefixes") ? List(map, "unclimbable_prefixes").Select(v => v as string ?? "").Where(v => v.Length > 0).ToImmutableArray() : ImmutableArray<string>.Empty,
            };
        // A vault is a mantle taken on the move, so it sits between the step and the climb; the mantle reaches above the vault.
        if (rules.StepUpMm < 0 || rules.VaultMm < 0 || rules.VaultMm < rules.StepUpMm
            || rules.MantleMm < 0 || rules.MantleMm > 0 && rules.MantleMm <= rules.VaultMm
            || rules.ScrambleGradeMmPerM < 0 || rules.SheerGradeMmPerM <= rules.ScrambleGradeMmPerM
            || rules.ScramblePercent is <= 0 or > 100)
            throw new FormatException("config.base_speeds step_up_m is at least 0, vault_m at least the step and no more than the mantle, and mantle_reach_m, if any, above the vault");
        if (rules.JumpApexMm <= 0 || rules.JumpRiseMs <= 0 || rules.JumpTuckRadiusMm <= 0 || rules.JumpTuckRadiusMm > rules.BodyRadiusMm
            || rules.CrouchHeightMm <= 0 || rules.CrouchHeightMm >= rules.StandHeightMm
            || rules.CrouchPercent is <= 0 or > 100)
            throw new FormatException("config.base_speeds needs a positive jump apex and rise, a tuck radius no wider than the body, a crouched height below the standing one, and crouch_percent in 1..100");
        return rules;
    }

    public static TierRules BuildTiers(ContentLoader loader)
    {
        var map = Config(loader, "config.simulation_tiers");
        var rules = new TierRules(Mm(map, "full_radius_m"), Mm(map, "regional_radius_m"), Mm(map, "abstract_radius_m"), Mm(map, "hysteresis_m"))
        {
            // The NPC budgets (WP12) are performance budgets for admission, never population limits; a pack without them keeps 60 and 300.
            FullBudget = map.ContainsKey("full_budget") ? Int(map, "full_budget") : 60,
            RegionalBudget = map.ContainsKey("regional_budget") ? Int(map, "regional_budget") : 300,
        };
        if (rules.FullBudget < 1 || rules.RegionalBudget < 1)
            throw new FormatException("config.simulation_tiers' budgets admit at least one NPC each");
        if (!(0 < rules.FullRadiusMm && rules.FullRadiusMm < rules.RegionalRadiusMm && rules.RegionalRadiusMm < rules.AbstractRadiusMm)
            || rules.HysteresisMm < 0 || rules.HysteresisMm * 2 >= rules.FullRadiusMm)
            throw new FormatException("config.simulation_tiers needs 0 < full < regional < abstract radii and a hysteresis under half the full radius");
        return rules;
    }

    /// <summary>Waystone travel's tuning (S-38), from <c>config.travel</c>; a pack from before Waystone travel keeps the defaults.</summary>
    public static TravelRules BuildTravel(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.travel"))
            return TravelRules.Default;
        var map = Config(loader, "config.travel");
        var rules = new TravelRules(Mm(map, "road_walked_within_m"), Text(map, "touch_text"));
        if (rules.RoadWalkedWithinMm <= 0)
            throw new FormatException("config.travel needs a positive road_walked_within_m");
        return rules;
    }

    /// <summary>Display names: every definition's <c>name</c> field, where it has one (DATA_MODEL.md §4.18 places do).</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames(ContentLoader loader) =>
        loader.Definitions.Values
            .Select(d => (d.Id, Name: DisplayName(loader, d)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .ToDictionary(d => d.Id, d => d.Name!, StringComparer.Ordinal);

    private static string? DisplayName(ContentLoader loader, ContentEnvelope definition)
    {
        if (loader.NameHeaders.TryGetValue(definition.Id, out var header) && header.Source == definition.YamlSource)
            return header.Name;
        string? name = Read(definition.YamlSource).GetValueOrDefault("name") as string;
        loader.NameHeaders[definition.Id] = (definition.YamlSource, name);
        return name;
    }

    /// <summary>The fixed domain tick in milliseconds, from <c>config.time</c>'s <c>ticks_per_second</c>.</summary>
    public static int TickMilliseconds(ContentLoader loader)
    {
        var map = Config(loader, "config.time");
        int perSecond = Int(map, "ticks_per_second");
        if (perSecond <= 0 || 1000 % perSecond != 0)
            throw new FormatException($"config.time ticks_per_second {perSecond} must divide 1000 ms exactly");
        return 1000 / perSecond;
    }

    /// <summary>What the world clock reads at tick 0, in hours, from <c>config.time</c>'s <c>clock_at_world_start_h</c>: the sky and the
    /// creatures' hours both run from it.</summary>
    public static double ClockAtWorldStartHours(ContentLoader loader)
    {
        var map = Config(loader, "config.time");
        double hour = Number(map, "clock_at_world_start_h");
        double hoursPerDay = Number(map, "game_hours_per_day");
        if (hour < 0 || hour >= hoursPerDay)
            throw new FormatException($"config.time clock_at_world_start_h {hour} is an hour of the day, from 0 to {hoursPerDay}");
        return hour;
    }

    /// <summary>The world clock's day (<c>config.time</c>): its length in ticks, and where midnight falls from <c>clock_at_world_start_h</c>.</summary>
    public static WorldClock BuildClock(ContentLoader loader)
    {
        var map = Config(loader, "config.time");
        double ticksPerHour = Int(map, "ticks_per_second") * Number(map, "seconds_per_game_minute") * Number(map, "game_minutes_per_hour");
        double hoursPerDay = Number(map, "game_hours_per_day");
        long ticksPerDay = (long)Math.Round(ticksPerHour * hoursPerDay, MidpointRounding.AwayFromZero);
        if (ticksPerDay <= 0)
            throw new FormatException("config.time gives a world day of no ticks");
        if (hoursPerDay < 1 || hoursPerDay != Math.Floor(hoursPerDay))
            throw new FormatException($"config.time game_hours_per_day {hoursPerDay} must be a whole number of hours");
        return new WorldClock(ticksPerDay, (long)Math.Round(ClockAtWorldStartHours(loader) * ticksPerHour, MidpointRounding.AwayFromZero))
        {
            HoursPerDay = (int)hoursPerDay,
        };
    }

    /// <summary>
    /// The calendar (<c>config.time</c>'s <c>days_per_tally</c>, <c>tallies_per_reckoning</c> and <c>hollow_days_per_reckoning</c>; WP7). A
    /// pack that declares none counts the canon Reckoning.
    /// </summary>
    public static CalendarRules BuildCalendar(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.time"))
            return CalendarRules.Default;
        var map = Config(loader, "config.time");
        if (!map.ContainsKey("days_per_tally") && !map.ContainsKey("tallies_per_reckoning") && !map.ContainsKey("hollow_days_per_reckoning"))
            return CalendarRules.Default;
        return new CalendarRules(Int(map, "days_per_tally"), Int(map, "tallies_per_reckoning"), Int(map, "hollow_days_per_reckoning")).Validated();
    }

    /// <summary>
    /// How the weather's fronts are laid out and drawn (<c>config.weather</c>; WP7). A pack without it has D6's weather. Every state named
    /// in <c>weights</c> must be one the simulation knows.
    /// </summary>
    public static WeatherRules BuildWeather(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.weather"))
            return WeatherRules.Default;
        var map = Config(loader, "config.weather");
        var weights = new Dictionary<WeatherState, int>();
        foreach (var (key, _) in Map(map, "weights"))
        {
            string name = key as string ?? throw new FormatException("a weather weight is named by its state");
            weights[WeatherStates.Parse(name) ?? throw new FormatException($"config.weather weights '{name}', which is not a weather state")] =
                Int(Map(map, "weights"), name);
        }
        return new WeatherRules(Int(map, "front_hours_min"), Int(map, "front_hours_max"), Int(map, "span_hours"),
            weights.ToImmutableSortedDictionary()).Validated();
    }

    /// <summary>
    /// The catch-up cap and the login grace (<c>config.time</c>'s <c>offline_cap_game_days</c> and <c>login_grace_seconds</c>; WP11). A pack
    /// that declares neither catches nothing up and gives no grace.
    /// </summary>
    public static OfflineRules BuildOffline(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.time"))
            return OfflineRules.None;
        var map = Config(loader, "config.time");
        if (!map.ContainsKey("offline_cap_game_days") && !map.ContainsKey("login_grace_seconds"))
            return OfflineRules.None;
        double days = Number(map, "offline_cap_game_days"), grace = Number(map, "login_grace_seconds");
        if (days < 0 || grace < 0)
            throw new FormatException("config.time offline_cap_game_days and login_grace_seconds are never negative");
        return new OfflineRules((long)Math.Round(days * BuildClock(loader).TicksPerDay, MidpointRounding.AwayFromZero),
            (long)Math.Round(grace * Int(map, "ticks_per_second"), MidpointRounding.AwayFromZero));
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
            // `space:` is the space this region's coordinates belong to (M8 D10). Absent means the exterior,
            // which is every region that existed before M8 and every one that is part of the world; an
            // interior - the Barrowworks' thirteen rooms - names its own. Its cells then have to be written
            // space-qualified, and CellKey.Parse has read that prefix since P1.
            var spaceId = map.TryGetValue("space", out var rawSpace) && rawSpace is string spaceName
                ? SpaceId.Parse(spaceName)
                : SpaceId.Exterior;
            var cells = List(map, "cells").Select(c => CellKey.Parse(c as string ?? throw new FormatException("a cell key must be text"))).ToList();
            Check(cells.Count > 0, "WLD002", "a region covers at least one cell");
            Check(cells.All(c => c.Region == regionKey), "WLD002", $"every cell must be in {regionKey}");
            Check(cells.Distinct().Count() == cells.Count, "WLD002", "a cell is listed twice");
            // A region is one space, so a cell in another one is a content error rather than something to
            // reconcile: the two would share no flags, no deltas and no random streams.
            Check(cells.All(c => c.Space == spaceId), "WLD002",
                spaceId.IsExterior
                    ? "an exterior region must list exterior cells"
                    : $"every cell must be in {spaceId}");

            var bounds = Map(map, "bounds_m");
            var (minX, minZ) = Pair(bounds, "min");
            var (maxX, maxZ) = Pair(bounds, "max");
            Check(minX < maxX && minZ < maxZ, "WLD002", "bounds_m min must be below max");
            Check(Covered(cells, spaceId, minX, minZ, maxX, maxZ), "WLD002", "the walkable bounds must lie inside the region's cells");

            var terrain = BuildTerrain(Map(map, "terrain"));
            Check(terrain.OriginXMm <= minX && terrain.OriginZMm <= minZ && terrain.MaxXMm >= maxX && terrain.MaxZMm >= maxZ,
                "WLD003", "the terrain grid must cover the walkable bounds");

            // What a body may climb (owner finding 50, widened by BRIEF D6): everything except the deliberate barriers - the fold and the
            // Other's walls, and sealed doors. climb_prefixes, when a pack names some, adds faces with handholds beyond the mantle reach.
            var speeds = Config(loader, "config.base_speeds");
            var climbable = speeds is not null && speeds.ContainsKey("climb_prefixes")
                ? List(speeds, "climb_prefixes").Select(p => p as string ?? throw new FormatException("climb_prefixes are id prefixes")).ToList()
                : new List<string>();
            var unclimbable = speeds is not null && speeds.ContainsKey("unclimbable_prefixes")
                ? List(speeds, "unclimbable_prefixes").Select(p => p as string ?? throw new FormatException("unclimbable_prefixes are id prefixes")).ToList()
                : new List<string>();
            var structures = List(map, "structures").Select((s, i) => Blocker(s as Dictionary<object, object>
                ?? throw new FormatException($"structures[{i}] must be a map")))
                // Everything is climbable except the deliberate barriers (BRIEF D6): the fold and the Other's walls, and sealed doors.
                // climb_prefixes, when a pack names some, is read above and adds nothing here - the reach is the mantle's.
                .Select(b => unclimbable.Any(p => b.Id.StartsWith(p, StringComparison.Ordinal)) ? b with { Climbable = false } : b with { Climbable = true })
                .ToImmutableArray();
            Check(structures.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == structures.Length, "WLD002", "two structures share an id");

            var flags = loader.GetByKind("world_flag");
            SiteFlagRequirement Requirement(Dictionary<object, object> data)
            {
                string flag = Text(data, "flag_ref"), location = Text(data, "location_ref");
                Check(flags.ContainsKey(flag), "WLD013", $"requirement names undefined world flag '{flag}'");
                Check(loader.GetByKind("location").ContainsKey(location), "WLD013", $"requirement names undefined location '{location}'");
                return new SiteFlagRequirement(flag, location);
            }
            SiteItem SiteItem(Dictionary<object, object> data)
            {
                string item = Text(data, "item_ref");
                int count = Int(data, "count");
                Check(loader.GetByKind("item").ContainsKey(item) && count > 0, "WLD013", $"site item '{item}' needs a defined item and positive count");
                return new SiteItem(item, count);
            }
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
                var locked = door.GetValueOrDefault("locked_text") as string;
                var latch = door.GetValueOrDefault("latch_side") as string;
                Check(latch is null or "north" or "south" or "east" or "west", "WLD004", $"{key}: invalid latch_side");
                Check((latch is null && !door.ContainsKey("requirement") && !door.ContainsKey("fitted")) || !string.IsNullOrWhiteSpace(locked), "WLD004", $"{key}: a locked door needs locked_text");
                bool staysOpen = door.ContainsKey("stays_open") && bool.Parse(door["stays_open"].ToString()!);
                Check(!door.ContainsKey("fitted") || staysOpen, "WLD004", $"{key}: a door that holds a fitted item must stay open");
                doors.Add(new DoorSite(key, flag, footprint)
                {
                    Requirement = door.ContainsKey("requirement") ? Requirement(Map(door, "requirement")) : null,
                    LatchSide = latch,
                    LockedText = locked,
                    StaysOpen = staysOpen,
                    Fitted = door.ContainsKey("fitted") ? SiteItem(Map(door, "fitted")) : null,
                });
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
                    Mm(discovery, "radius_m"), Long(discovery, "xp"))
                {
                    RevealRadiusMm = place.ContainsKey("map") ? Mm(Map(place, "map"), "reveals_radius_m") : 0,
                };
                Check(site.XMm >= minX && site.XMm <= maxX && site.ZMm >= minZ && site.ZMm <= maxZ, "WLD005", $"{location.Id} lies outside the walkable bounds");
                Check(site.DiscoveryRadiusMm > 0 && site.DiscoveryXp >= 0, "WLD005", $"{location.Id} needs a positive discovery radius and non-negative XP");
                locations.Add(site);
            }

            var containers = ImmutableArray.CreateBuilder<ContainerSite>();
            foreach (var (entry, i) in (map.ContainsKey("containers") ? List(map, "containers") : new List<object>()).Select((c, i) => (c, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"containers[{i}] must be a map");
                var (cx, cz) = Pair(site, "position_m");
                var container = new ContainerSite(Text(site, "key"), Text(site, "loot_ref"), cx, cz, Int(site, "stack_slots"))
                {
                    EmptyFlag = site.GetValueOrDefault("flag_ref") as string,
                    Stash = site.ContainsKey("stash") && bool.Parse(site["stash"].ToString()!),
                };
                Check(container.EmptyFlag is null || flags.ContainsKey(container.EmptyFlag), "WLD009", $"{container.Key}: flag_ref must name a world flag");
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
                var station = new StationSite(Text(site, "key"), Text(site, "kind"), sx, sz)
                {
                    Grade = site.ContainsKey("grade") ? Int(site, "grade") : 0,
                };
                Check(station.Key.StartsWith("station.", StringComparison.Ordinal), "WLD011", $"station key '{station.Key}' must start with 'station.'");
                Check(station.Grade >= 0, "WLD011", $"{station.Key}: grade is 0 (a common station) or more (upgraded)");
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
                    Text(site, "verb"), Text(site, "done_text"), site.GetValueOrDefault("locked_text") as string)
                {
                    Requirements = site.ContainsKey("requirements") ? List(site, "requirements").Select(r => Requirement((Dictionary<object, object>)r)).ToImmutableArray() : default,
                    Excludes = site.ContainsKey("excludes") ? List(site, "excludes").Select(r => (string)r).ToImmutableArray() : default,
                    Consume = site.ContainsKey("consume") ? SiteItem(Map(site, "consume")) : null,
                    Reward = site.ContainsKey("reward") ? SiteItem(Map(site, "reward")) : null,
                    Learn = site.GetValueOrDefault("recipe_ref") as string,
                };
                Check(key.StartsWith("switch.", StringComparison.Ordinal), "WLD013", $"switch key '{key}' must start with 'switch.'");
                foreach (string flag in requires.Prepend(sw.FlagId))
                    Check(flags.ContainsKey(flag), "WLD013", $"{key} names world flag '{flag}', which is not a declared world_flag");
                Check(requires.IsEmpty || !string.IsNullOrWhiteSpace(sw.LockedText), "WLD013", $"{key} has requirements, so it says why it will not work (locked_text)");
                if (!sw.Excludes.IsDefaultOrEmpty)
                    foreach (var flag in sw.Excludes)
                        Check(flags.ContainsKey(flag), "WLD013", $"{key}: undefined exclusion '{flag}'");
                if (sw.Learn is { } learned)
                    Check(loader.GetByKind("recipe").ContainsKey(learned), "WLD013", $"{key}: recipe_ref must name a recipe");
                Check((sw.Requirements.IsDefaultOrEmpty && sw.Excludes.IsDefaultOrEmpty && sw.Consume is null) || !string.IsNullOrWhiteSpace(sw.LockedText),
                    "WLD013", $"{key}: guarded interaction needs locked_text");
                switches.Add(sw);
            }
            Check(switches.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() == switches.Count, "WLD013", "two switches share a key");
            Check(switches.Select(s => s.FlagId).Concat(doors.Select(d => d.FlagId)).Distinct(StringComparer.Ordinal).Count() == switches.Count + doors.Count,
                "WLD013", "two doors or switches share a world flag");
            // A requirement is read in the switch's cell, so something in that cell must set it - or the switch could never work.
            foreach (var sw in switches)
            {
                foreach (string flag in sw.Requires.Where(f => !switches.Any(o => o.FlagId == f && CellOf(spaceId, o.Body) == CellOf(spaceId, sw.Body))))
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
                Check(switches.Any(s => s.FlagId == barrier.FlagId && CellOf(spaceId, s.Body) == CellOf(spaceId, barrier.Footprint)), "WLD014",
                    $"{key} is lifted by {barrier.FlagId}, which no switch in its cell sets");
                barriers.Add(barrier);
            }
            Check(barriers.Select(b => b.Key).Distinct(StringComparer.Ordinal).Count() == barriers.Count, "WLD014", "two barriers share a key");

            // Readables (spec 41): something written, standing where it can be read. Each names the lore entry it files - one page of it, or
            // the whole book (page 0) - and stands on its own footprint, so the reach to it is the reach to a switch.
            var readables = ImmutableArray.CreateBuilder<ReadableSite>();
            foreach (var (entry, i) in (map.ContainsKey("readables") ? List(map, "readables") : new List<object>()).Select((r, i) => (r, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"readables[{i}] must be a map");
                string key = Text(site, "key");
                string entryRef = Text(site, "lore_ref");
                int page = site.ContainsKey("page") ? Int(site, "page") : 0;
                var readable = new ReadableSite(key, entryRef, page, Blocker(new Dictionary<object, object>(site) { ["id"] = key }),
                    Text(site, "name"), Text(site, "verb"));
                Check(key.StartsWith("readable.", StringComparison.Ordinal), "WLD016", $"readable key '{key}' must start with 'readable.'");
                Check(loader.Definitions.TryGetValue(entryRef, out var written) && written.Kind == "lore", "WLD016",
                    $"{key} names '{entryRef}', which is not a lore entry this pack has");
                Check(page >= 0, "WLD016", $"{key} names page {page}; a page is 1 or more, and 0 is the whole book");
                // What is written in the world is read in the world's words (spec 41, owner ruling): no prompt says prototype or design.
                Check(UNNAMED.Domain.Knowledge.LoreRules.Leak(readable.Name) is null, "WLD016",
                    $"{key}'s name reads '{UNNAMED.Domain.Knowledge.LoreRules.Leak(readable.Name)}', which is the trade's word and not the world's");
                Check(UNNAMED.Domain.Knowledge.LoreRules.Leak(readable.Verb) is null, "WLD016",
                    $"{key}'s verb reads '{UNNAMED.Domain.Knowledge.LoreRules.Leak(readable.Verb)}', which is the trade's word and not the world's");
                readables.Add(readable);
            }
            Check(readables.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() == readables.Count, "WLD016", "two readables share a key");
            Check(readables.Select(r => r.Body).All(b => b is CircleBlocker or BoxBlocker), "WLD016", "a readable stands on a circle_m or a box_m");

            // Portals (M8 D10): the ways into an interior space. A region may declare them whenever its
            // entrances are authored; whether they are registered at all is the run's decision, and the gate
            // that makes it is in GameSession, not here.
            var portals = ImmutableArray.CreateBuilder<SpacePortal>();
            foreach (var (entry, i) in (map.ContainsKey("portals") ? List(map, "portals") : new List<object>()).Select((p, i) => (p, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"portals[{i}] must be a map");
                string key = Text(site, "key");
                var portalSpace = SpaceId.Parse(Text(site, "space"));
                var (exteriorX, exteriorZ) = Pair(site, "exterior_m");
                var (interiorX, interiorZ) = Pair(site, "interior_m");
                Check(key.StartsWith("portal.", StringComparison.Ordinal), "WLD015", $"portal key '{key}' must start with 'portal.'");
                Check(!portalSpace.IsExterior, "WLD015", $"{key} leads to the exterior, which is entered through nothing");
                // The exterior anchor is a place in this region, so it has to be walkable ground in it.
                Check(exteriorX >= minX && exteriorX <= maxX && exteriorZ >= minZ && exteriorZ <= maxZ, "WLD015",
                    $"{key}'s exterior anchor is outside the walkable bounds");
                PortalGivesWay? givesWay = null;
                if (site.ContainsKey("gives_way"))
                {
                    var gives = Map(site, "gives_way");
                    long drop = (long)Math.Round(Number(gives, "drop_m") * 1000);
                    Check(drop > 0, "WLD015", $"{key}'s gives_way needs a drop_m above zero");
                    givesWay = new PortalGivesWay(Requirement(gives), drop);
                }
                portals.Add(new SpacePortal(key, portalSpace, exteriorX, exteriorZ, interiorX, interiorZ)
                {
                    ReachableFlag = site.ContainsKey("reachable_flag") ? Requirement(Map(site, "reachable_flag")) : null,
                    OpensWith = site.ContainsKey("opens_with") ? Requirement(Map(site, "opens_with")) : null,
                    GivesWay = givesWay,
                });
            }
            Check(portals.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() == portals.Count, "WLD015", "two portals share a key");
            // Waystones (S-38): each stands on a structure, beside a place, with a clear spot to arrive at; a dormant one names the flag
            // that wakes it in its cell and the line of a conversation that holds its rite.
            long bodyRadius = loader.Definitions.ContainsKey("config.base_speeds") ? BuildMovement(loader).BodyRadiusMm : 0;
            var waystones = ImmutableArray.CreateBuilder<WaystoneSite>();
            foreach (var (entry, i) in (map.ContainsKey("waystones") ? List(map, "waystones") : new List<object>()).Select((w, i) => (w, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"waystones[{i}] must be a map");
                string key = Text(site, "key");
                string structure = Text(site, "structure");
                var body = structures.FirstOrDefault(s => s.Id == structure);
                Check(body is not null, "WLD015", $"{key} stands on structure '{structure}', which the region does not have");
                var (ax, az) = Pair(site, "arrival_m");
                string place = Text(site, "location_ref");
                var stone = new WaystoneSite(key, Text(site, "name"), body ?? new CircleBlocker(structure, 0, 0, 1, 0), place, ax, az);
                Check(key.StartsWith("waystone.", StringComparison.Ordinal), "WLD015", $"waystone key '{key}' must start with 'waystone.'");
                Check(locations.Any(l => l.Id == place), "WLD015", $"{key} names place '{place}', which is not a location in this region");
                Check(Kinematics.IsClear(ax, az, bodyRadius, new WalkSpace(minX, minZ, maxX, maxZ, terrain, structures), ImmutableArray<Blocker>.Empty, spaceId.IsExterior ? 0 : 1800),
                    "WLD015", $"{key}'s arrival_m must be inside the bounds and clear of every structure");
                if (site.ContainsKey("dormant"))
                {
                    var dormant = Map(site, "dormant");
                    var rite = Map(dormant, "rite");
                    var sleeping = new DormantStone(Text(dormant, "flag_ref"), Text(rite, "dialogue_ref"), Text(rite, "node"), Text(dormant, "dormant_text"),
                        Text(dormant, "wake_text"));
                    stone = stone with { Dormant = sleeping };
                    Check(flags.ContainsKey(sleeping.FlagId), "WLD015", $"{key} is woken by world flag '{sleeping.FlagId}', which is not a declared world_flag");
                    Check(RiteExists(loader, sleeping), "WLD015",
                        $"{key}'s rite is line '{sleeping.RiteNodeId}' of '{sleeping.RiteDialogueId}', which that conversation does not have");
                }
                waystones.Add(stone);
            }
            Check(waystones.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count() == waystones.Count, "WLD015", "two waystones share a key");
            var wakeFlags = waystones.Where(w => w.Dormant is not null).Select(w => w.Dormant!.FlagId).ToList();
            Check(wakeFlags.Concat(switches.Select(s => s.FlagId)).Concat(doors.Select(d => d.FlagId)).Distinct(StringComparer.Ordinal).Count()
                  == wakeFlags.Count + switches.Count + doors.Count, "WLD015", "a waystone shares its world flag with another interactable");

            var roads = ImmutableArray.CreateBuilder<RoadSite>();
            foreach (var (entry, i) in (map.ContainsKey("roads") ? List(map, "roads") : new List<object>()).Select((r, i) => (r, i)))
            {
                var site = entry as Dictionary<object, object> ?? throw new FormatException($"roads[{i}] must be a map");
                string id = Text(site, "id");
                var points = List(site, "points_m").Select((p, j) =>
                {
                    var pair = p as List<object> ?? throw new FormatException($"{id} points_m[{j}] must be [x, z]");
                    return pair.Count == 2 ? (ToMm(pair[0], id), ToMm(pair[1], id)) : throw new FormatException($"{id} points_m[{j}] must be [x, z]");
                }).ToImmutableArray();
                Check(id.StartsWith("road.", StringComparison.Ordinal) && !id.Contains('#'), "WLD016", $"road id '{id}' must start with 'road.' and hold no '#'");
                Check(points.Length >= 2, "WLD016", $"{id} needs at least two points");
                Check(points.All(p => p.Item1 >= minX && p.Item1 <= maxX && p.Item2 >= minZ && p.Item2 <= maxZ), "WLD016", $"{id} leaves the walkable bounds");
                roads.Add(new RoadSite(id, Text(site, "name"), points));
            }
            Check(roads.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == roads.Count, "WLD016", "two roads share an id");

            var spawnMap = Map(map, "spawn");
            var (spawnX, spawnZ) = Pair(spawnMap, "position_m");
            long facingMdeg = (long)Math.Round(Number(spawnMap, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
            Check(facingMdeg is >= 0 and < MoveIntent.FullTurnMdeg, "WLD007", "spawn facing_deg must be in [0, 360)");
            // The opening's start (PJ06 §5.1): optional, and held to the spawn's rules below.
            Body? opening = null;
            if (map.ContainsKey("opening"))
            {
                var openingMap = Map(map, "opening");
                var (openingX, openingZ) = Pair(openingMap, "position_m");
                long openingMdeg = (long)Math.Round(Number(openingMap, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
                Check(openingMdeg is >= 0 and < MoveIntent.FullTurnMdeg, "WLD007", "opening facing_deg must be in [0, 360)");
                opening = new Body(openingX, terrain.HeightAtMm(openingX, openingZ), openingZ, (int)openingMdeg);
            }

            var supports = map.ContainsKey("walkable_surfaces") ? List(map,"walkable_surfaces")
                .Select(s => BuildTerrain(s as Dictionary<object,object> ?? throw new FormatException("walkable_surfaces entries must be terrain grids")))
                .ToImmutableArray() : ImmutableArray<TerrainGrid>.Empty;
            foreach (var surface in supports)
                Check(surface.OriginXMm >= minX && surface.OriginZMm >= minZ && surface.MaxXMm <= maxX && surface.MaxZMm <= maxZ,
                    "WLD003", "walkable surfaces must lie within the region bounds");
            var space = new WalkSpace(minX, minZ, maxX, maxZ, terrain, structures) { WalkableSurfaces = supports };
            var layout = new RegionLayout(regionId, cells.Select(c => c.ToString()).ToImmutableArray(), space, doors.ToImmutable(),
                locations.ToImmutable(), new Body(spawnX, terrain.HeightAtMm(spawnX, spawnZ), spawnZ, (int)facingMdeg), regionGeneration)
            {
                Containers = containers.ToImmutable(),
                Nodes = nodes.ToImmutable(),
                Stations = stations.ToImmutable(),
                Npcs = npcs.ToImmutable(),
                Switches = switches.ToImmutable(),
                Barriers = barriers.ToImmutable(),
                Readables = readables.ToImmutable(),
                // The space this region's coordinates belong to: the exterior for the world's regions, its own
                // for an interior (M8 D10). Everything derived from a site in this layout follows it.
                SpaceId = spaceId,
                Portals = new SpacePortalSet(portals.ToImmutable()).Validate(),
                Waystones = waystones.ToImmutable(),
                Roads = roads.ToImmutable(),
                Opening = opening,
            };

            // The spawn must stand clear with every door shut and every barrier standing, the harshest case. An NPC may stand
            // behind a barrier - that is what the fold is for - but not in a wall or a doorway.
            var closed = layout.ClosedDoors(_ => false);
            long radius = loader.Definitions.ContainsKey("config.base_speeds") ? BuildMovement(loader).BodyRadiusMm : 0;
            Check(Kinematics.IsClear(spawnX, spawnZ, radius, space, closed, spaceId.IsExterior ? 0 : 1800), "WLD007", "the spawn point must be inside the bounds and clear of every structure, door and barrier");
            if (opening is not null)
                Check(Kinematics.IsClear(opening.XMm, opening.ZMm, radius, space, closed, spaceId.IsExterior ? 0 : 1800), "WLD007", "the opening's start must be inside the bounds and clear of every structure, door and barrier");
            var shut = layout.ClosedDoors(_ => false, _ => true);
            foreach (var npc in layout.Npcs)
                Check(Kinematics.IsClear(npc.XMm, npc.ZMm, radius, space, shut, spaceId.IsExterior ? 0 : 1800), "WLD012", $"{npc.NpcId} must stand clear of every structure and door");
            return errors.Count == before ? layout : null;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("WLD001", $"{regionId} is malformed: {e.Message}", file));
            return null;
        }
    }

    /// <summary>A dormant stone's rite is a line its conversation really has.</summary>
    private static bool RiteExists(ContentLoader loader, DormantStone dormant) =>
        loader.Definitions.TryGetValue(dormant.RiteDialogueId, out var dialogue) && dialogue.Kind == "dialogue"
        && Read(dialogue.YamlSource).GetValueOrDefault("nodes") is Dictionary<object, object> nodes && nodes.ContainsKey(dormant.RiteNodeId);

    /// <summary>The cell a footprint stands in, in the space the region stands in: a switch's or a barrier's flag
    /// lives in that cell's delta, and the runtime resolves it the same way (review B2).</summary>
    private static CellKey CellOf(SpaceId space, Blocker blocker)
    {
        var (x, z) = Footprints.Center(blocker);
        return CellKey.OfInterior(space, x / 1000.0, z / 1000.0);
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
        // A turned boulder's own outline (owner finding 50): its hull, written by tools/rocks/fit_rock_hulls.py.
        if (map.ContainsKey("hull_m"))
        {
            var corners = List(map, "hull_m").Select(c => c as List<object> is { Count: 2 } xz ? (ToMm(xz[0], id), ToMm(xz[1], id))
                : throw new FormatException($"structure {id}: hull_m is a list of [x, z]")).ToList();
            try { return new PolygonBlocker(id, corners, height) { ClearanceMm = clearance }; }
            catch (ArgumentException e) { throw new FormatException(e.Message); }
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

    private static bool Covered(List<CellKey> cells, SpaceId space, long minX, long minZ, long maxX, long maxZ)
    {
        const long size = WorldMath.CellSizeMeters * 1000L;
        // Every 1 mm-inset corner of every cell-sized tile of the bounds must fall in a listed cell.
        for (long x = minX; x < maxX; x += size)
        for (long z = minZ; z < maxZ; z += size)
        {
            foreach (var (px, pz) in new[] { (x, z), (Math.Min(x + size, maxX) - 1, z), (x, Math.Min(z + size, maxZ) - 1), (Math.Min(x + size, maxX) - 1, Math.Min(z + size, maxZ) - 1) })
            {
                // In the region's own space: an interior's local metres would otherwise be checked against
                // the exterior cells those metres happen to name (M8 D10).
                if (!cells.Contains(CellKey.OfInterior(space, px / 1000.0, pz / 1000.0)))
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
