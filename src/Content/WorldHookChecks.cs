// UNNAMED Content - the world-hooks audit (brief D15)
// No Godot references - pure C#

using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace UNNAMED.Content;

/// <summary>What the audit found for one hook in one area.</summary>
public enum HookState
{
    /// <summary>The content shows at least the required number of hooks.</summary>
    Placed,

    /// <summary>The content could hold this hook and does not: the area is missing it.</summary>
    Missing,

    /// <summary>Today's content model cannot express this hook at all, so nothing could place it.</summary>
    Unmodelled,
}

/// <summary>One line of the audit: a hook, an area, what is placed and what was required.</summary>
public sealed record HookFinding(string Area, string HookId, HookState State, int Placed, int Required, string Note)
{
    /// <summary>A missing hook is an ERROR where the area is released, and a WARNING while it is in development.</summary>
    public string Severity(string areaStatus) =>
        State != HookState.Missing ? "info" : areaStatus == "released" ? "ERROR" : "WARNING";
}

/// <summary>One area the rules file declares: the dossier's identity, its release status and its level band.</summary>
public sealed record HookArea(string Slug, string Name, string Status, string Type, string Band);

/// <summary>The whole audit: a finding per required hook per area, plus whatever the rules file itself got wrong.</summary>
public sealed record WorldHookReport(IReadOnlyList<HookArea> Areas, IReadOnlyList<HookFinding> Findings, IReadOnlyList<string> Problems)
{
    public IEnumerable<HookFinding> For(string area) => Findings.Where(f => f.Area == area);

    public int Count(HookState state) => Findings.Count(f => f.State == state);

    public string StatusOf(string area) => Areas.FirstOrDefault(a => a.Slug == area)?.Status ?? "development";

    /// <summary>Exit 1 only for a released area: a released place is a promise, a place in development is work.</summary>
    public bool Fails() => Findings.Any(f => f.State == HookState.Missing && StatusOf(f.Area) == "released");

    public string Text()
    {
        var lines = new List<string>
        {
            "World hooks (brief D15) - content/_world_hooks.yaml against the region, location, spawn and node content",
            "==========================================================================================================",
        };
        foreach (var area in Areas)
        {
            var mine = For(area.Slug).ToList();
            lines.Add("");
            lines.Add($"{area.Slug} - {area.Name}  [{area.Status}, {area.Type}, band {area.Band}]  " +
                      $"placed {mine.Count(f => f.State == HookState.Placed)}, " +
                      $"missing {mine.Count(f => f.State == HookState.Missing)}, " +
                      $"unmodelled {mine.Count(f => f.State == HookState.Unmodelled)}");
            foreach (var finding in mine.Where(f => f.State == HookState.Missing))
                lines.Add($"  {finding.Severity(area.Status),-7} {finding.HookId,-34} {finding.Placed}/{finding.Required}  {finding.Note}");
            foreach (var finding in mine.Where(f => f.State == HookState.Unmodelled))
                lines.Add($"  info    {finding.HookId,-34} unmodelled: {finding.Note}");
        }
        lines.Add("");
        lines.Add($"totals: {Count(HookState.Placed)} placed, {Count(HookState.Missing)} missing, " +
                  $"{Count(HookState.Unmodelled)} unmodelled across {Areas.Count} areas");
        foreach (string problem in Problems)
            lines.Add($"problem: {problem}");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Brief D15's third artefact. Reads the machine-readable minimums (<c>content/_world_hooks.yaml</c>) together with
/// the region, location, spawn and node content, and says per area which required world hooks are not there. A hook
/// the schema cannot carry is reported as unmodelled instead of passing quietly, because that is exactly how a skill
/// ends up with nowhere to be used.
/// </summary>
public static class WorldHookChecks
{
    /// <summary>How far from an area's anchor a placement may sit and still count as that area's.</summary>
    private const double AssignmentRadiusM = 220.0;

    private static readonly string[] PositionFields = { "position_m", "arrival_m", "exterior_m", "circle_m", "box_m", "world" };

    public static WorldHookReport Audit(string contentRoot, ContentLoader loader)
    {
        var problems = new List<string>();
        string rulesPath = Path.Combine(contentRoot, "_world_hooks.yaml");
        if (!File.Exists(rulesPath))
            problems.Add($"{rulesPath} is missing: there are no minimums to audit against");
        else if (Rules(rulesPath, problems) is { } rules)
            return Audit(Areas(rules, loader, problems), Hooks(rules, problems), BandScales(rules), loader, problems);
        return new WorldHookReport([], [], problems);
    }

    private static WorldHookReport Audit(List<AuditArea> areas, List<AuditHook> hooks, Dictionary<string, double> bandScales,
        ContentLoader loader, List<string> problems)
    {
        var facts = new WorldFacts(loader);
        var findings = new List<HookFinding>();
        var shares = Shares(areas, loader, problems);

        foreach (var area in areas)
        {
            if (area.Regions.Length == 0)
            {
                problems.Add($"{area.Slug} names {string.Join(", ", area.RegionRefs)}, which no region definition provides");
                continue;
            }
            var placed = shares[area.Slug];
            foreach (var hook in hooks.Where(h => h.Scope == "area"))
            {
                if (!hook.Minimums.TryGetValue(area.Type, out int minimum) || minimum <= 0)
                    continue;
                int required = hook.BandScaled ? Scale(minimum, bandScales.GetValueOrDefault(area.Band, 1.0)) : minimum;
                if (hook.Unmodelled)
                {
                    findings.Add(new HookFinding(area.Slug, hook.Id, HookState.Unmodelled, 0, required, hook.Note));
                    continue;
                }
                int count = Count(hook, area, placed, facts);
                findings.Add(new HookFinding(area.Slug, hook.Id, count >= required ? HookState.Placed : HookState.Missing,
                    count, required, hook.Note));
            }
        }

        // Content-scope hooks are audited once: a recipe, a piece or an effect is not something an area places.
        foreach (var hook in hooks.Where(h => h.Scope != "area"))
            findings.Add(hook.Unmodelled
                ? new HookFinding("content", hook.Id, HookState.Unmodelled, 0, 1, hook.Note)
                : new HookFinding("content", hook.Id, HookState.Placed, Count(hook, null, [], facts), 1, hook.Note));

        var declared = areas.Select(a => new HookArea(a.Slug, a.Name, a.Status, a.Type, a.Band)).ToList();
        return new WorldHookReport(declared, findings, problems);
    }

    // ---- the rules file -------------------------------------------------------------------------

    private static YamlMappingNode? Rules(string path, List<string> problems)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(path)));
            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root)
                return root;
            problems.Add($"{path} has no mapping at its root");
        }
        catch (YamlDotNet.Core.YamlException error)
        {
            problems.Add($"{path} does not parse: {error.Message}");
        }
        return null;
    }

    private static Dictionary<string, double> BandScales(YamlMappingNode rules)
    {
        var scales = new Dictionary<string, double>(StringComparer.Ordinal);
        if (Map(rules, "band_scales") is { } map)
            foreach (var (key, value) in map.Children)
                if (AsNumber(value) is { } scale)
                    scales[ScalarValue(key) ?? ""] = scale;
        return scales;
    }

    private static List<AuditArea> Areas(YamlMappingNode rules, ContentLoader loader, List<string> problems)
    {
        var areas = new List<AuditArea>();
        foreach (var entry in Maps(rules, "areas"))
        {
            string? slug = Scalar(entry, "slug");
            var regionRefs = TextOrTexts(entry, "region_ref");
            if (slug is null || regionRefs.Length == 0)
            {
                problems.Add("an area is missing its slug or region_ref");
                continue;
            }
            var regions = regionRefs
                .Select(reference => loader.Definitions.TryGetValue(reference, out var definition) && definition.Kind == "region"
                    ? ContentChecks.Parse(definition)
                    : null)
                .Where(region => region is not null)
                .Select(region => region!)
                .ToArray();
            areas.Add(new AuditArea(
                slug,
                Scalar(entry, "name") ?? slug,
                regionRefs,
                Scalar(entry, "type") ?? "wilderness",
                Scalar(entry, "status") ?? "development",
                Scalar(entry, "band") ?? "1-5",
                Texts(entry, "locations").ToArray(),
                Texts(entry, "portals_from").ToArray(),
                regions));
        }
        if (areas.Count == 0)
            problems.Add("the rules file declares no areas, so nothing can be audited");
        return areas;
    }

    private static List<AuditHook> Hooks(YamlMappingNode rules, List<string> problems)
    {
        var hooks = new List<AuditHook>();
        foreach (var entry in Maps(rules, "hooks"))
        {
            string? id = Scalar(entry, "id");
            if (id is null || Map(entry, "evidence") is not { } evidence || evidence.Children.Count == 0)
            {
                problems.Add($"{id ?? "(no id)"} has no evidence, so the audit cannot check it");
                continue;
            }
            var minimums = new Dictionary<string, int>(StringComparer.Ordinal);
            if (Map(entry, "min") is { } min)
                foreach (var (key, value) in min.Children)
                    if (AsNumber(value) is { } count)
                        minimums[ScalarValue(key) ?? ""] = (int)count;

            string kind = ScalarValue(evidence.Children.Keys.First()) ?? "";
            string? why = kind == "unmodelled" && evidence.Children.Values.First() is YamlMappingNode reason
                ? Scalar(reason, "why")
                : null;
            hooks.Add(new AuditHook(
                id,
                Scalar(entry, "scope") ?? "area",
                why ?? Scalar(entry, "note") ?? "",
                Scalar(entry, "band_scaled") == "true",
                why is not null,
                evidence,
                minimums));
        }
        if (hooks.Count == 0)
            problems.Add("the rules file declares no hooks");
        return hooks;
    }

    // ---- what an area has placed ----------------------------------------------------------------

    /// <summary>
    /// Every placement in the area: a region's list entries that carry a position, plus the spawn definitions that
    /// name the region. An entry without a position of its own (a switch or a barrier mounted on a structure) takes
    /// its structure's position. Where a region holds several areas, a placement belongs to the area whose location
    /// anchor is nearest it, which is why the rules file and the dossiers must agree on which locations an area owns.
    /// </summary>
    /// <summary>
    /// Divides each region's placements among the areas that claim it: a placement belongs to the area whose location
    /// anchor is nearest, and to that area alone, so two areas in one region cannot both count the same forge. What no
    /// anchor reaches stays unassigned and is reported. An area that names a region and owns no location in it claims
    /// the whole region (the standalone Ashen Hollow); an area reached from another region also takes that region's
    /// portals, because the door to a place is placed in the place you leave.
    /// </summary>
    private static Dictionary<string, List<Placement>> Shares(List<AuditArea> areas, ContentLoader loader, List<string> problems)
    {
        var shares = areas.ToDictionary(area => area.Slug, _ => new List<Placement>(), StringComparer.Ordinal);
        var byRegion = new Dictionary<string, List<AuditArea>>(StringComparer.Ordinal);
        foreach (var area in areas)
            foreach (string regionRef in area.RegionRefs)
            {
                if (!byRegion.TryGetValue(regionRef, out var list))
                    byRegion[regionRef] = list = [];
                list.Add(area);
            }

        foreach (var (regionRef, claimants) in byRegion)
        {
            if (!loader.Definitions.TryGetValue(regionRef, out var definition) || ContentChecks.Parse(definition) is not { } region)
                continue;
            var anchors = claimants.SelectMany(area => Anchors(area, loader).Select(anchor => (Anchor: anchor, Area: area))).ToList();
            // A record struct's default has no slug: that is how "no whole-region owner" is told apart from one.
            string? wholeRegion = claimants.FirstOrDefault(area => area.Locations.Length == 0).Slug;

            foreach (var placement in RegionPlacements(region, regionRef, loader))
            {
                if (wholeRegion is { } owner)
                {
                    shares[owner].Add(placement);
                    continue;
                }
                var nearest = anchors
                    .Select(candidate => (candidate.Area, Distance: Distance(candidate.Anchor, placement)))
                    .Where(candidate => candidate.Distance <= AssignmentRadiusM)
                    .OrderBy(candidate => candidate.Distance)
                    .FirstOrDefault();
                if (nearest.Area.Slug is not null)
                    shares[nearest.Area.Slug].Add(placement);
            }
        }

        foreach (var area in areas)
            foreach (string from in area.PortalsFrom)
                if (loader.Definitions.TryGetValue(from, out var definition) && ContentChecks.Parse(definition) is { } region)
                    foreach (var portal in Items(region, "portals").OfType<YamlMappingNode>())
                        shares[area.Slug].Add(new Placement("portals", portal, 0, 0));


        // A place no dossier owns is a place nobody audits, and silence would read as coverage.
        foreach (var definition in loader.Definitions.Values.Where(d => d.Kind == "location"))
        {
            if (ContentChecks.Parse(definition) is not { } root || Scalar(root, "region_ref") is not { } regionRef)
                continue;
            if (!byRegion.ContainsKey(regionRef) || areas.Any(area => area.Locations.Contains(definition.Id)))
                continue;
            problems.Add($"{definition.Id} sits in {regionRef} and no area claims it, so its hooks go unaudited");
        }

        return shares;
    }

    /// <summary>Everything a region places, with a position: its section entries (mounted entries at their structure) and its spawns.</summary>
    private static List<Placement> RegionPlacements(YamlMappingNode region, string regionRef, ContentLoader loader)
    {
        var all = new List<Placement>();
        var structures = Structures(region);
        foreach (var (sectionNode, value) in region.Children)
        {
            if (value is not YamlSequenceNode sequence)
                continue;
            string section = ScalarValue(sectionNode) ?? "";
            foreach (var item in sequence.Children.OfType<YamlMappingNode>())
            {
                if (TryPosition(item, out double x, out double y))
                    all.Add(new Placement(section, item, x, y));
                else if (structures.GetValueOrDefault(Scalar(item, "structure") ?? "") is { } mounted)
                    all.Add(new Placement(section, item, mounted.X, mounted.Y));
            }
        }
        foreach (var definition in loader.Definitions.Values.Where(d => d.Kind == "spawn"))
        {
            if (ContentChecks.Parse(definition) is not { } root || Scalar(root, "region_ref") != regionRef)
                continue;
            if (TryPosition(root, out double x, out double y))
                all.Add(new Placement("spawns", root, x, y));
        }
        return all;
    }

    /// <summary>A region's structures by id, so an entry mounted on one can be given a place to be audited at.</summary>
    private static Dictionary<string, (double X, double Y)> Structures(YamlMappingNode region)
    {
        var structures = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        foreach (var entry in Maps(region, "structures"))
            if (Scalar(entry, "id") is { } id && TryPosition(entry, out double x, out double y))
                structures[id] = (x, y);
        return structures;
    }

    private static List<(double X, double Y)> Anchors(AuditArea area, ContentLoader loader)
    {
        var anchors = new List<(double, double)>();
        foreach (string id in area.Locations)
        {
            if (!loader.Definitions.TryGetValue(id, out var definition) || ContentChecks.Parse(definition) is not { } root)
                continue;
            if (Map(root, "anchor") is { } anchor && TryPosition(anchor, out double x, out double y))
                anchors.Add((x, y));
        }
        return anchors;
    }

    private static double Distance((double X, double Y) anchor, Placement placement) =>
        Math.Sqrt(Math.Pow(anchor.X - placement.X, 2) + Math.Pow(anchor.Y - placement.Y, 2));

    private static bool TryPosition(YamlMappingNode node, out double x, out double y)
    {
        x = y = 0;
        if (TryCoordinates(node, out x, out y))
            return true;
        return Map(node, "at") is { } at && TryCoordinates(at, out x, out y);
    }

    private static bool TryCoordinates(YamlMappingNode node, out double x, out double y)
    {
        x = y = 0;
        foreach (string field in PositionFields)
        {
            if (Node(node, field) is not YamlSequenceNode sequence || sequence.Children.Count < 2)
                continue;
            if (AsNumber(sequence.Children[0]) is not { } first || AsNumber(sequence.Children[1]) is not { } second)
                continue;
            x = first;
            y = second;
            // A location anchor is [x, y, z]: the third number is the map's second axis.
            if (field == "world" && sequence.Children.Count >= 3
                && AsNumber(sequence.Children[2]) is { } z)
            {
                x = first;
                y = z;
            }
            // box_m is [x1, y1, x2, y2]: the centre stands in for the door's place.
            if (field == "box_m" && sequence.Children.Count >= 4
                && AsNumber(sequence.Children[2]) is { } third && AsNumber(sequence.Children[3]) is { } fourth)
            {
                x = (first + third) / 2;
                y = (second + fourth) / 2;
            }
            return true;
        }
        return false;
    }

    // ---- the evidence vocabulary ----------------------------------------------------------------

    private static int Count(AuditHook hook, AuditArea? area, List<Placement> placed, WorldFacts facts)
    {
        string kind = ScalarValue(hook.Evidence.Children.Keys.First()) ?? "";
        if (hook.Evidence.Children.Values.First() is not YamlMappingNode argument)
            return 0;

        switch (kind)
        {
            case "any_field":
                return placed.Count(p => p.Section == Scalar(argument, "section") && Has(p.Node, Scalar(argument, "field")));
            case "any_field_min":
            {
                int atLeast = (int)(AsNumber(Node(argument, "at_least")) ?? 0);
                return placed.Count(p => p.Section == Scalar(argument, "section")
                                         && AsNumber(Node(p.Node, Scalar(argument, "field"))) is { } value && value >= atLeast);
            }
            case "field_equals":
                return placed.Count(p => p.Section == Scalar(argument, "section")
                                         && Scalar(p.Node, Scalar(argument, "field")) == Scalar(argument, "equals"));
            case "node":
            {
                string prefix = $"node.{Scalar(argument, "prefix")?.TrimEnd('.')}.";
                return placed.Count(p => p.Section == "nodes"
                                         && Scalar(p.Node, "node_ref")?.StartsWith(prefix, StringComparison.Ordinal) == true);
            }
            case "readable_field":
                return placed.Count(p => p.Section == "readables" && Has(p.Node, Scalar(argument, "field")));
            case "ambience":
            {
                // A place's own sound, bound in content (D18 SYSTEMS, needed-2): one `ambience` definition per place.
                return facts.AmbiencePlaces;
            }
            case "grave":
            {
                // A grave in this area (D18 SYSTEMS, needed-2; HOOK.DEATH.GRAVE): a marker placed as a readable that
                // opens a grave's page, which is how a name is kept in the world.
                // A grave is counted by the place it names, not by which area the placement share-out happens to give
                // its marker to: the dossier owns the place, and the grave belongs to it (D18 SYSTEMS, needed-2).
                if (area is not { } owner)
                    return facts.Graves.Count;
                return facts.Graves.Count(g => g.Location is { } place && owner.Locations.Contains(place));
            }
            case "ritual":
            {
                // A circle in this area (D18 SYSTEMS, needed-2; HOOK.RITUAL.CIRCLE): the ritual content kind names its
                // place, and the dossier that owns the place is the one that has it.
                if (area is not { } circleArea)
                    return facts.Rituals.Count;
                return facts.Rituals.Count(c => c.Location is { } place && circleArea.Locations.Contains(place));
            }
            case "node_tier":
            {
                // A node of a given grade: "a second and third ore grade" is a tier, and the build states it on the node
                // kind (a placement may override it). Content scope: the capability exists in the build for areas to use.
                string prefix = $"node.{Scalar(argument, "prefix")?.TrimEnd('.')}.";
                int atLeast = (int)(AsNumber(Node(argument, "min_tier")) ?? 2);
                return facts.NodeList.Count(n => n.Id.StartsWith(prefix, StringComparison.Ordinal) && n.Tier >= atLeast);
            }
            case "node_hidden":
            {
                string prefix = $"node.{Scalar(argument, "prefix")?.TrimEnd('.')}.";
                
                return facts.NodeList.Count(n => n.Id.StartsWith(prefix, StringComparison.Ordinal) && n.Hidden);
            }
            case "node_ref":
            {
                // One named node, for a hook that is about that kind of thing rather than a prefix of it (resin).
                string? wanted = Scalar(argument, "ref");
                return facts.NodeList.Count(n => n.Id == wanted);
            }
            case "node_skill":
            {
                // The node's own harvest skill: what rises from it, rather than every node riding survival.
                string? wanted = Scalar(argument, "skill_ref");
                string? notWanted = Scalar(argument, "not_skill");
                return placed.Count(p => p.Section == "nodes"
                                         && Scalar(p.Node, "node_ref") is { } node
                                         && facts.Nodes.GetValueOrDefault(node).Skill is { Length: > 0 } skill
                                         && (string.IsNullOrEmpty(wanted) || skill == wanted)
                                         && (string.IsNullOrEmpty(notWanted) || skill != notWanted));
            }
            case "spawn":
                return placed.Count(p => p.Section == "spawns" && SpawnMatches(p.Node, argument, facts));
            case "spawn_pack":
            {
                int group = (int)(AsNumber(Node(argument, "min_count")) ?? 2);
                return placed.Count(p => p.Section == "spawns" && LargestGroup(p.Node) >= group);
            }
            case "teaches":
                return placed.Count(p => p.Section == "npcs"
                                         && (facts.NpcTeaches.GetValueOrDefault(Scalar(p.Node, "npc_ref") ?? "") ?? [])
                                             .Contains(Scalar(argument, "kind")));
            case "npc_services":
                return placed.Count(p => p.Section == "npcs"
                                         && (facts.NpcServices.GetValueOrDefault(Scalar(p.Node, "npc_ref") ?? "") ?? [])
                                             .Contains(Scalar(argument, "service")));
            case "location":
                return area?.Locations.Count(id => facts.LocationKinds.GetValueOrDefault(id) == Scalar(argument, "kind_of_place")) ?? 0;
            default:
                return 0;
        }
    }

    private static bool SpawnMatches(YamlMappingNode spawn, YamlMappingNode argument, WorldFacts facts)
    {
        double? level = Items(spawn, "level_band").Select(AsNumber).FirstOrDefault();
        if (AsNumber(Node(argument, "level_min")) is { } minimum && (level is null || level < minimum))
            return false;
        if (AsNumber(Node(argument, "level_max")) is { } maximum && (level is null || level > maximum))
            return false;
        string? archetype = Scalar(argument, "archetype");
        if (archetype is null)
            return true;
        return Maps(spawn, "creatures").Any(entry =>
            facts.Archetypes.GetValueOrDefault(Scalar(entry, "creature_ref") ?? "") == archetype);
    }

    /// <summary>The largest group a spawn places, from its <c>count: [min, max]</c> range.</summary>
    private static int LargestGroup(YamlMappingNode spawn)
    {
        int largest = 1;
        foreach (var entry in Maps(spawn, "creatures"))
            largest = Math.Max(largest, (int)(Items(entry, "count").Select(AsNumber).LastOrDefault() ?? 1));
        return largest;
    }

    private static int Scale(int minimum, double factor) => (int)Math.Ceiling(minimum * factor);

    // ---- content lookups the evidence needs -----------------------------------------------------

    /// <summary>What a spawn or an NPC means, read once from the definitions the loader already holds.</summary>
    private sealed class WorldFacts
    {
        public Dictionary<string, string> Archetypes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string?>> NpcTeaches { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string?>> NpcServices { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string?> LocationKinds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (int Tier, bool Hidden, string Skill)> Nodes { get; } = new(StringComparer.Ordinal);
        public List<(string Id, int Tier, bool Hidden, string Skill)> NodeList { get; } = [];

        /// <summary>How many places content binds an ambience to (the `ambience` kind).</summary>
        public int AmbiencePlaces { get; private set; }

        /// <summary>The graves content defines, each with the place it names (D18 SYSTEMS, needed-2; HOOK.DEATH.GRAVE).</summary>
        public List<(string Id, string? Location)> Graves { get; } = [];

        /// <summary>The ritual circles content defines, each with the place it names (HOOK.RITUAL.CIRCLE).</summary>
        public List<(string Id, string? Location)> Rituals { get; } = [];

        public WorldFacts(ContentLoader loader)
        {
            foreach (var definition in loader.Definitions.Values)
            {
                if (ContentChecks.Parse(definition) is not { } root)
                    continue;
                switch (definition.Kind)
                {
                    case "creature":
                        Archetypes[definition.Id] = Scalar(root, "archetype") ?? "";
                        break;
                    case "location":
                        LocationKinds[definition.Id] = Scalar(root, "kind_of_place");
                        break;
                    case "node":
                        Nodes[definition.Id] = (
                            int.TryParse(Scalar(root, "tier"), out int tier) ? tier : 1,
                            Scalar(root, "hidden") is { } hiddenText && string.Equals(hiddenText, "true", StringComparison.OrdinalIgnoreCase),
                            Scalar(root, "harvest_skill") ?? "");
                        NodeList.Add((definition.Id, Nodes[definition.Id].Tier, Nodes[definition.Id].Hidden, Nodes[definition.Id].Skill));
                        break;
                    case "ambience":
                        AmbiencePlaces++;
                        break;
                    case "grave":
                        Graves.Add((definition.Id, Scalar(root, "location_ref")));
                        break;
                    case "ritual":
                        Rituals.Add((definition.Id, Scalar(root, "location_ref")));
                        break;
                    case "npc":
                        NpcTeaches[definition.Id] = Maps(root, "teaches").Select(entry => Scalar(entry, "kind")).ToList();
                        NpcServices[definition.Id] = Texts(root, "services").Select(s => (string?)s).ToList();
                        break;
                }
            }
        }
    }

    // ---- small YAML reads -----------------------------------------------------------------------

    private readonly record struct AuditArea(string Slug, string Name, string[] RegionRefs, string Type, string Status, string Band,
        string[] Locations, string[] PortalsFrom, YamlMappingNode[] Regions);

    private readonly record struct AuditHook(string Id, string Scope, string Note, bool BandScaled, bool Unmodelled,
        YamlMappingNode Evidence, Dictionary<string, int> Minimums);

    private readonly record struct Placement(string Section, YamlMappingNode Node, double X, double Y);

    private static YamlNode? Node(YamlMappingNode? map, string? key) =>
        map is not null && key is not null && map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static YamlMappingNode? Map(YamlMappingNode? map, string? key) => Node(map, key) as YamlMappingNode;

    private static string? ScalarValue(YamlNode? node) => (node as YamlScalarNode)?.Value;

    private static string? Scalar(YamlMappingNode? map, string? key) => ScalarValue(Node(map, key));

    private static bool Has(YamlMappingNode map, string? key) =>
        key is not null && map.Children.ContainsKey(new YamlScalarNode(key));

    private static List<YamlNode> Items(YamlMappingNode? map, string key) =>
        Node(map, key) is YamlSequenceNode sequence ? sequence.Children.ToList() : [];

    private static List<YamlMappingNode> Maps(YamlMappingNode? map, string key) => Items(map, key).OfType<YamlMappingNode>().ToList();

    private static List<string> Texts(YamlMappingNode? map, string key) =>
        Items(map, key).Select(ScalarValue).Where(text => text is not null).Select(text => text!).ToList();

    /// <summary>A field that is either one value or a list of them: an area's <c>region_ref</c>.</summary>
    private static string[] TextOrTexts(YamlMappingNode? map, string key) =>
        Node(map, key) is YamlSequenceNode ? Texts(map, key).ToArray() : Scalar(map, key) is { } single ? [single] : [];

    private static double? AsNumber(YamlNode? node) =>
        double.TryParse(ScalarValue(node), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
}
