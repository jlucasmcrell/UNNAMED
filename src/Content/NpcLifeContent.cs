// UNNAMED Content - NPC life from content: routines, anchors, jobs, pairs and the rules of needs and arrivals (WP12)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="NpcLifeSetup"/> for a region and lints every region's routines (NPL codes): NPL001 a malformed routine
/// or <c>config.npc_life</c>; NPL002 a merchant whose work hours are not the trading hours (§2.13: merchants trade 06:00-20:00); NPL003 an
/// anchor a body cannot stand at or reach from the spawn; NPL004 a named NPC with two routines in one region, or a routine for an NPC the
/// region does not place. There is no limit on how many routines, residents or pairs a pack may hold (owner, 2026-10-17).
/// <c>config.npc_life</c> is optional: a pack without it lives by <see cref="NpcLifeRules.Default"/>.
/// </summary>
public static class NpcLifeContent
{
    public const string ConfigId = "config.npc_life";

    /// <summary>How near a flooded node must be to an anchor: a body stands there.</summary>
    public const long AnchorReachMm = 400;

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (loader.GetByKind("routine").Count == 0 && !loader.Definitions.ContainsKey(ConfigId))
            return errors;
        NpcLifeRules rules;
        try
        {
            rules = BuildRules(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(NavigationContent.Error("NPL001", $"{ConfigId}: {e.Message}", loader.Definitions.GetValueOrDefault(ConfigId)?.SourceFile));
            return errors;
        }
        ImmutableSortedDictionary<string, NpcDefinition> npcs;
        try
        {
            npcs = SocialContent.BuildNpcs(loader);
            EconomyContent.Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return errors;   // the SOC and ECO codes report it
        }
        var regions = loader.GetByKind("routine").Values.Select(r => Read(r.YamlSource).GetValueOrDefault("region_ref") as string)
            .OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal);
        foreach (string regionId in regions)
        {
            NpcLifeSetup setup;
            try
            {
                setup = Build(loader, regionId);
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
            {
                errors.Add(NavigationContent.Error("NPL001", $"routines of {regionId}: {e.Message}", null));
                continue;
            }
            RegionLayout layout;
            try
            {
                layout = WorldContent.BuildLayout(loader, regionId);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
            {
                errors.Add(NavigationContent.Error("NPL004", $"routines name {regionId}, which is not a region that builds", null));
                continue;
            }
            foreach (var routine in setup.Routines.Values)
            {
                string? file = loader.Definitions[routine.Id].SourceFile;
                if (routine.NpcId is { } npcId)
                {
                    if (!layout.Npcs.Any(n => n.NpcId == npcId))
                        errors.Add(NavigationContent.Error("NPL004", $"{routine.Id}: {regionId} does not place {npcId}", file));
                    if (npcs.TryGetValue(npcId, out var npc) && npc.Offers(NpcServices.Trade)
                        && (routine.WorkFromHour * 60 != rules.TradeFromMinute || routine.WorkToHour * 60 != rules.TradeToMinute))
                        errors.Add(NavigationContent.Error("NPL002", $"{routine.Id}: {npcId} trades, so works the trading hours", file));
                }
            }
            foreach (var group in setup.Routines.Values.Where(r => r.NpcId is not null).GroupBy(r => r.NpcId!).Where(g => g.Count() > 1))
                errors.Add(NavigationContent.Error("NPL004", $"{group.Key} has {group.Count()} routines in {regionId}", null));

            NavConfig config;
            try
            {
                config = NavigationContent.Build(loader);
            }
            catch (FormatException)
            {
                continue;   // the NAV codes report it
            }
            var grid = NavigationLayout.Build(layout, config);
            var reach = NavReach.FromSpawn(grid, config, new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm));
            var person = new NavAgent(0, true);
            foreach (var routine in setup.Routines.Values)
            {
                foreach (var (kind, anchor) in routine.Anchors)
                {
                    var at = new NavPoint(anchor.XMm, anchor.ZMm);
                    if (!NavSearch.Standable(grid, person, _ => true, at, config.Classes[0].RadiusMm) || !reach.Near(at, AnchorReachMm))
                        errors.Add(NavigationContent.Error("NPL003",
                            $"{routine.Id}: its {kind} anchor ({anchor.XMm / 1000.0:0.###}, {anchor.ZMm / 1000.0:0.###}) cannot be stood at, or reached from the spawn",
                            loader.Definitions[routine.Id].SourceFile));
                }
            }
        }
        return errors;
    }

    /// <summary>The lives of one region: its routines, the pairs they declare, and the rules.</summary>
    public static NpcLifeSetup Build(ContentLoader loader, string regionId)
    {
        var rules = BuildRules(loader);
        var npcs = SocialContent.BuildNpcs(loader);
        var routines = new List<RoutineDefinition>();
        var declared = new List<(string From, string To, string Kind, string Id)>();
        foreach (var definition in loader.GetByKind("routine").Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            string id = definition.Id;
            var map = Read(definition.YamlSource);
            if (Text(map, "region_ref") != regionId)
                continue;
            string? npcId = map.GetValueOrDefault("npc_ref") as string;
            if (npcId is not null && !npcs.ContainsKey(npcId))
                throw new FormatException($"{id} is for {npcId}, which is not an NPC");
            string? people = map.GetValueOrDefault("people_ref") as string;
            if (npcId is null && people is null)
                throw new FormatException($"{id} is a role slot, so names the people its residents come from (people_ref)");
            var anchors = Map(map, "anchors").ToImmutableSortedDictionary(kv => kv.Key as string ?? throw new FormatException($"{id} anchors are named"),
                kv => Anchor(kv.Value as Dictionary<object, object> ?? throw new FormatException($"{id} anchor {kv.Key} must be a map"), $"{id} anchor {kv.Key}"),
                StringComparer.Ordinal);
            foreach (string kind in anchors.Keys)
            {
                if (!NpcAnchorKinds.All.Contains(kind))
                    throw new FormatException($"{id} has an anchor '{kind}'; anchors are {string.Join(", ", NpcAnchorKinds.All)}");
            }
            if (!anchors.ContainsKey(NpcAnchorKinds.Home))
                throw new FormatException($"{id} has no home anchor");
            var hours = List(map, "work_hours");
            if (hours.Count != 2)
                throw new FormatException($"{id} work_hours is [from, to]");
            int from = IntOf(hours, 0, $"{id} work_hours"), to = IntOf(hours, 1, $"{id} work_hours");
            if (from is < 0 or > 24 || to is < 0 or > 24)
                throw new FormatException($"{id} work_hours lie in 0..24");
            if (from != to && !anchors.ContainsKey(NpcAnchorKinds.Work))
                throw new FormatException($"{id} works hours but has no work anchor");
            var weights = map.TryGetValue("weights", out var w) && w is Dictionary<object, object> wm
                ? wm.ToImmutableSortedDictionary(kv => NpcActivities.IsActivity(kv.Key as string ?? "") ? (string)kv.Key : throw new FormatException($"{id} weights '{kv.Key}', which is not an activity"),
                    kv => int.TryParse(kv.Value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new FormatException($"{id} weight {kv.Key} must be a whole number"),
                    StringComparer.Ordinal)
                : ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
            NpcJob? job = null;
            if (map.TryGetValue("job", out var j))
            {
                var jm = j as Dictionary<object, object> ?? throw new FormatException($"{id} job must be a map");
                job = new NpcJob(Text(jm, "market_ref"), Text(jm, "item_ref"), Int(jm, "per_day"));
                if (job.PerDay < 0)
                    throw new FormatException($"{id} job makes at least 0 a day");
            }
            var routine = new RoutineDefinition(id, npcId, Text(map, "settlement"), Text(map, "role"), anchors, from % 24, to % 24, weights)
            {
                PeopleId = people,
                Job = job,
            };
            routines.Add(routine);
            foreach (var row in Rows(map, "pairs", id))
            {
                string other = row.GetValueOrDefault("npc_ref") as string ?? row.GetValueOrDefault("routine_ref") as string
                    ?? throw new FormatException($"{id} pairs name an npc_ref or a routine_ref");
                string kind = Text(row, "kind");
                if (!NpcPairKinds.All.Contains(kind))
                    throw new FormatException($"{id} pairs with {other} as '{kind}'; pairs are {string.Join(", ", NpcPairKinds.All)}");
                declared.Add((routine.Key, other, kind, id));
            }
        }
        var keys = routines.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var pairs = new SortedDictionary<string, NpcPair>(StringComparer.Ordinal);
        foreach (var (a, b, kind, id) in declared)
        {
            if (!keys.Contains(b) || a == b)
                throw new FormatException($"{id} pairs with {b}, which has no routine in {regionId}");
            var pair = string.CompareOrdinal(a, b) < 0 ? new NpcPair(a, b, kind) : new NpcPair(b, a, kind);
            if (pairs.TryGetValue(pair.Key, out var seen) && seen.Kind != kind)
                throw new FormatException($"{pair.A} and {pair.B} are paired as both {seen.Kind} and {kind}");
            pairs[pair.Key] = pair;
        }
        var markets = EconomyContent.Build(loader).Markets;
        foreach (var routine in routines.Where(r => r.Job is not null))
        {
            if (!markets.TryGetValue(routine.Job!.MarketId, out var market) || !market.Goods.ContainsKey(routine.Job.ItemId))
                throw new FormatException($"{routine.Id}'s job makes {routine.Job.ItemId} for {routine.Job.MarketId}, which does not keep it");
        }
        return new NpcLifeSetup(routines.ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal), pairs.Values.ToImmutableArray(), rules);
    }

    /// <summary><c>config.npc_life</c>'s rules, or the defaults for a pack without it.</summary>
    public static NpcLifeRules BuildRules(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey(ConfigId))
            return NpcLifeRules.Default;
        var map = Config(loader, ConfigId);
        var needs = Map(map, "needs");
        var rise = new SortedDictionary<NpcNeed, int>();
        var satisfy = new SortedDictionary<NpcNeed, int>();
        foreach (var need in Enum.GetValues<NpcNeed>())
        {
            var row = Map(needs, NpcNeeds.Key(need));
            rise[need] = Milli(row, "rise_per_hour");
            satisfy[need] = Milli(row, "satisfied_per_hour");
        }
        var night = List(map, "night_hours");
        var trading = List(map, "trading_hours");
        var arrival = List(map, "arrival_days");
        if (night.Count != 2 || trading.Count != 2 || arrival.Count != 2)
            throw new FormatException("night_hours, trading_hours and arrival_days are each [from, to]");
        var rules = new NpcLifeRules(rise.ToImmutableSortedDictionary(), satisfy.ToImmutableSortedDictionary(),
            IntOf(night, 0, "night_hours"), IntOf(night, 1, "night_hours"), Minute(trading[0]), Minute(trading[1]),
            IntOf(arrival, 0, "arrival_days"), IntOf(arrival, 1, "arrival_days"));
        if (map.ContainsKey("meal_hours"))
        {
            var meals = List(map, "meal_hours");
            rules = rules with { MealHours = Enumerable.Range(0, meals.Count).Select(i => IntOf(meals, i, "meal_hours")).ToImmutableArray() };
        }
        return rules.Problem() is { } problem ? throw new FormatException(problem) : rules;
    }

    private static NpcAnchor Anchor(Dictionary<object, object> row, string what)
    {
        var at = List(row, "position_m");
        if (at.Count != 2)
            throw new FormatException($"{what} position_m is [x, z]");
        long Metres(int i) => (long)Math.Round(double.Parse(at[i] as string ?? throw new FormatException($"{what} position_m holds numbers"),
            NumberStyles.Float, CultureInfo.InvariantCulture) * 1000, MidpointRounding.AwayFromZero);
        int facing = (int)Math.Round(Number(row, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
        return new NpcAnchor(Metres(0), Metres(1), ((facing % 360_000) + 360_000) % 360_000);
    }

    private static int Milli(Dictionary<object, object> row, string key) =>
        (int)Math.Round(Number(row, key) * 1000, MidpointRounding.AwayFromZero);

    private static int Minute(object value)
    {
        string text = value as string ?? throw new FormatException("trading_hours are \"HH:MM\"");
        var parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m) || h > 24 || m > 59)
            throw new FormatException($"'{text}' is not a time of day (\"HH:MM\")");
        return h * 60 + m;
    }
}
