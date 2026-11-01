using System.Globalization;
using System.Text.RegularExpressions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Bodies do not stand in each other (owner finding 15): three adventurers leaving by the same gate stood in the same spot, and the frame
/// showed one figure where three were. The party leaves in file now, and nothing else in the day puts two bodies in one place either.
/// Owner finding 27 (Playtest 2): townsfolk shared stand points, so two people breathed in and out of each other at the Steps and at
/// Cairnwold. The two live tests below only see the one settlement they are opened in; the last test reads every routine in the pack so
/// no settlement can hide a shared spot from a test that happens to run elsewhere.
/// </summary>
public class NpcSpacingTests
{
    private const long Hour = 2_400;
    private const long Minute = 40;
    private const long NearestMm = 400;
    private const long TwoBodiesMm = 900;

    [Fact]
    public void TheParty_LeavesTheGateInFile_AStrideApart()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // C2 ships one Hollow adventurer; retain the original three-body regression fixture.
        var adventurers = session.Setup.Adventurers!;
        var setup = session.Setup with { Adventurers = adventurers with {
            Settlements = adventurers.Settlements.SetItem("ashen_hollow", adventurers.Settlements["ashen_hollow"] with { Target = 3 }) } };
        var arena = Arena.OpenCreatures(session, setup, (448.6, 727), 90,
            Array.Empty<(string, double, double, string)>(), seed: 42, keepSpawns: true, origin: Harness.Region);
        var simulation = arena.Simulation;
        // The second day, 07:50: five minutes before the departure hour, the three of them are on the way out.
        simulation.Advance(58_400);
        var leaving = simulation.Npcs.Where(n => n.Role == "adventurer" && n.Id.Contains("ashen_hollow")).ToList();
        Assert.Equal(3, leaving.Count);
        Assert.All(leaving, b => Assert.True(Far(b, leaving)));
        // In file: about 0.8 m between one and the next, not scattered about.
        var ordered = leaving.OrderBy(b => b.Body.XMm).ThenBy(b => b.Body.ZMm).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            long step = Tow(ordered[i].Body, ordered[i - 1].Body);
            Assert.InRange(step, 700, 900);
        }
    }

    [Fact]
    public void NoTwoBodies_StandInTheSamePlace_ThroughTheDay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var simulation = arena.Simulation;
        // A whole day of the second day, a minute at a time: every place the schedule puts people, walked and not walked.
        simulation.Advance(39_600);
        for (int minute = 0; minute < 24 * 60; minute++)
        {
            var bodies = simulation.Npcs.Where(n => n.Role != "player").ToList();
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                    Assert.True(Tow(bodies[i].Body, bodies[j].Body) >= NearestMm,
                        $"{bodies[i].Id} and {bodies[j].Id} stand {Tow(bodies[i].Body, bodies[j].Body) / 1000.0:0.00} m apart at " +
                        $"{session.Setup.Clock.MinuteOfDay(simulation.WorldTick) / 60:00}:{session.Setup.Clock.MinuteOfDay(simulation.WorldTick) % 60:00}");
            simulation.Advance(Minute);
        }
    }

    /// <summary>
    /// Every routine in every settlement: two different people may not have a stand point within two body radii of each other, wherever
    /// the other one happens to be at that hour. A shared anchor is the one thing the live tests cannot catch from a different world.
    /// </summary>
    [Fact]
    public void NoTwoRoutines_InAnySettlement_ShareAStandingPoint()
    {
        var routines = Directory.EnumerateFiles(Path.Combine(ContentRoot(), "routines"), "*.yaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path, System.Text.Encoding.UTF8)))
            .Select(file => new
            {
                Npc = Regex.Match(file.Text, @"(?m)^npc_ref:\s*(\S+)").Groups[1].Value,
                Settlement = Regex.Match(file.Text, @"(?m)^settlement:\s*(\S+)").Groups[1].Value,
                Text = file.Text,
                Path = file.Path,
            })
            .Where(routine => routine.Npc.Length > 0 && routine.Settlement.Length > 0)
            .Select(routine => new
            {
                routine.Npc,
                routine.Settlement,
                routine.Path,
                Points = Regex.Matches(routine.Text, @"position_m:\s*\[\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\]")
                    .Select(m => (
                        X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        Z: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
                    .ToArray(),
            })
            .ToArray();

        Assert.NotEmpty(routines);

        var shared = new List<string>();
        for (int i = 0; i < routines.Length; i++)
            for (int j = i + 1; j < routines.Length; j++)
            {
                var a = routines[i];
                var b = routines[j];
                if (a.Settlement != b.Settlement || a.Npc == b.Npc)
                    continue;
                foreach (var pa in a.Points)
                    foreach (var pb in b.Points)
                    {
                        double dx = pa.X - pb.X, dz = pa.Z - pb.Z;
                        long gapMm = (long)Math.Round(Math.Sqrt(dx * dx + dz * dz) * 1000);
                        if (gapMm < TwoBodiesMm)
                            shared.Add($"{a.Settlement}: {a.Npc} and {b.Npc} stand {gapMm} mm apart " +
                                       $"({Path.GetFileName(a.Path)} / {Path.GetFileName(b.Path)})");
                    }
            }

        Assert.Empty(shared);
    }

    private static string ContentRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "content", "routines")))
            at = at.Parent;
        Assert.NotNull(at);
        return Path.Combine(at!.FullName, "content");
    }

    private static bool Far(NpcView body, List<NpcView> others) =>
        others.Where(o => o.Id != body.Id).All(o => Tow(o.Body, body.Body) >= NearestMm);

    private static long Tow(Body a, Body b)
    {
        long dx = a.XMm - b.XMm, dz = a.ZMm - b.ZMm;
        return (long)Math.Sqrt(dx * dx + dz * dz);
    }
}
