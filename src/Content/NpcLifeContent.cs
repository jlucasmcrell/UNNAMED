// UNNAMED Content - NPC life from content: routines, anchors, fixed schedules, jobs and the rules of night, trading and arrivals (WP12)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="NpcLifeSetup"/> for a region and lints every region's routines (NPL codes): NPL001 a malformed routine
/// or <c>config.npc_life</c>; NPL002 a merchant whose work hours are not the trading hours (§2.13: merchants trade 06:00-20:00); NPL003 an
/// anchor a body cannot stand at or reach from the spawn; NPL004 a named NPC with two routines in one region, or a routine for an NPC the
/// region does not place; NPL005 a routine's roles (WP12b): a role <c>config.npc_life</c> does not define, a trader without
/// <c>vendor</c>, a quest's giver without <c>quest_giver</c>, a vendor or quest giver whose NPC file still says they hire on, or a speaker
/// type the chatter schema does not know; NPL006 a trip's meeting place or destination a body cannot stand at or reach from the spawn.
/// NPL007 someone who can join or be hired, placed in a region with routines, without a routine of their own there - the home anchor the
/// party lets them walk back to (WP12b). There is no limit on how many routines or residents a pack may hold (owner, 2026-10-17).
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
            var givers = loader.GetByKind("quest").Values.Select(q => Read(q.YamlSource).GetValueOrDefault("giver_ref") as string).OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            ImmutableSortedSet<string>? speakers = null;
            try
            {
                if (loader.GetByKind("chatter").Count > 0)
                    speakers = ChatterContent.ReadSchema(loader).SpeakerTypes;
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException or IOException)
            {
                // the CHT codes report it
            }
            foreach (var routine in setup.Routines.Values)
            {
                string? file = loader.Definitions[routine.Id].SourceFile;
                foreach (string role in routine.Roles.Where(r => !setup.Roles.ContainsKey(r)))
                    errors.Add(NavigationContent.Error("NPL005", $"{routine.Id}: role '{role}' is not one config.npc_life defines", file));
                if (routine.NpcId is not { } npcId)
                    continue;
                if (npcs.TryGetValue(npcId, out var npc) && npc.Offers(NpcServices.Trade) && !routine.Roles.Contains(NpcRoleKeys.Vendor))
                    errors.Add(NavigationContent.Error("NPL005", $"{routine.Id}: {npcId} trades, so is a {NpcRoleKeys.Vendor}", file));
                if (givers.Contains(npcId) && !routine.Roles.Contains(NpcRoleKeys.QuestGiver))
                    errors.Add(NavigationContent.Error("NPL005", $"{routine.Id}: {npcId} gives a quest, so is a {NpcRoleKeys.QuestGiver}", file));
                if (npc?.Hireling is not null && routine.Roles.Any(NpcRoleKeys.NeverHired.Contains))
                    errors.Add(NavigationContent.Error("NPL005", $"{routine.Id}: {npcId} is a vendor or quest giver, who never hires on, yet their file has a hireling block", file));
            }
            if (speakers is not null)
            {
                foreach (var role in setup.Roles.Values.Where(r => r.SpeakerType is { } t && !speakers.Contains(t)))
                    errors.Add(NavigationContent.Error("NPL005", $"{ConfigId}: role '{role.Id}' talks as '{role.SpeakerType}', which is not a chatter speaker type",
                        loader.Definitions.GetValueOrDefault(ConfigId)?.SourceFile));
            }
            // Companion home anchors (WP12b): someone the party lets go walks back to their routine, so whoever can join or be hired has one.
            foreach (var site in layout.Npcs.Where(n => npcs.TryGetValue(n.NpcId, out var d) && (d.Companion is not null || d.Hireling is not null)))
            {
                if (!setup.Routines.Values.Any(r => r.NpcId == site.NpcId))
                    errors.Add(NavigationContent.Error("NPL007", $"{site.NpcId} can join or be hired in {regionId}, so has a routine there: the home they walk back to", null));
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
            var person = new NavAgent(config.PersonClass, true);
            foreach (var routine in setup.Routines.Values)
            {
                foreach (var (kind, anchor) in routine.Anchors)
                {
                    var at = new NavPoint(anchor.XMm, anchor.ZMm);
                    if (!NavSearch.Standable(grid, person, _ => true, at, config.Classes[config.PersonClass].RadiusMm) || !reach.Near(at, AnchorReachMm))
                        errors.Add(NavigationContent.Error("NPL003",
                            $"{routine.Id}: its {kind} anchor ({anchor.XMm / 1000.0:0.###}, {anchor.ZMm / 1000.0:0.###}) cannot be stood at, or reached from the spawn",
                            loader.Definitions[routine.Id].SourceFile));
                }
                if (routine.Trip is not { } trip)
                    continue;
                foreach (var (kind, anchor) in new[] { ("meeting place", trip.Meet), ("destination", trip.Destination) })
                {
                    var at = new NavPoint(anchor.XMm, anchor.ZMm);
                    if (!NavSearch.Standable(grid, person, _ => true, at, config.Classes[config.PersonClass].RadiusMm) || !reach.Near(at, AnchorReachMm))
                        errors.Add(NavigationContent.Error("NPL006",
                            $"{routine.Id}: its trip's {kind} ({anchor.XMm / 1000.0:0.###}, {anchor.ZMm / 1000.0:0.###}) cannot be stood at, or reached from the spawn",
                            loader.Definitions[routine.Id].SourceFile));
                }
            }
        }
        return errors;
    }

    /// <summary>The lives of one region: its routines and the rules.</summary>
    public static NpcLifeSetup Build(ContentLoader loader, string regionId)
    {
        var rules = BuildRules(loader);
        var npcs = SocialContent.BuildNpcs(loader);
        var routines = new List<RoutineDefinition>();
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
            // A fixed daily schedule (owner, 2026-10-17): hour bands at named anchors; any hour no band covers is spent at home.
            var schedule = Rows(map, "schedule", id).Select(row =>
            {
                var span = List(row, "hours");
                if (span.Count != 2)
                    throw new FormatException($"{id} schedule hours is [from, to]");
                var band = new NpcScheduleBand(IntOf(span, 0, $"{id} schedule hours") % 24, IntOf(span, 1, $"{id} schedule hours") % 24, Text(row, "at"));
                return anchors.ContainsKey(band.Anchor) ? band : throw new FormatException($"{id} schedule names '{band.Anchor}', which is not one of its anchors");
            }).ToImmutableArray();
            foreach (string kind in anchors.Keys.Where(k => k is not (NpcAnchorKinds.Home or NpcAnchorKinds.Work) && !schedule.Any(b => b.Anchor == k)))
                throw new FormatException($"{id} has an anchor '{kind}' its schedule never uses");
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
            NpcJob? job = null;
            if (map.TryGetValue("job", out var j))
            {
                var jm = j as Dictionary<object, object> ?? throw new FormatException($"{id} job must be a map");
                job = new NpcJob(Text(jm, "market_ref"), Text(jm, "item_ref"), Int(jm, "per_day"));
                if (job.PerDay < 0)
                    throw new FormatException($"{id} job makes at least 0 a day");
            }
            var roles = map.ContainsKey("roles")
                ? List(map, "roles").Select(r => r as string ?? throw new FormatException($"{id} roles are names")).Distinct(StringComparer.Ordinal).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            var routine = new RoutineDefinition(id, npcId, Text(map, "settlement"), Text(map, "role"), anchors, from % 24, to % 24)
            {
                ReleaseBarrier = map.GetValueOrDefault("release_barrier") as string,
                Schedule = schedule,
                PeopleId = people,
                Job = job,
                Roles = roles,
                Trip = map.TryGetValue("trip", out var t) ? Trip(loader, id, t as Dictionary<object, object> ?? throw new FormatException($"{id} trip must be a map")) : null,
            };
            if (routine.ReleaseBarrier is { } barrier && (npcId is null || !WorldContent.BuildLayout(loader, regionId).Barriers.Any(b => b.Key == barrier)))
                throw new FormatException($"{id} release_barrier must name a barrier in its layout and belong to a named NPC");
            routines.Add(routine);
        }
        var markets = EconomyContent.Build(loader).Markets;
        foreach (var routine in routines.Where(r => r.Job is not null))
        {
            if (!markets.TryGetValue(routine.Job!.MarketId, out var market) || !market.Goods.ContainsKey(routine.Job.ItemId))
                throw new FormatException($"{routine.Id}'s job makes {routine.Job.ItemId} for {routine.Job.MarketId}, which does not keep it");
        }
        var (vocabulary, body, lead) = BuildRoles(loader);
        return new NpcLifeSetup(routines.ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal), rules)
        {
            Roles = vocabulary,
            ResidentBody = body,
            TripLeadMinutes = lead,
        };
    }

    /// <summary>
    /// <c>trip</c> (WP12b): what for (<c>purpose</c>: hunt, bounty or errand) and what names it - a hunt's <c>creature_ref</c>, a bounty's
    /// <c>quest_ref</c>, an errand's <c>item_ref</c> - where they wait (<c>meet_location_ref</c> names it, <c>meet</c> is the spot) and where
    /// they go (<c>destination_location_ref</c>, <c>destination</c>), a day's <c>chance_percent</c>, the <c>hours</c> it may leave in, and
    /// <c>away_hours</c>.
    /// </summary>
    private static NpcTrip Trip(ContentLoader loader, string id, Dictionary<object, object> map)
    {
        string purpose = Text(map, "purpose");
        string named = purpose switch
        {
            LfgPurposes.Hunt => NameOf(loader, Text(map, "creature_ref"), id),
            LfgPurposes.Bounty => NameOf(loader, Text(map, "quest_ref"), id),
            LfgPurposes.Errand => NameOf(loader, Text(map, "item_ref"), id),
            _ => throw new FormatException($"{id} trip purpose '{purpose}' is not one of {string.Join(", ", LfgPurposes.All)}"),
        };
        string meetId = Text(map, "meet_location_ref");
        var hours = List(map, "hours");
        if (hours.Count != 2)
            throw new FormatException($"{id} trip hours is [from, to]");
        var trip = new NpcTrip(purpose, named, meetId, NameOf(loader, meetId, id), Anchor(Map(map, "meet"), $"{id} trip meet"),
            NameOf(loader, Text(map, "destination_location_ref"), id), Anchor(Map(map, "destination"), $"{id} trip destination"),
            Int(map, "chance_percent"), IntOf(hours, 0, $"{id} trip hours"), IntOf(hours, 1, $"{id} trip hours"), Int(map, "away_hours"));
        return trip.Problem() is { } problem ? throw new FormatException($"{id}: {problem}") : trip;
    }

    /// <summary>A definition's name as content gives it (<c>name</c>, or a quest's <c>title</c>): the words a trip's offer says.</summary>
    private static string NameOf(ContentLoader loader, string refId, string id)
    {
        if (!loader.Definitions.TryGetValue(refId, out var definition))
            throw new FormatException($"{id} trip names {refId}, which is not defined");
        var map = Read(definition.YamlSource);
        return map.GetValueOrDefault("name") as string ?? map.GetValueOrDefault("title") as string
            ?? throw new FormatException($"{id} trip names {refId}, which has no name to say");
    }

    /// <summary>
    /// <c>config.npc_life</c>'s town roles (WP12b): each role's <c>hires_as</c> (guard, porter, gatherer, crafter) and the chatter
    /// <c>speaker</c> type it talks as; <c>vendor</c> and <c>quest_giver</c> must be defined and never hire out. Then the body a hired
    /// resident fights with (<c>resident_hireling</c>, as an NPC's <c>hireling</c> block without roles) and the trips' lead.
    /// </summary>
    public static (ImmutableSortedDictionary<string, NpcRole> Roles, CompanionProfile? ResidentBody, int TripLeadMinutes) BuildRoles(ContentLoader loader)
    {
        var none = ImmutableSortedDictionary.Create<string, NpcRole>(StringComparer.Ordinal);
        if (!loader.Definitions.ContainsKey(ConfigId))
            return (none, null, 40);
        var map = Config(loader, ConfigId);
        var roles = map.ContainsKey("roles")
            ? Map(map, "roles").ToImmutableSortedDictionary(kv => kv.Key as string ?? throw new FormatException("roles are named"), kv =>
            {
                string id = (string)kv.Key;
                var row = kv.Value as Dictionary<object, object> ?? new Dictionary<object, object>();
                var hires = row.ContainsKey("hires_as")
                    ? List(row, "hires_as").Select(r => PartyKeys.ParseRole(r as string ?? "") ?? throw new FormatException($"role {id} hires as '{r}', which is not a hireling role"))
                        .Distinct().OrderBy(r => r).ToImmutableArray()
                    : ImmutableArray<HirelingRole>.Empty;
                if (NpcRoleKeys.NeverHired.Contains(id) && !hires.IsEmpty)
                    throw new FormatException($"role {id} never hires out");
                return new NpcRole(id, hires, row.GetValueOrDefault("speaker") as string);
            }, StringComparer.Ordinal)
            : none;
        if (!roles.IsEmpty && NpcRoleKeys.NeverHired.FirstOrDefault(r => !roles.ContainsKey(r)) is { } missing)
            throw new FormatException($"roles defines {missing}: the roles that never hire out are always there");
        var body = map.ContainsKey("resident_hireling") ? SocialContent.HirelingBody(ConfigId, Map(map, "resident_hireling"), loader) : null;
        int lead = map.ContainsKey("trip_lead_minutes") ? Int(map, "trip_lead_minutes") : 40;
        if (lead is < 20 or > 60)
            throw new FormatException("trip_lead_minutes is 20 to 60: an offer goes up at least 20 game minutes ahead, within the hour of waiting");
        return (roles, body, lead);
    }

    /// <summary><c>config.npc_life</c>'s rules, or the defaults for a pack without it.</summary>
    public static NpcLifeRules BuildRules(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey(ConfigId))
            return NpcLifeRules.Default;
        var map = Config(loader, ConfigId);
        var night = List(map, "night_hours");
        var trading = List(map, "trading_hours");
        var arrival = List(map, "arrival_days");
        if (night.Count != 2 || trading.Count != 2 || arrival.Count != 2)
            throw new FormatException("night_hours, trading_hours and arrival_days are each [from, to]");
        var rules = new NpcLifeRules(IntOf(night, 0, "night_hours"), IntOf(night, 1, "night_hours"), Minute(trading[0]), Minute(trading[1]),
            IntOf(arrival, 0, "arrival_days"), IntOf(arrival, 1, "arrival_days"));
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
