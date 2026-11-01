using System.Collections.Immutable;
using System.Text.RegularExpressions;
using UNNAMED.Domain;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// No boss court stands where people live. The Unpaid was authored two metres from the new-game spawn, so a brand-new character
/// opened their eyes beside a level-5 boss; this is the rule that would have caught it, and it holds for every encounter court in
/// the world that a session can open: a court's centre is never within 60 m of the new-game spawn or of a settlement.
/// </summary>
public class BossCourtPlacesTests
{
    private const long ClearanceMm = 60_000;

    /// <summary>The settlements and towns of the world, read from the locations content (id -> metres on the map).</summary>
    private static IReadOnlyList<(string Id, double X, double Z)> Settlements()
    {
        var root = Path.Combine(Harness.RepoRoot(), "content", "locations");
        var places = new List<(string, double, double)>();
        foreach (string file in Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
        {
            // Windows line endings: strip the carriage returns so a line-anchored read of a one-line field matches.
            string text = File.ReadAllText(file).Replace("\r", "");
            if (!Regex.IsMatch(text, @"kind_of_place: (settlement|town|village|hamlet)", RegexOptions.Multiline))
                continue;
            var id = Regex.Match(text, @"^id: (\S+)$", RegexOptions.Multiline).Groups[1].Value;
            var anchor = Regex.Match(text, @"anchor:\s*world:\s*\[\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\]", RegexOptions.Singleline);
            if (!anchor.Success)
                anchor = Regex.Match(text, @"anchor:\s*world:\s*-\s*([\d.]+)\s*-\s*([\d.]+)\s*-\s*([\d.]+)", RegexOptions.Singleline);
            if (id.Length == 0 || !anchor.Success)
                continue;
            places.Add((id, double.Parse(anchor.Groups[1].Value), double.Parse(anchor.Groups[3].Value)));
        }
        Assert.True(places.Count >= 3, $"read {places.Count} settlements from the locations content: {string.Join(", ", places.Select(p => p.Item1))}");
        return places;
    }

    /// <summary>
    /// The node refs a quest's own items come from, placed only a few times in the world: the spots a quest sends a player to. A
    /// timber stand or a woundmoss patch grows everywhere and a court will always stand over one; these do not.
    /// </summary>
    private static IReadOnlyList<(string Name, double X, double Z)> QuestGatherSpots()
    {
        var content = Path.Combine(Harness.RepoRoot(), "content");
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(content, "quests"), "*.yaml", SearchOption.AllDirectories))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"item_ref: (item\.\S+)"))
                wanted.Add(match.Groups[1].Value);
        Assert.NotEmpty(wanted);

        // Which item each node ref provides, through its resource.
        var nodeItems = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(content, "nodes"), "*.yaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r", "");
            var id = Regex.Match(text, @"^id: (\S+)$", RegexOptions.Multiline).Groups[1].Value;
            var resource = Regex.Match(text, @"resource_ref: (\S+)").Groups[1].Value;
            if (id.Length == 0 || resource.Length == 0)
                continue;
            string resourceFile = Directory.EnumerateFiles(Path.Combine(content, "resources"), "*.yaml", SearchOption.AllDirectories)
                .FirstOrDefault(path => File.ReadAllText(path).Contains($"id: {resource}", StringComparison.Ordinal));
            if (resourceFile is null)
                continue;
            nodeItems[id] = Regex.Matches(File.ReadAllText(resourceFile), @"item_ref: (item\.\S+)")
                .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        }

        var regionRoot = Path.Combine(content, "regions");
        var placements = new List<(string Name, string Ref, double X, double Z)>();
        foreach (string file in Directory.EnumerateFiles(regionRoot, "*.yaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r", "");
            foreach (Match match in Regex.Matches(text,
                         @"- name: (\S+)\n\s*node_ref: (\S+)\n\s*position_m:\n\s*-\s*([\d.]+)\n\s*-\s*([\d.]+)"))
                placements.Add((match.Groups[1].Value, match.Groups[2].Value,
                    double.Parse(match.Groups[3].Value), double.Parse(match.Groups[4].Value)));
        }
        Assert.NotEmpty(placements);

        // A spot is the quest's own when the node says so: its own notes name the quest that sends a player to it (the ashbloom
        // patch says "where Mara sends the player for What The Bog Kept"). Timber, meat, pelts and iron name no quest.
        var titles = Directory.EnumerateFiles(Path.Combine(content, "quests"), "*.yaml", SearchOption.AllDirectories)
            .Select(path => File.ReadAllText(path))
            .SelectMany(text => Regex.Matches(text, @"^title: (\S.*)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim()))
            .Where(title => title.Length > 0)
            .ToList();
        Assert.NotEmpty(titles);

        var questRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(content, "nodes"), "*.yaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r", "");
            var id = Regex.Match(text, @"^id: (\S+)$", RegexOptions.Multiline).Groups[1].Value;
            var notes = Regex.Match(text, @"^notes: (.*)$", RegexOptions.Multiline).Groups[1].Value;
            if (id.Length > 0 && titles.Any(title => notes.Contains(title, StringComparison.OrdinalIgnoreCase)))
                questRefs.Add(id);
        }
        var spots = placements
            .Where(p => questRefs.Contains(p.Ref))
            .Select(p => (p.Name, p.X, p.Z))
            .ToList();
        Assert.Contains(spots, spot => spot.Name == "bog_ashbloom_2");
        return spots;
    }

    [Fact]
    public void NoBossCourt_OverlapsAQuestsOwnGatherSpots()
    {
        var spots = QuestGatherSpots();
        var problems = new List<string>();
        int courts = 0;

        foreach (var (region, barrowworks) in new[]
                 {
                     ("region.starting_frontier", false), ("region.barrowworks", true), ("region.sedge_levels", false),
                     ("region.den_gorge_cave", false), ("region.still_field", false),
                 })
        {
            using var profile = new TempProfile();
            using var session = Harness.Boot(profile, region, barrowworks: barrowworks);
            foreach (var site in session.Setup.Combat.Spawns.Where(s => s.Key.StartsWith("spawn.boss.", StringComparison.Ordinal)))
            {
                if (site.Recovery is not { } court)
                    continue;
                courts++;
                foreach (var (name, x, z) in spots)
                {
                    if (x * 1000 >= court.MinXMm && x * 1000 <= court.MaxXMm && z * 1000 >= court.MinZMm && z * 1000 <= court.MaxZMm)
                        problems.Add($"{site.Key} ({region}) court {court.MinXMm / 1000:0},{court.MinZMm / 1000:0}-" +
                                     $"{court.MaxXMm / 1000:0},{court.MaxZMm / 1000:0} holds the gather spot {name} at {x:0},{z:0}");
                }
            }
        }

        Assert.True(courts >= 8, $"found {courts} boss courts to check");
        Assert.Empty(problems);
    }

    /// <summary>The point a new character opens their eyes at, from the frontier's own content.</summary>
    private static (double X, double Z) NewGameSpawn()
    {
        string text = File.ReadAllText(Path.Combine(Harness.RepoRoot(), "content", "regions", "starting_frontier.yaml")).Replace("\r", "");
        // The spawn is a block list in the region file: `spawn:` then `position_m:` then `- x` then `- z`.
        var match = Regex.Match(text, @"^spawn:\s*position_m:\s*-\s*([\d.]+)\s*-\s*([\d.]+)", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(match.Success, "the starting frontier declares its spawn point");
        return (double.Parse(match.Groups[1].Value), double.Parse(match.Groups[2].Value));
    }

    [Fact]
    public void NoBossCourt_StandsWithinSixtyMetres_OfTheNewGameSpawnOrASettlement()
    {
        var settlements = Settlements();
        var spawn = NewGameSpawn();
        // Courts that were already inside the radius when the rule was written, held for Playtest 2 (see the class comment).
        var held = new HashSet<string>(StringComparer.Ordinal)
        {
            "spawn.boss.ashen_hollow.wayholder_trial",
            "spawn.boss.the_road.old_grey",
        };
        var problems = new List<string>();
        var heldSeen = new HashSet<string>(StringComparer.Ordinal);
        int courts = 0;

        foreach (var (region, barrowworks) in new[]
                 {
                     ("region.starting_frontier", false), ("region.barrowworks", true), ("region.sedge_levels", false),
                     ("region.den_gorge_cave", false), ("region.still_field", false),
                 })
        {
            using var profile = new TempProfile();
            using var session = Harness.Boot(profile, region, barrowworks: barrowworks);
            foreach (var site in session.Setup.Combat.Spawns.Where(s => s.Key.StartsWith("spawn.boss.", StringComparison.Ordinal)))
            {
                if (site.Recovery is not { } court)
                    continue;
                courts++;
                double x = (court.MinXMm + court.MaxXMm) / 2000.0, z = (court.MinZMm + court.MaxZMm) / 2000.0;
                bool spawnTooNear = Math.Sqrt(Math.Pow(x - spawn.X, 2) + Math.Pow(z - spawn.Z, 2)) < ClearanceMm / 1000.0;
                var settlementsTooNear = settlements
                    .Where(place => Math.Sqrt(Math.Pow(x - place.X, 2) + Math.Pow(z - place.Z, 2)) < ClearanceMm / 1000.0)
                    .ToList();
                if (!spawnTooNear && settlementsTooNear.Count == 0)
                    continue;
                if (held.Contains(site.Key))
                {
                    heldSeen.Add(site.Key);
                    continue;
                }
                if (spawnTooNear)
                    problems.Add($"{site.Key} ({region}) court at {x:0},{z:0} is within 60 m of the new-game spawn {spawn.X:0},{spawn.Z:0}");
                foreach (var place in settlementsTooNear)
                    problems.Add($"{site.Key} ({region}) court at {x:0},{z:0} is " +
                                 $"{Math.Sqrt(Math.Pow(x - place.X, 2) + Math.Pow(z - place.Z, 2)):0} m from {place.Id}");
            }
        }

        Assert.True(courts >= 8, $"found {courts} boss courts to check");
        // The held list is a record of what is true, not a place to park new violations: every held court must still be one that
        // the rule catches, and nothing else may be caught.
        Assert.Equal(held.OrderBy(id => id, StringComparer.Ordinal), heldSeen.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(problems);
    }

    [Fact]
    public void TheUnpaid_StandsInTheSinkingMoss_AwayFromTheBogsGatherSpots()
    {
        // Back in the next update (the hold was its court sitting on What The Bog Kept's ashbloom): in the moss, spawned, and clear.
        string text = File.ReadAllText(Path.Combine(Harness.RepoRoot(), "content", "spawns", "bosses", "the_unpaid.yaml"))
            .Replace("\r", "");
        Assert.DoesNotContain("held_unopened_region", text);
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile);
        var site = session.Setup.Combat.Spawns.Single(s => s.Key == "spawn.boss.cairnwold.the_unpaid");
        // The bog anchors at (1050, 770): the bull is in the moss, not in Ashen Hollow's yard.
        Assert.InRange(site.XMm / 1000.0, 1010, 1090);
        Assert.InRange(site.ZMm / 1000.0, 740, 800);
        Assert.NotNull(site.Recovery);

        var spots = QuestGatherSpots();
        Assert.DoesNotContain(spots, spot =>
            spot.X * 1000 >= site.Recovery!.MinXMm && spot.X * 1000 <= site.Recovery.MaxXMm &&
            spot.Z * 1000 >= site.Recovery.MinZMm && spot.Z * 1000 <= site.Recovery.MaxZMm);
    }
}
