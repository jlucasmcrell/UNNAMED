using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;
namespace UNNAMED.Application.Tests;

public class C1CombatTableTests
{
    [Fact]
    public void AllCreatureKitsHaveThreeMoves_AndTheHeavyHasAReadableCommittedTell()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        foreach (var creature in session.Setup.Combat.Creatures.Values)
        {
            Assert.True(creature.Moves.Length >= 3, creature.Id);
            Assert.True(creature.Moves.Select(a => a.Source).Distinct().Count() >= 3, creature.Id);
            Assert.Contains(creature.Moves, a => a.WindupTicks >= 16 && a.CommitFacing);
        }
    }

    [Fact]
    public void GenerateC1TradingBlowsTable()
    {
        string? output = Environment.GetEnvironmentVariable("OTHERREACH_C1_TABLE");
        if (output is null) return;
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var csv = new StreamWriter(output) { AutoFlush = true };
        csv.WriteLine("people,player_level,creature,enemy_level,seed,outcome,seconds,damage_taken,hit_damage,effect_damage,enemy_health_left,player_health_left");
        foreach (string people in session.Characters.Peoples.Keys)
        for (int level = 1; level <= 5; level++)
        foreach (var creature in session.Setup.Combat.Creatures.Values)
        for (int enemyLevel = 1; enemyLevel <= 6; enemyLevel++)
            Fight(people, level, creature, enemyLevel, false);
        foreach (string people in session.Characters.Peoples.Keys)
        for (int level = 1; level <= 5; level++)
            Fight(people, level, session.Setup.Combat.Creatures[Arena.Wolf], 3, true);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);


        void Fight(string people, int level, CreatureDefinition creature, int enemyLevel, bool pack)
        {
            var creatures = session.Setup.Combat.Creatures.SetItem(creature.Id, creature with { Level = enemyLevel });
            var setup = session.Setup with { Combat = session.Setup.Combat with { Creatures = creatures }, NpcLife = NpcLifeSetup.Empty, Adventurers=null };
            var positions = pack
                ? new[] { (Arena.Wolf, 120.0, 61.8, "pack_hunter"), (Arena.Wolf, 121.8, 60.0, "pack_hunter"), ("creature.beast.wolf_grey_alpha", 118.2, 60.0, "den_alpha") }
                : new[] { (creature.Id, 120.0, 61.8, "hunter") };
            // One deterministic world per full matrix cell; no dodge, guard, healing, companion, abilities or backstab.
            const ulong seed = 713;
            var arena = Arena.OpenCreatures(session, setup, (120, 60), 0, positions, fresh =>
            {
                var spec = Harness.Named("C1 wanderer") with { PeopleId = people };
                var identity = fresh.Identity with { PeopleId = people };
                var progression = setup.Character.RaceGrants(spec).Aggregate(fresh.Progression,
                    (state, grant) => ProgressionEngine.Grant(state, grant, setup.Progression)) with
                {
                    Level = level,
                    Allocation = ImmutableSortedDictionary<CharacterAttribute, int>.Empty.Add(CharacterAttribute.Might, level - 1),
                };
                var sword = fresh.Inventory.First(e => e.DefId == "item.weapon.rusted_sword");
                return new PlayerRecord(fresh.Id, fresh.Name, fresh.XMm, fresh.YMm, fresh.ZMm, fresh.AppearanceSeed,
                    fresh.Inventory, progression, fresh.FacingMdeg, fresh.Discoveries,
                    ImmutableSortedDictionary<EquipSlot, UNNAMED.Domain.EntityId>.Empty.Add(EquipSlot.MainHand, sword.ItemId), fresh.Currency, fresh.Effects)
                    { Identity = identity };
            }, seed);
            int damage = 0,effectDamage=0;
            arena.Bus.Subscribe<HitResolved>(hit => { if (hit.Target == arena.Player) damage += hit.Damage; });
            arena.Bus.Subscribe<HealthChanged>(change => { if(change.Target==arena.Player && change.Delta<0)effectDamage-=change.Delta; });
            bool dead = false;
            var kills=arena.Record<CreatureKilled>();
            arena.Bus.Subscribe<PlayerDied>(_ => dead = true);
            int ticks = 0;
            for (; ticks < 1200 && !dead && arena.Simulation.Creatures.Any(c => c.Alive); ticks++)
            {
                var target = arena.Simulation.Creatures.Where(c => c.Alive)
                    .OrderBy(c => Math.Pow(c.Body.XMm-arena.Simulation.Player.Body.XMm,2)+Math.Pow(c.Body.ZMm-arena.Simulation.Player.Body.ZMm,2)).First();
                arena.Face(target);
                if (arena.Simulation.Combat.Phase == CombatPhase.Idle) arena.Submit(new AttackCommand(arena.Player));
                arena.Tick();
            }
            Assert.All(kills,k=>Assert.Equal(arena.Player,k.Killer));
            string outcome = dead ? "enemy_win" : arena.Simulation.Creatures.All(c => !c.Alive) ? "player_win" : "unresolved_60s";
            csv.WriteLine(string.Join(",", people, level, pack ? "wolf_pack_2_plus_alpha" : creature.Id, enemyLevel, seed, outcome,
                (ticks*.05).ToString("0.00", CultureInfo.InvariantCulture), damage+effectDamage,damage,effectDamage, arena.Simulation.Creatures.Sum(c => c.Health), arena.Simulation.Combat.Health));
        }
    }
}
