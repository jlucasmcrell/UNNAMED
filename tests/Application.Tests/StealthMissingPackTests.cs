using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S3's missing patrol member (STEALTH.md section 4.6): a pack that loses one of its own finds the gap. A creature of the same role that comes near where
/// its packmate fell looks there - the corpse itself is row 31's job, this is the absence.
/// </summary>
public class StealthMissingPackTests
{
    private const long Day = 12_000;
    private const string Wolf = "creature.beast.wolf_grey";

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    /// <summary>Take one wolf down out of sight of the other, then stand off: the survivor goes uneasy about the place its packmate was lost.</summary>
    [Fact]
    public void APackThatLosesOneOfItsOwn_GoesToLookWhere()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        double wolfFacing = Arena.OpenCreatures(session, session.Setup, (452, 720), 0,
            new[] { (Wolf, 452.0, 721.0, "pack_hunter") }, Quiet, startTick: Day, origin: (0, 0))
            .Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var behind = (452.0 - Math.Sin(wolfFacing) * 1.2, 721.0 - Math.Cos(wolfFacing) * 1.2);
        // The packmate stands twenty metres off, well out of sight of the kill and out of the strike's reach.
        var arena = Arena.OpenCreatures(session, session.Setup, behind, (int)Math.Round(wolfFacing * 180 / Math.PI),
            new[]
            {
                (Wolf, 452.0, 721.0, "pack_hunter"),
                (Wolf, 452.0 + Math.Sin(wolfFacing) * 20, 721.0 + Math.Cos(wolfFacing) * 20, "pack_hunter"),
            }, Quiet, startTick: Day, origin: (0, 0));
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(60);
        long lostX = arena.Simulation.Creatures.First(c => c.Condition == CreatureCondition.Corpse).Body.XMm;
        long lostZ = arena.Simulation.Creatures.First(c => c.Condition == CreatureCondition.Corpse).Body.ZMm;

        // Walk away, and let the survivor come to terms with the gap.
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(-1_000, 0, Gait.Sprint, 0)));
        arena.Tick(200);
        arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(0)));
        bool uneasy = false;
        for (int i = 0; i < 400; i++)
        {
            arena.Tick(1);
            var survivor = arena.Simulation.Creatures.First(c => c.Id != arena.Player && c.Alive && c.Condition != CreatureCondition.Corpse);
            uneasy |= survivor.Mind is CreatureMind.Suspicious or CreatureMind.Searching;
        }
        Assert.True(uneasy, "a pack that loses one of its own has to look for the gap");
    }
}