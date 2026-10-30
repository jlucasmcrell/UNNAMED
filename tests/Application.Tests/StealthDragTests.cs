using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S4's drag (STEALTH.md section 6.2): a body comes with the character a metre at a time and stays in the open, which is the slow and exposed way to move
/// one - hiding it in cover is the quick way, and the two are different acts on purpose.
/// </summary>
public class StealthDragTests
{
    private const long Day = 12_000;

    /// <summary>A dagger in the hand and nothing else: the kit a takedown needs.</summary>
    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    /// <summary>A blade from behind in the first moments leaves a corpse at the character's feet; pulling it moves it a step, toward them.</summary>
    [Fact]
    public void ABodyIsDraggedAStepAtATime_TowardTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The recipe the body-discovery test proved: read the wolf's own facing, then stand along it and take it from behind.
        double wolfFacing = Arena.OpenCreatures(session, session.Setup, (452, 720), 0,
            new[] { (Arena.Wolf, 452.0, 721.0, "pack_hunter") }, Quiet, startTick: Day, origin: (0, 0))
            .Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var behind = (452.0 - Math.Sin(wolfFacing) * 1.2, 721.0 - Math.Cos(wolfFacing) * 1.2);
        var arena = Arena.OpenCreatures(session, session.Setup, behind, (int)Math.Round(wolfFacing * 180 / Math.PI),
            new[] { (Arena.Wolf, 452.0, 721.0, "pack_hunter") }, Quiet, startTick: Day, origin: (0, 0));
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(60);
        var corpse = arena.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf);
        Assert.True(corpse.Condition == CreatureCondition.Corpse, $"the takedown has to leave a body: {corpse.Condition}");
        long beforeX = corpse.Body.XMm, beforeZ = corpse.Body.ZMm;
        long gap = (long)Math.Sqrt(Math.Pow(corpse.Body.XMm - arena.Simulation.Player.Body.XMm, 2)
            + Math.Pow(corpse.Body.ZMm - arena.Simulation.Player.Body.ZMm, 2));

        var dragged = arena.Record<BodyDragged>();
        Assert.Null(arena.Submit(new DragBodyCommand(arena.Player, corpse.Id)));
        arena.Tick(2);

        var after = arena.Simulation.Creatures.Single(c => c.Id == corpse.Id);
        double moved = Math.Sqrt(Math.Pow(after.Body.XMm - beforeX, 2) + Math.Pow(after.Body.ZMm - beforeZ, 2));
        Assert.Equal(1, dragged.Count);
        Assert.InRange(moved, 900, 1_100);
        long now = (long)Math.Sqrt(Math.Pow(after.Body.XMm - arena.Simulation.Player.Body.XMm, 2)
            + Math.Pow(after.Body.ZMm - arena.Simulation.Player.Body.ZMm, 2));
        Assert.True(now < gap, $"the body comes toward the character ({gap} mm to {now} mm)");
        Assert.True(after.Condition == CreatureCondition.Corpse, "it is still a body, not a carried object");
    }
}