using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// ROADMAP.md M3c's exit proof: the time-to-kill table is generated from the build - the game's content and the real
/// simulation, fight by fight - never authored by hand. The committed table (docs/M3C_TTK_TABLE.md) must equal what
/// the build produces; regenerate it with UNNAMED_WRITE_TTK=1 after an intended tuning change and review the diff.
/// </summary>
public class TtkTableTests
{
    private const int Worlds = 24;
    private const double TickSeconds = 0.05;
    private static readonly (double X, double Z) Ground = (120, 60);

    /// <summary>VERTICAL_SLICE.md §5.1: 4-8 s to kill a standard enemy at peer level and gear; 8-15 s to die of sustained mistakes.</summary>
    private static readonly (double Low, double High) KillBand = (4, 8);
    private static readonly (double Low, double High) DieBand = (8, 15);

    private sealed record Row(string Label, int Level, IReadOnlyList<double> Seconds, IReadOnlyList<int> Blows)
    {
        public double Median => Percentile(0.5);
        public double Percentile(double p)
        {
            var sorted = Seconds.OrderBy(s => s).ToList();
            return sorted[(int)Math.Round(p * (sorted.Count - 1))];
        }
    }

    [Fact]
    public void TheTtkTable_IsGeneratedFromTheBuild_AndTheWeaponFamiliesKillInBand()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var kills = new List<Row>();
        foreach (var (weapon, label) in new[] { ("item.weapon.rusted_sword", "Rusted Sword"), ("item.weapon.hunting_bow", "Hunting Bow"), ("unarmed", "Unarmed") })
        {
            for (int level = 1; level <= 3; level++)
                kills.Add(TimeToKill(session, session.Setup, weapon, label, level));
        }
        var dies = new List<Row>();
        foreach (bool armored in new[] { false, true })
        {
            foreach (int wolves in new[] { 1, 2, 4 })
                dies.Add(TimeToDie(session, session.Setup, armored, wolves));
        }

        // The over-band check: the same wolf body authored four levels higher with a doubled bite. Its health is
        // unchanged, so it dies as fast; it is more dangerous only because it hits harder.
        var wolf = session.Setup.Combat.Creatures[Arena.Wolf];
        var overBand = wolf with { Level = wolf.Level + 4, Attack = wolf.Attack with { DamageMin = wolf.Attack.DamageMin * 2, DamageMax = wolf.Attack.DamageMax * 2 } };
        var harder = session.Setup with
        {
            Combat = session.Setup.Combat with { Creatures = session.Setup.Combat.Creatures.SetItem(Arena.Wolf, overBand) },
        };
        var overKill = TimeToKill(session, harder, "item.weapon.rusted_sword", "Rusted Sword", 1);
        var overDie = TimeToDie(session, harder, armored: false, wolves: 1);

        string table = Render(session, kills, dies, wolf, overBand, kills[0], overKill, dies[0], overDie);
        string path = Path.Combine(Harness.RepoRoot(), "docs", "M3C_TTK_TABLE.md");
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_TTK") == "1")
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(table));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), table);

        foreach (var row in kills.Where(r => r.Label != "Unarmed"))
            Assert.InRange(row.Median, KillBand.Low, KillBand.High);
        // Phase 1's encounters are pairs (the valley strays, the respawning pack) and the den's four: a pair is the standard one.
        Assert.InRange(dies.Single(d => d.Label == "2 wolves, armor: none").Median, DieBand.Low, DieBand.High);
        Assert.Equal(kills[0].Median, overKill.Median, 1);           // an over-band creature is no sponge...
        Assert.True(overDie.Median < dies[0].Median * 0.7);          // ...it is lethal by damage
    }

    private static PlayerRecord Character(SimulationSetup setup, PlayerRecord fresh, int level, string weapon, bool armored, int arrows)
    {
        var inventory = fresh.Inventory.ToList();
        if (arrows > 0)
            inventory.Add(Arena.Stack("item.ammo.arrow_rough", arrows));
        var equipment = new List<KeyValuePair<EquipSlot, EntityId>>();
        if (weapon != "unarmed")
            equipment.Add(KeyValuePair.Create(EquipSlot.MainHand, inventory.Single(e => e.DefId == weapon).ItemId));
        if (armored)
        {
            foreach (var (piece, slot) in new[] { ("item.armor.hide_vest", EquipSlot.Chest), ("item.armor.hide_cap", EquipSlot.Head) })
            {
                var entry = Arena.Stack(piece, 1);
                inventory.Add(entry);
                equipment.Add(KeyValuePair.Create(slot, entry.ItemId));
            }
        }
        // A level-L character has spent its L-1 level-up points on Might, the live attribute that hits harder.
        var progression = fresh.Progression with
        {
            Level = level,
            Allocation = level > 1
                ? ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, level - 1) })
                : ImmutableSortedDictionary<CharacterAttribute, int>.Empty,
        };
        return new PlayerRecord(fresh.Id, fresh.Name, fresh.XMm, fresh.YMm, fresh.ZMm, fresh.AppearanceSeed, inventory, progression,
            fresh.FacingMdeg, fresh.Discoveries, equipment, fresh.Currency, fresh.Effects);
    }

    /// <summary>Time from the first swing, draw or punch to the kill, over many worlds. The bow opens from 15 m.</summary>
    private static Row TimeToKill(GameSession session, SimulationSetup setup, string weapon, string label, int level)
    {
        var seconds = new List<double>();
        var blows = new List<int>();
        bool ranged = weapon == "item.weapon.hunting_bow";
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenWith(session, setup, Ground, 0, new[] { (Ground.X, Ground.Z + (ranged ? 15.0 : 1.9)) },
                r => Character(setup, r, level, weapon, armored: false, arrows: ranged ? 40 : 0), seed);
            var started = arena.Record<AttackStarted>();
            int ticks = arena.Fight(arena.Creature(), 1_200);
            Assert.False(arena.Creature().Alive, $"{label} L{level} world {seed} did not kill the wolf in 60 s");
            long first = started.First(s => s.Attacker == arena.Player).Tick;
            seconds.Add((arena.Simulation.WorldTick - first) * TickSeconds);
            blows.Add(started.Count(s => s.Attacker == arena.Player));
        }
        return new Row(label, level, seconds, blows);
    }

    /// <summary>Time from the first blow taken to death, standing still after waking the pack: no guard, no dodge, no reply.</summary>
    private static Row TimeToDie(GameSession session, SimulationSetup setup, bool armored, int wolves)
    {
        var seconds = new List<double>();
        var ring = Enumerable.Range(0, wolves).Select(i => (Ground.X + 1.9 * Math.Sin(i * Math.PI / 2), Ground.Z + 1.9 * Math.Cos(i * Math.PI / 2))).ToArray();
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenWith(session, setup, Ground, 0, ring, r => Character(setup, r, 1, "item.weapon.rusted_sword", armored, arrows: 0), seed);
            var hits = arena.Record<HitResolved>();
            var died = arena.Record<PlayerDied>();
            // Wake each wolf with one swing, then stand.
            foreach (int facing in Enumerable.Range(0, wolves).Select(i => i * 90))
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
            long first = hits.First(h => h.Target == arena.Player && h.Damage > 0).Tick;
            seconds.Add((death.Tick - first) * TickSeconds);
        }
        string gear = armored ? "hide vest + cap" : "none";
        return new Row($"{wolves} {(wolves == 1 ? "wolf" : "wolves")}, armor: {gear}", 1, seconds, Array.Empty<int>());
    }

    private static string Render(GameSession session, List<Row> kills, List<Row> dies, CreatureDefinition wolf, CreatureDefinition overBand,
        Row killBase, Row killOver, Row dieBase, Row dieOver)
    {
        string S(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture);
        string Band(double value, (double Low, double High) band) => value >= band.Low && value <= band.High ? "yes" : "no";
        var b = new StringBuilder();
        b.Append("# M3c time-to-kill table\n\n");
        b.Append("**Generated from the build; do not edit.** `TtkTableTests` fights each row in the real simulation, over the game's own content, ");
        b.Append($"in {Worlds} worlds (seeds 1-{Worlds}), and fails when this file differs from what the build produces. Regenerate after an ");
        b.Append("intended tuning change with `UNNAMED_WRITE_TTK=1 dotnet test tests/Application.Tests --filter TtkTable` and review the diff.\n\n");
        b.Append("Design bands (`VERTICAL_SLICE.md` §5.1): **4-8 s** to kill a standard enemy at peer level and gear; **8-15 s** to die of ");
        b.Append("sustained mistakes. Every number below is placeholder tuning with a stated shape (`PROTOTYPE.md` A-5).\n\n");

        b.Append($"## Killing one grey wolf (level {wolf.Level}, {wolf.MaxHealth} health, fur {wolf.Armor.GetValueOrDefault(BodyRegion.Torso)} on the torso)\n\n");
        b.Append("A level-L character has put its L-1 level-up points into Might. The sword and bare hands close to reach; the bow opens ");
        b.Append("from 15 m and keeps shooting. Time runs from the first swing or draw to the kill.\n\n");
        b.Append("| Weapon | Level | Might | Median s | Fastest s | Slowest s | Median attacks | In the 4-8 s band |\n");
        b.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var row in kills)
        {
            int median = row.Blows.OrderBy(x => x).ElementAt(row.Blows.Count / 2);
            string band = row.Label == "Unarmed" ? "no (a fallback, not a weapon family)" : Band(row.Median, KillBand);
            b.Append($"| {row.Label} | {row.Level} | {10 + row.Level - 1} | {S(row.Median)} | {S(row.Percentile(0))} | {S(row.Percentile(1))} | {median} | {band} |\n");
        }

        b.Append("\n## Dying, standing still\n\n");
        b.Append("A level-1 character wakes the wolves with one swing each, then stands: no guard, no dodge, no reply. Time runs from the ");
        b.Append("first wound to death. Bleeding is part of it. Phase 1's encounters come in pairs (the valley strays, the respawning ");
        b.Append("pack) and in the den's four, so the pair is the standard encounter the band is read against.\n\n");
        b.Append("| Wolves and armor | Median s | Fastest s | Slowest s | In the 8-15 s band |\n");
        b.Append("|---|---|---|---|---|\n");
        foreach (var row in dies)
            b.Append($"| {row.Label} | {S(row.Median)} | {S(row.Percentile(0))} | {S(row.Percentile(1))} | {Band(row.Median, DieBand)} |\n");

        b.Append("\n## No health sponges\n\n");
        b.Append($"The same body authored at level {overBand.Level} with a doubled bite ({overBand.Attack.DamageMin}-{overBand.Attack.DamageMax}). ");
        b.Append("Level is not an input to the damage pipeline, and the health pool is unchanged, so it dies as fast as the level-2 wolf; ");
        b.Append("it is more dangerous only because it hits harder.\n\n");
        b.Append("| Wolf | Level | Health | Bite | Kill with the sword, median s | Death standing still, median s |\n");
        b.Append("|---|---|---|---|---|---|\n");
        b.Append($"| As authored | {wolf.Level} | {wolf.MaxHealth} | {wolf.Attack.DamageMin}-{wolf.Attack.DamageMax} | {S(killBase.Median)} | {S(dieBase.Median)} |\n");
        b.Append($"| Over-band variant | {overBand.Level} | {overBand.MaxHealth} | {overBand.Attack.DamageMin}-{overBand.Attack.DamageMax} | {S(killOver.Median)} | {S(dieOver.Median)} |\n");
        return b.ToString();
    }
}
