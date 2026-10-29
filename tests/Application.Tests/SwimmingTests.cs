using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Swimming (D20): a water's own depth makes it a body rather than a bank. The millpond is over the head and floats the character at the
/// surface, at half a walk and no sprint; the creek and its gravel ford are knee deep and are waded exactly as they were. Out of wind, the
/// water puts the body on the nearest bank and takes a little health for it, and a shore that rises within 0.3 m of the surface is climbed
/// out onto while the water is still over the body's feet.
/// </summary>
public class SwimmingTests
{
    private const string Pond = "water.cw_millpond";

    // The creek at region (492, 748), its gravel ford at (452, 760) and the millpond at (958, 700), in the hollow's own metres.
    private static readonly (double X, double Z) AtThePond = (558, 100);
    private static readonly (double X, double Z) AtTheCreek = (92, 148);
    private static readonly (double X, double Z) AtTheFord = (52, 160);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change ?? Arena.Usual);

    private static int Stamina(Arena arena) =>
        arena.Simulation.Player.Progression.Pools.Stamina ?? (int)arena.Simulation.Player.Stats.StaminaMax;

    private static PlayerRecord Tired(PlayerRecord record, int stamina) =>
        record.WithProgression(record.Progression with { Pools = record.Progression.Pools with { Stamina = stamina } });

    /// <summary>Twenty ticks of one full-deflection eastward intent, and how far east it carried the body, in millimetres.</summary>
    private static long Eastward(Arena arena, Gait gait)
    {
        long from = arena.Simulation.Player.Body.XMm;
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(MoveIntent.FullDeflection, 0, gait, 90_000)));
        arena.Tick(20);
        return arena.Simulation.Player.Body.XMm - from;
    }

    [Fact]
    public void AWaterOverTheHeadFloatsTheBody_AndKneeDeepWaterIsStillWaded()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var movement = session.Setup.Movement;

        // The millpond is 2.4 m deep: the body floats, its feet at the surface less its float, so it sits lower than it stands and its
        // head and eye are above the water. It is not standing on the bed, and the pond's ground is 0.35 m under its sheet.
        var pond = At(session, AtThePond);
        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);
        var floating = pond.Simulation.Player.Body;
        Assert.Equal(4_350 - movement.SwimFloatMm, floating.YMm);
        Assert.True(floating.YMm + movement.StandHeightMm > 4_350, "the head is above the surface");
        Assert.Equal(4_000, session.Setup.Layout.Space.HeightAtMm(floating.XMm, floating.ZMm));   // the pond's bed, under the feet

        // The creek is 0.55 m deep in its channel and the ford 0.15: wading, unchanged, the feet on the ground.
        var creek = At(session, AtTheCreek);
        creek.Tick(1);
        Assert.False(creek.Simulation.Swimming);
        var ford = At(session, AtTheFord);
        ford.Tick(1);
        Assert.False(ford.Simulation.Swimming);
        var wading = ford.Simulation.Player.Body;
        Assert.Equal(6_400, wading.YMm);   // the ford's own gravel, under the feet
        Assert.Equal(session.Setup.Layout.Space.HeightAtMm(wading.XMm, wading.ZMm), wading.YMm);
    }

    [Fact]
    public void SwimmingSpendsStamina_AndASpentSwimmerIsPutAshore_ForALittleHealth()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var pond = At(session, (AtThePond.X + 0.5, AtThePond.Z), record => Tired(record, 4));
        var under = pond.Record<WentUnder>();
        int health = pond.Simulation.Combat.Health;

        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);
        Assert.Equal(4, Stamina(pond));
        pond.Tick(8);
        Assert.True(Stamina(pond) < 4, $"the swim spends the body's wind ({Stamina(pond)} left)");

        // Spent, it goes under: ashore at the nearest bank, on its feet and wading, a little health the worse for it.
        for (int tick = 0; tick < 200 && under.Count == 0; tick++)
            pond.Tick();
        var going = Assert.Single(under);
        Assert.Equal(Pond, going.WaterKey);
        Assert.False(pond.Simulation.Swimming);
        Assert.Equal(health - 5, pond.Simulation.Combat.Health);
        var ashore = pond.Simulation.Player.Body;
        Assert.Equal(session.Setup.Layout.Space.HeightAtMm(ashore.XMm, ashore.ZMm), ashore.YMm);   // walking again
        // Out along the line from the pond's own point through the body, a body's radius past the water's edge (13 m + 350 mm).
        Assert.InRange(ashore.XMm, 958_000 + 13_000, 958_000 + 13_400);
        Assert.Equal(700_000, ashore.ZMm);
    }

    [Fact]
    public void WalkingOutOfThePond_LeavesTheSwim_AndTheBodyWalks()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var pond = At(session, AtThePond);

        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);
        Assert.True(pond.WalkTo(AtThePond.X + 15, AtThePond.Z), "swam out of the pond");

        Assert.False(pond.Simulation.Swimming);
        var walked = pond.Simulation.Player.Body;
        Assert.Equal(session.Setup.Layout.Space.HeightAtMm(walked.XMm, walked.ZMm), walked.YMm);
    }

    /// <summary>
    /// The climb-out itself, on a shore the content does not have: a water ten paces wide with its bed cut 2.4 m below the surface and the
    /// ground rising a hand's width a stride. The body swims at half a walk, and the moment the ground under it comes up within 0.3 m of
    /// the surface it is on its feet and walking - a long way short of the water's own edge, so the rise is what ends the swim.
    /// </summary>
    [Fact]
    public void TheGroundRisingWithinAThirdOfAMetreOfTheSurface_IsClimbedOutOf_StillInTheWater()
    {
        var rules = new MovementRules(3_200, 50, 160, 350, 1_600);
        var heights = new long[2 * 21];
        for (int column = 0; column <= 20; column++)
            heights[column] = heights[21 + column] = -2_400 + column * 150;   // a shore rising to 0.6 m above the surface
        var space = new WalkSpace(0, 0, 20_000, 1_000, new TerrainGrid(0, 0, 1_000, 21, 2, heights), ImmutableArray<Blocker>.Empty)
        {
            Waters = ImmutableArray.Create(new WaterSite("water.test_shore", "pond", 0, ImmutableArray.Create((0L, 0L), (20_000L, 0L)), 20_000)
            {
                DepthMm = 2_400,
            }),
        };
        var east = new MoveIntent(MoveIntent.FullDeflection, 0, Gait.Walk, 90_000);
        var body = new Body(1_000, -2_250, 500, 90_000);
        var posture = Posture.Grounded;
        bool Swims() => Kinematics.SwimWater(body.XMm, body.ZMm, space) is not null;

        // In it is a swim, the feet at the surface less the body's float, and half a walk: 3.2 m/s at a walk's 50 % is 1.6 m/s, so 40 mm
        // a tick rather than the 80 the same key gives ashore.
        (body, posture) = Kinematics.Step(body, posture, east, rules, space, Array.Empty<Blocker>(), 50);
        Assert.True(Swims());
        Assert.Equal(0 - rules.SwimFloatMm, body.YMm);
        long from = body.XMm;
        (body, posture) = Kinematics.Step(body, posture, east, rules, space, Array.Empty<Blocker>(), 50);
        Assert.Equal(40, body.XMm - from);

        // And on until the ground under it comes up within 0.3 m of the surface: it climbs out and is on its feet, a long way short of
        // the water's own edge - so the rise, not the far bank, is what ends the swim.
        int swam = 1;
        for (; swam < 400 && Swims(); swam++)
            (body, posture) = Kinematics.Step(body, posture, east, rules, space, Array.Empty<Blocker>(), 50);
        Assert.True(swam < 400, "it never climbed out");
        Assert.False(Swims());
        Assert.True(0 - space.HeightAtMm(body.XMm, body.ZMm) <= WaterSite.ClimbOutMm, "the ground is within 0.3 m of the surface");
        Assert.True(body.XMm < 20_000, "the water still reaches past where it climbed out");

        // Walking again, at the walk's own pace, the feet on the ground it climbed onto.
        from = body.XMm;
        (body, posture) = Kinematics.Step(body, posture, east, rules, space, Array.Empty<Blocker>(), 50);
        Assert.Equal(80, body.XMm - from);
        Assert.Equal(space.HeightAtMm(body.XMm, body.ZMm), body.YMm);
    }

    [Fact]
    public void TheSprintAndTheLeap_AreRefusedInTheWater()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A Kal, which leaps on its wings from a sprint ashore.
        static PlayerRecord Kal(PlayerRecord record) => record with { Identity = record.Identity with { PeopleId = "people.kal" } };

        // The gait changes nothing in the water: the swim is half a walk, so a sprint carries the body exactly as far as the walk does.
        var walked = At(session, AtThePond, Kal);
        walked.Tick(1);
        Assert.True(walked.Simulation.Swimming);
        long swim = Eastward(walked, Gait.Walk);
        var sprinted = At(session, AtThePond, Kal);
        sprinted.Tick(1);
        Assert.True(sprinted.Simulation.Swimming);
        Assert.Equal(swim, Eastward(sprinted, Gait.Sprint));

        // And the water is not a place to leap from: the jump is refused, so the leap never opens the wings.
        var wings = sprinted.Record<WingsChanged>();
        Assert.Equal("the water is not a place to leap from", sprinted.Submit(new JumpCommand(sprinted.Player)));
        Assert.False(sprinted.Simulation.Posture.Airborne);
        Assert.True(sprinted.Simulation.Swimming);
        Assert.Empty(wings);
    }
}
