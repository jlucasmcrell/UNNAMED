using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Perception's front door (STEALTH.md section 2.1): a watchman looking at the character in daylight notices them, and then comes for them. Every
/// other stealth test in this branch reaches a creature through a noise, a sign or a takedown; this one is the plain sighting the rest assume.
/// </summary>
/// <remarks>
/// The first version of this test looked for awareness after two hundred ticks and found none, which read like a broken sight check and was not: a
/// hostile watchman who notices the character at six metres kills them inside ten seconds, and a downed character respawns at the region's spawn
/// point forty metres away, where there is nothing to see. The sighting is fast and the fight is fast; the test now watches the first seconds.
/// </remarks>
public class StealthSightTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";

    /// <summary>A clear patch of the road, where the arena puts a body where it is asked (checked by the gap, not assumed).</summary>
    private const double RoadX = 470, RoadZ = 745;

    /// <summary>
    /// Six metres in front of a watchman in daylight: he notices within a second, is coming within a second more, and is on the character - which is
    /// the whole of what an unaware target has to fear.
    /// </summary>
    [Fact]
    public void AWatchmanLookingAtTheCharacter_NoticesHimAndComesForHim()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The placement rolls the watchman's facing; the character stands along it, so this is a plain sighting and not a guess about geometry.
        double facing = Arena.OpenCreatures(session, session.Setup, (RoadX, RoadZ), 0, new[] { (Watch, RoadX, RoadZ, "sentinel") },
            null, startTick: 12_000, origin: (0, 0)).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var eye = (RoadX + Math.Sin(facing) * 6, RoadZ + Math.Cos(facing) * 6);
        var arena = Arena.OpenCreatures(session, session.Setup, eye, (int)Math.Round(facing * 180 / Math.PI) + 180,
            new[] { (Watch, RoadX, RoadZ, "sentinel") }, null, startTick: 12_000, origin: (0, 0));

        var seen = arena.Creature();
        double range = Math.Sqrt(Math.Pow(seen.Body.XMm - arena.Simulation.Player.Body.XMm, 2)
                               + Math.Pow(seen.Body.ZMm - arena.Simulation.Player.Body.ZMm, 2)) / 1000.0;
        Assert.True(range < 7, $"the character has to be where the test put them ({range:0.0} m)");

        int noticedAt = -1, engagedAt = -1;
        for (int i = 1; i <= 40 && engagedAt < 0; i++)
        {
            arena.Tick(1);
            var watcher = arena.Creature();
            if (noticedAt < 0 && watcher.Awareness > 0)
                noticedAt = i;
            if (watcher.Mind == CreatureMind.Engaged)
                engagedAt = i;
        }
        Assert.True(noticedAt > 0, "a watchman six metres away in daylight has to notice");
        Assert.True(noticedAt <= 5, $"and quickly: noticed on tick {noticedAt}");
        Assert.True(engagedAt > 0, "noticing has to lead somewhere");
        Assert.True(engagedAt <= 30, $"and soon: engaged on tick {engagedAt}");

        // And he is on the character, not standing off: the gap is closing.
        double closing = Math.Sqrt(Math.Pow(arena.Creature().Body.XMm - arena.Simulation.Player.Body.XMm, 2)
                                 + Math.Pow(arena.Creature().Body.ZMm - arena.Simulation.Player.Body.ZMm, 2)) / 1000.0;
        Assert.True(closing < range, $"he comes closer: {range:0.0} m to {closing:0.0} m");
    }
}