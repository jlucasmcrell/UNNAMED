using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-16 in the world (COMBAT:368-381, OD-C17(f)): a shove that puts a body on the floor, a guard that stops it, a trip refused while the
/// body it would floor is still getting up, a disarm that leaves the weapon on the ground for the existing pick-up path, and prone surviving a
/// save. The rules themselves are Domain.Tests' ShoveTripDisarmTests; these are the same rules reached the way a player reaches them, on the
/// boar-charge precedent's arena.
/// </summary>
public class CreatureControlTests
{
    private const string Boar = "creature.beast.bristleback_boar";
    private const string Sword = "item.weapon.rusted_sword";

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Where a creature placed at a spot faces when the world starts: the placement roll is keyed, so it faces the same way again
    /// (CreatureTests' own probe). A creature that could not see the character would only ever stand its post.
    /// </summary>
    private static double CreatureFacing(GameSession session, (double X, double Z) at, string defId, string role) =>
        Arena.OpenCreatures(session, session.Setup, (at.X, at.Z - 60), 0, new[] { (defId, at.X, at.Z, role) })
            .Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;

    /// <summary>
    /// The character where a test wants them with a weapon in hand, and one creature a <paramref name="gap"/> metres away, standing in front of
    /// its eyes so it engages without being provoked - and facing it, so the character's guard covers the side the creature comes from. Peer
    /// level to what it meets, so a level gap takes nothing away from the rule under test.
    /// </summary>
    private static Arena Facing(GameSession session, (double X, double Z) creature, string defId, string role, string weapon,
        double gap = 1.5, bool guarding = false, bool empty = false)
    {
        int level = session.Setup.Combat.Creatures[defId].Level;
        double looking = CreatureFacing(session, creature, defId, role);
        var player = (X: creature.X + Math.Sin(looking) * gap, Z: creature.Z + Math.Cos(looking) * gap);
        int facing = Degrees(looking + Math.PI);
        var arena = Arena.OpenCreatures(session, session.Setup, player, facing,
            new[] { (defId, creature.X, creature.Z, role) }, r =>
            {
                var inventory = r.Inventory.ToList();
                if (inventory.All(e => e.DefId != weapon))
                    inventory.Add(Arena.Stack(weapon, 1));
                var item = inventory.Single(e => e.DefId == weapon).ItemId;
                return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory,
                    r.Progression with
                    {
                        Level = level,
                        Pools = empty ? r.Progression.Pools with { Stamina = 0 } : r.Progression.Pools,
                    },
                    r.FacingMdeg, r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, r.Currency, r.Effects);
            });
        arena.TurnTo(facing);
        arena.Tick();
        if (guarding)
            Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        return arena;
    }

    /// <summary>Millidegree-facing degrees wrapped into [0, 360).</summary>
    private static int Degrees(double radians)
    {
        int degrees = (int)Math.Round(radians * 180 / Math.PI) % 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static CreatureView Only(Arena arena) => arena.Simulation.Creatures.Single();

    /// <summary>Make a control move and return the outcome it published; a command the simulation turns down fails with its own reason.</summary>
    private static ControlResolved Move(Arena arena, ControlMove move) => Wait(arena, arena.Record<ControlResolved>(),
        () => Assert.Null(arena.Submit(new ControlCommand(arena.Player, move))));

    private static ControlResolved Wait(Arena arena, List<ControlResolved> outcomes, Action submit)
    {
        submit();
        for (int i = 0; i < 60 && outcomes.Count == 0; i++)
            arena.Tick();
        return Assert.Single(outcomes);
    }

    // ── the shove ───────────────────────────────────────────────────────────

    [Fact]
    public void APlayerShove_KnocksAnUnguardedBoarOffItsFeet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Facing(session, (120, 60), Boar, "sentinel", Sword);
        Assert.False(Only(arena).Prone);

        var outcome = Move(arena, ControlMove.Shove);
        Assert.Equal(ControlMove.Shove, outcome.Move);
        Assert.True(outcome.Landed);
        Assert.True(outcome.Prone);
        Assert.Equal(ControlRules.ShoveStamina, outcome.StaminaSpent);
        // Written to the body that owns its footing, and the body that is on the floor is the point of the move.
        Assert.True(Only(arena).Prone);
        // Rising is automatic: nothing is pressed, and a second later it is up.
        arena.Tick(ControlRules.ProneRiseTicks);
        Assert.False(Only(arena).Prone);
    }

    [Fact]
    public void AShoveAtNothing_CostsItsStaminaAndLeavesEveryoneStanding()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, Array.Empty<(string, double, double, string)>());
        int before = arena.Simulation.Combat.Stamina;

        var outcome = Move(arena, ControlMove.Shove);
        Assert.False(outcome.Landed);
        Assert.False(outcome.Prone);
        // Nothing in reach is still a move made: the stamina is spent and the reason says why nothing happened.
        Assert.Equal("nothing within reach", outcome.Reason);
        Assert.True(arena.Simulation.Combat.Stamina < before);
    }

    [Fact]
    public void AControlMove_IsRefusedWithoutTheStaminaToMakeIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Facing(session, (120, 60), Boar, "sentinel", Sword, empty: true);

        Assert.Equal("too tired to shove", arena.Submit(new ControlCommand(arena.Player, ControlMove.Shove)));
        Assert.Equal("too tired to trip", arena.Submit(new ControlCommand(arena.Player, ControlMove.Trip)));
        Assert.Equal("too tired to disarm", arena.Submit(new ControlCommand(arena.Player, ControlMove.Disarm)));
        Assert.False(Only(arena).Prone);
    }

    // ── the guard answers the shove, and only from the front ────────────────

    /// <summary>Run the arena until one event of a kind arrives that answers <paramref name="asked"/>, and return the first of them.</summary>
    private static T Until<T>(Arena arena, Func<T, bool> asked)
    {
        var events = arena.Record<T>();
        for (int i = 0; i < 400 && !events.Any(asked); i++)
            arena.Tick();
        return Assert.Single(events.Where(asked).Take(1));
    }

    [Fact]
    public void AGuardedBodyIsShoved_ButKeepsItsFeet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The character guards, and the wolf comes at them from the front: its bite is turned aside, so it shoves - and a guard holds.
        var arena = Facing(session, (120, 60), Arena.Wolf, "sentinel", Sword, guarding: true);
        Assert.True(arena.Simulation.Combat.Blocking);

        var shove = Until(arena, (ControlResolved e) => e.Actor != arena.Player);
        Assert.Equal(ControlMove.Shove, shove.Move);
        Assert.False(shove.Prone);
        Assert.False(arena.Simulation.Combat.Prone);
    }

    [Fact]
    public void ACharacterTurnedFromTheShover_IsKnockedDown_AndIsStillProneAfterSaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The wolf's first bite is taken on the guard, which is what teaches it to shove (CMB-16's awareness). Turning the guard onto another
        // side then leaves the shove reaching a body that is not defending the side it comes from - and the guard is still up.
        int looking = Degrees(CreatureFacing(session, (120, 60), Arena.Wolf, "sentinel"));
        var arena = Facing(session, (120, 60), Arena.Wolf, "sentinel", Sword, guarding: true);
        Until(arena, (HitResolved h) => h.Target == arena.Player && h.Blocked);
        arena.TurnTo(Degrees(looking + Math.PI / 2));
        arena.Tick();

        var shove = Until(arena, (ControlResolved e) => e.Actor != arena.Player);
        Assert.True(shove.Prone);
        Assert.True(arena.Simulation.Combat.Prone);
        int ticksLeft = arena.Simulation.Combat.ProneTicksLeft;
        Assert.InRange(ticksLeft, 1, ControlRules.ProneRiseTicks);

        // Saved off their feet and loaded: still off their feet, with the rise they had left - never standing by accident.
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("prone"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("prone"), new LoadContext(session.Generator, session.Content, new Registry()));
        var again = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.True(again.Combat.Prone);
        Assert.Equal(ticksLeft, again.Combat.ProneTicksLeft);
        // Nothing else drifted either: the whole persistent state round-trips, footing among it.
        Assert.Equal(arena.Simulation.StateDigest(), again.StateDigest());
    }

    // ── the trip, and the immunity after rising ─────────────────────────────

    [Fact]
    public void ATrip_IsRefusedWhileTheBodyIsStillGettingUp_AndLandsOnceTheImmunityHasRun()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Facing(session, (120, 60), Arena.Wolf, "sentinel", Sword);

        var first = Move(arena, ControlMove.Trip);
        Assert.True(first.Landed);
        Assert.True(Only(arena).Prone);

        // Back out of the trip's own recovery: the wolf has risen but its three seconds of immunity are still running, so the rule refuses
        // the second trip for that reason and the body it would have floored keeps its feet.
        arena.Tick(20);
        Assert.False(Only(arena).Prone);
        var refused = Wait(arena, arena.Record<ControlResolved>(),
            () => Assert.Null(arena.Submit(new ControlCommand(arena.Player, ControlMove.Trip))));
        Assert.Equal(ControlMove.Trip, refused.Move);
        Assert.False(refused.Landed);
        Assert.Equal("still getting up", refused.Reason);
        Assert.False(Only(arena).Prone);

        // Three seconds later the same move lands: the immunity is the whole of the refusal.
        arena.Tick(ControlRules.ProneImmunityTicks);
        var again = Wait(arena, arena.Record<ControlResolved>(),
            () => Assert.Null(arena.Submit(new ControlCommand(arena.Player, ControlMove.Trip))));
        Assert.True(again.Landed);
        Assert.True(Only(arena).Prone);
    }

    // ── the disarm, and the weapon on the ground ────────────────────────────

    [Fact]
    public void ADisarmedWeapon_LiesOnTheGround_AndThePickUpPathCollectsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A bite taken on the guard is what teaches the wolf to reach for something else; with the guard dropped, a sword that outranges its
        // bite is what it reaches for.
        var arena = Facing(session, (120, 60), Arena.Wolf, "sentinel", Sword, guarding: true);
        Assert.True(arena.Simulation.Player.Equipment.ContainsKey(EquipSlot.MainHand));
        Until(arena, (HitResolved h) => h.Target == arena.Player && h.Blocked);
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, false)));
        arena.Tick();

        var disarm = Until(arena, (ControlResolved e) => e.Actor != arena.Player && e.Disarmed);
        Assert.Equal(ControlMove.Disarm, disarm.Move);
        Assert.True(disarm.Disarmed);
        Assert.Contains("weapon", disarm.Awareness);
        // Out of the hand, which falls back on bare hands...
        Assert.Equal("unarmed", arena.Simulation.Combat.Weapon.Source);
        Assert.False(arena.Simulation.Player.Equipment.ContainsKey(EquipSlot.MainHand));
        // ...and lying where it fell, as the same record a drop makes, no longer carried.
        var lying = arena.Simulation.WorldItems.Where(i => i.DefId == Sword).ToList();
        Assert.Single(lying);
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Sword);

        // The existing path collects it: a move from the ground into the pack, with no new command for picking up.
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, lying[0].Id.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
        Assert.Empty(arena.Simulation.WorldItems.Where(i => i.DefId == Sword));
        Assert.True(arena.Simulation.Player.Inventory.Any(e => e.DefId == Sword));
    }
}
