using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S4's first piece (STEALTH.md section 5.2): a choke is not a blade. A body taken from behind without a wound sleeps where it stands; a blade
/// in the same hands leaves a corpse. The player chooses which, and the difference is what a route is made of.
/// </summary>
public class StealthSubdueTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";
    private const long Night = 40_000;

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    /// <summary>The choke: same reach and same unaware target as the blade, and the body is left breathing. Nothing dies and nothing shouts.</summary>
    [Fact]
    public void AChokeLeavesTheBodyAsleep_NotDead_AndNotAlarmed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (452, 720), 180,
            new[] { (Watch, 452.0, 719.0, "sentinel") }, Quiet, startTick: Night, origin: (0, 0));
        arena.Tick(10);
        var subdued = arena.Record<Subdued>();
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player, NonLethal: true)));
        var one = Assert.Single(subdued);
        Assert.Equal(Watch, one.DefId);
        arena.Tick(5);
        var body = arena.Creature();
        Assert.True(body.Alive, "a choke leaves a living body behind");
        Assert.False(body.Hostile, "and one that is not in a fight with anybody");
        Assert.True(body.Asleep, "put down, and asleep where it stood");
        Assert.Equal(CreatureCondition.Alive, body.Condition);
    }

    /// <summary>And a blade is still a blade: the same takedown, lethally, leaves a corpse instead.</summary>
    [Fact]
    public void TheBladeStillLeavesACorpse()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (452, 720), 180,
            new[] { (Arena.Wolf, 452.0, 719.0, "pack_hunter") }, Quiet, startTick: Night, origin: (0, 0));
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(5);
        Assert.Equal(CreatureCondition.Corpse, arena.Creature().Condition);
    }
}