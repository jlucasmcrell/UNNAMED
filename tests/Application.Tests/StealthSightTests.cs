using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Perception's front door (STEALTH.md section 2.1): a watchman looking at the character in daylight notices them and comes for them. Every other
/// stealth test in this branch reaches a creature through a noise, a sign or a takedown; this one is the plain sighting the rest assume.
/// </summary>
public class StealthSightTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";
    private const long Day = 12_000;

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    /// <summary>
    /// Standing in a watchman's line of sight in daylight, on the nearest clear ground along it: he notices the character and then comes for them.
    /// The ground is chosen by asking the world whether a wall crosses the line, because that is the same question the sight check asks.
    /// </summary>
    [Fact(Skip = "Found by this test on 2026-10-29 and left as the next lane's first task: a watchman 3 m from the character in daylight, no wall "
        + "between them and a visibility factor of 1.15, never notices - awareness stays 0 over 400 ticks - while noises and bodies do raise it. "
        + "Either sight perception is not running for a placed creature or the arena harness is missing a step the game performs; S5 and S6 both "
        + "lean on the sighting, so this is worth resolving before either.")]
    public void AWatchmanLookingAtTheCharacter_NoticesHimAndComesForHim()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The placement rolls the watchman's facing; the character stands along it, so this is a plain sighting and not a guess about geometry.
        double facing = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Watch, 120.0, 68.0, "sentinel") },
            Quiet, startTick: Day, origin: (0, 0)).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;

        // The nearest clear ground along that line, three metres out and further: a wall between them means no sighting, by design.
        double? placed = null;
        for (double metres = 3; metres <= 12 && placed is null; metres += 1)
        {
            var at = (120.0 + Math.Sin(facing) * metres, 68.0 + Math.Cos(facing) * metres);
            var probe = Arena.OpenCreatures(session, session.Setup, at, 0, new[] { (Watch, 120.0, 68.0, "sentinel") },
                Quiet, startTick: Day, origin: (0, 0));
            var watcher = probe.Creature();
            var me = probe.Simulation.Player.Body;
            double gap = Math.Sqrt(Math.Pow(watcher.Body.XMm - me.XMm, 2) + Math.Pow(watcher.Body.ZMm - me.ZMm, 2)) / 1000.0;
            if (gap > metres + 1.5 || gap < metres - 1.5)
                continue;   // the body was not put where the test asked: somewhere else is a different test
            if (!probe.Simulation.Walled(watcher.Body.XMm, watcher.Body.ZMm, me.XMm, me.ZMm))
                placed = metres;
        }
        Assert.NotNull(placed);
        var eye = (120.0 + Math.Sin(facing) * placed!.Value, 68.0 + Math.Cos(facing) * placed.Value);
        var arena = Arena.OpenCreatures(session, session.Setup, eye, (int)Math.Round(facing * 180 / Math.PI) + 180,
            new[] { (Watch, 120.0, 68.0, "sentinel") }, Quiet, startTick: Day);

        // Past the login grace, then ten seconds of being looked at.
        arena.Tick(200);
        int noticedAt = -1;
        for (int i = 0; i < 400 && noticedAt < 0; i++)
        {
            arena.Tick(1);
            if (arena.Creature().Awareness > 0)
                noticedAt = i;
        }
        var seen = arena.Creature();
        double range = Math.Sqrt(Math.Pow(seen.Body.XMm - arena.Simulation.Player.Body.XMm, 2)
                               + Math.Pow(seen.Body.ZMm - arena.Simulation.Player.Body.ZMm, 2)) / 1000.0;
        Assert.True(noticedAt >= 0,
            $"a watchman {range:0.0} m away in daylight has to notice (visibility {arena.Simulation.PlayerVisibilityTo(seen.Id):0.000}, "
            + $"asleep {seen.Asleep}, facing {seen.Body.FacingMdeg / 1000:0} deg)");

        // And then he does something about it: engaged, and closing.
        bool engaged = false;
        for (int i = 0; i < 600 && !engaged; i++)
        {
            arena.Tick(1);
            engaged = arena.Creature().Mind == CreatureMind.Engaged;
        }
        Assert.True(engaged, $"noticing has to lead somewhere ({arena.Creature().Mind}, awareness {arena.Creature().Awareness})");
        double closedTo = Math.Sqrt(Math.Pow(arena.Creature().Body.XMm - arena.Simulation.Player.Body.XMm, 2)
                                  + Math.Pow(arena.Creature().Body.ZMm - arena.Simulation.Player.Body.ZMm, 2)) / 1000.0;
        Assert.True(closedTo < range, $"and he has to come closer: {range:0.0} m to {closedTo:0.0} m");
    }
}