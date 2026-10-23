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
        // Schema 38: legacy creatures retain their original health and level (100%, no bonus).
        (new Regex(@"^\$\.world\.Creatures\[\d+\]\.ScalePercent$"), "100"),
        (new Regex(@"^\$\.world\.Creatures\[\d+\]\.LevelBonus$"), "0"),
        (new Regex(@"^\$\.world\.Noises$"), "[]"),
        // Schema 15: the player knows no Waystone, has chosen none to return to, and has walked no road.
        (new Regex(@"^\$\.player\.(Waystones|RoadsWalked)$"), "[]"),
        (new Regex(@"^\$\.player\.ReturnPoint$"), "null"),
        // Schema 16: the player has counted no deed.
        (new Regex(@"^\$\.player\.Deeds$"), "[]"),
        // Schema 17: the character the game made before the creator - "they", a Veth of the default body, with no Call.
        (new Regex(@"^\$\.player\.Identity$"), """{"Pronouns":"They","PeopleId":"people.veth","BodyPresetId":"body.default","CallId":null,"Appearance":null}"""),
        // Schema 19: every stack may carry a notable record; none before.
        (new Regex(@"^\$\.(player\.Inventory|world\.Created|world\.Containers\[\d+\]\.Items)\[\d+\]\.Notable$"), "null"),
        // Schema 24 (WP3b): every stack may carry its wear; nothing was worn before.
        (new Regex(@"^\$\.(player\.Inventory|world\.Created|world\.Containers\[\d+\]\.Items)\[\d+\]\.Wear$"), "null"),
        // Schema 20: no faction was ever told anything - the empty ledger.
        (new Regex(@"^\$\.player\.Factions$"), """{"NextActSeq":1,"Acts":[],"Knowledge":[],"Standing":[]}"""),
        // Schema 21 (WP15a): every party member was a companion, aggressive, keeping to the leash, with no contract; no one was recovering
        // or fallen.
        (new Regex(@"^\$\.player\.Companions\[\d+\]\.Membership$"), "\"Companion\""),
        (new Regex(@"^\$\.player\.Companions\[\d+\]\.Stance$"), "\"Aggressive\""),
        (new Regex(@"^\$\.player\.Companions\[\d+\]\.Tactics$"), """{"KeepToLeash":true,"ProtectInjured":false,"HoldHealing":false,"FocusTarget":false}"""),
        (new Regex(@"^\$\.player\.Companions\[\d+\]\.Contract$"), "null"),
        (new Regex(@"^\$\.player\.Party$"), null),
        // Schema 22: no economy was kept, so the markets start at their targets and the merchants with full purses, from content.
        (new Regex(@"^\$\.world\.(Economy|Merchants)$"), null),
        // Schema 23: an empty quick bar with nothing cleared off it; the world fills it from what is carried and known once it runs, not at
        // the load.
        (new Regex(@"^\$\.player\.QuickBar$"), "[null,null,null,null,null,null,null,null,null,null,null,null]"),
        (new Regex(@"^\$\.player\.QuickBarCleared$"), "[]"),
        // Schema 25 (WP15b): no party member had grown, worn or kept anything - no sheets.
        (new Regex(@"^\$\.player\.PartySheets$"), "[]"),
        // Schema 26: no lives were kept, so the NPCs begin their routines when the world starts (WP12).
        (new Regex(@"^\$\.world\.NpcLife$"), "null"),
        // Schema 27: no attack was kept under way - a load started every attack over.
        (new Regex(@"^\$\.player\.Attack$"), "null"),
        // Schema 29: no adventurers were kept, so each settlement's slots fill when the world starts (WP13): newcomers drawn from the world.
        (new Regex(@"^\$\.world\.Adventurers$"), null),
        // Schema 30 (the Barrowworks): the character stood in the exterior, come in by no portal.
        (new Regex(@"^\$\.player\.SpaceId$"), "\"\""),
        (new Regex(@"^\$\.player\.SpaceTraversal$"), "null"),
        // Schema 31 (UI progression F): no experiment recorded what it came to, so no Finding.
        (new Regex(@"^\$\.player\.Findings$"), "[]"),
        // Schema 36 (pets): no animal had joined the character, so no pets.
        (new Regex(@"^\$\.player\.Pets$"), "[]"),
        // Schema 35 (lore): no page had been filed in the journal, so no lore.
        (new Regex(@"^\$\.player\.Lore$"), "[]"),
        // Schema 32 (action bars): the quick bar is twelve slots, the other five bars are empty, and no gear set was kept.
        (new Regex(@"^\$\.player\.ExtraBars$"), "[[null,null,null,null,null,null,null,null,null,null,null,null],[null,null,null,null,null,null,null,null,null,null,null,null],[null,null,null,null,null,null,null,null,null,null,null,null],[null,null,null,null,null,null,null,null,null,null,null,null],[null,null,null,null,null,null,null,null,null,null,null,null]]"),
        (new Regex(@"^\$\.player\.GearSets$"), "[]"),
        // Schema 33 (inventory organisation): four empty bag slots, and each carried stack lies somewhere in the pack (the pack places it where its kind goes),
        // locked and marked as junk by no one.
        (new Regex(@"^\$\.player\.Bags$"), "[null,null,null,null]"),
        (new Regex(@"^\$\.player\.Inventory\[\d+\]\.(Bag|Slot)$"), null),
        (new Regex(@"^\$\.player\.Inventory\[\d+\]\.(Locked|Junk)$"), "false"),
        // Schema 34 (before testers): no weapon family held and no layouts yet - the first tick of a world adopts the one in hand.
        (new Regex(@"^\$\.player\.HeldFamily$"), "\"\""),
        (new Regex(@"^\$\.player\.FamilyBars$"), null),
    };

    [Fact]
    public void TheM6AcceptanceSave_LoadsUnderTodaysGame_AsItWasSaved_AndPlaysOn()
    {
        using var profile = new TempProfile();
        string slot = SaveSlots.Manual("acceptance");
        Copy(Path.Combine(Fixture("m6_acceptance"), "save"), Path.Combine(profile.Root, slot));
        // The M6 build played the hollow as a region of its own, before recovery 04f079a moved it into region.starting_frontier: its save
        // is of that region, and loads only there.
        var session = Harness.Boot(profile, "region.ashen_hollow");
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
        int stacks = differences.Count(d => d.Contains(".Notable: ", StringComparison.Ordinal));
        Assert.True(stacks >= expected["player"]!["Inventory"]!.AsArray().Count, "every carried stack gains its (empty) notable record");
        Assert.Equal(stacks, differences.Count(d => d.Contains(".Wear: ", StringComparison.Ordinal)));   // and its (empty) wear, schema 24
        // The posture and digest, four fields a record, the noises, the Waystones, the deeds, the identity, each stack's notable record,
        // the faction ledger, and the economy and merchants.
        int members = expected["player"]!["Companions"]!.AsArray().Count;
        // ... and, schema 21, four fields each party member and the party's ledger; schema 23, the quick bar and its cleared record;
        // schema 24, each stack's (empty) wear; schema 25, the party's sheets; schema 26, the NPCs' lives; schema 27, no attack under way; schema 29, the adventurers; schema 30, the space and the portal; schema 31, the Findings; schema 32, the further bars and the gear sets; and, schema 33, the bag slots and each stack's place, lock and junk mark; schema 34, the held weapon family and its layouts; schema 35, the empty lore; and, schema 36, the empty pets.
        Assert.Equal(2 + 6 * creatureRecords + 1 + 3 + 1 + 1 + stacks + 1 + 2 + 4 * members + 1 + 2 + stacks + 1 + 1 + 1 + 1 + 2 + 1 + 2 + 1 + 4 * stacks + 4, differences.Count);

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
