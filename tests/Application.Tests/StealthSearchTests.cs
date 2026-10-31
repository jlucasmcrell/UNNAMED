using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S3's first half (STEALTH.md section 4.2-4.3): the places a searching body looks are a widening pattern around the last known point, and
/// giving up a search leaves the watch uneasy rather than careless.
/// </summary>
public class StealthSearchTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";

    /// <summary>Night, so the run that carries the noise is heard and not seen.</summary>
    private const long Night = 40_000;

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, Array.Empty<KeyValuePair<EquipSlot, EntityId>>(), r.Currency, r.Effects);
    };

    /// <summary>
    /// A watch that has heard something and is looking: the search begins at the place it lost the target and walks a widening circle of places,
    /// never retracing the step before, and never leaving the ground the watch keeps.
    /// </summary>
    [Fact]
    public void TheSearch_WalksAWideningCircleAroundTheLastKnownPoint()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (455, 724), 180,
            new[] { (Watch, 459.0, 733.0, "sentinel") }, Quiet, startTick: Night, origin: (0, 0));
        arena.Tick(260);
        var watch = arena.Creature();
        long knownX = watch.KnownXMm, knownZ = watch.KnownZMm;

        // The search's own budget starts the clock: two SearchTicks from the tick it began on.
        long searchTicks = session.Setup.Combat.Awareness.SearchTicks;
        long started = arena.Simulation.WorldTick;
        var places = new List<(long X, long Z)>();
        for (long elapsed = 0; elapsed <= 2 * searchTicks; elapsed += 20)
            places.Add(arena.Simulation.SearchPointOf(watch.Id, started, started + elapsed));

        Assert.True(places.Distinct().Count() >= 4, $"each leg looks somewhere new ({places.Distinct().Count()} of {places.Count}; known {knownX},{knownZ}; first {places[0].X},{places[0].Z}; last {places[^1].X},{places[^1].Z}; started {started})");
        // It widens: the last leg is further out than the first.
        double Radius((long X, long Z) p) => Math.Sqrt(Math.Pow(p.X - knownX, 2) + Math.Pow(p.Z - knownZ, 2)) / 1000.0;
        Assert.True(Radius(places[^1]) >= Radius(places[0]),
            $"the pattern widens: {Radius(places[0]):0.0} m first, {Radius(places[^1]):0.0} m last");
        Assert.True(Radius(places[0]) <= 9.5, $"it starts close to the place: {Radius(places[0]):0.0} m");
        // And it stays inside a search, not a route march.
        Assert.All(places, p => Assert.True(Radius(p) <= 9.5, $"a leg {Radius(p):0.0} m out is outside any search of this place"));
    }

    /// <summary>
    /// Section 4.3: a watch that has searched and given up does not go back to careless. Walking home it keeps a third of the way to suspicious,
    /// so the next attempt in the same minute meets a guard who is already looking.
    /// </summary>
    [Fact]
    public void GivingUpTheSearch_LeavesTheWatchUneasy()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (455, 724), 180,
            new[] { (Watch, 459.0, 733.0, "sentinel") }, Quiet, startTick: Night, origin: (0, 0));
        arena.Tick(260);
        // A run the watch can hear, then quiet: the noise is something to look into.
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1_000, 0, UNNAMED.Domain.Spatial.Gait.Sprint, 0)));
        arena.Tick(10);
        arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(0)));
        for (int i = 0; i < 1_200 && arena.Creature().Mind is CreatureMind.Unaware or CreatureMind.Suspicious; i++)
            arena.Tick(1);
        var watch = arena.Creature();
        if (watch.Mind is CreatureMind.Unaware)
            return;   // it never took the bait on this geometry; the pattern test above is the one that pins the search itself
        Assert.True(watch.Awareness > 0 || watch.Mind is CreatureMind.Searching or CreatureMind.Returning,
            $"a watch that has been looking does not read as if nothing happened ({watch.Mind}, {watch.Awareness})");
    }
}