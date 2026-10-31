using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-06 in the running world: a shield raised in the off hand turns a blow at the front, costs stamina with the blow's weight, is driven
/// aside by a blow heavy enough for it, and stops an arrow at the front - all on the one guard path (CMB-24's stance, the same resolver).
/// </summary>
public class ShieldBlockTests
{
    private const string Shield = "item.armor.timber_shield";
    private const string Bow = "item.weapon.hunting_bow";
    private const string Arrows = "item.ammo.arrow_rough";
    private const string Raider = "creature.humanoid.back_ledger_raider";
    private const string Brute = "creature.grazer.barrowback";
    private const string Wolf = Arena.Wolf;

    private static readonly (double X, double Z) Open = (120, 60);

    /// <summary>Carrying the shield by the handful, as a shopper would (the pattern the combat tests use).</summary>
    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] items) =>
        r.WithInventory(r.Inventory.Concat(items));

    private static EntityId Entry(Simulation simulation, string defId) =>
        simulation.Player.Inventory.Single(e => e.DefId == defId).ItemId;

    [Fact]
    public void ARAisedShield_TurnsABlowAtTheFront_AndCostsStaminaWithItsWeight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var shield = Arena.Stack(Shield, 1);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Wolf, Open.X, Open.Z - 2.0, "pack_hunter") },
            r => Carrying(r, shield));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Entry(arena.Simulation, Shield))));
        var hits = arena.Record<HitResolved>();
        arena.Face(arena.Creature());
        // A swing of the character's own brings the wolf on; a blow lowers the guard, so the shield goes up after the swing.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(40);
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        Assert.True(arena.Simulation.Combat.Blocking);
        int seen = hits.Count(h => h.Target == arena.Player);
        int before = arena.Simulation.Combat.Stamina;
        for (int i = 0; i < 300 && hits.Count(h => h.Target == arena.Player) == seen; i++)
            arena.Tick();

        var blow = hits.Where(h => h.Target == arena.Player).Last();
        Assert.True(blow.Blocked, "the shield was up and the blow came from the front");
        int after = arena.Simulation.Combat.Stamina;
        // What holding it up cost: the shield's price (half the guard's, scaled by the blow), never the whole plain price on one blow.
        Assert.InRange(before - after, 1, 30);
        Assert.True(arena.Simulation.Combat.Blocking, "a blow it can take does not lower the shield");
    }

    [Fact]
    public void ABlowHeavyEnoughForTheShield_DrivesItAside_AndStaggers()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var shield = Arena.Stack(Shield, 1);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Brute, Open.X, Open.Z - 2.0, "pack_hunter") },
            r => Carrying(r, shield));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Entry(arena.Simulation, Shield))));
        var broken = arena.Record<GuardBroken>();
        var hits = arena.Record<HitResolved>();
        arena.Face(arena.Creature());
        // The barrowback's blow is heavier than a timber shield can take: the guard goes, and the body behind it is staggered. The guard goes
        // up after the character's own swing, which would otherwise lower it.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(40);
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        for (int i = 0; i < 400 && broken.Count == 0; i++)
        {
            arena.Tick();
            if (!arena.Simulation.Combat.Blocking && broken.Count == 0 && hits.Any(h => h.Target == arena.Player))
                Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        }

        Assert.NotEmpty(broken);
        Assert.False(arena.Simulation.Combat.Blocking, "a driven-aside shield is down");
        Assert.Contains(hits, h => h.Target == arena.Player && !h.Blocked);
    }

    [Fact]
    public void AnArrowAtTheFront_IsStopped_ByARaisedShield()
    {
        using var profile = new TempProfile();
        // The Sedge Levels, where the raiders are: the same ground the raider's own engagement test stands on.
        var session = Harness.Boot(profile, "region.sedge_levels");
        var bow = Arena.Stack(Bow, 1);
        var arrows = Arena.Stack(Arrows, 10);
        var arena = Arena.OpenCreatures(session, session.Setup, (5135, 1267.5), 180,
            new[] { (Raider, 5135.0, 1265.0, "sentinel") }, r => Carrying(r, bow, arrows), origin: Harness.Region);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Entry(arena.Simulation, Bow))));
        var hits = arena.Record<HitResolved>();
        var raider = arena.Creature();
        for (int i = 0; i < 200 && !arena.Creature().Guarding; i++)
            arena.Tick();
        Assert.True(arena.Creature().Guarding, "the raider has its board up");

        int seen = hits.Count(h => h.Target == raider.Id);
        for (int shot = 0; shot < 6 && hits.Count(h => h.Target == raider.Id) == seen; shot++)
        {
            arena.Face(arena.Creature());
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            arena.Tick(60);
        }
        var arrow = hits.Where(h => h.Target == raider.Id).FirstOrDefault();
        Assert.NotNull(arrow);
        Assert.True(arrow.Blocked, "an arrow at a raised shield's front is stopped");
    }

    [Fact]
    public void WithoutAShield_TheGuardStillTurnsABlow_OnTheSamePath()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Wolf, Open.X, Open.Z - 2.0, "pack_hunter") });
        var hits = arena.Record<HitResolved>();
        arena.Face(arena.Creature());
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(40);
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        int seen = hits.Count(h => h.Target == arena.Player);
        for (int i = 0; i < 300 && hits.Count(h => h.Target == arena.Player) == seen; i++)
            arena.Tick();
        Assert.True(hits.Where(h => h.Target == arena.Player).Last().Blocked, "a weapon's raised guard turns a frontal blow");
    }

    [Fact]
    public void TheTimberShield_IsASHieldInContent_WithItsOwnNumbers()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var item = session.Setup.Items.Catalog.Find(Shield);
        Assert.NotNull(item);
        var shield = Assert.IsType<ShieldProfile>(item.Shield);
        Assert.Equal((150_000L, 85, 60, 26), (shield.ArcMdeg, shield.MitigationPercent, shield.StaminaPercent, shield.BreakPower));
        // Every other piece of armor is not a shield, and the block table's weight reference is content.
        Assert.Null(session.Setup.Items.Catalog.Find("item.armor.padded_jack")?.Shield);
        Assert.Equal(10, session.Setup.Combat.Constants.BlockWeightReference);
        // A creature's guard is its weapon's: a board on every raider turned a level-appropriate fight into a grind, so the shield is the
        // character's, and the guard a creature answers an arrow with is the same one path.
        Assert.Null(session.Setup.Combat.Creatures[Raider].Shield);
    }
}
