using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Social;
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
        // Schema 15: the world gains its placed pieces - none - and their sequence, and the named NPCs' errands - none; the player a
        // faction ledger that knows nothing, and each companion a route - none.
        (new Regex(@"^\$\.world\.(Pieces|NpcErrands)$"), "[]"),
        (new Regex(@"^\$\.world\.StructureSequence$"), "0"),
        (new Regex(@"^\$\.player\.Factions$"), """{"NextActSeq":1,"Acts":[],"Knowledge":[],"Standing":[]}"""),
        (new Regex(@"^\$\.player\.Companions\[\d+\]\.Route$"),
            """{"Status":"None","GoalXMm":0,"GoalZMm":0,"Corners":[],"PlannedTick":0,"Stamp":0,"Watch":{"MinXMm":0,"MinZMm":0,"MaxXMm":0,"MaxZMm":0},"Partial":false}"""),
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
        int companions = expected["player"]!["Companions"]!.AsArray().Count;
        // The posture and digest, four fields a record, the noises; M7's world three, the ledger and a route a companion.
        Assert.Equal(2 + 4 * creatureRecords + 1 + 3 + 1 + companions, differences.Count);

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

    /// <summary>
    /// The same save under M7 (design §7.14), for what the field-by-field compare does not name: the fourth migration step, no transition
    /// and nothing to resolve, nothing built, no errand and no act known, Tavar with no route, and a first save that keeps the original
    /// byte for byte. E3 and E5 add their rows here.
    /// </summary>
    [Fact]
    public void TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand()
    {
        using var profile = new TempProfile();
        string slot = SaveSlots.Manual("acceptance");
        string committed = Path.Combine(Fixture("m6_acceptance"), "save");
        Copy(committed, Path.Combine(profile.Root, slot));
        var session = Harness.Boot(profile);
        var loaded = session.Load(slot);

        // Four schemas traversed, three migrations run: 14 -> 15 is the third of three.
        Assert.Equal(3, loaded.Report.Steps.Count);
        Assert.StartsWith("schema 12 -> 13:", loaded.Report.Steps[0]);
        Assert.StartsWith("schema 13 -> 14:", loaded.Report.Steps[1]);
        Assert.StartsWith("schema 14 -> 15:", loaded.Report.Steps[2]);
        Assert.Empty(loaded.Report.CellsRebased);
        Assert.Empty(loaded.Report.CellsMismatched);
        Assert.Empty(loaded.Report.Blockers);
        Assert.Empty(loaded.Report.Loss);
        Assert.Empty(loaded.Report.Aliases);
        Assert.Equal(session.Generator.Fingerprint, loaded.Manifest.WorldgenFingerprint);

        var simulation = session.Simulation!;
        Assert.Empty(simulation.World.Pieces);
        Assert.Equal(0, simulation.World.StructureSequence);
        Assert.Null(simulation.World.NpcErrand("npc.ashen_hollow.kera_voss"));
        var site = session.Setup.Layout.Npcs.Single(n => n.NpcId == "npc.ashen_hollow.kera_voss");
        var kera = simulation.Npcs.Single(n => n.Id == "npc.ashen_hollow.kera_voss");
        Assert.Equal((site.XMm, site.ZMm), (kera.Body.XMm, kera.Body.ZMm));
        Assert.Equal(FactionLedger.Empty, simulation.CaptureRecord().Factions);
        // E3: every faction neutral, nothing in the act log, Kera's billets withheld and Sel's notes not offered.
        Assert.Equal(new[] { "faction.ashen_hollow.survey", "faction.ashen_hollow.waystation" }, simulation.Factions.Select(f => f.Id));
        Assert.All(simulation.Factions, f => Assert.Equal((0, "neutral", 0), (f.Points, f.Tier, f.Level)));
        Assert.Empty(simulation.Acts);
        Assert.DoesNotContain(simulation.Wares("npc.ashen_hollow.kera_voss")!.Wares, w => w.ItemId == "item.material.iron_ingot");
        var sel = session.Setup.Social.Dialogues["dialogue.ashen_hollow.sel_arien"];
        var notes = sel.Nodes["again"].Choices.Single(c => c.Id == "notes");
        Assert.False(Assert.Single(notes.Conditions) is ReputationCondition standing
            && standing.MinLevel <= simulation.Factions.Single(f => f.Id == standing.FactionId).Level);
        // E5: the timber stack untouched - no record, its table's full 80 - and nothing built or audited.
        var stack = simulation.Containers.Single(c => c.Site.Key == "container.timber_stack");
        Assert.Null(stack.Id);
        Assert.Equal(80, stack.Items.Where(i => i.DefId == "item.material.timber").Sum(i => i.Count));
        Assert.Empty(simulation.StructureAudit);
        Assert.Empty(simulation.Pieces);
        Assert.Equal(0, simulation.StructureRevision);

        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixture("m6_acceptance"), "state_saved.json")))!;
        var tavar = Assert.Single(simulation.CaptureRecord().Companions);
        Assert.Equal(NavRoute.None, tavar.Route);
        Assert.Equal(saved["player"]!["Companions"]![0]!["Trail"]!.ToJsonString(), JsonSerializer.Serialize(tavar.Trail));

        // The first save keeps the original exactly as committed, under its exact pre-migration name (L-05), and reloads unchanged.
        string before = StateDump.Render(simulation);
        session.Save(slot);
        string original = Path.Combine(profile.Root, $"pre_migration_12_{slot}");
        foreach (string file in Directory.GetFiles(committed))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(original, Path.GetFileName(file))));
        var reloaded = Harness.Boot(profile);
        reloaded.Load(slot);
        Assert.Empty(StateDump.Compare(before, StateDump.Render(reloaded.Simulation!), out _));
        Assert.Equal(0, session.SubscriberFailures);
        Assert.Equal(0, reloaded.SubscriberFailures);
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }
}
