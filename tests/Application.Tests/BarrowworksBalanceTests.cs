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
/// The Barrowworks' place in the frontier band (PROGRESSION.md §3.5, "band, not level"; M8 09). Generated from the build like the
/// M3c table: every row is fought in the real simulation over the game's own content, and docs/BARROWWORKS_BALANCE.md must equal
/// what the build produces. Regenerate with UNNAMED_WRITE_BALANCE=1 after an intended tuning change and review the diff.
/// </summary>
public class BarrowworksBalanceTests
{
    private const int Worlds = 12;
    private const double TickSeconds = 0.05;
    private const string Custodian = "creature.construct.ring_custodian";
    private static readonly (double X, double Z) Ground = (120, 60);

    /// <summary>The reference visitor (M8 09): a level-5 character with an ordinary competent frontier loadout.</summary>
    private static readonly string[] Kit = ["item.armor.hide_vest", "item.armor.hide_cap", "item.armor.hide_boots"];

    private sealed record Row(string Label, int Level, IReadOnlyList<double> Seconds, int Wins, IReadOnlyList<int> HitsTaken)
    {
        public double Median(IReadOnlyList<double> values) => values.Count == 0 ? double.NaN : values.OrderBy(s => s).ElementAt(values.Count / 2);
    }

    private static PlayerRecord Character(PlayerRecord fresh, int level, string weapon, bool kit, int salves)
    {
        var inventory = fresh.Inventory.Where(e => !e.DefId.StartsWith("item.consumable.", StringComparison.Ordinal)).ToList();
        var equipment = new List<KeyValuePair<EquipSlot, EntityId>>();
        if (inventory.All(e => e.DefId != weapon))
            inventory.Add(Arena.Stack(weapon, 1));
        equipment.Add(KeyValuePair.Create(EquipSlot.MainHand, inventory.First(e => e.DefId == weapon).ItemId));
        if (kit)
        {
            foreach (var (piece, slot) in Kit.Zip(new[] { EquipSlot.Chest, EquipSlot.Head, EquipSlot.Feet }))
            {
                var entry = Arena.Stack(piece, 1);
                inventory.Add(entry);
                equipment.Add(KeyValuePair.Create(slot, entry.ItemId));
            }
        }
        if (salves > 0)
            inventory.Add(Arena.Stack("item.consumable.salve_minor", salves));
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

    /// <summary>
    /// The competent solo fight the review route plays (BarrowworksReview.Fight): close, then circle toward the slow side, dodge a late
    /// windup when there is stamina for it, swing whenever free, and take a salve below a third of health.
    /// </summary>
    private static (bool Won, int Ticks) Fight(Arena arena, CreatureView target, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            var creature = arena.Simulation.Creatures.Single(c => c.Key == target.Key);
            var combat = arena.Simulation.Combat;
            if (!creature.Alive) return (true, i);
            if (arena.Simulation.Player.Progression.Pools.Health is 0 || combat.Health <= 0) return (false, i);
            var body = arena.Simulation.Player.Body;
            double dx = creature.Body.XMm - body.XMm, dz = creature.Body.ZMm - body.ZMm;
            double distance = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
            int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm);
            var (tx, tz) = (dz / distance, -dx / distance);
            var (mx, mz) = distance > 1600 ? (dx / distance, dz / distance) : (tx, tz);
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent((int)Math.Round(mx * 1000), (int)Math.Round(mz * 1000), Gait.Run, facing)));
            int stamina = arena.Simulation.Player.Progression.Pools.Stamina ?? combat.MaxStamina;
            if (combat.Health * 3 < combat.MaxHealth && i % 20 == 0
                && arena.Simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.consumable.salve_minor") is { } salve)
                arena.Simulation.Enqueue(new UseItemCommand(arena.Player, salve.ItemId));
            if (creature.Phase == CombatPhase.Windup && creature.PhaseTicksLeft < 7 && combat.Phase == CombatPhase.Idle && stamina > 20)
                arena.Simulation.Enqueue(new DodgeCommand(arena.Player, (int)Math.Round(tx * 1000), (int)Math.Round(tz * 1000)));
            else if (distance < 1900 && combat.Phase == CombatPhase.Idle && stamina > 15)
                arena.Simulation.Enqueue(new AttackCommand(arena.Player));
            arena.Tick();
            if (arena.Simulation.Combat.Health <= 0 || arena.Simulation.Player.Progression.Pools.Health is 0) return (false, i);
        }
        return (false, maxTicks);
    }

    private static Row Boss(GameSession session, string weapon, string label, int level, bool kit, int salves)
    {
        var seconds = new List<double>();
        var hits = new List<int>();
        int wins = 0;
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (50, 102), 0, [], r => Character(r, level, weapon, kit, salves), seed,
                keepSpawns: true, origin: (0, 0));
            var struck = arena.Record<HitResolved>();
            var (won, ticks) = Fight(arena, arena.Simulation.Creatures.Single(c => c.DefId == Custodian), 6_000);
            hits.Add(struck.Count(h => h.Target == arena.Player && h.Damage > 0));
            if (!won) continue;
            wins++;
            seconds.Add(ticks * TickSeconds);
        }
        return new Row(label, level, seconds, wins, hits);
    }

    private static Row Ordinary(GameSession session, string creature, string role, int level)
    {
        var seconds = new List<double>();
        var hits = new List<int>();
        int wins = 0;
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0, [(creature, Ground.X, Ground.Z + 1.9, role)],
                r => Character(r, level, "item.weapon.rusted_sword", kit: true, salves: 0), seed);
            var struck = arena.Record<HitResolved>();
            var (won, ticks) = Fight(arena, arena.Creature(), 2_400);
            hits.Add(struck.Count(h => h.Target == arena.Player && h.Damage > 0));
            if (!won) continue;
            wins++;
            seconds.Add(ticks * TickSeconds);
        }
        return new Row(creature.Split('.').Last().Replace('_', ' '), level, seconds, wins, hits);
    }

    /// <summary>Custodian blows a standing character takes to fall, from full health: the M8 08 calibration is 4-6 at the reference.</summary>
    private static int BlowsToFall(GameSession session, int level, bool kit)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, (50, 104), 0, [], r => Character(r, level, "item.weapon.rusted_sword", kit, 0), 1,
            keepSpawns: true, origin: (0, 0));
        var struck = arena.Record<HitResolved>();
        var died = arena.Record<PlayerDied>();
        arena.Face(arena.Simulation.Creatures.Single(c => c.DefId == Custodian));
        arena.Submit(new AttackCommand(arena.Player));
        for (int i = 0; i < 4_000 && died.Count == 0; i++)
            arena.Tick();
        Assert.Single(died);
        return struck.Count(h => h.Target == arena.Player && h.Damage > 0);
    }

    [Fact]
    public void TheBarrowworks_SitsAtTheTopOfTheFrontierBand()
    {
        using var profile = new TempProfile();
        using var ring = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        using var frontierProfile = new TempProfile();
        using var frontier = Harness.Boot(frontierProfile);
        var boss = new List<Row>();
        foreach (int level in new[] { 1, 3, 5 })
        {
            boss.Add(Boss(ring, "item.weapon.rusted_sword", "Rusted Sword, hide kit, 3 salves", level, kit: true, salves: 3));
            boss.Add(Boss(ring, "item.weapon.march_spear", "March Spear, hide kit, 3 salves", level, kit: true, salves: 3));
        }
        var ordinary = new List<Row>();
        foreach (var (creature, role) in new[] { ("creature.beast.cave_hunting_spider", "ambusher"), ("creature.undead.bone_walker_husk", "roamer") })
            foreach (int level in new[] { 3, 5 })
                ordinary.Add(Ordinary(frontier, creature, role, level));
        var falls = new[] { (1, false), (5, true) }.Select(f => (f.Item1, f.Item2, BlowsToFall(ring, f.Item1, f.Item2))).ToList();

        string table = Render(ring, boss, ordinary, falls);
        string path = Path.Combine(Harness.RepoRoot(), "docs", "BARROWWORKS_BALANCE.md");
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_BALANCE") == "1")
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(table));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), table);

        // The reference visitor who keeps to the slow side wins nearly every attempt; staying behind it is the fight (M8 08).
        Assert.All(boss.Where(r => r.Level == 5), r => Assert.True(r.Wins * 4 >= Worlds * 3, $"{r.Label} L5 won {r.Wins}/{Worlds}"));
        // M8 08: four to six unmitigated blows fell the reference character; an unarmoured level-1 character falls in fewer.
        Assert.InRange(falls.Single(f => f.Item1 == 5).Item3, 4, 6);
        Assert.True(falls.Single(f => f.Item1 == 1).Item3 < falls.Single(f => f.Item1 == 5).Item3, "the poorly equipped visitor is in more danger");
    }

    private static string Render(GameSession session, List<Row> boss, List<Row> ordinary, List<(int Level, bool Kit, int Blows)> falls)
    {
        string S(double seconds) => double.IsNaN(seconds) ? "-" : seconds.ToString("0.0", CultureInfo.InvariantCulture);
        var custodian = session.Setup.Combat.Creatures[Custodian];
        var b = new StringBuilder();
        b.Append("# Barrowworks balance table\n\n");
        b.Append("**Generated from the build; do not edit.** `BarrowworksBalanceTests` fights each row in the real simulation over the game's ");
        b.Append($"own content in {Worlds} worlds and fails when this file differs. Regenerate after an intended tuning change with ");
        b.Append("`UNNAMED_WRITE_BALANCE=1 dotnet test tests/Application.Tests --filter BarrowworksBalance` and review the diff.\n\n");
        b.Append("The fighter is the review route's: it closes, circles toward the Custodian's slow side, dodges a late windup with stamina to ");
        b.Append("spare, swings whenever free and takes a minor salve below a third of health. A level-L character has put its L-1 points into Might.\n\n");
        b.Append($"## The Ring Custodian (level {custodian.Level}, {custodian.MaxHealth} health)\n\n");
        b.Append("| Loadout | Level | Wins | Median s to kill (wins) | Median blows taken |\n|---|---|---|---|---|\n");
        foreach (var row in boss)
            b.Append($"| {row.Label} | {row.Level} | {row.Wins}/{Worlds} | {S(row.Median(row.Seconds))} | {row.HitsTaken.OrderBy(h => h).ElementAt(row.HitsTaken.Count / 2)} |\n");
        b.Append("\n## Custodian blows to fell a character standing still\n\n| Level | Kit | Blows |\n|---|---|---|\n");
        foreach (var (level, kit, blows) in falls)
            b.Append($"| {level} | {(kit ? "hide vest, cap, boots" : "none")} | {blows} |\n");
        b.Append("\n## The ring's ordinary creatures, one at a time (rusted sword, hide kit)\n\n");
        b.Append("| Creature | Level | Wins | Median s to kill | Median blows taken |\n|---|---|---|---|---|\n");
        foreach (var row in ordinary)
            b.Append($"| {row.Label} | {row.Level} | {row.Wins}/{Worlds} | {S(row.Median(row.Seconds))} | {row.HitsTaken.OrderBy(h => h).ElementAt(row.HitsTaken.Count / 2)} |\n");
        return b.ToString();
    }
}
