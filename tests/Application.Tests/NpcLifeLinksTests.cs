using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP12b: NPC life's ties to the rest of the world over the game's own content - town roles and derived hireability, the hired life state
/// and walking home, the trip planner behind looking-for-company, residents as chatter speakers, tiers around a set of observers, and the
/// crowd at the cookfire making way.
/// </summary>
public class NpcLifeLinksTests
{
    private const long Day = 57_600;
    private const long Hour = Day / 24;
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string Porter = "routine.ashen_hollow.porter";

    private readonly ITestOutputHelper _output;

    public NpcLifeLinksTests(ITestOutputHelper output) => _output = output;

    /// <summary>A world with the character at the region's spawn (07:30), purse full, in absolute metres.</summary>
    private static Arena Open(GameSession session, SimulationSetup? rules = null, long coin = 200, ulong seed = 42, (double X, double Z)? at = null) =>
        Arena.OpenCreatures(session, rules ?? session.Setup, at ?? (session.Setup.Layout.Spawn.XMm / 1000.0, session.Setup.Layout.Spawn.ZMm / 1000.0), 0,
            Array.Empty<(string, double, double, string)>(), r => WithCoin(r, coin), seed: seed, keepSpawns: true, origin: (0, 0));

    private static PlayerRecord WithCoin(PlayerRecord r, long coin) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin);

    /// <summary>The character put down beside someone, within a hand's reach, on ground they can stand on.</summary>
    private static void Beside(Arena arena, string npcId)
    {
        var body = arena.Simulation.Npcs.Single(n => n.Id == npcId).Body;
        var terrain = arena.Simulation.Setup.Layout.Space.Terrain;
        foreach (var (dx, dz) in new[] { (1_000L, 0L), (-1_000L, 0L), (0L, 1_000L), (0L, -1_000L), (700L, 700L), (-700L, -700L) })
        {
            long x = body.XMm + dx, z = body.ZMm + dz;
            Assert.Null(arena.Simulation.Submit(new Relocate(new Body(x, terrain.HeightAtMm(x, z), z, CombatRules.FacingTowards(x, z, body.XMm, body.ZMm)))));
            if (arena.Simulation.HireTerms(npcId, HirelingRole.Porter, 2).Refusal?.Contains("out of reach") != true)
                return;
        }
        Assert.Fail($"nowhere beside {npcId} within reach");
    }

    private static NpcLifeRecord LifeOf(Simulation simulation, string key) => simulation.World.NpcLife!.Npcs[key];

    // ── roles and hireability ───────────────────────────────────────────────

    [Fact]
    public void Hireability_IsDerivedFromTownRoles_VendorsAndQuestGiversNever_MostResidentsYes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = Open(session).Simulation;

        foreach (string npc in new[] { Kera, Renn, "npc.ashen_hollow.sel_arien", "npc.cairnwold.durn_skell", "npc.cairnwold.mara_ling", "npc.cairnwold.tam_crowden" })
        {
            var view = simulation.Hiring.Hireability(npc)!;
            Assert.False(view.Hireable, npc);
            Assert.True(view.Roles.Contains(NpcRoleKeys.Vendor) || view.Roles.Contains(NpcRoleKeys.QuestGiver), npc);
            Assert.Empty(view.HiresAs);
        }
        Assert.False(simulation.IsHireable(Tavar));    // a companion, who joins rather than hires on
        Assert.False(simulation.IsHireable(Berrin));   // a steward, whose role does not hire out
        Assert.Contains("is needed in town", simulation.HireTerms(Kera, HirelingRole.Guard, 3).Refusal);

        var residents = simulation.Lives.Where(l => l.Resident).ToList();
        Assert.Equal(12, residents.Count);
        // Every resident's roles hire out; each is hireable unless a trip has them waiting to leave or gone.
        Assert.All(residents, r => Assert.NotEmpty(simulation.Hiring.Hireability(r.Key)!.HiresAs));
        Assert.All(residents.Where(r => r.Activity is not (NpcActivities.SetOut or NpcActivities.Away)), r => Assert.True(simulation.IsHireable(r.Key), r.Key));
        Assert.True(residents.Count(r => simulation.IsHireable(r.Key)) >= 8);
        Assert.Contains(HirelingRole.Porter, simulation.Hiring.Hireability(Porter)!.HiresAs);
    }

    [Fact]
    public void HiringAResident_HandsThemToTheParty_SuspendsTheirLife_AndDismissal_WalksThemHome()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session);
        var simulation = arena.Simulation;
        var changes = arena.Record<HireabilityChanged>();
        var returned = arena.Record<NpcReturnedToLife>();
        Beside(arena, Porter);

        Assert.Null(arena.Submit(new HireCommand(arena.Player, Porter, HirelingRole.Porter, 3, AcceptsRisk: true)));
        Assert.Equal(NpcCondition.Hired, LifeOf(simulation, Porter).Condition);
        Assert.Equal(new HireabilityChanged(Porter, false, "is already in someone's hire", simulation.WorldTick), Assert.Single(changes));
        Assert.False(simulation.IsHireable(Porter));
        var needs = LifeOf(simulation, Porter).Needs;

        // Two game hours with the party: their routine waits - no needs rise, no choices - and the party moves them.
        arena.Tick((int)(2 * Hour));
        var held = LifeOf(simulation, Porter);
        Assert.Equal((NpcCondition.Hired, needs, NpcActivities.Idle), (held.Condition, held.Needs, held.Activity));
        Assert.Contains(simulation.Companions, c => c.NpcId == Porter);

        // Saved and loaded in the party's hands: still hired, still with the character.
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("hired"), SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), session.Content, simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("hired"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        Assert.Empty(loaded.RejectedRecords);
        var resumed = Simulation.Start(simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        Assert.Equal(simulation.StateDigest(), resumed.StateDigest());
        Assert.Equal(NpcCondition.Hired, LifeOf(resumed, Porter).Condition);

        // Dismissed: no teleport - they stand where the party left them, back in their life, and walk to it.
        var at = simulation.Npcs.Single(n => n.Id == Porter).Body;
        Assert.Null(arena.Submit(new DismissCommand(arena.Player, Porter)));
        var free = LifeOf(simulation, Porter);
        Assert.Equal(NpcCondition.Living, free.Condition);
        Assert.Equal((at.XMm, at.ZMm), (simulation.Npcs.Single(n => n.Id == Porter).Body.XMm, simulation.Npcs.Single(n => n.Id == Porter).Body.ZMm));
        Assert.Equal(Porter, Assert.Single(returned).Key);
        Assert.Equal(new HireabilityChanged(Porter, true, null, simulation.WorldTick), changes.Last());
        var last = at;
        long walked = 0;
        for (int i = 0; i < 600; i++)
        {
            arena.Tick();
            var now = simulation.Npcs.Single(n => n.Id == Porter).Body;
            long dx = now.XMm - last.XMm, dz = now.ZMm - last.ZMm;
            Assert.True(dx * dx + dz * dz <= 500L * 500, $"jumped {Math.Sqrt(dx * dx + dz * dz):0} mm");
            walked += (long)Math.Sqrt(dx * dx + dz * dz);
            last = now;
        }
        Assert.True(walked > 1_000 || simulation.Npcs.Single(n => n.Id == Porter).Moving || LifeOf(simulation, Porter).Route.Status == NavRouteStatus.None,
            "they walk back to their life");
    }

    [Fact]
    public void AnInjuredResident_CannotBeHired_UntilWell()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session);
        var changes = arena.Record<HireabilityChanged>();
        Assert.True(arena.Simulation.IsHireable(Porter));
        Assert.Null(arena.Simulation.Submit(new InjureNpc(Porter, arena.Simulation.WorldTick + 3 * Hour)));
        Assert.False(arena.Simulation.IsHireable(Porter));
        Assert.Equal((Porter, false), (Assert.Single(changes).Key, changes[0].Hireable));
        Beside(arena, Porter);
        Assert.Contains("injured", arena.Simulation.HireTerms(Porter, HirelingRole.Porter, 2).Refusal);
        arena.Simulation.Advance(4 * Hour);
        arena.Tick();
        Assert.True(arena.Simulation.IsHireable(Porter) || LifeOf(arena.Simulation, Porter).Activity is NpcActivities.SetOut or NpcActivities.Away);
    }

    [Fact]
    public void AResidentHirelingWhoDies_LeavesTheirSlotToAnArrival()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session);
        Beside(arena, Porter);
        var first = arena.Simulation.Npcs.Single(n => n.Id == Porter).InstanceId;
        Assert.Null(arena.Submit(new HireCommand(arena.Player, Porter, HirelingRole.Porter, 3, AcceptsRisk: true)));
        Assert.Null(arena.Simulation.Submit(new RemoveNpc(Porter)));
        var vacancy = arena.Simulation.World.NpcLife!.Vacancies[Porter];
        Assert.InRange(vacancy.ArrivalDay - vacancy.LeftDay, 3, 7);
        // Nobody arrives in sight (spawn suppression): the character goes off to Cairnwold first.
        var far = arena.Simulation.Setup.Layout.Npcs.Single(n => n.NpcId == Berrin);
        Assert.Null(arena.Simulation.Submit(new Relocate(new Body(far.XMm + 2_000, arena.Simulation.Setup.Layout.Space.Terrain.HeightAtMm(far.XMm + 2_000, far.ZMm), far.ZMm, 0))));
        arena.Tick(10);
        arena.Simulation.Advance((vacancy.ArrivalDay + 1) * Day - arena.Simulation.WorldTick);
        var next = arena.Simulation.Npcs.SingleOrDefault(n => n.Id == Porter);
        Assert.NotNull(next);
        Assert.NotEqual(first, next!.InstanceId);
        Assert.Equal(NpcCondition.Living, LifeOf(arena.Simulation, Porter).Condition);
    }

    // ── trips and looking for company ───────────────────────────────────────

    [Fact]
    public void TheTripPlanner_PostsEachSettingOut_AtLeastTwentyMinutesAhead_AndTheSameWorldPlansTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var a = Open(session).Simulation;
        var b = Open(session).Simulation;
        var other = Open(session, seed: 43).Simulation;
        var week = a.Trips.Between(a.WorldTick, a.WorldTick + 7 * Day).ToList();
        Assert.True(week.Count >= 5, $"{week.Count} trips in a week");
        long twentyMinutes = 20 * Day / (24 * 60);
        Assert.All(week, s => Assert.True(s.DepartTick - s.Tick >= twentyMinutes));
        Assert.All(week, s => Assert.Contains(s.Purpose, LfgPurposes.All));
        Assert.Equal(week.OrderBy(s => s.Tick).ThenBy(s => s.Npc), week);
        var names = a.Npcs.Where(n => n.Resident).ToDictionary(n => n.InstanceId, n => n.Name);
        Assert.All(week, s => Assert.Equal(names[s.Npc], s.NpcName));
        Assert.Equal(week, b.Trips.Between(b.WorldTick, b.WorldTick + 7 * Day));
        Assert.NotEqual(week, other.Trips.Between(other.WorldTick, other.WorldTick + 7 * Day));
        // Any split of the span gives the same offers.
        long mid = a.WorldTick + 3 * Day + 1_111;
        Assert.Equal(week, a.Trips.Between(a.WorldTick, mid).Concat(a.Trips.Between(mid, a.WorldTick + 7 * Day)));
    }

    [Fact]
    public void ASettingOut_IsPostedToWorldChat_TheyWaitAtTheMeetingPlace_ThenLeave()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session);
        var simulation = arena.Simulation;
        var raised = arena.Record<SettingOut>();
        var first = simulation.Trips.Between(simulation.WorldTick + Hour, simulation.WorldTick + 7 * Day).First();
        string key = simulation.Npcs.Single(n => n.InstanceId == first.Npc).Id;
        var trip = simulation.Setup.NpcLife.Routines[key].Trip!;

        simulation.Advance(first.Tick - 2 - simulation.WorldTick);
        arena.Tick(4);
        Assert.Equal(first, Assert.Single(raised));
        Assert.Contains(simulation.GroupOffers, o => o.Npc == first.Npc && o.DepartTick == first.DepartTick);
        Assert.Contains(simulation.Chat, m => m.Channel == ChatChannel.World && m.Speaker == first.Npc && m.LineKey.StartsWith($"lfg.{first.Purpose}.", StringComparison.Ordinal));
        Assert.Equal(NpcActivities.SetOut, LifeOf(simulation, key).Activity);
        Assert.False(simulation.IsHireable(key));

        // They wait at the meeting place until the hour turns, then they are away.
        arena.Tick((int)(first.DepartTick - simulation.WorldTick));
        Assert.Equal(NpcActivities.Away, LifeOf(simulation, key).Activity);
        Assert.DoesNotContain(simulation.GroupOffers, o => o.Npc == first.Npc);
        _output.WriteLine($"{first.NpcName} set out for {first.Destination} ({first.PurposeName}) from {first.AnchorName}; meet ({trip.Meet.XMm}, {trip.Meet.ZMm})");
    }

    [Fact]
    public void LivesTripsAndOffers_GoOnAlike_HoweverAnAbsenceIsSplit()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var start = Open(session).Simulation;
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("trips"), SaveDocuments.Capture(start.World, start.CaptureRecord(), session.Content, start.WorldTick, 0));
        Simulation Fresh()
        {
            var loaded = store.Load(SaveSlots.Manual("trips"), new LoadContext(session.Generator, session.Content, new Registry()));
            return Simulation.Start(start.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        }
        string Offers(Simulation s) => string.Join(";", s.GroupOffers.Select(o => $"{o.Npc}@{o.Purpose}@{o.DepartTick}@{o.PostedTick}"));
        string Seen(Simulation s) => s.StateDigest() + "|" + string.Join(";", s.Lives.Select(l => $"{l.Key}:{l.Activity}:{l.Condition}")) + "|" + Offers(s);
        var once = Fresh();
        once.Advance(4 * Day + 777);
        foreach (var pieces in new[] { new long[] { Hour + 3, 4 * Day + 777 - Hour - 3 }, new long[] { Day, Day, Day, Day, 777 } })
        {
            var chunked = Fresh();
            foreach (long piece in pieces)
                chunked.Advance(piece);
            Assert.Equal(Seen(once), Seen(chunked));
        }
        // And they go on alike, posting the same offers.
        var again = Fresh();
        again.Advance(4 * Day + 777);
        for (int i = 0; i < 2 * (int)Hour; i++)
        {
            once.Step();
            again.Step();
        }
        Assert.Equal(Seen(once), Seen(again));
    }

    // ── chatter speakers ────────────────────────────────────────────────────

    [Fact]
    public void TheRoleResidents_AreTheChatterSpeakers_AndTheNamedNeverSpeakACorpusLine()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var corpus = Enumerable.Range(1, 30).Select(i => new ChatLine($"chat.t0001.{i:0000}", $"Line {i} in {{place}}.", i % 2 == 0 ? ChatChannel.World : ChatChannel.Local)
        {
            AiGenerated = true,
            Batch = "t0001",
        }).ToImmutableArray();
        var setup = session.Setup with
        {
            Chat = session.Setup.Chat with { Corpus = corpus, Rules = session.Setup.Chat.Rules with { LocalChancePercent = 100, WorldChancePercent = 100, HistoryMessages = 100_000 } },
        };
        var arena = Open(session, setup);
        arena.Tick((int)(3 * Hour));
        var residents = arena.Simulation.Npcs.Where(n => n.Resident).Select(n => n.InstanceId).ToHashSet();
        var said = arena.Simulation.Chat.Where(m => m.LineKey.StartsWith("chat.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(said);
        Assert.All(said, m => Assert.Contains(m.Speaker!, residents));
        Assert.All(said, m => Assert.Contains(m.Params["place"], new[] { "Ashen Hollow", "Cairnwold", "the wilds" }));
    }

    // ── tiers around observers ──────────────────────────────────────────────

    [Fact]
    public void Tiers_AreTheNearestObserversDistance_NotTheCharactersAlone()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var berrin = session.Setup.Layout.Npcs.Single(n => n.NpcId == Berrin);
        string cell = CellKey.OfWorld(berrin.XMm / 1000.0, berrin.ZMm / 1000.0).ToString();
        var alone = Open(session).Simulation;
        Assert.True(alone.CellTiers[cell] >= SimulationTier.B);
        Assert.Single(alone.Observers);

        var other = new TierObserver("observer.test", berrin.XMm, berrin.ZMm);
        var shared = Open(session, session.Setup with { Observers = TierObservers.With(new[] { other }) }).Simulation;
        Assert.Equal(SimulationTier.A, shared.CellTiers[cell]);
        Assert.Equal(2, shared.Observers.Count);
        // The character's own cell is as it was.
        string mine = CellKey.OfWorld(shared.Player.Body.XMm / 1000.0, shared.Player.Body.ZMm / 1000.0).ToString();
        Assert.Equal(alone.CellTiers[mine], shared.CellTiers[mine]);
        // And Cairnwold's people walk their day there, as they would round the character.
        for (int i = 0; i < 200; i++)
            shared.Step();
        Assert.Equal(SimulationTier.A, shared.CellTiers[cell]);
    }

    // ── the crowd at the cookfire ───────────────────────────────────────────

    [Theory]
    [InlineData("cairnwold")]
    [InlineData("ashen_hollow")]
    public void TheEatAnchors_StandWideEnoughApart_ToWalkBetween(string town)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var eats = session.Setup.NpcLife.Routines.Values.Where(r => r.Settlement == town && r.Anchors.ContainsKey(NpcAnchorKinds.Eat))
            .Select(r => r.Anchors[NpcAnchorKinds.Eat]).ToList();
        var ring = eats.Where(a => eats.Count(b => Math.Abs(a.XMm - b.XMm) < 10_000 && Math.Abs(a.ZMm - b.ZMm) < 10_000) > 3).ToList();
        Assert.True(ring.Count >= 9, $"{ring.Count} eat at {town}'s fire");
        foreach (var a in ring)
        {
            foreach (var b in ring.Where(b => b != a))
            {
                double d = Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));
                Assert.True(d >= 1_800, $"eat anchors {d:0} mm apart at {town}");
            }
        }
    }

    /// <summary>
    /// The integrator's crowd trap: everyone sat round a town's cookfire, the character in among them. Pushing out any way the town leaves
    /// open gets them out within seconds - the NPC in the way steps aside - and the NPCs stay solid all the while.
    /// </summary>
    [Theory]
    [InlineData("cairnwold")]
    [InlineData("ashen_hollow")]
    public void AtMealtime_TheCrowdAtTheCookfire_MakesWay_ButStaysSolid(string town)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup;
        var eats = setup.NpcLife.Routines.Values.Where(r => r.Settlement == town && r.Anchors.ContainsKey(NpcAnchorKinds.Eat)).Select(r => r.Anchors[NpcAnchorKinds.Eat]).ToList();
        var ring = eats.Where(a => eats.Count(b => Math.Abs(a.XMm - b.XMm) < 10_000 && Math.Abs(a.ZMm - b.ZMm) < 10_000) > 3).ToList();
        double cx = ring.Average(a => a.XMm), cz = ring.Average(a => a.ZMm);
        // Everyone at the fire: a test rule that has them eat whenever they can, so the ring is full.
        var hungry = setup with
        {
            NpcLife = setup.NpcLife with
            {
                Routines = setup.NpcLife.Routines.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value.Settlement == town
                    ? kv.Value with { Weights = kv.Value.Weights.SetItem(NpcActivities.Eat, 5_000), Trip = null }
                    : kv.Value, StringComparer.Ordinal),
            },
        };
        // Each way out: the character pushes outward for ten seconds, first past an empty fire (a control: some ways are walled by the
        // village itself), then through the crowd. Every way open without the crowd is open with it.
        var empty = setup with
        {
            NpcLife = setup.NpcLife with
            {
                Routines = setup.NpcLife.Routines.Where(kv => kv.Value.Settlement != town).ToImmutableSortedDictionary(StringComparer.Ordinal),
            },
            Social = setup.Social with { Npcs = setup.Social.Npcs.Where(kv => !kv.Key.StartsWith($"npc.{town}.", StringComparison.Ordinal)).ToImmutableSortedDictionary(StringComparer.Ordinal) },
        };
        bool Out(SimulationSetup rules, int direction, bool crowd)
        {
            double angle = direction * Math.PI / 4;
            var arena = Open(session, rules, at: ((cx + Math.Sin(angle) * 2_000) / 1000.0, (cz + Math.Cos(angle) * 2_000) / 1000.0));
            arena.Simulation.Advance(Hour + 30 * Hour / 60);   // everyone settled where their routine has them
            arena.Tick(40);
            if (crowd)
            {
                int seated = arena.Simulation.Lives.Count(l => l.Settlement == town && l.Activity == NpcActivities.Eat);
                Assert.True(seated >= 8, $"{seated} at the fire");
                // They stay solid: what the character's prediction walks round.
                Assert.All(arena.Simulation.Npcs.Where(n => n.Id.StartsWith($"npc.{town}.", StringComparison.Ordinal) || n.Id.StartsWith($"routine.{town}.", StringComparison.Ordinal)),
                    n => Assert.Contains(arena.Simulation.DynamicBlockers, b => b.Id == n.Id));
            }
            var start = arena.Simulation.Player.Body;
            int outward = CombatRules.FacingTowards((long)cx, (long)cz, start.XMm, start.ZMm);
            var intent = new MoveIntent((int)Math.Round(Math.Sin(angle) * 1000), (int)Math.Round(Math.Cos(angle) * 1000), Gait.Walk, outward);
            for (int i = 0; i < 10 * 20; i++)
            {
                arena.Simulation.Enqueue(new MoveCommand(arena.Player, intent));
                arena.Tick();
                var body = arena.Simulation.Player.Body;
                if (Math.Sqrt(Math.Pow(body.XMm - cx, 2) + Math.Pow(body.ZMm - cz, 2)) >= 5_500)
                    return true;
            }
            var end = arena.Simulation.Player.Body;
            _output.WriteLine($"way {direction} ({(crowd ? "crowd" : "empty")}): stopped at ({end.XMm}, {end.ZMm})");
            return false;
        }
        var open = Enumerable.Range(0, 8).Where(d => Out(empty, d, crowd: false)).ToList();
        Assert.True(open.Count >= 5, $"only {open.Count} ways out past an empty fire");
        var through = open.Where(d => Out(hungry, d, crowd: true)).ToList();
        _output.WriteLine($"{open.Count} ways open; {through.Count} of them through the crowd");
        Assert.Equal(open, through);
    }
}
