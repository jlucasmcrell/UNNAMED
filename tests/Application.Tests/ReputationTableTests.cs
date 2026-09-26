using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UNNAMED.Domain.Factions;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;
using static UNNAMED.Application.Tests.ReputationFixture;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The reputation fixture table (M7 design §5.15): <c>docs/M7_REPUTATION_TABLE.md</c>, generated from the build. It fails when the
/// committed file differs; <c>UNNAMED_WRITE_REPUTATION=1</c> writes it, and refuses under <c>CI=true</c> so CI can never make it a
/// tautology. It also asserts the ROADMAP proof itself: in F1 and P5 the same act moves two factions in opposite directions.
/// </summary>
public class ReputationTableTests
{
    private sealed record Row(string Id, string Scenario, string Act, string Reports, string A, string B, string Other, string Proves);

    private static readonly (double X, double Z) WolfNorth = (60.3, 142.3);
    private static readonly (double X, double Z) WolfWest = (57.8, 142.8);

    /// <summary>F14's seventeen wolves: one in the smithy, sixteen asleep on the open ground north-east of it, two metres apart.</summary>
    private static readonly (double X, double Z)[] Pack = new[] { WolfNorth }
        .Concat(Enumerable.Range(0, 16).Select(i => (X: 67.0 + 2.0 * (i % 4), Z: 151.0 + 2.0 * (i / 4)))).ToArray();

    private static readonly (double X, double Z)[] OutAndAround = { (51.8, 142), (52.2, 147.3), (58, 147.4), (65, 148.5) };

    private static string Short(string? npc) => npc is null ? "none" : npc[(npc.LastIndexOf('.') + 1)..].Split('_')[0];

    private static string Signed(int delta) => delta.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    /// <summary>A faction's column: its knowledge of the act and where it stands.</summary>
    private static string Column(Arena arena, string faction, long act)
    {
        var view = arena.Simulation.Factions.Single(f => f.Id == faction);
        var known = arena.Simulation.Acts.FirstOrDefault(a => a.Seq == act)?.Known.FirstOrDefault(k => k.Knower == faction);
        return known is null
            ? $"no row, {view.Points}, {view.Tier}"
            : $"{known.Source}, {Short(known.Via)}, {known.Identity}, {Signed(known.Delta)}, {view.Points}, {view.Tier}";
    }

    private static string Acts(Arena arena) =>
        arena.Simulation.Acts.IsEmpty ? "none" : string.Join("; ", arena.Simulation.Acts.Select(a => $"{a.Seq}, {a.Kind}, {Short(a.Subject)}"));

    private static List<Row> FixtureRows(GameSession session, TempProfile profile)
    {
        var rows = new List<Row>();
        var setup = Fixture(session);

        // F1
        var f1 = Open(session, setup, new[] { WolfNorth });
        var f1Changes = f1.Record<ReputationChanged>();
        Kill(f1, f1.Simulation.Creatures.Single());
        Tell(f1, Kera, "wolf");
        Tell(f1, Sel, "wolf");
        rows.Add(new Row("F1", "wolf killed; tell Kera, then Sel", Acts(f1), "A via kera; B via sel", Column(f1, Keepers, 1), Column(f1, Delvers, 1),
            $"{f1Changes.Count} ReputationChanged", "**the same act moves two factions in opposite directions, in a fixture (ROADMAP exit)**"));
        var a1 = f1.Simulation.Factions.Single(f => f.Id == Keepers).Points;
        var b1 = f1.Simulation.Factions.Single(f => f.Id == Delvers).Points;
        Assert.True(a1 > 0 && b1 < 0, $"F1: the keepers moved {a1}, the delvers {b1}: not opposite directions");

        // F5: F1, told again
        var f5Learned = f1.Record<FactionLearned>();
        var f5Changes = f1.Record<ReputationChanged>();
        Tell(f1, Kera, "wolf");
        Tell(f1, Sel, "wolf");
        rows.Add(new Row("F5", "F1, then tell Kera and Sel again", Acts(f1), "A, B again", Column(f1, Keepers, 1), Column(f1, Delvers, 1),
            $"{f5Learned.Count} FactionLearned, {f5Changes.Count} ReputationChanged", "reports are idempotent"));

        // F2
        var f2 = Open(session, setup, new[] { WolfNorth });
        Kill(f2, f2.Simulation.Creatures.Single());
        Assert.Null(f2.Submit(new TalkCommand(f2.Player, Kera)));
        bool offered = Offered(f2).Contains("wolf");
        f2.Submit(new LeaveCommand(f2.Player));
        rows.Add(new Row("F2", "wolf killed; tell nobody", Acts(f2), "none", Column(f2, Keepers, 1), Column(f2, Delvers, 1),
            $"knowledge rows: {f2.Simulation.Acts.Sum(a => a.Known.Length)}; act_done holds: {(offered ? "yes" : "no")}", "no report, no change"));

        // F4: a crafted save, the act known unidentified; told after the resume
        var wolf = new ActRecord(1, ActKinds.CreatureKilled, Wolf, CellKey.OfWorld(Spot.X, Spot.Z).ToString(), 60_300, 140_300, 5);
        var unknown = new FactionLedger(2, ImmutableArray.Create(wolf),
            ImmutableArray.Create(new FactionKnowledge(Delvers, 1, Identities.Unidentified, KnowledgeSources.Witnessed, Sel, 6, 0)),
            ImmutableArray<FactionStanding>.Empty);
        var crafted = Open(session, setup, Array.Empty<(double, double)>(), r => r with { Factions = unknown });
        string before = Column(crafted, Delvers, 1);
        var f4 = Resume(session, crafted, profile, "f4");
        var f4Learned = f4.Record<FactionLearned>();
        var f4Changes = f4.Record<ReputationChanged>();
        Tell(f4, Sel, "wolf");
        rows.Add(new Row("F4", "crafted save: act 1 known unidentified by B (witnessed, via sel, Δ 0); then tell Sel", Acts(f4), "B via sel", "none",
            $"before: {before}. After: {Column(f4, Delvers, 1)}{(f4Learned.SingleOrDefault()?.Upgraded == true ? " (upgraded)" : "")}",
            $"{f4Learned.Count} FactionLearned with Upgraded; {f4Changes.Count} ReputationChanged", "the identity seam: nothing moves until identified, then the delta applies once"));

        // F8
        var f8 = Open(session, setup, new[] { WolfNorth, WolfWest });
        var f8Learned = f8.Record<FactionLearned>();
        foreach (var creature in f8.Simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).ToList())
            Kill(f8, creature);
        Tell(f8, Kera, "wolf");
        var f8Keepers = f8.Simulation.Acts.Select(a => a.Known.Single(k => k.Knower == Keepers).Delta);
        rows.Add(new Row("F8", "two wolves killed; tell Kera once", Acts(f8), "A via kera",
            $"{string.Join(", ", f8Keepers.Select(Signed))}: {f8.Simulation.Factions.Single(f => f.Id == Keepers).Points}, {f8.Simulation.Factions.Single(f => f.Id == Keepers).Tier}",
            Column(f8, Delvers, 1), $"{f8Learned.Count} FactionLearned, in Seq order {string.Join(" then ", f8Learned.Select(l => l.ActSeq))}", "distinct acts each apply"));

        // F10: saved between the act and the report
        var twin = Open(session, setup, new[] { WolfNorth });
        var saved = Open(session, setup, new[] { WolfNorth }, _ => twin.Simulation.CaptureRecord());
        Kill(twin, twin.Simulation.Creatures.Single());
        Kill(saved, saved.Simulation.Creatures.Single());
        var f10 = Resume(session, saved, profile, "f10");
        Tell(twin, Sel, "wolf");
        Tell(f10, Sel, "wolf");
        int differences = StateDump.Compare(StateDump.Render(twin.Simulation, replayable: true), StateDump.Render(f10.Simulation, replayable: true), out _).Count;
        rows.Add(new Row("F10", "F2; save and resume between the act and the report; tell Sel after the resume", Acts(f10), "B via sel", Column(f10, Keepers, 1),
            Column(f10, Delvers, 1), $"StateDump.Compare: {differences} differences against an unsaved twin that did the same", "continuity across a save"));

        // F11: relations
        var f11 = Open(session, Fixture(session, keepersToDelvers: "opposed", delversToKeepers: "close"), new[] { WolfNorth });
        Kill(f11, f11.Simulation.Creatures.Single());
        Tell(f11, Kera, "wolf");
        rows.Add(new Row("F11", "F1's act with relations A → B opposed, B → A close; tell Kera only", Acts(f11), "A via kera", Column(f11, Keepers, 1),
            Column(f11, Delvers, 1), "", "relations never move standing"));

        // F12: a third faction with no member
        var f12 = Open(session, Fixture(session, watchers: true), new[] { WolfNorth });
        Kill(f12, f12.Simulation.Creatures.Single());
        Tell(f12, Kera, "wolf");
        Tell(f12, Sel, "wolf");
        rows.Add(new Row("F12", "wolf killed; a third faction C (faction.fixture.watchers) reacts +100 but has no member; tell Kera and Sel", Acts(f12), "A, B",
            Column(f12, Keepers, 1), Column(f12, Delvers, 1), $"C: {Column(f12, Watchers, 1)}", "a faction nobody told never learns"));

        // F13: an act no faction reacts to
        const string boar = "creature.beast.bristleback_boar";
        var boarSetup = setup with { Combat = setup.Combat with { Creatures = setup.Combat.Creatures.SetItem(boar, setup.Combat.Creatures[boar] with { MaxHealth = 1 }) } };
        var f13 = Arena.OpenCreatures(session, boarSetup, Spot, 0, new[] { (boar, WolfNorth.X, WolfNorth.Z, "sleeper") });
        var f13Acts = f13.Record<ActRecorded>();
        Kill(f13, f13.Simulation.Creatures.Single());
        rows.Add(new Row("F13", "a bristleback boar killed; no faction reacts", Acts(f13), "none", Column(f13, Keepers, 1), Column(f13, Delvers, 1),
            $"{f13Acts.Count} ActRecorded; NextActSeq {f13.Simulation.CaptureRecord().Factions.NextActSeq}", "irrelevant acts are not recorded"));

        // F14: a log of 16; act 1 told, then sixteen more
        var f14 = Open(session, Fixture(session, capacity: 16), Pack);
        var creatures = f14.Simulation.Creatures.ToList();
        var first = creatures.OrderBy(c => Math.Abs(c.Body.XMm - (long)(WolfNorth.X * 1000)) + Math.Abs(c.Body.ZMm - (long)(WolfNorth.Z * 1000))).First();
        Kill(f14, first);
        Tell(f14, Kera, "wolf");
        Assert.True(f14.WalkTo(54.5, 142), $"F14: the walk to the door stopped at {f14.Simulation.Player.Body}");
        Assert.Null(f14.Submit(new InteractCommand(f14.Player, "door.forge_shed")));
        foreach (var (x, z) in OutAndAround)
            Assert.True(f14.WalkTo(x, z), $"F14: the walk out stopped at {f14.Simulation.Player.Body}");
        foreach (var creature in creatures.Where(c => c.Key != first.Key).OrderBy(c => c.Body.ZMm).ThenBy(c => c.Body.XMm))
        {
            f14.Fight(f14.Simulation.Creatures.Single(c => c.Key == creature.Key), 600);
            Assert.False(f14.Simulation.Creatures.Single(c => c.Key == creature.Key).Alive, $"F14: {creature.Key} still lives");
        }
        var ledger = f14.Simulation.CaptureRecord().Factions;
        rows.Add(new Row("F14", "capacity 16: wolf 1 killed and told to Kera, then 16 more wolves killed", $"{ledger.Acts[0].Seq}..{ledger.Acts[^1].Seq} held",
            "A via kera (act 1)", $"{f14.Simulation.Factions.Single(f => f.Id == Keepers).Points}, {f14.Simulation.Factions.Single(f => f.Id == Keepers).Tier}, unchanged by eviction",
            Column(f14, Delvers, 1), $"after act 17: act 1 {(ledger.Acts.Any(a => a.Seq == 1) ? "held" : "gone")}, knowledge rows {ledger.Knowledge.Length}; NextActSeq {ledger.NextActSeq}",
            "the oldest act goes first; standing is untouched"));

        // U1: the Domain rule
        var u1 = FactionRules.Learn(FactionLedger.Empty.WithAct(wolf), setup.Factions.Factions[Keepers], 1, KnowledgeSources.Reported, Kera,
            Identities.Unidentified, 7, setup.Factions.Ladder);
        rows.Add(new Row("U1", "Domain unit: Learn with identity: unidentified", "synthetic", "none",
            $"row stored, Δ {u1.Ledger.Knowledge.Single().Delta}, points {FactionRules.PointsOf(u1.Ledger, Keepers)}", "", "", "an unidentified row applies no delta"));
        return rows;
    }

    private static Arena Resume(GameSession session, Arena arena, TempProfile profile, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        return Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry())));
    }

    private static List<Row> ScriptRows(GameSession session)
    {
        var run = Walk(session);
        string Col(string step, string faction, long act)
        {
            var view = run.At[step].Factions.Single(f => f.Id == faction);
            var known = run.At[step].Acts.FirstOrDefault(a => a.Seq == act)?.Known.FirstOrDefault(k => k.Knower == faction);
            return known is null ? $"no row, {view.Points}, {view.Tier}"
                : $"{known.Source}, {Short(known.Via)}, {known.Identity}, {Signed(known.Delta)}, {view.Points}, {view.Tier}";
        }
        string Offers(string key, string reply) => run.Replies.TryGetValue(key, out var replies) && replies.Contains(reply) ? "offered" : "not offered";
        return new List<Row>
        {
            new("P1", "shipped: the armour killed (§5.6.4)", "1, creature_killed, animated", "none", Col("P1", Waystation, 1), Col("P1", Survey, 1),
                $"billets {(run.Wares["P1"].Wares.Any(w => w.ItemId == Billet) ? "listed" : "absent")} from Wares(Kera); a raw BuyCommand for the billet ref: \"{run.RawBilletBuyBeforeTelling}\"",
                "nobody knows"),
            new("P2", "the heart steadied", "2, switch_set, steadied", "none", Col("P2", Waystation, 2), Col("P2", Survey, 2),
                $"acts recorded: {run.At["P2"].Acts.Length} (the three stone flags record none)", "Tavar is no member: nobody knows"),
            new("P3", "tell Kera armour; buy one billet", "1", "Waystation via kera", Col("P3", Waystation, 1), Col("P3", Survey, 1),
                $"billets {(run.Wares["P3"].Wares.Any(w => w.ItemId == Billet) ? "listed" : "absent")}, one bought: {(run.BilletBoughtAtP3 is null ? "yes" : run.BilletBoughtAtP3)}; " +
                $"Kera's relationship events: {run.Regard.Count(r => r.NpcId == Kera)}", "report knowledge; the service gate opens; the Survey does not hear"),
            new("P4", "tell Sel tavar_back", "2", "Survey via sel", Col("P4", Waystation, 1), Col("P4", Survey, 2),
                $"notes {Offers("P4 Sel again", "notes")}", "the dialogue gate opens"),
            new("P5", "tell Sel armour", "1", "Survey via sel", Col("P5", Waystation, 1), Col("P5", Survey, 1),
                $"notes {Offers("P5 Sel again", "notes")}; Survey points {run.Points("P5", Survey)}, {run.Tier("P5", Survey)}",
                "**one act: Waystation +100, Survey -100**; the gate closes"),
            new("N1", "checkpoint after P2", "1, 2", "none", Col("P2", Waystation, 1), Col("P2", Survey, 2),
                $"knowledge rows: {run.At["P2"].Acts.Sum(a => a.Known.Length)}; Kera's armour reply {Offers("P3 Kera again", "armour")} (P3), Sel's {Offers("P4 Sel again", "armour")} (P4)",
                "no psychic factions"),
        };
    }

    private static string Render(GameSession session, List<Row> rows)
    {
        var text = new StringBuilder();
        text.Append("# M7 reputation fixture table\n\n");
        text.Append("Generated from the build by `tests/Application.Tests/ReputationTableTests.cs` (M7 design §5.15). Do not edit by hand: " +
            "run `UNNAMED_WRITE_REPUTATION=1 dotnet test --filter ReputationTable` from `src/` (refused when `CI=true`).\n\n");

        text.Append("## 1. Ladder (`config.factions`)\n\n| Tier | Level | Points |\n|---|---|---|\n");
        var tiers = session.Setup.Factions.Ladder.Tiers;
        for (int i = 0; i < tiers.Length; i++)
        {
            int max = i == 0 ? session.Setup.Factions.Ladder.MaxPoints : tiers[i - 1].MinPoints - 1;
            text.Append($"| {tiers[i].Key} | {tiers[i].Level:+0;-0;0} | {(tiers[i].MinPoints == max ? $"{max}" : $"{tiers[i].MinPoints} .. {max}")} |\n");
        }

        var factions = session.Setup.Factions.Factions.Values.ToList();
        text.Append("\n## 2. Reactions (content)\n\n| Act | Subject | " + string.Join(" | ", factions.Select(f => f.Name)) + " | |\n|---|---|"
            + string.Concat(factions.Select(_ => "---|")) + "---|\n");
        foreach (var pair in factions.SelectMany(f => f.Reactions).Select(r => (r.Kind, r.Subject)).Distinct().OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.Subject, StringComparer.Ordinal))
        {
            var deltas = factions.Select(f => f.Reactions.FirstOrDefault(r => r.Kind == pair.Kind && r.Subject == pair.Subject)?.Delta).ToList();
            bool opposite = deltas.Any(d => d > 0) && deltas.Any(d => d < 0);
            text.Append($"| {pair.Kind} | {pair.Subject} | {string.Join(" | ", deltas.Select(d => d is { } v ? Signed(v) : "-"))} | {(opposite ? "**opposite**" : "")} |\n");
        }

        text.Append("\n## 3. Factions\n\n| Faction | Name | Seat | Members | Relations |\n|---|---|---|---|---|\n");
        foreach (var f in factions)
        {
            var members = session.Setup.Social.Npcs.Values.Where(n => n.FactionId == f.Id).Select(n => n.Id).OrderBy(n => n, StringComparer.Ordinal);
            text.Append($"| {f.Id} | {f.Name} | {f.SeatLocationId} | {string.Join(", ", members)} | {string.Join(", ", f.Relations.Select(r => $"{r.FactionId} {r.Attitude}"))} |\n");
        }

        text.Append($"\n## 4. Knowledge\n\n- Built: `{KnowledgeSources.Reported}` - a faction learns an act when the character tells one of its members.\n" +
            $"- Reserved: `{KnowledgeSources.Witnessed}` (M9).\n- Act log capacity: {session.Setup.Factions.LogCapacity}; the oldest act goes first.\n");

        text.Append("\n## 5. Scenarios\n\nA is the Waystation in P rows and the keepers in F rows; B is the Survey or the delvers. A column reads: source, via, " +
            "identity, Δ, points, tier.\n\n| # | Scenario | Act (seq, kind, subject) | Reports | A | B | Other checks | Proves |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var row in rows)
            text.Append($"| {row.Id} | {row.Scenario} | {row.Act} | {row.Reports} | {row.A} | {row.B} | {row.Other} | {row.Proves} |\n");
        return text.ToString();
    }

    [Fact]
    public void TheReputationTable_IsGeneratedFromTheBuild_AndTheSameActMovesTwoFactionsOppositeWays()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rows = FixtureRows(session, profile).Concat(ScriptRows(session)).ToList();
        string table = Render(session, rows);

        string path = Path.Combine(Harness.RepoRoot(), "docs", "M7_REPUTATION_TABLE.md");
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_REPUTATION") == "1")
        {
            Assert.True(Environment.GetEnvironmentVariable("CI") != "true", "the reputation table is never written in CI");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(table));
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), table);

        // The exit criterion itself, not only the table: F1 and P5 move the same act's two factions in opposite directions.
        var p5 = rows.Single(r => r.Id == "P5");
        Assert.Contains("+100, 100, accepted", p5.A);
        Assert.Contains("-100, 0, neutral", p5.B);
        Assert.Contains("+100, 100, accepted", rows.Single(r => r.Id == "F1").A);
        Assert.Contains("-100, -100, wary", rows.Single(r => r.Id == "F1").B);
    }
}
