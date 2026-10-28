using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Lockpicking: one timed activity over the game's own content - a pick that holds for its ticks and a roll when they have run - worked on
/// the two locks the frontier authors: Cairnwold's barred granary door (tier 1) and the sunken barrow's cist (tier 2). The handle will not
/// open either, the skill is practised either way, a picked lock stays picked, and a bad turn can take the picks with it.
/// </summary>
public class LockpickingTests
{
    private const string Granary = "door.granary";
    private const string GranaryOpen = "world.cairnwold.granary_door_open";
    private const string GranaryLock = "world.lock.door.granary";
    private const string Cist = "container.road.barrow_cache";
    private const string CistLock = "world.lock.container.road.barrow_cache";
    private const string Picks = "item.tool.lockpicks";
    private const string Skill = "skill.lockpicking";
    private const string Iron = "item.material.iron_ingot";

    // The granary door at region (940.3-941.7, 675.57-675.97) and the cist at (846.5, 631), in the hollow's own metres (Harness.HollowOf).
    private static readonly CellKey GranaryCell = CellKey.Parse("r_0_0:c_09_06");
    private static readonly CellKey CistCell = CellKey.Parse("r_0_0:c_08_06");
    private static readonly (double X, double Z) AtTheGranary = (541.0, 74.6);
    private static readonly (double X, double Z) AtTheCist = (446.5, 30.0);
    private static readonly (double X, double Z) DownTheRoad = (541.0, 60.0);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    /// <summary>A hand better than either lock: tier 1 asks a level of 12 for an even chance, and 60 is a master's.</summary>
    private static PlayerRecord Masterful(PlayerRecord r) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(Skill, new SkillState(60, 0)) });

    private static PlayerRecord WithoutPicks(PlayerRecord r) => r.WithInventory(r.Inventory.Where(e => e.DefId != Picks));

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    /// <summary>Work the lock once: accepted, holding for its ticks, and nothing left running when they have run.</summary>
    private static void Attempt(Arena arena, string key)
    {
        Assert.Null(arena.Submit(new PickLockCommand(arena.Player, key)));
        var activity = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        Assert.Equal(ActivityKind.PickLock, activity.Kind);
        Assert.Equal(key, activity.TargetKey);
        Assert.Equal(arena.Simulation.WorldTick + LockpickSystem.AttemptTicks, activity.UntilTick);
        arena.Tick(LockpickSystem.AttemptTicks);
        Assert.Null(arena.Simulation.Activity);
    }

    /// <summary>The cell the picks' own channel is opened on: the one the body stands in, which at either lock is the site's own.</summary>
    private static CellKey CellOfBody(Arena arena) =>
        CellKey.OfWorld(arena.Simulation.Player.Body.XMm / 1000.0, arena.Simulation.Player.Body.ZMm / 1000.0);

    /// <summary>
    /// The first tick from now whose attempt comes out as asked, read off the channel the system documents (the key, the pick, the tick it
    /// began): a test says which way its attempt goes instead of hoping the world seed agrees with it.
    /// </summary>
    private static long StartWhere(Arena arena, string key, bool turns, int skill, int tier)
    {
        ulong seed = arena.Simulation.World.WorldSeed;
        var cell = CellOfBody(arena);
        long tick = arena.Simulation.WorldTick;
        while (LockpickSystem.Turns(seed, cell, key, tick, skill, tier) != turns)
            tick++;
        return tick;
    }

    private static void StartAt(Arena arena, long tick) => arena.Tick((int)(tick - arena.Simulation.WorldTick));

    [Fact]
    public void APickedLock_IsAWorldFlag_SoASaveCarriesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtTheGranary, Masterful);
        var picked = arena.Record<LockPicked>();
        StartAt(arena, StartWhere(arena, Granary, turns: true, 60, 1));
        Attempt(arena, Granary);
        Assert.Single(picked);

        // The pick's own record is a flag of the door's cell, so a save carries it as it carries any other flag: no schema learned about locks.
        new UNNAMED.Persistence.SaveStore(profile.Root).Save(UNNAMED.Persistence.SaveSlots.Manual("lockpicking"),
            UNNAMED.Persistence.SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
                arena.Simulation.WorldTick, 0));
        Assert.True(session.Load(UNNAMED.Persistence.SaveSlots.Manual("lockpicking")).IsComplete);

        var loaded = session.Simulation!;
        Assert.False(loaded.IsLocked(Granary));
        Assert.True(loaded.Doors.Single(d => d.Site.Key == Granary).Open);
        Assert.Equal(1, loaded.World.GetFlag(GranaryCell, GranaryLock));
    }

    [Fact]
    public void TheGranaryDoor_RefusesTheHandle_TakesAPick_AndOpensForGood()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheGranary, Masterful);
        var started = arena.Record<LockAttemptStarted>();
        var picked = arena.Record<LockPicked>();
        var toggled = arena.Record<DoorToggled>();
        var simulation = arena.Simulation;

        // The handle: it is a lock, and the door's own words say what it wants.
        Assert.True(simulation.IsLocked(Granary));
        Assert.Contains("wants a pick", arena.Submit(new InteractCommand(arena.Player, Granary)));
        Assert.False(simulation.Doors.Single(d => d.Site.Key == Granary).Open);

        StartAt(arena, StartWhere(arena, Granary, turns: true, 60, 1));
        Assert.Null(arena.Submit(new PickLockCommand(arena.Player, Granary)));
        Assert.Equal((Granary, 1), (Assert.Single(started).TargetKey, started[0].Tier));
        Assert.Equal(ActivityKind.PickLock, simulation.Activity!.Kind);
        arena.Tick(LockpickSystem.AttemptTicks);

        // It gave: the door is open, the lock's own record is set in the door's cell, and the door's open flag with it.
        Assert.Single(picked);
        Assert.True(Assert.Single(toggled).Open);
        Assert.Equal(1, simulation.World.GetFlag(GranaryCell, GranaryLock));
        Assert.Equal(1, simulation.World.GetFlag(GranaryCell, GranaryOpen));
        Assert.True(simulation.Doors.Single(d => d.Site.Key == Granary).Open);
        Assert.False(simulation.IsLocked(Granary));

        // And never again: the picks are refused because the lock is open, and the handle works the door as any door.
        Assert.Equal("The lock is already open", arena.Submit(new PickLockCommand(arena.Player, Granary)));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, Granary)));
        Assert.Equal(0, simulation.World.GetFlag(GranaryCell, GranaryOpen));   // shut on its hinges, and no longer locked
        Assert.False(simulation.IsLocked(Granary));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, Granary)));
        Assert.True(simulation.Doors.Single(d => d.Site.Key == Granary).Open);
    }

    [Fact]
    public void TheCist_RefusesTakeAll_UntilThePicksHaveBeenToIt()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCist, Masterful);
        var picked = arena.Record<LockPicked>();
        var simulation = arena.Simulation;

        Assert.True(simulation.IsLocked(Cist));
        Assert.Equal($"{Cist} is locked", arena.Submit(new TakeAllCommand(arena.Player, Cist)));
        Assert.Equal(0, Carried(arena, Iron));

        StartAt(arena, StartWhere(arena, Cist, turns: true, 60, 2));
        Attempt(arena, Cist);

        Assert.Single(picked);
        Assert.Equal(1, simulation.World.GetFlag(CistCell, CistLock));
        Assert.False(simulation.IsLocked(Cist));
        // Opened, it is searched like any cache: the take-all goes through, and the grave goods are in the pack.
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, Cist)));
        Assert.True(Carried(arena, Iron) >= 2);
    }

    [Fact]
    public void APick_IsRefused_WithoutPicks_WithoutALock_OnceOpen_OutOfReach_AndWhileSomethingElseRuns()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var bare = At(session, AtTheGranary, WithoutPicks);
        Assert.Equal("You have no picks", bare.Submit(new PickLockCommand(bare.Player, Granary)));
        Assert.Null(bare.Simulation.Activity);

        var lockless = At(session, AtTheGranary);
        Assert.Equal("There is no lock on that", lockless.Submit(new PickLockCommand(lockless.Player, "door.cw_house_01")));
        Assert.Contains("is not in this place", lockless.Submit(new PickLockCommand(lockless.Player, "door.nowhere")));

        var far = At(session, DownTheRoad);
        Assert.Contains("reach is 1.60 m", far.Submit(new PickLockCommand(far.Player, Granary)));

        // One timed thing at a time: a pick already working refuses a second, and a cancel ends it cleanly with nothing granted.
        var busy = At(session, AtTheGranary, Masterful);
        var picked = busy.Record<LockPicked>();
        Assert.Null(busy.Submit(new PickLockCommand(busy.Player, Granary)));
        Assert.Equal("You are already doing something", busy.Submit(new PickLockCommand(busy.Player, Cist)));
        Assert.Null(busy.Submit(new CancelActivityCommand(busy.Player)));
        Assert.Null(busy.Simulation.Activity);
        Assert.Empty(picked);
        Assert.True(busy.Simulation.IsLocked(Granary));
    }

    [Fact]
    public void APickPractisesLockpicking_WhetherItTurnsOrHolds()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCist);
        var practised = arena.Record<SkillPracticed>();

        StartAt(arena, StartWhere(arena, Cist, turns: false, 0, 2));
        Attempt(arena, Cist);

        var lesson = Assert.Single(practised, p => p.SkillId == Skill);
        Assert.True(lesson.Xp > 0);
    }

    [Fact]
    public void AFailedPick_HoldsTheLock_AndATurnThatBinds_TakesThePicks()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCist);
        var failed = arena.Record<LockPickFailed>();
        var picked = arena.Record<LockPicked>();
        var simulation = arena.Simulation;

        // An untaught hand on a tier 2 lock: five in a hundred turn it, and the picks are what a bad turn costs.
        ulong seed = simulation.World.WorldSeed;
        var cell = CellOfBody(arena);
        long tick = simulation.WorldTick;
        while (LockpickSystem.Turns(seed, cell, Cist, tick, 0, 2) || !LockpickSystem.Snaps(seed, cell, Cist, tick))
            tick++;

        StartAt(arena, tick);
        Attempt(arena, Cist);

        var held = Assert.Single(failed);
        Assert.True(held.Broke);
        Assert.Empty(picked);
        Assert.True(simulation.IsLocked(Cist));
        Assert.Equal($"{Cist} is locked", arena.Submit(new TakeAllCommand(arena.Player, Cist)));
        Assert.Equal(0, Carried(arena, Picks));
        Assert.Equal("You have no picks", arena.Submit(new PickLockCommand(arena.Player, Cist)));
    }
}
