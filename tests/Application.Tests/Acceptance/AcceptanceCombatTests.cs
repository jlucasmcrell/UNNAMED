using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.Acceptance.AcceptanceReport;

namespace UNNAMED.Application.Tests.Acceptance;

public partial class AcceptanceWalkthroughTests
{
    private static PlayerRecord Wielding(PlayerRecord r, string weapon, int arrows)
    {
        var item = Arena.Stack(weapon, 1);
        var equipment = r.Equipment.Where(kv => kv.Key != EquipSlot.MainHand).Append(KeyValuePair.Create(EquipSlot.MainHand, item.ItemId));
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(new[] { item, Arena.Stack(Arrows, arrows) }), r.Progression,
            r.FacingMdeg, r.Discoveries, equipment, r.Currency, r.Effects);
    }

    private static string Describe(IEnumerable<string> weapons) => string.Join(", ", weapons.Select(w => w.Split('.').Last()));

    [Fact]
    public void TheCombat_SweptByTheChecklist()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolfAhead = new[] { (Arena.Wolf, OpenGround.X, OpenGround.Z + 2.0, "pack_hunter") };

        // ── §3.2 every weapon the build offers can be wielded and hits ─────────────
        Step("3.2", () =>
        {
            var weapons = session.Setup.Items.Catalog.Definitions.Keys.Where(k => k.StartsWith("item.weapon.", StringComparison.Ordinal)).ToList();
            That(weapons.Count >= 4, $"{weapons.Count} weapons in the catalogue");
            foreach (string weapon in weapons)
            {
                var arena = Open(session, wolfAhead, r => Wielding(r, weapon, 40));
                var held = arena.Simulation.Player.Inventory.Single(e => e.ItemId == arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
                Equal(weapon, held.DefId, "the weapon in hand");
                var (_, landed) = FightAWolf(arena, 1500);
                That(landed > 0, $"{weapon} never wounded the wolf in 75 s");
            }
            return $"wielded and landed blows with each of {weapons.Count} weapons ({Describe(weapons)})";
        });

        // ── §3.3 a hit taken ───────────────────────────────────────────────────────
        Step("3.3", () =>
        {
            var arena = Open(session, new[] { (Arena.Wolf, OpenGround.X, OpenGround.Z + 1.2, "pack_hunter") });
            var taken = arena.Record<HitResolved>();
            int full = arena.Simulation.Combat.MaxHealth;
            arena.Submit(new AttackCommand(arena.Player));   // a swing wakes it, as the first blow of any fight does
            for (int i = 0; i < 400 && !taken.Any(h => h.Target == arena.Player && h.Damage > 0); i++)
                arena.Tick();
            var hit = taken.FirstOrDefault(h => h.Target == arena.Player && h.Damage > 0);
            That(hit is not null, "the wolf never wounded a standing character");
            int health = arena.Simulation.Combat.Health;
            That(health < full, "health did not drop on a hit");
            Equal(health, arena.Simulation.Player.Progression.Pools.Health, "the bar's number and the sheet's");
            return $"a bite of {hit!.Damage} took health from {full} to {health}; the bar and the sheet agree";
        });

        // ── §3.4 health at zero: dead alone, downed with a hand near ───────────────
        Step("3.4", () =>
        {
            var alone = Open(session, new[] { (Arena.Wolf, OpenGround.X, OpenGround.Z + 1.9, "pack_hunter") }, r => WithHealth(r, 3));
            var died = alone.Record<PlayerDied>();
            alone.Submit(new AttackCommand(alone.Player));
            for (int i = 0; i < 400 && !alone.Simulation.Combat.Defeated; i++)
                alone.Tick();
            That(alone.Simulation.Combat.Defeated && !alone.Simulation.Combat.Downed, "alone, the character was not simply defeated");
            That(alone.Simulation.Combat.RespawnTicksLeft > 0, "no respawn follows the death");
            for (int i = 0; i < 2000 && alone.Simulation.Combat.Defeated; i++)
                alone.Tick();
            That(!alone.Simulation.Combat.Defeated && alone.Simulation.Combat.Health > 0, "the character stays dead: stuck at zero health");
            That(died.Count == 1, $"{died.Count} deaths published");

            var (tx, tz) = (OpenGround.X, OpenGround.Z - 1.5);
            var tavar = new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, Harness.InRegion(tx, tz).XMm, Harness.InRegion(tx, tz).ZMm, 0, 100);
            var helped = Open(session, new[] { (Arena.Wolf, OpenGround.X, OpenGround.Z + 1.9, "pack_hunter") }, r => WithHealth(r, 3).WithCompanions(new[] { tavar }));
            var downed = helped.Record<PlayerDowned>();
            var revived = helped.Record<PlayerRevived>();
            helped.Submit(new AttackCommand(helped.Player));
            for (int i = 0; i < 400 && !helped.Simulation.Combat.Defeated; i++)
                helped.Tick();
            That(helped.Simulation.Combat.Downed, "with a companion near the character was not downed");
            helped.Tick(helped.Simulation.Combat.DownedTicksLeft + 2);
            That(revived.Count == 1 && !helped.Simulation.Combat.Defeated && helped.Simulation.Combat.Health > 0, "the character was not helped up");
            return $"alone: defeated, then respawned alive; with Tavar near: downed ({downed.Count}), then helped up with {helped.Simulation.Combat.Health} health";
        });

        // ── §3.5 a group ───────────────────────────────────────────────────────────
        Step("3.5", () =>
        {
            var arena = Open(session, new[]
            {
                (Arena.Wolf, OpenGround.X - 1.0, OpenGround.Z + 2.5, "pack_hunter"), (Arena.Wolf, OpenGround.X + 1.0, OpenGround.Z + 2.5, "pack_hunter"),
            }, r => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = r.Progression.Pools.Health } }));
            var bites = arena.Record<HitResolved>();
            for (int round = 0; round < 6 && arena.Simulation.Creatures.Any(c => c.Alive) && !arena.Simulation.Combat.Defeated; round++)
                arena.Fight(arena.Simulation.Creatures.Where(c => c.Alive).OrderBy(c => c.Key, StringComparer.Ordinal).First(), 900);
            That(bites.Any(h => h.Target == arena.Player && h.Damage > 0), "the pack never attacked");
            That(arena.Simulation.Creatures.All(c => !c.Alive) || arena.Simulation.Combat.Defeated, "the pack neither died nor brought the character down in time");
            int dead = arena.Simulation.Creatures.Count(c => !c.Alive);
            That(dead >= 1, "no wolf of the group could be killed");
            return $"a pack of two approached and bit ({bites.Count(h => h.Target == arena.Player && h.Damage > 0)} bites); {dead} killed" + (arena.Simulation.Combat.Defeated ? ", then the pack won" : "");
        });

        // ── §3.6 guard and dodge ───────────────────────────────────────────────────
        Step("3.6", () =>
        {
            var arena = Open(session, Array.Empty<(string, double, double, string)>());
            Equal(null, arena.Submit(new BlockCommand(arena.Player, true)), "raising the guard");
            arena.Tick();
            That(arena.Simulation.Combat.Blocking, "the guard is not up");
            Equal(null, arena.Submit(new BlockCommand(arena.Player, false)), "lowering the guard");
            arena.Tick(2);
            int stamina = arena.Simulation.Combat.Stamina;
            Equal(null, arena.Submit(new DodgeCommand(arena.Player, 0, -1000)), "dodging");
            arena.Tick();
            That(arena.Simulation.Combat.Phase == CombatPhase.Dodge || arena.Simulation.Combat.Stamina < stamina, "the dodge did nothing visible and cost nothing");
            That(arena.Simulation.Combat.Stamina < stamina, "the dodge cost no stamina");
            return $"guard goes up and down; the dodge cost {stamina - arena.Simulation.Combat.Stamina} stamina (light: the swing in 3.1; heavy: PowerStrike, TeachingTests)";
        });

        // ── §4.4 findings ──────────────────────────────────────────────────────────
        Step("4.4", () =>
        {
            const string bench = "station.hollow.workbench";
            var arena = Arena.OpenCreatures(session, session.Setup, (38, 144.5), 0, Array.Empty<(string, double, double, string)>(),
                r => r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack("item.material.wolf_hide", 1), Arena.Stack("item.material.bog_cotton", 2),
                    Arena.Stack("item.material.timber", 2), Arena.Stack("item.material.small_hide", 1) })));
            var (found, total) = arena.Simulation.Experiments(bench);
            Equal(0, found, "findings before experimenting");
            That(total > 0, "the workbench has nothing to find");
            Equal(null, arena.Submit(new ExperimentCommand(arena.Player, bench)), "experimenting at the bench");
            That(arena.Simulation.Findings.Length > 0, "Findings lists nothing after an experiment");
            var (nowFound, _) = arena.Simulation.Experiments(bench);
            That(nowFound > 0 && nowFound <= total, "the found count did not move");
            return $"{arena.Simulation.Findings.Length} finding(s) recorded; the bench shows {nowFound} of {total} (what is unfound is a count, not spoiled)";
        });

        // ── §4.5 .. §4.7 the rung-3 lamework ───────────────────────────────────────
        Step("4.5", () =>
        {
            var arena = Arena.OpenCreatures(session, session.Setup, AtKeraForge, 0, Array.Empty<(string, double, double, string)>());
            var lessons = arena.Simulation.Teaching(Kera)!.Lessons.Select(l => l.DefinitionId).ToList();
            That(lessons.Any(l => l.Contains("lamework", StringComparison.OrdinalIgnoreCase) || l.Contains("lamell", StringComparison.OrdinalIgnoreCase)),
                $"Kera teaches no lamework: her {lessons.Count} lessons are {Describe(lessons)}; no item or recipe in content/ is lamework");
            return "Kera offers the lamework";
        });
        Step("4.6", () =>
        {
            That(session.Setup.Crafting.Recipes.Keys.Any(k => k.Contains("lamework", StringComparison.OrdinalIgnoreCase)), "there is no lamework recipe to craft");
            return "a lamework recipe exists";
        });

        Write();
        Assert.Empty(OwnFailures());
    }
}
