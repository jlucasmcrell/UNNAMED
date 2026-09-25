using System.Text.Json.Nodes;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, T-02: a save the game itself wrote with an older build, loaded under today's content, generator and
/// layout, compared with the state that build recorded at the save, and played on. The historical fixtures (Persistence.Tests) are a
/// probe's saves over a fixture world, never ticked; these are the game's own (GameSaves/README.md).
/// </summary>
public class GameSaveTests
{
    private static string Fixture(string name) => Path.Combine(Harness.RepoRoot(), "tests", "Application.Tests", "GameSaves", name);

    /// <summary>What each schema step since schema 12 adds to the dump, as the step gives it; every other field must be as it was saved.</summary>
    private static readonly SortedDictionary<string, string> AddedSince12 = new(StringComparer.Ordinal)
    {
        // Schema 13: the player gains a posture, standing on the ground; the player's digest covers it, so it moves.
        ["$.player.Posture"] = """{"Stance":"Standing","Airborne":false,"AirMs":0}""",
        ["$.player.Digest"] = "(recomputed)",
    };

    [Fact]
    public void TheM6AcceptanceSave_LoadsUnderTodaysGame_AsItWasSaved_AndPlaysOn()
    {
        using var profile = new TempProfile();
        string slot = SaveSlots.Manual("acceptance");
        Copy(Path.Combine(Fixture("m6_acceptance"), "save"), Path.Combine(profile.Root, slot));
        var session = Harness.Boot(profile);
        var loaded = session.Load(slot);

        Assert.True(loaded.IsComplete, $"quarantined [{string.Join(", ", loaded.QuarantinedSections)}], rejected " +
            $"[{string.Join("; ", loaded.RejectedRecords.Select(r => $"{r.Section} {r.Key}: {r.Reason}"))}], loss [{string.Join("; ", loaded.Report.Loss)}]");
        Assert.Equal(12, loaded.Report.SourceSchema);
        Assert.Equal(SaveFormat.SchemaVersion, loaded.Report.CurrentSchema);

        // Field by field against what the M6 build dumped at the save: equal, but for what later schema steps add.
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixture("m6_acceptance"), "state_saved.json")))!.AsObject();
        var actual = JsonNode.Parse(StateDump.Render(session.Simulation!))!.AsObject();
        actual.Remove("live");
        var differences = StateDump.Compare(expected.ToJsonString(), actual.ToJsonString(), out int leaves);
        Assert.True(leaves > 400, $"only {leaves} fields were compared");
        Assert.Equal(AddedSince12.Keys, differences.Select(d => d[..d.IndexOf(": expected ", StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
        foreach (var (path, value) in AddedSince12.Where(a => a.Value != "(recomputed)"))
            Assert.Contains($"{path}: expected (absent), actual {value}", differences);

        // And it plays on: a fixed stretch of ticks, then a walk to the Ashen Waystone, with no observer failing, no body inside anything,
        // Tavar still at the character's side and every quest still answering the debugger.
        var simulation = session.Simulation!;
        Harness.Ticks(session, 200);
        Assert.True(Harness.WalkTo(session, 30_000, 156_000), $"the walk home stopped at {simulation.Player.Body}");
        Harness.Ticks(session, 200);

        Assert.Equal(0, session.SubscriberFailures);
        var space = session.Setup.Layout.Space;
        var player = simulation.Player.Body;
        Assert.True(Kinematics.IsClear(player.XMm, player.ZMm, session.Setup.Movement.BodyRadiusMm, space, simulation.DynamicBlockers),
            $"the character stands inside something at {player}");
        foreach (var creature in simulation.Creatures.Where(c => c.Alive))
            Assert.True(Kinematics.IsClear(creature.Body.XMm, creature.Body.ZMm, session.Setup.Combat.Creatures[creature.DefId].RadiusMm, space,
                Array.Empty<Blocker>()), $"{creature.Key} stands inside something at {creature.Body}");
        var tavar = Assert.Single(simulation.Companions);
        Assert.Equal(CompanionOrder.Follow, tavar.Order);
        Assert.True(Math.Sqrt(Math.Pow(tavar.Body.XMm - player.XMm, 2) + Math.Pow(tavar.Body.ZMm - player.ZMm, 2)) < 8_000,
            $"Tavar is at {tavar.Body}, the character at {player}");
        Assert.NotEmpty(simulation.Quests);
        foreach (var quest in simulation.Quests)
            Assert.Empty(simulation.Diagnose(quest.Id).Problems);
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }
}
