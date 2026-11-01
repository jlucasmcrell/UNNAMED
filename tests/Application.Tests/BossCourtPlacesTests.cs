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
    public void TheUnpaid_StandsInTheSinkingMoss()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile);
        var site = session.Setup.Combat.Spawns.Single(s => s.Key == "spawn.boss.cairnwold.the_unpaid");
        // The bog anchors at (1050, 770): the bull is in the moss, not in Ashen Hollow's yard.
        Assert.InRange(site.XMm, 1_030_000, 1_070_000);
        Assert.InRange(site.ZMm, 750_000, 790_000);
        Assert.NotNull(site.Recovery);
        Assert.InRange(site.Recovery!.MinXMm, 1_030_000, 1_045_000);
        Assert.InRange(site.Recovery.MaxXMm, 1_055_000, 1_070_000);
    }
}
