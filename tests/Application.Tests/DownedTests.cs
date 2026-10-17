using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP10's Downed (OD-13 = B, SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.10 X8): brought to nothing with a companion or hireling up and
/// near, the character lies downed for 30 s and is helped up; with no hand near - alone, the hand too far, or the hand down too - they
/// die as before. Downed costs nothing: no XP debt, no weakness, no respawn. A save made while downed loads as the death (DN15).
/// </summary>
public class DownedTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    /// <summary>An open stretch east of the smithy, clear of everything (the companion tests' own).</summary>
    private static readonly (double X, double Z) Player = (70, 150);

    private static PlayerRecord Weak(PlayerRecord record) =>
        record.WithProgression(record.Progression with { Pools = record.Progression.Pools with { Health = 3 } });

    private static CompanionRecord TavarAt(double x, double z, CompanionCondition condition = CompanionCondition.Up) =>
        new(Tavar, CompanionOrder.Wait, condition, Harness.InRegion(x, z).XMm, Harness.InRegion(x, z).ZMm, 0,
            condition == CompanionCondition.Up ? 100 : 0);

    /// <summary>The character at 3 health facing a wolf 1.9 m off, and Tavar waiting where he is put (or nowhere).</summary>
    private static Arena Fight(GameSession session, CompanionRecord? tavar) =>
        Arena.OpenCreatures(session, session.Setup, Player, 0, new[] { (Arena.Wolf, Player.X, Player.Z + 1.9, "pack_hunter") },
            r => tavar is null ? Weak(r) : Weak(r).WithCompanions(new[] { tavar }));

    /// <summary>Strike the wolf, then wait until the character is down (downed or dead).</summary>
    private static void BroughtDown(Arena arena)
    {
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 300 && !arena.Simulation.Combat.Defeated; i++)
            arena.Tick();
        Assert.True(arena.Simulation.Combat.Defeated, "the wolf never brought the character down");
    }

    [Fact]
    public void WithTavarNear_TheCharacterIsDowned_ThenHelpedUp_AndOwesNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Fight(session, TavarAt(Player.X, Player.Z - 1.5));
        var downed = arena.Record<PlayerDowned>();
        var revived = arena.Record<PlayerRevived>();
        var died = arena.Record<PlayerDied>();
        var before = arena.Simulation.Player.Progression;
        var constants = session.Setup.Combat.Constants;

        BroughtDown(arena);
        arena.Tick();

        var down = Assert.Single(downed);
        Assert.Equal((Arena.Wolf, Arena.Bite, Tavar), (down.KillerDefId, down.Cause, down.HandNpcId));
        Assert.Equal(down.Tick + constants.DownedTicks, down.RevivedAtTick);
        Assert.Equal(600, constants.DownedTicks);   // config.damage_constants: downed 30 s at 20 Hz
        var combat = arena.Simulation.Combat;
        Assert.True(combat.Downed && combat.Defeated);
        Assert.Equal(0, combat.RespawnTicksLeft);
        Assert.InRange(combat.DownedTicksLeft, 1, constants.DownedTicks);
        Assert.Equal("downed", arena.Submit(new AttackCommand(arena.Player)));
        var lying = arena.Simulation.Player.Body;

        arena.Tick((int)(down.RevivedAtTick - arena.Simulation.WorldTick) - 1);
        Assert.Empty(revived);
        Assert.True(arena.Simulation.Combat.Downed);
        Assert.Equal((lying.XMm, lying.ZMm), (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm));
        arena.Tick();

        var up = Assert.Single(revived);
        Assert.Equal((Tavar, down.RevivedAtTick), (up.NpcId, up.Tick));
        int max = arena.Simulation.Combat.MaxHealth;
        Assert.Equal(Math.Max(1, max * constants.DownedRevivePercent / 100), up.Health);
        Assert.Equal(up.Health, arena.Simulation.Combat.Health);
        Assert.False(arena.Simulation.Combat.Defeated || arena.Simulation.Combat.Downed);
        Assert.Empty(died);
        Assert.Equal(before.XpDebt, arena.Simulation.Player.Progression.XpDebt);
        Assert.DoesNotContain(arena.Simulation.Combat.Effects, e => e.EffectId == constants.DeathEffect);
        Assert.Equal((lying.XMm, lying.ZMm), (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm));   // where they lay
    }

    [Fact]
    public void Alone_TheCharacterDies_AsBefore()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Fight(session, null);
        var downed = arena.Record<PlayerDowned>();
        var died = arena.Record<PlayerDied>();

        BroughtDown(arena);
        arena.Tick();

        Assert.Empty(downed);
        Assert.Single(died);
        Assert.False(arena.Simulation.Combat.Downed);
        Assert.Equal("dead", arena.Submit(new AttackCommand(arena.Player)));
    }

    [Fact]
    public void AHandTooFar_OrDownToo_IsNoHand()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        double beyond = session.Setup.Combat.Constants.DownedHandWithinMm / 1000.0 + 2;
        foreach (var tavar in new[] { TavarAt(Player.X, Player.Z - beyond), TavarAt(Player.X, Player.Z - 1.5, CompanionCondition.Downed) })
        {
            var arena = Fight(session, tavar);
            var downed = arena.Record<PlayerDowned>();
            var died = arena.Record<PlayerDied>();

            BroughtDown(arena);
            arena.Tick();

            Assert.Empty(downed);
            Assert.Single(died);
        }
    }

    [Fact]
    public void ASaveMadeWhileDowned_LoadsAsTheDeath()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var constants = session.Setup.Combat.Constants;
        var arena = Fight(session, TavarAt(Player.X, Player.Z - 1.5));
        BroughtDown(arena);
        arena.Tick(10);
        Assert.True(arena.Simulation.Combat.Downed);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("downed"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("downed"), new LoadContext(session.Generator, session.Content, new Registry())));
        var downed = loaded.Record<PlayerDowned>();
        var died = loaded.Record<PlayerDied>();
        var respawned = loaded.Record<PlayerRespawned>();
        Assert.True(loaded.Simulation.Combat.Defeated && !loaded.Simulation.Combat.Downed);
        Assert.Equal(0, loaded.Simulation.Combat.Health);

        // Tavar is still near, but the death is past help: it is settled as any other, once.
        loaded.Tick();
        Assert.Empty(downed);
        var death = Assert.Single(died);
        Assert.True(death.DebtAdded > 0);
        Assert.Equal(death.DebtAdded, loaded.Simulation.Player.Progression.XpDebt);
        Assert.Contains(loaded.Simulation.Combat.Effects, e => e.EffectId == constants.DeathEffect);

        loaded.Tick((int)(death.RespawnTick - loaded.Simulation.WorldTick));
        var back = Assert.Single(respawned);
        Assert.Equal((back.Body.XMm, back.Body.ZMm), (loaded.Simulation.Player.Body.XMm, loaded.Simulation.Player.Body.ZMm));
        Assert.False(loaded.Simulation.Combat.Defeated);
        Assert.Equal(loaded.Simulation.Combat.MaxHealth, loaded.Simulation.Combat.Health);
        Assert.Single(died);
    }
}
