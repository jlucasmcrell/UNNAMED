using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Factions;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;
using static UNNAMED.Application.Tests.ReputationFixture;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Factions v1 in the running world (M7 design §5, §5.16): acts are recorded, a faction learns an act only when a member is told, what it
/// learns moves its standing once, and the two gates follow the standing. The P script plays it on shipped content; the F fixture
/// isolates each rule.
/// </summary>
public class FactionTests
{
    [Fact]
    public void TheSameKnownAct_MovesTwoFactionsInOppositeDirections()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var run = Walk(session);

        // P1: the armour is act 1, and nobody knows.
        var armour = run.Acts[0];
        Assert.Equal((1L, ActKinds.CreatureKilled, Armour), (armour.Seq, armour.Kind, armour.Subject));
        Assert.Equal((0, "neutral", 0, "neutral"), (run.Points("P1", Waystation), run.Tier("P1", Waystation), run.Points("P1", Survey), run.Tier("P1", Survey)));
        Assert.DoesNotContain(run.Wares["P1"].Wares, w => w.ItemId == Billet);
        Assert.Equal("Kera Voss will not sell you that", run.RawBilletBuyBeforeTelling);   // a raw buy at her side, before she is told

        // P2: the heart is act 2; the stones record nothing, and Tavar belongs to no faction.
        Assert.Equal(new[] { (1L, ActKinds.CreatureKilled, Armour), (2L, ActKinds.SwitchSet, Heart) }, run.Acts.Select(a => (a.Seq, a.Kind, a.Subject)));
        Assert.All(run.At["P2"].Acts, a => Assert.Empty(a.Known));

        // P3: Kera is told; the Waystation is accepted, the billets are sold, and the Survey has not heard.
        Assert.Contains("armour", run.Replies["P3 Kera again"]);
        Assert.Equal((100, "accepted"), (run.Points("P3", Waystation), run.Tier("P3", Waystation)));
        Assert.Equal(0, run.Points("P3", Survey));
        Assert.Contains(run.Wares["P3"].Wares, w => w.ItemId == Billet);
        Assert.Null(run.BilletBoughtAtP3);

        // P4: Sel is told of the heart; her notes open.
        Assert.Equal((100, "accepted"), (run.Points("P4", Survey), run.Tier("P4", Survey)));
        Assert.Contains("notes", run.Replies["P4 Sel again"]);

        // P5: Sel is told of the armour: the same act that raised the Waystation lowers the Survey, and the notes close.
        Assert.Equal((100, "accepted"), (run.Points("P5", Waystation), run.Tier("P5", Waystation)));
        Assert.Equal((0, "neutral"), (run.Points("P5", Survey), run.Tier("P5", Survey)));
        Assert.DoesNotContain("notes", run.Replies["P5 Sel again"]);
        var byArmour = run.Changes.Where(c => c.ActSeq == 1).Select(c => (c.FactionId, c.To - c.From, c.Via)).ToList();
        Assert.Equal(new[] { (Waystation, 100, (string?)Kera), (Survey, -100, (string?)Sel) }, byArmour);
        Assert.All(run.Learned, l => Assert.Equal((KnowledgeSources.Reported, Identities.Identified, false), (l.Source, l.Identity, l.Upgraded)));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static readonly (double X, double Z) AtKera = Spot;
    private static readonly (double X, double Z) AtSel = (71, 123);

    private static ActRecord ArmourAct(long seq = 1) =>
        new(seq, ActKinds.CreatureKilled, Armour, CellKey.OfWorld(120, 63).ToString(), 120_000, 63_000, 10);

    private static ActRecord HeartAct(long seq = 2) =>
        new(seq, ActKinds.SwitchSet, Heart, CellKey.OfWorld(153, 50.2).ToString(), 153_000, 50_200, 20);

    private static FactionLedger Ledger(params ActRecord[] acts) =>
        new(acts.Length + 1, acts.ToImmutableArray(), ImmutableArray<FactionKnowledge>.Empty, ImmutableArray<FactionStanding>.Empty);

    /// <summary>A shipped-content arena at a place, the character holding the given ledger (and, optionally, coin and a heard line).</summary>
    private static Arena At(GameSession session, (double X, double Z) place, FactionLedger ledger, long coin = 0,
        IEnumerable<InventoryEntry>? carried = null, IEnumerable<ConversationMemory>? heard = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), r =>
            new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(carried ?? Array.Empty<InventoryEntry>()),
                r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin, r.Effects, r.Relationships, heard ?? r.Conversations, r.Quests, r.Companions)
            {
                Posture = r.Posture,
                Factions = ledger,
            });

    private static string? Choose(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    /// <summary>Talk to Kera until her <c>again</c> line: her first line is <c>greet</c>, so leave it once.</summary>
    private static string[] KeraAgain(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        if (arena.Simulation.Conversation!.NodeId == "greet")
        {
            Assert.Null(Choose(arena, "leave"));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        }
        Assert.Equal("again", arena.Simulation.Conversation!.NodeId);
        return Offered(arena);
    }

    private static int Standing(Arena arena, string faction) => arena.Simulation.Factions.Single(f => f.Id == faction).Points;

    private static string TierOf(Arena arena, string faction) => arena.Simulation.Factions.Single(f => f.Id == faction).Tier;

    private static int Regard(Arena arena, string npc, string dimension) =>
        arena.Simulation.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == npc && r.Dimension == dimension)?.Value ?? 0;

    /// <summary>Save through the store and resume under the arena's own rules (its spawn list replaces the shipped one).</summary>
    private static Arena SaveAndResume(GameSession session, Arena arena, TempProfile profile, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        return Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry())));
    }

    private static CreatureView WolfAt(Arena arena, int index) => arena.Simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).ElementAt(index);

    private static readonly (double X, double Z) WolfNorth = (60.3, 142.3);
    private static readonly (double X, double Z) WolfWest = (57.8, 142.8);

    // ── no psychic factions ────────────────────────────────────────────────────

    [Fact]
    public void AFactionThatNeitherSawNorWasTold_DoesNotUpdate()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // N1 and P3 on shipped content: two acts, no knowledge until a report; the Survey has not heard what Kera was told.
        var run = Walk(session);
        Assert.Equal(2, run.At["P2"].Acts.Length);
        Assert.All(run.At["P2"].Acts, a => Assert.Empty(a.Known));
        Assert.Contains("armour", run.Replies["P3 Kera again"]);
        Assert.Contains("armour", run.Replies["P4 Sel again"]);
        Assert.Equal(0, run.Points("P3", Survey));

        // F2: a wolf killed and nobody told.
        var told = ReputationFixture.Fixture(session);
        var arena = Open(session, told, new[] { WolfNorth });
        Kill(arena, WolfAt(arena, 0));
        Assert.Equal(new[] { (1L, ActKinds.CreatureKilled, Wolf) }, arena.Simulation.Acts.Select(a => (a.Seq, a.Kind, a.Subject)));
        Assert.Empty(arena.Simulation.Acts[0].Known);
        Assert.Equal((0, 0), (Standing(arena, Keepers), Standing(arena, Delvers)));

        // F12: a faction that reacts but has no member is never told.
        var watched = ReputationFixture.Fixture(session, watchers: true);
        var twelve = Open(session, watched, new[] { WolfNorth });
        Kill(twelve, WolfAt(twelve, 0));
        Tell(twelve, Kera, "wolf");
        Tell(twelve, Sel, "wolf");
        Assert.Equal((100, -100, 0, "neutral"), (Standing(twelve, Keepers), Standing(twelve, Delvers), Standing(twelve, Watchers), TierOf(twelve, Watchers)));
        Assert.DoesNotContain(twelve.Simulation.Acts.Single().Known, k => k.Knower == Watchers);
        Assert.Empty(twelve.Simulation.Factions.Single(f => f.Id == Watchers).Members);
    }

    // ── the identity seam, repeats and distinct acts ────────────────────────────

    [Fact]
    public void AnUnknownActor_MovesNoStanding_UntilIdentified()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = ReputationFixture.Fixture(session);
        var wolf = new ActRecord(1, ActKinds.CreatureKilled, Wolf, CellKey.OfWorld(Spot.X, Spot.Z).ToString(), 60_300, 140_300, 5);
        var unknown = new FactionLedger(2, ImmutableArray.Create(wolf),
            ImmutableArray.Create(new FactionKnowledge(Delvers, 1, Identities.Unidentified, KnowledgeSources.Witnessed, Sel, 6, 0)),
            ImmutableArray<FactionStanding>.Empty);
        var crafted = Open(session, setup, Array.Empty<(double, double)>(), r => r with { Factions = unknown });
        Assert.Equal((0, "neutral"), (Standing(crafted, Delvers), TierOf(crafted, Delvers)));

        // Saved through the store and resumed: the unidentified row survives, and still moves nothing.
        var arena = SaveAndResume(session, crafted, profile, "unknown");
        Assert.Equal(unknown, arena.Simulation.CaptureRecord().Factions);
        var learned = arena.Record<FactionLearned>();
        var changed = arena.Record<ReputationChanged>();

        Tell(arena, Sel, "wolf");
        var upgrade = Assert.Single(learned);
        Assert.True(upgrade.Upgraded);
        Assert.Equal((Delvers, KnowledgeSources.Reported, (string?)Sel, Identities.Identified), (upgrade.FactionId, upgrade.Source, upgrade.Via, upgrade.Identity));
        var change = Assert.Single(changed);
        Assert.Equal((0, -100, "neutral", "wary"), (change.From, change.To, change.TierFrom, change.TierTo));

        Tell(arena, Sel, "wolf");   // and only once
        Assert.Single(learned);
        Assert.Equal(-100, Standing(arena, Delvers));
    }

    [Fact]
    public void RepeatedReports_ApplyOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, ReputationFixture.Fixture(session), new[] { WolfNorth });
        Kill(arena, WolfAt(arena, 0));
        Tell(arena, Kera, "wolf");
        Tell(arena, Sel, "wolf");
        var ledger = arena.Simulation.CaptureRecord().Factions;
        Assert.Equal((100, "accepted", -100, "wary"), (Standing(arena, Keepers), TierOf(arena, Keepers), Standing(arena, Delvers), TierOf(arena, Delvers)));

        var learned = arena.Record<FactionLearned>();
        var changed = arena.Record<ReputationChanged>();
        Tell(arena, Kera, "wolf");
        Tell(arena, Sel, "wolf");
        Assert.Empty(learned);
        Assert.Empty(changed);
        Assert.Equal(ledger, arena.Simulation.CaptureRecord().Factions);
    }

    [Fact]
    public void DistinctActs_EachApply()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, ReputationFixture.Fixture(session), new[] { WolfNorth, WolfWest });
        Kill(arena, WolfAt(arena, 0));
        Kill(arena, WolfAt(arena, 1));
        var learned = arena.Record<FactionLearned>();

        Tell(arena, Kera, "wolf");
        Assert.Equal(new long[] { 1, 2 }, learned.Select(l => l.ActSeq));
        Assert.Equal((200, "accepted"), (Standing(arena, Keepers), TierOf(arena, Keepers)));
    }

    [Fact]
    public void FactionRelations_NeverMoveStanding()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = ReputationFixture.Fixture(session, keepersToDelvers: "opposed", delversToKeepers: "close");
        var arena = Open(session, setup, new[] { WolfNorth });
        Kill(arena, WolfAt(arena, 0));
        Tell(arena, Kera, "wolf");

        Assert.Equal((100, "accepted", 0, "neutral"), (Standing(arena, Keepers), TierOf(arena, Keepers), Standing(arena, Delvers), TierOf(arena, Delvers)));
        Assert.Equal("opposed", arena.Simulation.Factions.Single(f => f.Id == Keepers).Relations.Single().Attitude);
        Assert.Equal("close", arena.Simulation.Factions.Single(f => f.Id == Delvers).Relations.Single().Attitude);
    }

    // ── continuity: save and load, and replay ──────────────────────────────────

    [Fact]
    public void FactionState_ContinuesAcrossASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // F10: saved between the act and the report; told after the resume; equal to a twin that never saved.
        var setup = ReputationFixture.Fixture(session);
        var twin = Open(session, setup, new[] { WolfNorth });
        var saved = Open(session, setup, new[] { WolfNorth }, _ => twin.Simulation.CaptureRecord());
        Kill(twin, WolfAt(twin, 0));
        Kill(saved, WolfAt(saved, 0));
        var resumed = SaveAndResume(session, saved, profile, "f10");
        Tell(twin, Sel, "wolf");
        Tell(resumed, Sel, "wolf");
        Assert.Equal((-100, "wary"), (Standing(resumed, Delvers), TierOf(resumed, Delvers)));
        Assert.Empty(StateDump.Compare(StateDump.Render(twin.Simulation, replayable: true), StateDump.Render(resumed.Simulation, replayable: true), out _));

        // And after the whole P script on shipped content.
        var run = Walk(session);
        var after = SaveAndResume(session, run.Arena, profile, "p5");
        Assert.Empty(StateDump.Compare(StateDump.Render(run.Arena.Simulation), StateDump.Render(after.Simulation), out int leaves));
        Assert.True(leaves > 400, $"only {leaves} fields were compared");
    }

    [Fact]
    public void SaveThenContinue_EqualsContinue_WithFactions()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var run = Walk(session, through: 3);
        var resumed = run.On(SaveAndResume(session, run.Arena, profile, "p3"));
        Assert.Equal(run.Arena.Simulation.StateDigest(), resumed.Arena.Simulation.StateDigest());

        foreach (int step in new[] { 4, 5 })
        {
            Step(run, step);
            Step(resumed, step);
            Assert.Equal(run.Arena.Simulation.StateDigest(), resumed.Arena.Simulation.StateDigest());
        }
        Assert.Equal(run.Arena.Simulation.CaptureRecord().Factions, resumed.Arena.Simulation.CaptureRecord().Factions);
    }

    [Fact]
    public void FactionActs_ReplayFromTheCommandLog_EndIdentical()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var live = Walk(session);
        var replay = Begin(session, live.Start);
        var log = live.Arena.Simulation.CommandLog.GroupBy(c => c.Tick).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Sequence).ToList());
        while (replay.Arena.Simulation.WorldTick < live.Arena.Simulation.WorldTick)
        {
            foreach (var command in log.GetValueOrDefault(replay.Arena.Simulation.WorldTick) ?? new List<LoggedCommand>())
                replay.Arena.Simulation.Enqueue(command.Command);
            replay.Arena.Tick();
        }
        foreach (var command in log.GetValueOrDefault(replay.Arena.Simulation.WorldTick) ?? new List<LoggedCommand>())   // said after the last tick
            replay.Arena.Simulation.Enqueue(command.Command);
        replay.Arena.Simulation.DrainCommands();

        var liveLog = live.Arena.Simulation.CommandLog.Select(e => (e.Tick, e.Command.GetType().Name, e.RejectedReason)).ToList();
        var replayLog = replay.Arena.Simulation.CommandLog.Select(e => (e.Tick, e.Command.GetType().Name, e.RejectedReason)).ToList();
        int first = Enumerable.Range(0, Math.Min(liveLog.Count, replayLog.Count)).FirstOrDefault(i => liveLog[i] != replayLog[i], -1);
        Assert.True(first < 0 && liveLog.Count == replayLog.Count,
            first < 0 ? $"{liveLog.Count} logged live, {replayLog.Count} replayed" : $"command {first}: live {liveLog[first]}, replay {replayLog[first]}");

        // The purchase mints item IDs afresh (G8), so the replayable dump is compared, with the ledger by value and the faction events in order.
        Assert.Empty(StateDump.Compare(StateDump.Render(live.Arena.Simulation, replayable: true), StateDump.Render(replay.Arena.Simulation, replayable: true), out _));
        Assert.Equal(live.Arena.Simulation.CaptureRecord().Factions, replay.Arena.Simulation.CaptureRecord().Factions);
        Assert.Equal(live.Acts, replay.Acts);
        Assert.Equal(live.Learned, replay.Learned);
        Assert.Equal(live.Changes, replay.Changes);
    }

    [Fact]
    public void TwoFreshRuns_ProduceTheSameReplayableDump()
    {
        using var profile = new TempProfile();
        var one = Walk(Harness.Boot(profile));
        var two = Walk(Harness.Boot(profile));
        Assert.Equal(StateDump.Render(one.Arena.Simulation, replayable: true), StateDump.Render(two.Arena.Simulation, replayable: true));
        Assert.Equal(one.Arena.Simulation.CaptureRecord().Factions, two.Arena.Simulation.CaptureRecord().Factions);
    }

    // ── layers kept apart ──────────────────────────────────────────────────────

    [Fact]
    public void PersonalRelationships_StaySeparateFromStanding()
    {
        using var profile = new TempProfile();
        var run = Walk(Harness.Boot(profile));
        int selTrust = run.Regard.Where(r => r.NpcId == Sel && r.Dimension == "trust").Sum(r => r.To - r.From);
        Assert.Equal(selTrust, Regard(run.Arena, Sel, "trust"));                        // only the authored writes that fired
        Assert.DoesNotContain(run.Regard, r => r.NpcId == Kera);                         // P3 moved the Waystation, not Kera
        Assert.Equal((0, 0), (Regard(run.Arena, Kera, "respect"), Regard(run.Arena, Kera, "trust")));
        Assert.Equal(0, run.Points("P5", Survey));                                        // Sel's trust is not the Survey's standing

        string factions = File.ReadAllText(Path.Combine(Harness.RepoRoot(), "src", "World", "Runtime", "Factions.cs"));
        Assert.DoesNotContain("ChangeRelationship", factions);
        string social = File.ReadAllText(Path.Combine(Harness.RepoRoot(), "src", "World", "Runtime", "Social.cs"));
        int start = social.IndexOf("class RelationshipSystem", StringComparison.Ordinal);
        Assert.True(start >= 0, "RelationshipSystem is not in Social.cs");
        int next = social.IndexOf("\nclass ", start + 1, StringComparison.Ordinal) is var n && n > 0 ? n : social.IndexOf("internal sealed class", start + 30, StringComparison.Ordinal);
        string relationships = social[start..(next > start ? next : social.Length)];
        Assert.DoesNotContain("RecordAct", relationships);
        Assert.DoesNotContain("ReportAct", relationships);
    }

    [Fact]
    public void ACompanionsKill_IsNotThePlayersAct()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup with
        {
            Combat = session.Setup.Combat with { Creatures = session.Setup.Combat.Creatures.SetItem(Armour, session.Setup.Combat.Creatures[Armour] with { MaxHealth = 1 }) },
        };
        // In front of the armour, which faces about 308 degrees at this placement, so it sees the character and comes; Tavar meets it.
        var tavar = new CompanionRecord(Tavar, Domain.Companions.CompanionOrder.Follow, Domain.Companions.CompanionCondition.Up, 116_200, 64_400, 0, 100);
        var arena = Arena.OpenCreatures(session, setup, (116.8, 65.4), 0, new[] { (Armour, 120.0, 63.0, "pack_hunter") }, r => r.WithCompanions(new[] { tavar }));
        var killed = arena.Record<CreatureKilled>();
        var acts = arena.Record<ActRecorded>();
        var hits = arena.Record<HitResolved>();
        for (int i = 0; i < 1_200 && killed.Count == 0; i++)
            arena.Tick();

        Assert.True(killed.Count == 1, $"no kill: {hits.Count} hits ({string.Join(", ", hits.Take(4).Select(h => $"{h.Attacker}->{h.Target} {h.Damage}"))}); " +
            $"armour {arena.Simulation.Creatures.Single()}; companion {arena.Simulation.Companions.Single()}");
        var kill = killed[0];
        Assert.NotEqual(arena.Player, kill.Killer);
        Assert.Empty(acts);
        Assert.Empty(arena.Simulation.Acts);   // so Kera's armour reply, which needs the act, is never offered
    }

    // ── the gates ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheBilletGate_HidesTheWare_AndRefusesARawBuyCommand_UntilAccepted()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ingot = Arena.Stack(Billet, 1);
        var arena = At(session, AtKera, Ledger(ArmourAct()), coin: 100, carried: new[] { ingot });

        Assert.DoesNotContain(arena.Simulation.Wares(Kera)!.Wares, w => w.ItemId == Billet);
        Assert.Equal("Kera Voss will not sell you that", arena.Submit(new BuyCommand(arena.Player, Kera, UntouchedBilletRef(session), 1)));

        // Selling is not gated; the ingot sold joins her wares and is withheld with the rest, keyed by item.
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, ingot.ItemId, 1)));
        Assert.DoesNotContain(arena.Simulation.Wares(Kera)!.Wares, w => w.ItemId == Billet);
        var withheld = arena.Simulation.World.Container(KeraWares)!.Items.Where(i => i.DefId == Billet).ToList();
        Assert.Equal(4, withheld.Sum(i => i.Count));
        Assert.Equal("Kera Voss will not sell you that", arena.Submit(new BuyCommand(arena.Player, Kera, withheld[0].ItemId.Value, 1)));

        // Told of the armour: accepted, and the billets are listed and sold.
        Assert.Contains("armour", KeraAgain(arena));
        Assert.Null(Choose(arena, "armour"));
        Assert.Null(Choose(arena, "back"));
        arena.Submit(new LeaveCommand(arena.Player));
        Assert.Equal("accepted", TierOf(arena, Waystation));
        var billet = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Billet);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, billet.Ref, 1)));
    }

    [Fact]
    public void TheReputationCondition_OpensAndClosesSelsNotes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var heardTavar = new[] { new ConversationMemory("dialogue.ashen_hollow.tavar_orr", ImmutableArray.Create("greet")) };
        var arena = At(session, AtSel, Ledger(ArmourAct(), HeartAct()), heard: heardTavar);

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.DoesNotContain("notes", Offered(arena));
        Assert.Null(Choose(arena, "tavar_back"));
        Assert.Null(Choose(arena, "back"));
        Assert.Contains("notes", Offered(arena));                // the Survey learned of the heart: accepted
        Assert.Equal("accepted", TierOf(arena, Survey));

        Assert.Null(Choose(arena, "armour"));
        Assert.Null(Choose(arena, "back"));
        Assert.DoesNotContain("notes", Offered(arena));          // and of the armour: neutral again
        Assert.Equal("neutral", TierOf(arena, Survey));
        Assert.NotNull(Choose(arena, "notes"));                  // and a raw choice of it is refused
    }

    [Fact]
    public void ActDone_OffersTheReportOnlyAfterTheAct()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var none = At(session, AtKera, FactionLedger.Empty);
        Assert.DoesNotContain("armour", KeraAgain(none));

        var done = At(session, AtKera, Ledger(ArmourAct()));
        Assert.Contains("armour", KeraAgain(done));
        Assert.Null(Choose(done, "armour"));
        Assert.Null(Choose(done, "back"));
        done.Submit(new LeaveCommand(done.Player));
        Assert.DoesNotContain("armour", KeraAgain(done));        // told once
    }

    // ── no currency crossing ───────────────────────────────────────────────────

    private const string Boar = "creature.beast.bristleback_boar";
    private static readonly (double X, double Z) AtTheStone = (148.5, 78);

    /// <summary>One cell of the 2x2: a kill no faction reacts to, a reported switch act, both or neither.</summary>
    private static (long Xp, int Level, long SkillXp, int Standing) Cell(GameSession session, bool kill, bool report)
    {
        var setup = ReputationFixture.Fixture(session, selAt: (147.3, 78.9, 0));
        setup = setup with { Combat = setup.Combat with { Creatures = setup.Combat.Creatures.SetItem(Boar, setup.Combat.Creatures[Boar] with { MaxHealth = 1 }) } };
        var arena = Arena.OpenCreatures(session, setup, AtTheStone, 0, kill ? new[] { (Boar, 148.5, 79.8, "sleeper") } : Array.Empty<(string, double, double, string)>());
        if (kill)
        {
            arena.Fight(arena.Simulation.Creatures.Single(), 600);
            Assert.False(arena.Simulation.Creatures.Single().Alive);
            Assert.True(arena.WalkTo(AtTheStone.X, AtTheStone.Z));
        }
        if (report)
        {
            Assert.Null(arena.Submit(new InteractCommand(arena.Player, "switch.stone_north")));
            Tell(arena, Sel, "stone");
        }
        var progression = arena.Simulation.Player.Progression;
        return (progression.LifetimeXp.Values.Sum(), progression.Level, progression.Skills.Values.Sum(s => s.ProgressXp), Standing(arena, Delvers));
    }

    [Fact]
    public void AxisIndependence_ReputationByLevel_2x2()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var neither = Cell(session, kill: false, report: false);
        var killed = Cell(session, kill: true, report: false);
        var reported = Cell(session, kill: false, report: true);
        var both = Cell(session, kill: true, report: true);

        Assert.True(killed.Xp > neither.Xp, "the kill earns experience");
        Assert.Equal((100, neither.Xp, neither.Level), (reported.Standing, reported.Xp, reported.Level));   // standing at level 1, from a switch that pays nothing
        Assert.Equal(0, killed.Standing);                                                                    // experience at neutral
        Assert.Equal((killed.Xp, killed.Level, reported.Standing), (both.Xp, both.Level, both.Standing));     // each axis moves by its own currency only
    }

    [Fact]
    public void AxisIndependence_ReputationBySkill_2x2()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var neither = Cell(session, kill: false, report: false);
        var practised = Cell(session, kill: true, report: false);
        var reported = Cell(session, kill: false, report: true);
        var both = Cell(session, kill: true, report: true);

        Assert.True(practised.SkillXp > neither.SkillXp, "the blows practise the weapon skill");
        Assert.Equal((100, neither.SkillXp), (reported.Standing, reported.SkillXp));
        Assert.Equal(0, practised.Standing);
        Assert.Equal((practised.SkillXp, reported.Standing), (both.SkillXp, both.Standing));
    }
}
