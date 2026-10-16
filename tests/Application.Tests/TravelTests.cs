using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Travel;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Waystone travel in region.starting_frontier (S-38; NEXT_AREA_TRAVEL_CITY_PLAN.md D.3, accepted): the Ashen Waystone and the dormant
/// Rim Stone above Cairnwold - knowing a stone by touching it, waking the dormant one with Berrin's rite, choosing a return point as its
/// own act, the cost of a journey shown and paid in world time, and all of it kept by a save.
/// </summary>
public class TravelTests
{
    private const string Ashen = "waystone.ashen";
    private const string Rim = "waystone.rim";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string BerrinsTalk = "dialogue.cairnwold.berrin_garth";

    /// <summary>Within a hand's reach of each stone, in region metres.</summary>
    private static readonly (double X, double Z) AtAshen = (428.9, 758.0);
    private static readonly (double X, double Z) AtRim = (880.6, 702.5);
    private static readonly (double X, double Z) AtBerrin = (959.0, 644.0);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null,
        IEnumerable<(string DefId, double X, double Z, string Role)>? creatures = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, creatures ?? Array.Empty<(string, double, double, string)>(), change, origin: Harness.Region);

    private static PlayerRecord Knowing(PlayerRecord record, string? returnPoint, params string[] stones) =>
        record with
        {
            Waystones = stones.Select(s => new WaystoneRecord(s, "region.starting_frontier", WaystoneMethod.Touched, 0)).ToImmutableArray(),
            ReturnPoint = returnPoint,
        };

    private static long RimFlag(Arena arena)
    {
        var stone = arena.Simulation.Setup.Layout.FindWaystone(Rim)!;
        var (x, z) = Domain.Spatial.Footprints.Center(stone.Body);
        return arena.Simulation.World.GetFlag(CellKey.OfWorld(x / 1000.0, z / 1000.0), stone.Dormant!.FlagId);
    }

    // ── discovery: touching and waking ──────────────────────────────────────

    [Fact]
    public void TheRegion_HasTheAshenWaystone_AndTheDormantRimStone_OnTheOldEastRoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;

        Assert.Equal(new[] { Ashen, Rim }, layout.Waystones.Select(w => w.Key));
        Assert.Null(layout.FindWaystone(Ashen)!.Dormant);
        Assert.Equal(("world.road.rim_stone_woken", BerrinsTalk, "rim_stone"),
            (layout.FindWaystone(Rim)!.Dormant!.FlagId, layout.FindWaystone(Rim)!.Dormant!.RiteDialogueId, layout.FindWaystone(Rim)!.Dormant!.RiteNodeId));
        Assert.Equal(("location.outpost", "location.road.basin_rim"), (layout.FindWaystone(Ashen)!.LocationId, layout.FindWaystone(Rim)!.LocationId));
        Assert.Equal("road.old_east_road", Assert.Single(layout.Roads).Id);
        // Arriving at the Ashen Waystone is arriving where a new character starts.
        Assert.Equal((layout.Spawn.XMm, layout.Spawn.ZMm), (layout.FindWaystone(Ashen)!.ArrivalXMm, layout.FindWaystone(Ashen)!.ArrivalZMm));
    }

    [Fact]
    public void TouchingAnAwakeStone_KnowsItOnce_AndOnlyWithinReach()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var far = At(session, (AtAshen.X + 4, AtAshen.Z));
        Assert.Contains("reach", far.Submit(new TouchStoneCommand(far.Player, Ashen)));
        Assert.Empty(far.Simulation.CaptureRecord().Waystones);

        var arena = At(session, AtAshen);
        var touched = arena.Record<StoneTouched>();
        Assert.False(arena.Simulation.Waystones.Single(w => w.Site.Key == Ashen).Known);
        Assert.Null(arena.Submit(new TouchStoneCommand(arena.Player, Ashen)));
        Assert.Null(arena.Submit(new TouchStoneCommand(arena.Player, Ashen)));   // a stone already known: nothing new, no refusal

        var once = Assert.Single(touched);
        Assert.Equal((Ashen, WaystoneMethod.Touched), (once.StoneKey, once.Method));
        Assert.Equal(session.Setup.Travel.TouchText, once.Text);
        Assert.Equal(new WaystoneRecord(Ashen, "region.starting_frontier", WaystoneMethod.Touched, 0), Assert.Single(arena.Simulation.CaptureRecord().Waystones));
        Assert.True(arena.Simulation.Waystones.Single(w => w.Site.Key == Ashen).Known);
        Assert.Equal(new[] { Ashen }, arena.Simulation.Map.Stones);
    }

    [Fact]
    public void TheRimStone_IsCold_UntilBerrinHasToldTheRite_ThenATouchWakesIt_ForGood()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var cold = At(session, AtRim);
        var refusal = cold.Submit(new TouchStoneCommand(cold.Player, Rim));
        Assert.Equal(session.Setup.Layout.FindWaystone(Rim)!.Dormant!.DormantText, refusal);
        Assert.Equal(0, RimFlag(cold));
        Assert.Empty(cold.Simulation.CaptureRecord().Waystones);
        Assert.False(cold.Simulation.Waystones.Single(w => w.Site.Key == Rim).Awake);

        // Berrin tells it: the line is heard, and that is what the stone asks of a hand.
        var lodge = At(session, AtBerrin);
        Assert.Null(lodge.Submit(new TalkCommand(lodge.Player, Berrin)));
        Assert.Null(lodge.Submit(new ChooseCommand(lodge.Player, "rim_stone")));
        Assert.Null(lodge.Submit(new ChooseCommand(lodge.Player, "back")));
        var heard = lodge.Simulation.CaptureRecord().Conversations.Single(c => c.DialogueId == BerrinsTalk).Heard;
        Assert.Contains("rim_stone", heard);

        var rim = At(session, AtRim, r => r.WithSocial(r.Relationships, new[] { new ConversationMemory(BerrinsTalk, heard) }));
        var touched = rim.Record<StoneTouched>();
        var flags = rim.Record<WorldFlagChanged>();
        Assert.Null(rim.Submit(new TouchStoneCommand(rim.Player, Rim)));

        Assert.Equal((Rim, WaystoneMethod.Woken), (Assert.Single(touched).StoneKey, touched[0].Method));
        Assert.Contains("awake", touched[0].Text);
        Assert.Equal(("world.road.rim_stone_woken", 1L), (Assert.Single(flags).FlagId, flags[0].To));
        Assert.Equal(1, RimFlag(rim));
        Assert.True(rim.Simulation.Waystones.Single(w => w.Site.Key == Rim).Awake);
        Assert.Equal(WaystoneMethod.Woken, Assert.Single(rim.Simulation.CaptureRecord().Waystones).Method);
    }

    // ── binding: the return point is a deliberate act ───────────────────────

    [Fact]
    public void TheReturnPoint_IsChosenAtAKnownStone_AndNeverByTravel()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtAshen);
        Assert.Contains("Touch the stone first", arena.Submit(new BindStoneCommand(arena.Player, Ashen)));
        Assert.Null(arena.CaptureReturnPoint());

        Assert.Null(arena.Submit(new TouchStoneCommand(arena.Player, Ashen)));
        Assert.Null(arena.CaptureReturnPoint());   // touching does not bind
        var bound = arena.Record<ReturnPointBound>();
        Assert.Null(arena.Submit(new BindStoneCommand(arena.Player, Ashen)));
        Assert.Equal(Ashen, Assert.Single(bound).StoneKey);
        Assert.Equal(Ashen, arena.CaptureReturnPoint());

        // Bound at the Rim, then gone home to Ashen by the stones: the Rim is still the return point.
        var rim = At(session, AtRim, r => Knowing(r, null, Ashen, Rim));
        Assert.Contains("reach", rim.Submit(new BindStoneCommand(rim.Player, Ashen)));   // only at the stone itself
        Assert.Null(rim.Submit(new BindStoneCommand(rim.Player, Rim)));
        Assert.Null(rim.Submit(new TravelCommand(rim.Player, Rim, Ashen)));
        Assert.Equal(Rim, rim.CaptureReturnPoint());
    }

    [Fact]
    public void Dying_ReturnsTheCharacter_AtTheStoneTheyChose_AndAtTheSpawnWhileTheyChoseNone()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = new[] { (Arena.Wolf, AtRim.X, AtRim.Z + 1.9, "pack_hunter") };
        PlayerRespawned Die(Arena arena)
        {
            var respawned = arena.Record<PlayerRespawned>();
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            for (int i = 0; i < 400 && respawned.Count == 0; i++)
                arena.Tick();
            return Assert.Single(respawned);
        }
        PlayerRecord Weak(PlayerRecord r) => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = 3 } });

        var bound = At(session, AtRim, r => Knowing(Weak(r), Rim, Ashen, Rim), wolf);
        var stone = session.Setup.Layout.FindWaystone(Rim)!;
        var back = Die(bound);
        Assert.Equal((stone.ArrivalXMm, stone.ArrivalZMm), (back.Body.XMm, back.Body.ZMm));

        var unbound = At(session, AtRim, r => Knowing(Weak(r), null, Ashen, Rim), wolf);
        var spawn = session.Setup.Layout.Spawn;
        back = Die(unbound);
        Assert.Equal((spawn.XMm, spawn.ZMm), (back.Body.XMm, back.Body.ZMm));
    }

    // ── travel and its cost ─────────────────────────────────────────────────

    [Fact]
    public void TheJourney_CostsAQuarterOfTheRoadsWalk_InWorldTime_ShownBeforeItIsTaken()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtAshen, r => Knowing(r, null, Ashen, Rim));
        var option = Assert.Single(arena.Simulation.TravelOptions(Ashen));
        Assert.Equal((Rim, "region.starting_frontier"), (option.ToKey, option.RegionId));
        Assert.Null(option.Blocked);

        // By the Old East Road, the Rim is a little over half a kilometre from the Ashen Waystone, the last 43 m across the rim's turf.
        Assert.InRange(option.Cost.RoadMm, 520_000, 600_000);
        double walkMmPerSecond = session.Setup.Movement.SpeedMmPerSecond(Domain.Spatial.Gait.Walk);
        Assert.Equal((long)Math.Ceiling(option.Cost.RoadMm / walkMmPerSecond * 1000 / session.Setup.TickMilliseconds), option.Cost.WalkTicks);
        Assert.Equal(option.Cost.WalkTicks / 4, option.Cost.WorldTicks);
        // The other way round costs the same.
        var rim = At(session, AtRim, r => Knowing(r, null, Ashen, Rim));
        Assert.Equal(option.Cost, Assert.Single(rim.Simulation.TravelOptions(Rim)).Cost);

        var travelled = arena.Record<PlayerTravelled>();
        long before = arena.Simulation.WorldTick;
        Assert.Null(arena.Submit(new TravelCommand(arena.Player, Ashen, Rim)));
        var journey = Assert.Single(travelled);
        Assert.Equal((Ashen, Rim, option.Cost), (journey.FromKey, journey.ToKey, journey.Cost));
        Assert.Equal(before + option.Cost.WorldTicks, arena.Simulation.WorldTick);
        var stone = session.Setup.Layout.FindWaystone(Rim)!;
        Assert.Equal((stone.ArrivalXMm, stone.ArrivalZMm), (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm));
        arena.Tick(5);   // the world goes on from the far stone
        Assert.Equal(before + option.Cost.WorldTicks + 5, arena.Simulation.WorldTick);
    }

    [Fact]
    public void TheStones_GoOnlyFromAStone_ToAnotherTheCharacterKnows()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var field = At(session, (AtAshen.X + 6, AtAshen.Z), r => Knowing(r, null, Ashen, Rim));
        Assert.Contains("reach", field.Submit(new TravelCommand(field.Player, Ashen, Rim)));   // never from the field

        var stranger = At(session, AtAshen, r => Knowing(r, null, Ashen));
        Assert.Equal("That is not a stone you know", stranger.Submit(new TravelCommand(stranger.Player, Ashen, Rim)));
        Assert.Empty(stranger.Simulation.TravelOptions(Ashen));

        var untouched = At(session, AtAshen, r => Knowing(r, null, Rim));
        Assert.Contains("Touch the stone first", untouched.Submit(new TravelCommand(untouched.Player, Ashen, Rim)));
        Assert.Equal(0, untouched.Simulation.WorldTick);
    }

    [Fact]
    public void TheStones_WillNotTakeAPartyThatIsBeingHunted_OrThatLeavesSomeoneDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var hunted = At(session, AtRim, r => Knowing(r, null, Ashen, Rim), new[] { (Arena.Wolf, AtRim.X, AtRim.Z + 3, "pack_hunter") });
        for (int i = 0; i < 100 && !hunted.Simulation.Creatures.Any(c => c.Hostile); i++)
            hunted.Tick();
        Assert.Contains(hunted.Simulation.Creatures, c => c.Hostile);
        Assert.Contains("hunted", hunted.Submit(new TravelCommand(hunted.Player, Rim, Ashen)));
        Assert.Contains("hunted", Assert.Single(hunted.Simulation.TravelOptions(Rim)).Blocked);

        var downed = At(session, AtRim, r => Knowing(r, null, Ashen, Rim).WithCompanions(new[]
        {
            new CompanionRecord("npc.ashen_hollow.tavar_orr", CompanionOrder.Follow, CompanionCondition.Downed, 882_000, 704_500, 0, 0) { DownedTick = 0 },
        }));
        Assert.Contains("Help them up first", downed.Submit(new TravelCommand(downed.Player, Rim, Ashen)));
    }

    [Fact]
    public void AFollowingCompanion_ArrivesWithTheCharacter_AndOneToldToWait_StaysWhereTheyWait()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Tavar = "npc.ashen_hollow.tavar_orr";
        var arena = At(session, AtRim, r => Knowing(r, null, Ashen, Rim).WithCompanions(new[]
        {
            new CompanionRecord(Tavar, CompanionOrder.Follow, CompanionCondition.Up, 882_500, 704_000, 0, 40),
        }));
        var travelled = arena.Record<PlayerTravelled>();
        Assert.Null(arena.Submit(new TravelCommand(arena.Player, Rim, Ashen)));
        Assert.Equal(new[] { Tavar }, Assert.Single(travelled).Party);
        var player = arena.Simulation.Player.Body;
        var tavar = arena.Simulation.Companions.Single().Body;
        Assert.InRange(Math.Sqrt(Math.Pow(tavar.XMm - player.XMm, 2) + Math.Pow(tavar.ZMm - player.ZMm, 2)), 1_000, 3_000);

        var waiting = At(session, AtRim, r => Knowing(r, null, Ashen, Rim).WithCompanions(new[]
        {
            new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 882_500, 704_000, 0, 40),
        }));
        Assert.Null(waiting.Submit(new TravelCommand(waiting.Player, Rim, Ashen)));
        Assert.Equal((882_500L, 704_000L), (waiting.Simulation.Companions.Single().Body.XMm, waiting.Simulation.Companions.Single().Body.ZMm));
    }

    // ── the map, and a save ─────────────────────────────────────────────────

    [Fact]
    public void TheMap_HoldsOnlyWhatTheCharacterFound_AndTheRoadTheyWalked()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Harness.Ticks(session, 2);
        var map = simulation.Map;
        Assert.Equal(new[] { "location.outpost" }, map.Places.Select(p => p.LocationId));
        Assert.Equal(60_000, map.Places[0].RevealRadiusMm);
        Assert.Empty(map.Stones);
        // The character starts on the road's first stretch, at the Ashen Waystone, and knows no more of it.
        var first = Assert.Single(map.Roads);
        Assert.Equal(("road.old_east_road", 430_000L, 758_000L), (first.RoadId, first.X0Mm, first.Z0Mm));
        Assert.Equal(new[] { Roads.StretchKey("road.old_east_road", 0) }, simulation.CaptureRecord().RoadsWalked);

        Assert.True(Harness.WalkTo(session, 464_000, 748_000));
        Assert.Contains(simulation.Map.Roads, r => r.X0Mm == 440_000 && r.X1Mm == 464_000);
        Assert.DoesNotContain(simulation.Map.Places, p => p.LocationId == "location.road.basin_rim");
        Assert.DoesNotContain(simulation.Map.Roads, r => r.X0Mm >= 600_000);
    }

    [Fact]
    public void KnownStones_TheReturnPoint_AndTheWalkedRoad_SurviveASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkTo(session, (long)(AtAshen.X * 1000), (long)(AtAshen.Z * 1000), Domain.Spatial.Gait.Walk));
        session.Submit(new TouchStoneCommand(simulation.PlayerId, Ashen));
        session.Submit(new BindStoneCommand(simulation.PlayerId, Ashen));
        Harness.Ticks(session, 2);
        var saved = simulation.CaptureRecord();
        Assert.Equal(Ashen, saved.ReturnPoint);
        Assert.Single(saved.Waystones);
        Assert.NotEmpty(saved.RoadsWalked);
        session.Save(SaveSlots.Quick);
        string digest = simulation.StateDigest();

        var reloaded = Harness.Boot(profile);
        var result = reloaded.Load(SaveSlots.Quick);
        Assert.True(result.IsComplete);
        var loaded = reloaded.Simulation!.CaptureRecord();
        Assert.Equal(saved.Waystones.ToArray(), loaded.Waystones.ToArray());
        Assert.Equal(saved.ReturnPoint, loaded.ReturnPoint);
        Assert.Equal(saved.RoadsWalked.ToArray(), loaded.RoadsWalked.ToArray());
        Assert.Equal(digest, reloaded.Simulation!.StateDigest());
        Assert.Equal(simulation.Map.Places.ToArray(), reloaded.Simulation!.Map.Places.ToArray());
        Assert.Equal(simulation.Map.Roads.ToArray(), reloaded.Simulation!.Map.Roads.ToArray());
        Assert.Equal(simulation.Map.Stones.ToArray(), reloaded.Simulation!.Map.Stones.ToArray());
    }
}

internal static class TravelArena
{
    public static string? CaptureReturnPoint(this Arena arena) => arena.Simulation.CaptureRecord().ReturnPoint;
}
