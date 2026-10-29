using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Washing (D20): the water's second use - a short rest at the bank, on the same timed activity fishing, lockpicking and the swing at a
/// node share, that gives a little of the body's wind back and says so. Refused away from the bank, from inside the water, and while
/// anything else is running; walking off the bank or cancelling grants nothing.
/// </summary>
public class WashingTests
{
    private const string Creek = "water.ah_creek";

    // The creek at region (492, 748) and the millpond at (958, 700), in the hollow's own metres.
    private static readonly (double X, double Z) AtTheCreek = (92, 148);
    private static readonly (double X, double Z) AtThePond = (558, 100);
    private static readonly (double X, double Z) DryGround = (120, 60);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change ?? Arena.Usual);

    private static PlayerRecord Tired(PlayerRecord record, int stamina) =>
        record.WithProgression(record.Progression with { Pools = record.Progression.Pools with { Stamina = stamina } });

    private static int Stamina(Arena arena) =>
        arena.Simulation.Player.Progression.Pools.Stamina ?? (int)arena.Simulation.Player.Stats.StaminaMax;

    /// <summary>
    /// The rest itself: thirty ticks, the wind back, and the news of it. A raised guard is the one standing state the combat's own rules
    /// do not regenerate in, so the pool the wash moves is the wash's own and nothing else's.
    /// </summary>
    [Fact]
    public void AWashAtTheBank_RunsItsThirtyTicks_GivesWindBack_AndSaysSo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtTheCreek, record => Tired(record, 20));
        var washed = arena.Record<Washed>();

        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        long began = arena.Simulation.WorldTick;
        Assert.Null(arena.Submit(new WashCommand(arena.Player)));
        var wash = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        Assert.Equal((ActivityKind.Wash, Creek, began + WashingSystem.WashTicks), (wash.Kind, wash.TargetKey, wash.UntilTick));

        // The last tick is the one it is done on: nothing is given back a tick early.
        arena.Tick(WashingSystem.WashTicks - 1);
        Assert.Equal(20, Stamina(arena));
        Assert.Empty(washed);
        arena.Tick(1);
        Assert.Null(arena.Simulation.Activity);
        Assert.Equal(Creek, Assert.Single(washed).WaterKey);
        Assert.Equal(20 + WashingSystem.StaminaRestored, Stamina(arena));
    }

    [Fact]
    public void AWashIsRefusedAwayFromTheBank_FromInsideTheWater_AndWhileSomethingElseRuns()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var dry = At(session, DryGround);
        Assert.Equal("Stand at the water's edge", dry.Submit(new WashCommand(dry.Player)));
        Assert.Null(dry.Simulation.Activity);

        // The millpond is a bank only from its edge: a body floating in it is not at one.
        var pond = At(session, AtThePond);
        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);
        Assert.Equal("Not while you are in the water", pond.Submit(new WashCommand(pond.Player)));
        Assert.Null(pond.Simulation.Activity);

        // And one timed thing at a time, as a cast or a pick is: the line is already out.
        var bank = At(session, AtTheCreek);
        Assert.Null(bank.Submit(new StartFishingCommand(bank.Player, Creek)));
        Assert.Equal("You are already doing something", bank.Submit(new WashCommand(bank.Player)));
        Assert.Equal(ActivityKind.FishCast, bank.Simulation.Activity!.Kind);
    }

    [Fact]
    public void CancellingOrWalkingOffTheBank_EndsTheWash_AndGrantsNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtTheCreek);
        var washed = arena.Record<Washed>();

        // A cancel ends one where it stands, and the same command with nothing running is not an error.
        Assert.Null(arena.Submit(new WashCommand(arena.Player)));
        arena.Tick(4);
        Assert.Null(arena.Submit(new CancelActivityCommand(arena.Player)));
        Assert.Null(arena.Simulation.Activity);
        arena.Tick(WashingSystem.WashTicks);
        Assert.Empty(washed);
        Assert.Null(arena.Submit(new CancelActivityCommand(arena.Player)));

        // Walking off the bank ends one too, on the tick the water is out of the body's reach - at a run, well before it is done.
        Assert.Null(arena.Submit(new WashCommand(arena.Player)));
        Assert.NotNull(arena.Simulation.Activity);
        Assert.True(arena.WalkTo(AtTheCreek.X + 8, AtTheCreek.Z), "walked off the bank");
        Assert.Null(arena.Simulation.Activity);
        arena.Tick(WashingSystem.WashTicks);
        Assert.Empty(washed);
    }
}
