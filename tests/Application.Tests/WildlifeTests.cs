using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The start area's wildlife and great grazers, over the game's own content: peaceful roles never start a fight, the skittish bolt from
/// someone close, a herd turns as one on whoever strikes one of it (DIRECTION_AND_CONTENT_PLAN E4), and roles that keep hours lie up.
/// </summary>
public class WildlifeTests
{
    private const string Doe = "creature.beast.doe";
    private const string Hen = "creature.beast.chicken";
    private const string Barrowback = "creature.grazer.barrowback";

    private static double Apart(Arena arena, CreatureView creature)
    {
        var player = arena.Simulation.Player.Body;
        return Math.Sqrt(Math.Pow(creature.Body.XMm - player.XMm, 2) + Math.Pow(creature.Body.ZMm - player.ZMm, 2)) / 1000;
    }

    [Fact]
    public void ADoe_LetsSomeoneFarOffBe_AndBoltsWhenTheyComeClose()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 40), 0, new[] { (Doe, 120.0, 80.0, "skittish") });

        arena.Tick(60);
        Assert.Equal(CreatureMind.Unaware, arena.Creature().Mind);

        // Walk straight at it, wherever it has drifted to, until it goes.
        var noticed = arena.Record<CreatureNoticed>();
        for (int i = 0; i < 600 && !noticed.Any(n => n.Mind == CreatureMind.Fleeing); i++)
        {
            var body = arena.Simulation.Player.Body;
            var doe = arena.Creature().Body;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(doe.XMm - body.XMm, doe.ZMm - body.ZMm, Gait.Walk)));
            arena.Tick();
        }
        double bolted = Apart(arena, arena.Creature());
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(arena.Simulation.Player.Body.FacingMdeg)));
        arena.Tick(20);
        Assert.Contains(noticed, n => n.Mind == CreatureMind.Fleeing);
        Assert.True(bolted <= 20.5, $"bolted at {bolted:0.0} m");
        Assert.DoesNotContain(noticed, n => n.Mind == CreatureMind.Engaged);
        Assert.True(Apart(arena, arena.Creature()) > bolted + 4, $"the doe did not run: {Apart(arena, arena.Creature()):0.0} m, {arena.Creature().Mind}, noticed {string.Join(",", noticed.Select(n => n.Mind + "@" + n.Tick))}");
    }

    [Fact]
    public void AGrazerHerd_GrazesBesideThePlayer_UntilOneIsStruck_ThenAllTurn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 64), 0,
            new[] { (Barrowback, 120.0, 80.0, "herd"), (Barrowback, 126.0, 84.0, "herd"), (Barrowback, 114.0, 86.0, "herd") }, r =>
            {
                var inventory = r.Inventory.Append(Arena.Stack("item.ammo.arrow_rough", 20)).ToList();
                if (inventory.All(e => e.DefId != "item.weapon.hunting_bow"))
                    inventory.Add(Arena.Stack("item.weapon.hunting_bow", 1));
                var bow = inventory.Single(e => e.DefId == "item.weapon.hunting_bow").ItemId;
                return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
                    new[] { KeyValuePair.Create(EquipSlot.MainHand, bow) }, r.Currency, r.Effects);
            });

        // Sixteen metres off in plain sight, for ten seconds: they graze.
        arena.Tick(200);
        Assert.All(arena.Simulation.Creatures, c => Assert.NotEqual(CreatureMind.Engaged, c.Mind));

        var first = arena.Creature();
        arena.Face(first);
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 40);

        Assert.True(arena.Creature().Health < arena.Creature().MaxHealth, "the arrow did not land");
        Assert.All(arena.Simulation.Creatures, c => Assert.Contains(c.Mind, new[] { CreatureMind.Engaged, CreatureMind.Searching }));
    }

    [Fact]
    public void AHen_RoostsAtNight_AndScratchesAboutByDay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var hours = session.Setup.Combat.Hours!;
        long night = Enumerable.Range(0, 48).Select(h => h * hours.TicksPerDay / 48).First(hours.IsNight);
        long day = Enumerable.Range(0, 48).Select(h => h * hours.TicksPerDay / 48).First(t => !hours.IsNight(t));

        var dark = Arena.OpenCreatures(session, session.Setup, (120, 40), 0, new[] { (Hen, 120.0, 80.0, "yard_fowl") }, startTick: night);
        dark.Tick(5);
        Assert.True(dark.Creature().Asleep);

        var light = Arena.OpenCreatures(session, session.Setup, (120, 40), 0, new[] { (Hen, 120.0, 80.0, "yard_fowl") }, startTick: day);
        light.Tick(5);
        Assert.False(light.Creature().Asleep);
        Assert.True(light.Creature().Peaceful);
    }
}
