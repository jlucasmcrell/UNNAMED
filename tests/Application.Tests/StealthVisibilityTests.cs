using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S1 through the world (STEALTH.md §2.1-§2.3): the light a body stands in decides how far it is seen, the authored light of the world is
/// what a body stands in, and the same body in the same place is read differently at night and by day.
/// </summary>
public class StealthVisibilityTests
{
    /// <summary>
    /// The night hours in ticks. One game day is 57,600 ticks and the clock starts at 07:30, so night (19:45 to 04:15) is ticks 29,400 to 49,800
    /// - this is the middle of it. Both starts are past the 10 s login grace, which blinds every creature at tick zero.
    /// </summary>
    private const long DeepNight = 40_000;

    private const long LateMorning = 12_000;

        private static readonly (double X, double Z) Open = (120, 60);

    /// <summary>A human watchman: 18 m of sight and no night sense, which is the body a stealth player actually has to get past.</summary>
    private const string Watchman = "creature.humanoid.back_ledger_raider";

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        // The usual body with nothing shiny and no torch: a plain night walker.
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var item = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, r.Currency, r.Effects);
    };

    /// <summary>
    /// Stand still, in the wolf's own line of sight, at this distance, and say whether it ever noticed anything. The wolf is placed where the
    /// test wants it and the character stands along the facing its placement rolled, so the only variable is the light.
    /// </summary>
    private static bool Noticed(GameSession session, long startTick, double metres)
    {
        double facing = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Watchman, Open.X, Open.Z, "sentinel") },
            Quiet, startTick: startTick).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var eye = (X: Open.X + Math.Sin(facing) * metres, Z: Open.Z + Math.Cos(facing) * metres);
        var arena = Arena.OpenCreatures(session, session.Setup, eye, (int)Math.Round(facing * 180 / Math.PI) + 180,
            new[] { (Watchman, Open.X, Open.Z, "sentinel") }, Quiet, startTick: startTick);
        arena.Tick(40);
        var wolf = arena.Creature();
        return wolf.Awareness > 0 || wolf.Mind != CreatureMind.Unaware;
    }

    /// <summary>
    /// STEALTH §2.1, the acceptance the doc asks for: a body in the dark at twelve metres is not seen where the same body in daylight is. The
    /// wolf's authored sight is 25 m, so by day it reads a body at twelve; at night that sight is 8.75 m and it does not. The factor is asked
    /// for directly, because what is under test is the rule and not where the ground happened to push the body.
    /// </summary>
    [Fact]
    public void TheDarkShortensTheSightLine_AndTheDayDoesNot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        (double Visibility, double SightMetres, CreatureMind Mind) At(long startTick)
        {
            var facing = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Watchman, Open.X, Open.Z, "sentinel") },
                Quiet, startTick: startTick).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
            var eye = (X: Open.X + Math.Sin(facing) * 12, Z: Open.Z + Math.Cos(facing) * 12);
            var arena = Arena.OpenCreatures(session, session.Setup, eye, (int)Math.Round(facing * 180 / Math.PI) + 180,
                new[] { (Watchman, Open.X, Open.Z, "sentinel") }, Quiet, startTick: startTick);
            arena.Tick(1);
            var wolf = arena.Creature();
            double visibility = arena.Simulation.PlayerVisibilityTo(wolf.Id);
            double sight = session.Setup.Combat.Creatures[Watchman].Senses.SightMm * visibility / 1000.0;
            arena.Tick(40);
            return (visibility, sight, arena.Creature().Mind);
        }

        var day = At(LateMorning);
        var night = At(DeepNight);
        // The same body, the same place, the same wolf: only the light it stands in has changed.
        Assert.True(day.Visibility > night.Visibility * 2,
            $"by day {day.Visibility:0.00} against {night.Visibility:0.00} at night");
        Assert.True(day.SightMetres > 12, $"by day a 25 m wolf reads twelve metres: {day.SightMetres:0.0} m");
        Assert.True(night.SightMetres < 12, $"in the dark the same wolf reads {night.SightMetres:0.0} m");
        // And it is still a floor, not a cloak: 8.75 m is a long way from nothing.
        Assert.True(night.SightMetres > 3, "the dark shortens the sight line; it never closes it");
        Assert.NotEqual(CreatureMind.Engaged, night.Mind);
    }

    /// <summary>
    /// STEALTH §2.1's floor, kept: the dark is not a cloak. The same night, the same wolf, at three metres - inside the 8.75 m the dark leaves
    /// it - and it sees.
    /// </summary>
    [Fact]
    public void EvenInTheDark_ABodyCloseEnoughIsStillSeen()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.True(Noticed(session, startTick: DeepNight, metres: 3), "shadow shortens the distance; it never grants invisibility");
    }

    /// <summary>
    /// STEALTH §2.3's periphery, exactly as the doc words it: out at the edge of the cone a watcher reads <em>movement</em>, so a body standing
    /// still beyond six metres is scenery - and inside six metres it is still a body in front of him, at the periphery's own 0.4. Getting this
    /// wrong in the strict direction made a watchman who had only ever seen a flash of someone stop being drawn to them at three metres.
    /// </summary>
    [Fact]
    public void StillBodiesInThePeriphery_AreSeenClose_AndNotFar()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        double Visibility(double metres)
        {
            // Fifty degrees off its facing: outside the 35-degree central band and inside its 130-degree field of view, which is the band the rule is about.
            var probe = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Watchman, Open.X, Open.Z, "sentinel") },
                Quiet, startTick: LateMorning).Creature();
            double facing = probe.Body.FacingMdeg / 1000.0 * Math.PI / 180;
            double off = 50 * Math.PI / 180;
            var side = (X: Open.X + Math.Sin(facing + off) * metres, Z: Open.Z + Math.Cos(facing + off) * metres);
            var arena = Arena.OpenCreatures(session, session.Setup, side, 0, new[] { (Watchman, Open.X, Open.Z, "sentinel") },
                Quiet, startTick: LateMorning);
            arena.Tick(1);
            return arena.Simulation.PlayerVisibilityTo(arena.Creature().Id);
        }

        double close = Visibility(3);
        double far = Visibility(8);
        Assert.True(close > 0, "three metres off, a body is a body even at the edge of the cone");
        Assert.True(far == 0, $"eight metres off and standing still, it is scenery to the periphery (got {far:0.00})");
        Assert.True(far < close);
    }

    /// <summary>
    /// STEALTH §2.2: the world's own light is real. The character standing at the waystation's yard lamp at night is in better light than one
    /// standing in the dark a street away - measured in the same per mille the sight check reads.
    /// </summary>
    [Fact]
    public void AnAuthoredLightLightsTheBodyStandingInIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var none = Array.Empty<(string DefId, double X, double Z, string Role)>();

        // Region metres, with no arena origin to translate: the authored light sits at [447, 730] in region.starting_frontier.
        var lit = Arena.OpenCreatures(session, session.Setup, (447, 730), 0, none, Quiet, startTick: DeepNight, origin: (0, 0));
        var dark = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, none, Quiet, startTick: DeepNight, origin: (0, 0));

        int atLamp = lit.Simulation.PlayerLitPermille;
        int away = dark.Simulation.PlayerLitPermille;
        Assert.True(atLamp > away, $"the yard lamp must light what stands in it ({atLamp} at the lamp, {away} away from it)");
        Assert.Equal(LightBand.Dark, dark.Simulation.PlayerLightBand);
        Assert.True(lit.Simulation.PlayerLightBand > LightBand.Dark, "standing at the lamp is not the dark");
        // And the band is the one the sight check uses, not a second number.
        Assert.Equal(lit.Simulation.Setup.Combat.Light.Band(atLamp), lit.Simulation.PlayerLightBand);
    }

    /// <summary>
    /// STEALTH §2.2, the Thief rule: carrying a light is the loudest thing a stealth player can do. A held torch makes the body it is carried by
    /// a lit thing, so the same dark street is no longer dark.
    /// </summary>
    [Fact]
    public void ACarriedTorchLightsTheBodyCarryingIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var none = Array.Empty<(string DefId, double X, double Z, string Role)>();

        var unlit = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, none, Quiet, startTick: DeepNight, origin: (0, 0));
        var carrying = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, none, r =>
        {
            var inventory = r.Inventory.Add(Arena.Stack("item.tool.torch", 1));
            var torch = inventory.First(e => e.DefId == "item.tool.torch").ItemId;
            return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
                r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.OffHand, torch) }, r.Currency, r.Effects);
        }, startTick: DeepNight, origin: (0, 0));

        Assert.True(carrying.Simulation.PlayerLitPermille > unlit.Simulation.PlayerLitPermille,
            "a torch in the off hand is a light standing where the body is");
        Assert.True(carrying.Simulation.PlayerLightBand > unlit.Simulation.PlayerLightBand);
    }
}
