using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Spill and the Stopped Gate hold their band (docs/FIELD_RUINS.md; FIELD_RUINS_PROPOSAL.md 3.3): the outer field is level 4,
/// the court level 5, and each fight is measured in the real simulation over the game's own content, the way the M3c table is. A
/// gate tender dies in the 4-8 s band to a level-4 sword; a pair of them kills a level-3 character who stands still in the 8-15 s
/// band; the Gate Warden is a long fight (30-60 s) that a level-5 character wins by staying behind it; and the nameplate reads the
/// band as designed for a character arriving at level 3, 4 or 5.
/// </summary>
public class FieldRuinsBandTests(ITestOutputHelper output)
{
    private const string Tender = "creature.construct.gate_tender";
    private const string Warden = "creature.construct.gate_warden";
    private const int Worlds = 12;
    private const double TickSeconds = 0.05;
    private static readonly (double X, double Z) Ground = (120, 60);

    [Fact]
    public void TheSpill_IsLevelFour_AndTheCourt_LevelFive_InEverySpawn()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;
        var creatures = setup.Combat.Creatures;
        var field = setup.Combat.Spawns.Where(s => s.Key.StartsWith("spawn.field.", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, field.Count);
        Assert.All(field.Where(s => s.Key.Contains("tenders", StringComparison.Ordinal)).SelectMany(s => s.Members),
            m => Assert.Equal(4, creatures[m.CreatureId].Level));
        Assert.All(field.Where(s => !s.Key.Contains("tenders", StringComparison.Ordinal)).SelectMany(s => s.Members),
            m => Assert.Equal(5, creatures[m.CreatureId].Level));
    }

    [Theory]
    [InlineData(3, Threat.Even, Threat.Hard)]
    [InlineData(4, Threat.Even, Threat.Even)]
    [InlineData(5, Threat.Even, Threat.Even)]
    public void TheNameplate_ReadsTheBand(int playerLevel, Threat tender, Threat warden)
    {
        using var profile = new TempProfile();
        var creatures = Harness.Boot(profile).Setup.Combat.Creatures;
        Assert.Equal(tender, ThreatRules.Of(creatures[Tender].Level, playerLevel));
        Assert.Equal(warden, ThreatRules.Of(creatures[Warden].Level, playerLevel));
    }

    [Fact]
    public void AGateTender_DiesInBand_ToALevelFourSword()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var seconds = new List<double>();
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0, new[] { (Tender, Ground.X, Ground.Z + 2.4, "sentinel") },
                r => Character(r, 4), seed);
            var started = arena.Record<AttackStarted>();
            Fight(arena, behind: false, maxTicks: 1_200);
            Assert.False(arena.Creature().Alive, $"world {seed}: the tender stood after 60 s");
            seconds.Add((arena.Simulation.WorldTick - started.First(s => s.Attacker == arena.Player).Tick) * TickSeconds);
        }
        double median = Median(seconds);
        output.WriteLine($"gate tender, level-4 sword: median {median:0.00} s ({seconds.Min():0.00}-{seconds.Max():0.00})");
        Assert.InRange(median, 4, 8);
    }

    [Fact]
    public void APairOfTenders_KillsALevelThreeWhoStandsStill_InBand()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var seconds = new List<double>();
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0,
                new[] { (Tender, Ground.X, Ground.Z + 2.4, "sentinel"), (Tender, Ground.X, Ground.Z - 2.4, "sentinel") }, r => Character(r, 3), seed);
            var hits = arena.Record<HitResolved>();
            var died = arena.Record<PlayerDied>();
            foreach (int facing in new[] { 0, 180 })
            {
                arena.TurnTo(facing);
                arena.Tick();
                arena.Submit(new AttackCommand(arena.Player));
                arena.Tick(14);
            }
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(0)));
            for (int i = 0; i < 2_400 && died.Count == 0; i++)
                arena.Tick();
            var death = Assert.Single(died);
            seconds.Add((death.Tick - hits.First(h => h.Target == arena.Player && h.Damage > 0).Tick) * TickSeconds);
        }
        double median = Median(seconds);
        output.WriteLine($"two tenders on a still level-3 character: median {median:0.00} s ({seconds.Min():0.00}-{seconds.Max():0.00})");
        Assert.InRange(median, 8, 15);
    }

    [Fact]
    public void TheGateWarden_IsALongFight_ThatALevelFiveWinsFromBehind()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var seconds = new List<double>();
        var taken = new List<int>();
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0, new[] { (Warden, Ground.X, Ground.Z + 4.5, "sentinel") },
                r => Character(r, 5), seed);
            var started = arena.Record<AttackStarted>();
            var hits = arena.Record<HitResolved>();
            Fight(arena, behind: true, maxTicks: 2_400);
            Assert.False(arena.Creature().Alive, $"world {seed}: the warden stood after 120 s");
            Assert.True(arena.Simulation.Combat.Health > 0, $"world {seed}: the level-5 character died fighting from behind");
            seconds.Add((arena.Simulation.WorldTick - started.First(s => s.Attacker == arena.Player).Tick) * TickSeconds);
            taken.Add(hits.Where(h => h.Target == arena.Player).Sum(h => h.Damage));
        }
        double median = Median(seconds);
        output.WriteLine($"gate warden, level-5 sword from behind: median {median:0.00} s ({seconds.Min():0.00}-{seconds.Max():0.00}); " +
                         $"damage taken median {Median(taken.Select(t => (double)t).ToList()):0}");
        Assert.InRange(median, 30, 60);
    }

    /// <summary>The TTK table's character: a level-L body with its L-1 points in Might and the Rusted Sword in hand.</summary>
    private static PlayerRecord Character(PlayerRecord fresh, int level)
    {
        var inventory = fresh.Inventory.ToList();
        var sword = inventory.Single(e => e.DefId == "item.weapon.rusted_sword");
        var progression = fresh.Progression with
        {
            Level = level,
            Allocation = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, level - 1) }),
        };
        return new PlayerRecord(fresh.Id, fresh.Name, fresh.XMm, fresh.YMm, fresh.ZMm, fresh.AppearanceSeed, inventory, progression,
            fresh.FacingMdeg, fresh.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, sword.ItemId) }, fresh.Currency, fresh.Effects);
    }

    /// <summary>
    /// Close to the sword's reach of the creature's own body and swing whenever ready; with <paramref name="behind"/>, keep circling
    /// to the point behind it first, the tactic its lore teaches ("get behind it and stay there").
    /// </summary>
    private static void Fight(Arena arena, bool behind, int maxTicks)
    {
        var creatures = arena.Simulation.Setup.Combat.Creatures;
        for (int i = 0; i < maxTicks; i++)
        {
            var creature = arena.Creature();
            if (!creature.Alive || arena.Simulation.Combat.Health <= 0)
                return;
            var body = arena.Simulation.Player.Body;
            var combat = arena.Simulation.Combat;
            double reach = combat.Weapon.ReachMm + creatures[creature.DefId].RadiusMm + 300;
            double goalX = creature.Body.XMm, goalZ = creature.Body.ZMm;
            if (behind)
            {
                var (fx, fz) = Forward(creature.Body.FacingMdeg);
                goalX -= fx * (reach - 400);
                goalZ -= fz * (reach - 400);
            }
            double dx = creature.Body.XMm - body.XMm, dz = creature.Body.ZMm - body.ZMm;
            double gx = goalX - body.XMm, gz = goalZ - body.ZMm;
            double distance = Math.Sqrt(dx * dx + dz * dz), toGoal = Math.Sqrt(gx * gx + gz * gz);
            int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm);
            bool inReach = distance <= reach;
            bool settled = behind ? toGoal < 700 : inReach;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, settled
                ? MoveIntent.Idle(facing)
                : new MoveIntent((int)Math.Round(gx / toGoal * 1000), (int)Math.Round(gz / toGoal * 1000), Gait.Run, facing)));
            if (inReach && combat.Phase == CombatPhase.Idle)
                arena.Simulation.Enqueue(new AttackCommand(arena.Player));
            arena.Tick();
        }
    }

    /// <summary>The unit vector a facing points along: CombatRules.FacingTowards is atan2(dx, dz).</summary>
    private static (double X, double Z) Forward(int facingMdeg) =>
        (Math.Sin(facingMdeg / 1000.0 * Math.PI / 180), Math.Cos(facingMdeg / 1000.0 * Math.PI / 180));

    private static double Median(List<double> values) => values.OrderBy(v => v).ElementAt(values.Count / 2);
}
