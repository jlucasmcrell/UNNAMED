using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The owner's ruling on bodies (2026-10-31): in the open world a body fades after ten minutes; at a guarded site - a dungeon, a camp, patrolled
/// ground - it lies until the site restocks. So a guard who comes by long after still finds it, and the restock is what clears it.
/// </summary>
public class StealthBodiesPersistTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";
    private const long Day = 12_000;
    private const double WolfX = 470, WolfZ = 745;
    private const string WolfSite = "spawn.test.persist_wolf";

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    private static SpawnSite Site(string key, string defId, string role, double x, double z, bool persist, long respawn = 0) =>
        new(key, (long)(x * 1000), (long)(z * 1000), 0, ImmutableArray.Create(new SpawnMember(defId, role))) { BodiesPersist = persist, RespawnTicks = respawn };

    /// <summary>A wolf killed from behind on the road, then eleven minutes left alone: what lies there is the answer.</summary>
    private static (Arena Arena, SimulationSetup Setup, TempProfile Profile, GameSession Session) KilledAndLeft(bool persist, long respawn)
    {
        var profile = new TempProfile();
        var session = Harness.Boot(profile);
        double wolfFacing = Arena.OpenCreatures(session, session.Setup, (WolfX, WolfZ - 15), 0, new[] { (Arena.Wolf, WolfX, WolfZ, "pack_hunter") },
            Quiet, startTick: Day, origin: (0, 0)).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var setup = session.Setup with
        {
            Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(Site(WolfSite, Arena.Wolf, "pack_hunter", WolfX, WolfZ, persist, respawn)) },
        };
        var arena = Arena.OpenCreatures(session, setup, (WolfX - Math.Sin(wolfFacing) * 1.2, WolfZ - Math.Cos(wolfFacing) * 1.2),
            (int)Math.Round(wolfFacing * 180 / Math.PI), Array.Empty<(string, double, double, string)>(), Quiet, startTick: Day, keepSpawns: true,
            origin: (0, 0));
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(60);
        Assert.Equal(CreatureCondition.Corpse, arena.Simulation.Creatures.Single().Condition);
        arena.Tick((int)session.Setup.Combat.CorpseDecayTicks + 1_000);
        return (arena, setup, profile, session);
    }

    [Fact]
    public void InTheOpenWorld_ABodyFadesAfterTenMinutes()
    {
        var (arena, _, profile, _) = KilledAndLeft(persist: false, respawn: 0);
        using (profile)
            Assert.Equal(CreatureCondition.Gone, arena.Simulation.Creatures.Single().Condition);
    }

    [Fact]
    public void AtAGuardedSite_AGuardFindsTheOldBody_AndTheRestockClearsIt()
    {
        var (arena, setup, profile, session) = KilledAndLeft(persist: true, respawn: 30_000);
        using var _ = profile;
        var corpse = arena.Simulation.Creatures.Single();
        Assert.Equal(CreatureCondition.Corpse, corpse.Condition);

        // A guard comes by: the save is loaded into the same site with a watchman standing three metres off, looking at where the body lies.
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("old_body"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        (double X, double Z) post = (0, 0);
        double best = double.MaxValue;
        for (int step = 0; step < 16; step++)
        {
            double around = step * Math.PI / 8;
            var at = (X: corpse.Body.XMm / 1000.0 + Math.Sin(around) * 3, Z: corpse.Body.ZMm / 1000.0 + Math.Cos(around) * 3);
            double facing = Arena.OpenCreatures(session, session.Setup, (WolfX, WolfZ - 40), 0, new[] { (Watch, at.X, at.Z, "sentinel") },
                Quiet, startTick: Day, origin: (0, 0)).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
            double off = Math.Abs(Math.IEEERemainder(facing - Math.Atan2(corpse.Body.XMm / 1000.0 - at.X, corpse.Body.ZMm / 1000.0 - at.Z), 2 * Math.PI));
            if (off < best)
                (best, post) = (off, at);
        }
        var guarded = setup with
        {
            Combat = setup.Combat with { Spawns = setup.Combat.Spawns.Add(Site("spawn.test.persist_watch", Watch, "sentinel", post.X, post.Z, persist: true)) },
        };
        var resumed = Arena.Resume(guarded, store.Load(SaveSlots.Manual("old_body"), new LoadContext(session.Generator, session.Content, new Registry())));
        Assert.Equal(CreatureCondition.Corpse, resumed.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf).Condition);

        bool found = false;
        for (int i = 0; i < 200 && !found; i++)
        {
            resumed.Tick(1);
            var watch = resumed.Simulation.Creatures.Single(c => c.DefId == Watch);
            found = watch.LastNotice == NoticeKind.Sign && watch.Awareness >= session.Setup.Combat.Awareness.Suspicious;
        }
        Assert.True(found, "a guard at a guarded site finds a body eleven minutes old");

        // The site restocks once the character is gone from it: the body is cleared and the wolf is back.
        resumed.Simulation.Advance(31_000);
        var back = resumed.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf);
        Assert.Equal(CreatureCondition.Alive, back.Condition);
    }
}
