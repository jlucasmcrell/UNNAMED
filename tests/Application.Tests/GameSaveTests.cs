using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// What each schema step since schema 12 adds to the dump, and the value the step gives it (null: derived, so it moves); every other
    /// field must be as it was saved.
    /// </summary>
    private static readonly (Regex Path, string? Given)[] AddedSince12 =
    {
        // Schema 13: the player gains a posture, standing on the ground; the player's digest covers it, so it moves.
        (new Regex(@"^\$\.player\.Posture$"), """{"Stance":"Standing","Airborne":false,"AirMs":0}"""),
        (new Regex(@"^\$\.player\.Digest$"), null),
        // Schema 14: every creature record gains its continuation - none was kept - and the world its sounds waiting to be heard - none.
        (new Regex(@"^\$\.world\.Creatures\[\d+\]\.(NextChargeTick|StaggerImmuneUntil|StaggerLastsTicks)$"), "0"),
        (new Regex(@"^\$\.world\.Creatures\[\d+\]\.StaggeredTick$"), "null"),
        (new Regex(@"^\$\.world\.Noises$"), "[]"),
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
        foreach (string difference in differences)
        {
            string path = difference[..difference.IndexOf(": expected ", StringComparison.Ordinal)];
            var (pattern, given) = AddedSince12.FirstOrDefault(a => a.Path.IsMatch(path));
            Assert.True(pattern is not null, $"{difference}: not something a schema step since 12 adds");
            if (given is not null)
                Assert.Equal($"{path}: expected (absent), actual {given}", difference);
        }
        int creatureRecords = expected["world"]!["Creatures"]!.AsArray().Count;
        Assert.True(creatureRecords > 0, "the M6 save holds no creature records");
        Assert.Equal(2 + 4 * creatureRecords + 1, differences.Count);   // the posture and digest, four fields a record, the noises

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
