using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Fishing: one timed activity over the game's own content - a cast that plays out, a bite set when it lands, and a reel that takes the
/// water's catch table or comes back empty - and the <c>skill.fishing</c> practice it pays. The two waters the frontier fishes are the
/// Ashen Hollow creek and the Cairnwold millpond; the millrace beside the pond names no catch table.
/// </summary>
public class FishingTests
{
    private const string Creek = "water.ah_creek";
    private const string Pond = "water.cw_millpond";
    private const string Millrace = "water.cw_millrace";
    private const string FishingSkill = "skill.fishing";
    private const string Trout = "item.material.fish_trout";
    private const string Finds = "item.material.river_finds";

    // The creek at region (492, 748) and the millpond at region (958, 700), in the hollow's own metres (Harness.HollowOf).
    private static readonly (double X, double Z) AtTheCreek = (92, 148);
    private static readonly (double X, double Z) AtThePond = (558, 100);
    private static readonly (double X, double Z) DryGround = (120, 60);

    private static Arena At(GameSession session, (double X, double Z) place)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), Arena.Usual);
        // The config names a rod, so a cast needs one in the pack: every test here fishes with the tackle a fisher carries.
        Assert.Null(arena.Simulation.Submit(new GrantItem(Rod, 1, Quality.Standard)));
        return arena;
    }

    private const string Rod = "item.tool.fishing_rod";
    private const string Worm = "item.material.bait_worm";

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    /// <summary>Cast, and step until the line is in the water: what every reel test starts from.</summary>
    private static PlayerActivity Waiting(Arena arena, string water)
    {
        Assert.Null(arena.Submit(new StartFishingCommand(arena.Player, water)));
        arena.Tick(FishingSystem.CastTicks + 1);
        return Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
    }

    [Fact]
    public void ACastAtTheCreek_PlaysOut_ThenWaitsWithABiteThreeToNineSecondsOut()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        var casts = arena.Record<FishCastStarted>();
        var lines = arena.Record<FishingLineOut>();

        Assert.Null(arena.Submit(new StartFishingCommand(arena.Player, Creek)));
        Assert.Equal(ActivityKind.FishCast, arena.Simulation.Activity!.Kind);
        Assert.Equal(Creek, arena.Simulation.Activity!.TargetKey);
        Assert.Equal(FishingSystem.CastTicks, arena.Simulation.Activity!.UntilTick);
        Assert.Single(casts);

        // The cast holds for its own ticks and then lands: the line is out, and the bite is 3 s to 9 s from there.
        arena.Tick(FishingSystem.CastTicks - 1);
        Assert.Equal(ActivityKind.FishCast, arena.Simulation.Activity!.Kind);
        arena.Tick(1);
        var waiting = arena.Simulation.Activity!;
        Assert.Equal(ActivityKind.FishWaiting, waiting.Kind);
        Assert.Equal(0, waiting.UntilTick);   // nothing ends a wait but the player, the bank or a fight
        Assert.InRange(waiting.BiteTick - arena.Simulation.WorldTick, FishingSystem.BiteMinTicks, FishingSystem.BiteMaxTicks);
        Assert.Single(lines);
        Assert.False(arena.Simulation.CanFish);   // a line is already out
    }

    [Fact]
    public void ACastWithoutARod_IsRefused_TheRuleTheConfigNames()
    {
        using var profile = new TempProfile();
        // A session at the water with the tackle taken back out of the pack: the cast is refused in the config's own words.
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, AtTheCreek, 0,
            Array.Empty<(string, double, double, string)>(), Arena.Usual);
        Assert.Equal("You need a rod", arena.Submit(new StartFishingCommand(arena.Player, Creek)));
        Assert.Null(arena.Simulation.Activity);
    }

    [Fact]
    public void ACastSpendsOneBait_AndTheBaitShortensTheWait()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        Assert.Null(arena.Simulation.Submit(new GrantItem(Worm, 2, Quality.Standard)));
        Assert.Equal(2, Carried(arena, Worm));

        Assert.Null(arena.Submit(new StartFishingCommand(arena.Player, Creek)));
        Assert.Equal(1, Carried(arena, Worm));   // the cast took one

        arena.Tick(FishingSystem.CastTicks + 1);
        var waiting = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        // The worm's scale is 0.8, so the wait is inside the scaled band rather than the plain one.
        int wait = (int)(waiting.BiteTick - arena.Simulation.WorldTick);
        Assert.InRange(wait, FishingSystem.BiteMinTicks / 2, FishingSystem.BiteMaxTicks);
    }

    [Fact]
    public void TheSameCastOfTheSameWater_MovesTheBiteTheSameWay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // The channel is the water and the cast, never call order: two runs of the same cast bite on the same tick.
        Assert.Equal(Waiting(At(session, AtTheCreek), Creek).BiteTick, Waiting(At(session, AtTheCreek), Creek).BiteTick);
    }

    [Fact]
    public void ACastAwayFromTheBank_OrWithALineAlreadyOut_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var dry = At(session, DryGround);
        Assert.Equal("Stand at the water's edge", dry.Submit(new StartFishingCommand(dry.Player, Creek)));
        Assert.Null(dry.Simulation.Activity);

        var bank = At(session, AtTheCreek);
        Assert.Null(bank.Submit(new StartFishingCommand(bank.Player, Creek)));
        Assert.Equal("You are already doing something", bank.Submit(new StartFishingCommand(bank.Player, Creek)));

        // The millrace is water with no catch table, so there is nothing there to fish for.
        var race = At(session, AtThePond);
        Assert.Equal("There is nothing to fish for here", race.Submit(new StartFishingCommand(race.Player, Millrace)));
    }

    [Fact]
    public void AReelInTheBiteWindow_TakesTheWatersCatch_AndTrainsFishing()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        var caught = arena.Record<FishCaught>();
        var practised = arena.Record<SkillPracticed>();
        var waiting = Waiting(arena, Creek);
        arena.Tick((int)(waiting.BiteTick - arena.Simulation.WorldTick));
        Assert.Equal(waiting.BiteTick, arena.Simulation.WorldTick);

        Assert.Null(arena.Submit(new ReelInCommand(arena.Player)));

        var fish = Assert.Single(caught);
        Assert.Equal(Creek, fish.WaterKey);
        Assert.Contains(fish.ItemId, new[] { Trout, Finds });   // the creek's table, and only the creek's
        Assert.Equal(fish.Count, Carried(arena, fish.ItemId));
        Assert.Equal(ActivityKind.FishReel, arena.Simulation.Activity!.Kind);   // the reel plays from the catch
        var lesson = Assert.Single(practised, p => p.SkillId == FishingSkill);
        Assert.True(lesson.Xp > 0);

        arena.Tick(FishingSystem.ReelTicks);
        Assert.Null(arena.Simulation.Activity);
    }

    [Fact]
    public void AReelBeforeTheBite_ComesBackEmpty_AndTheLineIsIn()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        var lost = arena.Record<FishLost>();
        var caught = arena.Record<FishCaught>();
        Waiting(arena, Creek);

        Assert.Null(arena.Submit(new ReelInCommand(arena.Player)));

        Assert.Equal("Nothing has taken it", Assert.Single(lost).Reason);
        Assert.Empty(caught);
        Assert.Null(arena.Simulation.Activity);
        Assert.Equal(0, Carried(arena, Trout) + Carried(arena, Finds));
    }

    [Fact]
    public void AReelAfterTheWindow_FindsTheFishGone()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtThePond);
        var lost = arena.Record<FishLost>();
        var caught = arena.Record<FishCaught>();
        var waiting = Waiting(arena, Pond);

        // Past the bite and its 5 s window: the fish was on, and is not any more.
        arena.Tick((int)(waiting.BiteTick - arena.Simulation.WorldTick) + FishingSystem.BiteWindowTicks + 1);
        Assert.Null(arena.Submit(new ReelInCommand(arena.Player)));

        Assert.Equal("It has gone", Assert.Single(lost).Reason);
        Assert.Empty(caught);
        Assert.Null(arena.Simulation.Activity);
    }

    [Fact]
    public void WalkingOffTheBank_AndCancelling_BothBringTheLineIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtTheCreek);

        Assert.NotNull(Waiting(arena, Creek));
        Assert.True(arena.WalkTo(AtTheCreek.X + 8, AtTheCreek.Z), "walked off the bank");
        Assert.Null(arena.Simulation.Activity);

        // Back at the bank the line goes out again, and a cancel ends one cleanly - and there being nothing to cancel is not an error.
        Assert.True(arena.WalkTo(AtTheCreek.X, AtTheCreek.Z), "walked back to the bank");
        Assert.NotNull(Waiting(arena, Creek));
        Assert.Null(arena.Submit(new CancelActivityCommand(arena.Player)));
        Assert.Null(arena.Simulation.Activity);
        Assert.Null(arena.Submit(new CancelActivityCommand(arena.Player)));
    }
}
