using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Tracking played through the world (canon skill.tracking): the animals on their feet leave spoor as they drift, the character reads
/// which way, how long ago and how many off it, and following it keeps their step at a walk's - so the quarry's own awareness model is
/// what the skill buys, and a read never reveals, moves or spawns a creature (STEALTH_DETECTION_AND_THREAT.md §1, §4).
/// </summary>
public class TrackingTests
{
    private const string Doe = "creature.beast.doe";
    private const string WolfDef = "creature.beast.wolf_grey";

    /// <summary>Where the character stands, in the Ashen Hollow's own metres (Harness.HollowOf).</summary>
    private static readonly (double X, double Z) Stand = (100, 100);

    /// <summary>
    /// The rules with a tracker's starting package: canon's own needs, met - tracking 60 and the insight and the other disciplines a
    /// reader at the top of the skill's ladder carries (config.progression's common ceiling is where a starting competence stops).
    /// </summary>
    private static SimulationSetup Tracked(GameSession session) => session.Setup with
    {
        Progression = session.Setup.Progression with
        {
            StartingPackage = session.Setup.Progression.StartingPackage with
            {
                AttributeBias = new Dictionary<CharacterAttribute, int> { [CharacterAttribute.Insight] = 20 }.ToImmutableSortedDictionary(),
                Skills = new Dictionary<string, int>
                {
                    ["skill.tracking"] = 60,
                    ["skill.survival"] = 30,
                    ["skill.stealth"] = 45,
                }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            },
        },
    };

    [Fact]
    public void ADoeDriftingPastTheCharacter_LeavesATrail_WhichIsReadAndFollowedAndLetGo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, Tracked(session), Stand, 0,
            new[] { (Doe, Stand.X, Stand.Z + 3, "wildlife") });
        var reads = arena.Record<SpoorRead>();
        var began = arena.Record<TrailFollowBegan>();
        var ended = arena.Record<TrailFollowEnded>();

        // The doe is a skittish animal that drifts and bolts: within a second or two it has walked the ground the character stands on.
        SpoorRead? trail = null;
        for (int i = 0; i < 200 && trail is null; i++)
        {
            arena.Tick(5);
            Assert.Null(arena.Submit(new ReadTrailCommand(arena.Player)));
            trail = reads.LastOrDefault(read => read.Readable);
        }
        Assert.NotNull(trail);
        Assert.Equal(1, trail!.Count);                       // one animal came this way, whatever its marks say
        Assert.InRange(trail.BearingMdeg, 0, 359_999);       // and which way it was going
        Assert.InRange(trail.AgeTicks, 0, 3_000);            // and how long ago, inside the ground's own memory
        Assert.False(trail.Uncertain);                       // a fresh trail on ground that keeps one holds no doubt

        Assert.Null(arena.Submit(new FollowTrailCommand(arena.Player)));
        Assert.Equal(trail.BearingMdeg, Assert.Single(began).BearingMdeg);

        Assert.Null(arena.Submit(new StopFollowingCommand(arena.Player)));
        Assert.Equal("You have stopped following", Assert.Single(ended).Reason);
    }

    [Fact]
    public void ThereBeingNothingToFollow_IsRefusedInTheGroundsOwnWords()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A hollow with nothing living in it: no mark was ever left, so there is nothing here to find or to follow.
        var arena = Arena.OpenCreatures(session, Tracked(session), Stand, 0,
            Array.Empty<(string, double, double, string)>());
        var reads = arena.Record<SpoorRead>();

        Assert.Null(arena.Submit(new ReadTrailCommand(arena.Player)));
        Assert.False(Assert.Single(reads).Readable);
        Assert.Contains(arena.Submit(new FollowTrailCommand(arena.Player)),
            new[] { "There is nothing here to follow", "The ground here keeps nothing" });
    }

    [Fact]
    public void FollowingTheTrail_KeepsTheStepAWalk_WhereASprintWithoutItIsHeard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A doe for the trail, and a wolf asleep four metres off the road: blind until woken, and it hears 12 m while it sleeps.
        var arena = Arena.OpenCreatures(session, Tracked(session), Stand, 0,
            new[] { (Doe, Stand.X, Stand.Z + 3, "wildlife"), (WolfDef, Stand.X + 4, Stand.Z, "sleeper") });
        var reads = arena.Record<SpoorRead>();
        var began = arena.Record<TrailFollowBegan>();

        SpoorRead? trail = null;
        for (int i = 0; i < 200 && trail is null; i++)
        {
            arena.Tick(5);
            Assert.Null(arena.Submit(new ReadTrailCommand(arena.Player)));
            trail = reads.LastOrDefault(read => read.Readable);
        }
        Assert.NotNull(trail);
        Assert.True(Wolf(arena).Asleep, "the wolf is asleep, so everything it knows it heard");

        Assert.Null(arena.Submit(new FollowTrailCommand(arena.Player)));
        Assert.Single(began);

        // Two seconds of sprinting out and back along the road while following: a followed step is a walk's, and does not carry.
        Sprint(arena, direction: -1, metres: 8);
        Assert.True(Wolf(arena).Asleep, "a followed step does not wake a sleeping wolf");
        Assert.Equal(0, Wolf(arena).Awareness);

        // The same sprint with the follow let go, coming back within earshot: a run's noise carries, and the wolf hears it.
        Assert.Null(arena.Submit(new StopFollowingCommand(arena.Player)));
        Sprint(arena, direction: 1, metres: 8);
        Assert.True(Wolf(arena).Awareness >= 60, $"the un-followed sprint carried to the wolf (awareness {Wolf(arena).Awareness})");
    }

    private static CreatureView Wolf(Arena arena) => arena.Simulation.Creatures.Single(c => c.DefId == WolfDef);

    /// <summary>Sprint out or back along the road for about <paramref name="metres"/>, one tick at a time, the way the keys would.</summary>
    private static void Sprint(Arena arena, int direction, int metres)
    {
        long from = arena.Simulation.Player.Body.XMm;
        for (int i = 0; i < 400; i++)
        {
            long travelled = (arena.Simulation.Player.Body.XMm - from) * direction;
            if (travelled >= metres * 1000L)
                return;
            arena.Submit(new MoveCommand(arena.Player, Harness.Toward(direction, 0, Gait.Sprint)));
            arena.Tick();
        }
    }
}
