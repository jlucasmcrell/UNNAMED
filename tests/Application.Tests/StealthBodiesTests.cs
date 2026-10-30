using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S3's bodies (STEALTH.md section 4.5): a body where a body should not be is a sign. A watch that comes upon one of its own kind dead goes alert
/// where it stands, takes the body as the place it knows, and walks to it - and the search that follows is a search of the place the work was done.
/// </summary>
public class StealthBodiesTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";
    /// <summary>Daylight: the watchman the test needs is awake, and the dark would only shorten the sight it has to use.</summary>
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
    /// A dead wolf is a sign to the watchman standing over it: he goes alert at once, takes the body as the place he knows, and goes to it. The
    /// watchman is placed on the body itself - a watch that walks up to a corpse is what this rule is about, and the arena helper cannot yet place a
    /// corpse and a watcher at chosen distances from one another without the rolled facings deciding the outcome.
    /// </summary>
    [Fact]
    public void AWatchmanWhoFindsABody_GoesAlertAndTakesTheBodyAsThePlaceItKnows()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The wolf's own facing decides where the character has to stand to be behind it, and a blade from behind is an execution.
        double wolfFacing = Arena.OpenCreatures(session, session.Setup, (452, 720), 0,
            new[] { (Arena.Wolf, 452.0, 721.0, "pack_hunter") }, Quiet, startTick: Day, origin: (0, 0))
            .Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var behind = (452.0 - Math.Sin(wolfFacing) * 1.2, 721.0 - Math.Cos(wolfFacing) * 1.2);
        var arena = Arena.OpenCreatures(session, session.Setup, behind, (int)Math.Round(wolfFacing * 180 / Math.PI),
            new[]
            {
                (Arena.Wolf, 452.0, 721.0, "pack_hunter"),
                // Out of the blade's reach and out of the player's way, but near enough to hear what happens next.
                (Watch, 452.0 + Math.Sin(wolfFacing) * 3.5, 721.0 + Math.Cos(wolfFacing) * 3.5, "sentinel"),
            }, Quiet, startTick: Day, origin: (0, 0));

        // During the login grace, before the wolf can be watching: this is what a takedown is for.
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(60);
        var corpse = arena.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf);
        Assert.True(corpse.Condition == CreatureCondition.Corpse,
            $"the takedown has to leave a body: {corpse.Condition}, health {corpse.Health} of {corpse.MaxHealth}");
        long corpseX = corpse.Body.XMm, corpseZ = corpse.Body.ZMm;

        // Out of the day's long sight: twenty-five metres off, then still. What is left on that ground is a body and a watchman.
        arena.Tick(40);
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1_000, 0, Gait.Sprint, 0)));
        arena.Tick(220);
        arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(0)));

        bool alert = false;
        double closest = double.MaxValue;
        for (int i = 0; i < 900; i++)
        {
            arena.Tick(1);
            var watcher = arena.Simulation.Creatures.Single(c => c.DefId == Watch);
            alert |= watcher.Awareness >= session.Setup.Combat.Awareness.Suspicious;
            closest = Math.Min(closest, Math.Sqrt(Math.Pow(watcher.Body.XMm - corpseX, 2) + Math.Pow(watcher.Body.ZMm - corpseZ, 2)) / 1000.0);
        }
        Assert.True(alert, "a watchman that has found one of its own dead goes alert");
        Assert.True(closest < 3, $"and he goes to it: the nearest he came to the body was {closest:0.0} m");
    }
}