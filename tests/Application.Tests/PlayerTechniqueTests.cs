using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class PlayerTechniqueTests
{
    private const string Blade = "skill.one_hand_blade";
    private static readonly (double X, double Z) Ground = (250, 100);

    private static PlayerRecord Trained(PlayerRecord record, string skill, int level, params string[] known)
    {
        var progression = record.Progression with { Skills = record.Progression.Skills.SetItem(skill, new SkillState(level, 0)) };
        foreach (string id in known) progression = ProgressionEngine.Learn(progression, new TechniqueLearning(id, LearningSource.Teacher, 0)).Progression;
        return record.WithProgression(progression);
    }

    private static PlayerRecord Armed(PlayerRecord record, string weapon)
    {
        var inventory = record.Inventory.ToList();
        if (inventory.All(e => e.DefId != weapon)) inventory.Add(Arena.Stack(weapon, 1));
        if (weapon == "item.weapon.hunting_bow") inventory.Add(Arena.Stack("item.ammo.arrow_rough", 5));
        return new PlayerRecord(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed,
            inventory, record.Progression, record.FacingMdeg, equipment: new[] { KeyValuePair.Create(EquipSlot.MainHand, inventory.First(e => e.DefId == weapon).ItemId) });
    }

    [Theory]
    [InlineData("ability.blade.gap_thrust", BodyRegion.Torso)]
    [InlineData("ability.blade.quiet_blade", BodyRegion.Head)]
    public void AContextualDaggerStrike_PlacesTheBlowOnAnUnawareBody(string technique, BodyRegion region)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var at = (X: Ground.X, Z: Ground.Z + 2);
        var probe = Arena.Open(session, Ground, 0, new[] { at });
        int facing = probe.Creature().Body.FacingMdeg;
        double angle = facing / 1000.0 * Math.PI / 180;
        var arena = Arena.Open(session, (at.X - Math.Sin(angle), at.Z - Math.Cos(angle)), facing / 1000, new[] { at }, r =>
        {
            var trained = Trained(Armed(r, "item.weapon.iron_dagger"), Blade, 30, "ability.blade.gap_thrust", "ability.blade.quiet_blade");
            return trained.WithProgression(trained.Progression with
            {
                Allocation = trained.Progression.Allocation.SetItem(CharacterAttribute.Precision, 10),
                Skills = trained.Progression.Skills.SetItem("skill.stealth", new SkillState(15, 0)),
            });
        });
        arena.Face(arena.Creature());
        arena.Tick();
        var hits = arena.Record<HitResolved>();
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, technique) { Region = region }));
        arena.Tick(10);
        var hit = Assert.Single(hits, h => h.Attacker == arena.Player && h.Damage > 0);
        Assert.Equal(region, hit.Region);
    }

    [Fact]
    public void QuestRewards_WaitAsAWhole_WhenTheirTechniqueGateIsUnmet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var objective = new ObjectiveDefinition("finish", "Stand ready", new WaitUntil(0), ImmutableArray<string>.Empty);
        var quest = new QuestDefinition("quest.test.technique", "A technique", "A lesson", null, "finish", [objective], [],
            [new CurrencyReward(10), new TechniqueReward("ability.blade.parry", "technique")]);
        var setup = session.Setup with { Quests = new QuestSetup(session.Setup.Quests.Quests.SetItem(quest.Id, quest)) };
        var arena = Arena.OpenWith(session, setup, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(r, Blade, 14).WithQuests([QuestRules.Start(quest, 0)]));
        long coin = arena.Simulation.Player.Currency;
        arena.Tick(2);
        Assert.Equal(QuestStatus.Active, arena.Simulation.Quests.Single(q => q.Id == quest.Id).Status);
        Assert.Equal(coin, arena.Simulation.Player.Currency);
        Assert.False(ProgressionEngine.Knows(arena.Simulation.Player.Progression, "ability.blade.parry"));
        var ready = Arena.OpenWith(session, setup, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(r, Blade, 15).WithQuests([QuestRules.Start(quest, 0)]));
        ready.Tick(2);
        Assert.Equal(QuestStatus.Completed, ready.Simulation.Quests.Single(q => q.Id == quest.Id).Status);
        Assert.Equal(coin + 10, ready.Simulation.Player.Currency);
        Assert.True(ProgressionEngine.Knows(ready.Simulation.Player.Progression, "ability.blade.parry"));
    }

    [Fact]
    public void ABookWithUnmetTechniqueRequirements_IsNeitherConsumedNorLearned()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string book = session.Setup.Magic.Teaches.Keys.First();
        var setup = session.Setup with { Magic = session.Setup.Magic with { Teaches = session.Setup.Magic.Teaches.SetItem(book, ["ability.blade.parry"]) } };
        var arena = Arena.OpenWith(session, setup, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(r.WithInventory(r.Inventory.Append(Arena.Stack(book, 1))), Blade, 14));
        var item = arena.Simulation.Player.Inventory.First(e => e.DefId == book);
        Assert.Contains("needs skill", arena.Submit(new UseItemCommand(arena.Player, item.ItemId)));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == item.ItemId);
        Assert.False(ProgressionEngine.Knows(arena.Simulation.Player.Progression, "ability.blade.parry"));
    }

    [Fact]
    public void GivingGround_ThrustsAndRetreatsThroughTheMovementOwner()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(Armed(r, "item.weapon.march_spear"), "skill.polearm", 15, "ability.polearm.giving_ground"));
        var before = arena.Simulation.Player.Body;
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, "ability.polearm.giving_ground")));
        arena.Tick(20);
        var after = arena.Simulation.Player.Body;
        Assert.InRange(before.ZMm - after.ZMm, 900, 1100);
        Assert.InRange(Math.Abs(after.XMm - before.XMm), 0, 5);
    }

    [Fact]
    public void ASteadyDraw_PlacesTheShot_AndSpendsOneArrow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, new[] { (Ground.X, Ground.Z + 12) },
            r => Trained(Armed(r, "item.weapon.hunting_bow"), "skill.bow", 15, "ability.bow.steady_draw"), role: "roamer");
        arena.Face(arena.Creature());
        arena.Tick();
        var hits = arena.Record<HitResolved>();
        var used = arena.Record<ItemConsumed>();
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, "ability.bow.steady_draw") { Region = BodyRegion.Limbs }));
        arena.Tick(40);
        Assert.Empty(hits.Where(h => h.Attacker == arena.Player));
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(60);
        var hit = Assert.Single(hits, h => h.Attacker == arena.Player);
        Assert.Equal(BodyRegion.Limbs, hit.Region);
        Assert.Single(used);
    }

    [Fact]
    public void MovingDuringAHeldDraw_RequiresAnotherFullSteadySecond()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(Armed(r, "item.weapon.hunting_bow"), "skill.bow", 15, "ability.bow.steady_draw"));
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, "ability.bow.steady_draw") { Region = BodyRegion.Head }));
        arena.Tick(40);
        Assert.True(arena.Simulation.Combat.HoldingDraw);
        Assert.True(arena.Simulation.Combat.DrawSteady);
        Assert.Null(arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1000, 0, Gait.Walk, 0))));
        arena.Tick();
        Assert.Null(arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(0))));
        arena.Tick();
        Assert.False(arena.Simulation.Combat.DrawSteady);
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        Assert.Null(arena.Simulation.Combat.AimedRegion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBaseBowVerb_CanTapToShoot_OrHoldToDiscoverSteadyDraw(bool hold)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, new[] { (Ground.X, Ground.Z + 12) },
            r => Trained(Armed(r, "item.weapon.hunting_bow"), "skill.bow", 15), role: "roamer");
        arena.Face(arena.Creature());
        arena.Tick();
        var shots = arena.Record<ShotLoosed>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player) { HoldDraw = true }));
        arena.Tick(hold ? 40 : 1);
        Assert.Null(arena.Submit(new AttackCommand(arena.Player) { ReleaseDrawOnly = true }));
        arena.Tick(40);
        Assert.Single(shots);
        Assert.Equal(hold, ProgressionEngine.Knows(arena.Simulation.Player.Progression, "ability.bow.steady_draw"));
        // Releasing again cannot start another shot.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player) { ReleaseDrawOnly = true }));
        arena.Tick(40);
        Assert.Single(shots);
    }

    [Fact]
    public void WeaponAndAttributeRequirements_AreCheckedAtUse_WithoutSpendingStamina()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(), r => Trained(r, Blade, 14, "ability.blade.parry"));
        int before = arena.Simulation.Combat.Stamina;
        Assert.Contains("needs skill", arena.Submit(new TechniqueCommand(arena.Player, "ability.blade.parry")));
        Assert.Equal(before, arena.Simulation.Combat.Stamina);
        Assert.True(ProgressionEngine.Knows(arena.Simulation.Player.Progression, "ability.blade.parry"));
    }

    [Fact]
    public void Roots_StopRetreatingFootwork_ButNotAStationaryTechnique()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var blade = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(), r => Trained(r, Blade, 15, "ability.blade.power_strike"));
        Assert.Null(blade.Simulation.Submit(new ApplyEffect(blade.Player, "effect.root")));
        Assert.Null(blade.Submit(new TechniqueCommand(blade.Player, "ability.blade.power_strike")));
        var spear = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(Armed(r, "item.weapon.march_spear"), "skill.polearm", 15, "ability.polearm.giving_ground"));
        Assert.Null(spear.Simulation.Submit(new ApplyEffect(spear.Player, "effect.root")));
        int stamina = spear.Simulation.Combat.Stamina;
        Assert.Equal("you cannot move", spear.Submit(new TechniqueCommand(spear.Player, "ability.polearm.giving_ground")));
        Assert.Equal(stamina, spear.Simulation.Combat.Stamina);
    }

    [Fact]
    public void AnAutomaticBowTechnique_DoesNotInheritAnOlderSteadyDrawTimer()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, new[] { (Ground.X, Ground.Z + 12) },
            r => Trained(Armed(r, "item.weapon.hunting_bow"), "skill.bow", 15, "ability.bow.aimed_shot"), startTick: 1000, role: "roamer");
        arena.Face(arena.Creature());
        arena.Tick();
        var shots = arena.Record<ShotLoosed>();
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, "ability.bow.aimed_shot")));
        arena.Tick(50);
        Assert.NotNull(Assert.Single(shots).Target);
        Assert.False(ProgressionEngine.Knows(arena.Simulation.Player.Progression, "ability.bow.steady_draw"));
    }

    [Fact]
    public void APressAndReleaseQueuedBeforeTheNextTick_DoNotLeaveAStuckDraw()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(),
            r => Trained(Armed(r, "item.weapon.hunting_bow"), "skill.bow", 15));
        var shots = arena.Record<ShotLoosed>();
        arena.Simulation.Enqueue(new AttackCommand(arena.Player) { HoldDraw = true });
        arena.Simulation.Enqueue(new AttackCommand(arena.Player) { ReleaseDrawOnly = true });
        arena.Tick(40);
        Assert.False(arena.Simulation.Combat.HoldingDraw);
        Assert.Single(shots);
    }

    [Fact]
    public void LateGuard_DiscoversParry_OnlyOnce_AndKnownParryDeflectsWithoutDamage()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, new[] { (Ground.X, Ground.Z + 1.6) }, r => Trained(r, Blade, 15));
        var found = arena.Record<TechniqueDiscovered>();
        bool raised = false;
        for (int i = 0; i < 160 && found.Count == 0; i++)
        {
            arena.Face(arena.Creature());
            var creature = arena.Creature();
            if (creature.Phase == CombatPhase.Windup && creature.PhaseTicksLeft <= 2 && !raised)
            {
                Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
                raised = true;
            }
            arena.Tick();
        }
        Assert.Contains(found, e => e.TechniqueId == "ability.blade.parry");
        Assert.Single(found.Where(e => e.TechniqueId == "ability.blade.parry"));
        Assert.Contains("ability.blade.parry", arena.Simulation.Player.Progression.NoveltyFirsts);

        var trained = Arena.Open(session, Ground, 0, new[] { (Ground.X, Ground.Z + 1.6) }, r => Trained(r, Blade, 15, "ability.blade.parry"));
        var hits = trained.Record<HitResolved>();
        for (int i = 0; i < 80 && hits.All(h => h.Target != trained.Player); i++)
        {
            trained.Face(trained.Creature());
            if (trained.Creature().Phase == CombatPhase.Windup && trained.Creature().PhaseTicksLeft <= 2)
                Assert.Null(trained.Submit(new BlockCommand(trained.Player, true)));
            trained.Tick();
        }
        Assert.Contains(hits, h => h.Target == trained.Player && h.Blocked && h.Damage == 0);
    }

    [Fact]
    public void BeatTheGuard_DrainsTwentyFiveAndBreaksTheGuard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = session.Setup.Combat.Creatures[Arena.Wolf];
        var setup = session.Setup with { Combat = session.Setup.Combat with { Creatures = session.Setup.Combat.Creatures.SetItem(Arena.Wolf,
            wolf with { Guard = new GuardProfile(20, 10), Attack = wolf.Attack with { RecoveryTicks = 80 } }) } };
        var arena = Arena.OpenWith(session, setup, Ground, 0, new[] { (Ground.X, Ground.Z + 1.6) },
            r => Trained(r, Blade, 15, "ability.blade.beat_the_guard"));
        for (int i = 0; i < 80 && arena.Creature().Phase != CombatPhase.Recovery; i++) { arena.Face(arena.Creature()); arena.Tick(); }
        var broken = arena.Record<GuardBroken>();
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, "ability.blade.beat_the_guard")));
        arena.Tick(18);
        Assert.Contains(broken, b => b.Actor == arena.Creature().Id);
    }

    [Fact]
    public void APolearmGuard_StopsAChargeOnThePoint()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0,
            new[] { ("creature.beast.bristleback_boar", Ground.X, Ground.Z + 8, "roamer") },
            r => Trained(Armed(r, "item.weapon.march_spear"), "skill.polearm", 30, "ability.polearm.set_against_charge"));
        var used = arena.Record<TechniqueUsed>();
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        for (int i = 0; i < 180 && used.Count == 0; i++) { arena.Face(arena.Creature()); arena.Tick(); }
        Assert.Contains(used, u => u.TechniqueId == "ability.polearm.set_against_charge");
        Assert.Equal(arena.Simulation.Combat.MaxHealth, arena.Simulation.Combat.Health);
        Assert.Equal(CombatPhase.Staggered, arena.Creature().Phase);
    }

    [Fact]
    public void GapThrust_NeedsADagger_AndAnOpening()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(), r => Trained(r, Blade, 15, "ability.blade.gap_thrust"));
        int stamina = arena.Simulation.Combat.Stamina;
        Assert.Contains("dagger in hand", arena.Submit(new TechniqueCommand(arena.Player, "ability.blade.gap_thrust")));
        Assert.Equal(stamina, arena.Simulation.Combat.Stamina);
    }

    [Fact]
    public void AgilityAndPrecision_CanBeSpent_InsightStillWaitsForStudy()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Ground, 0, Array.Empty<(double, double)>(),
            r => r.WithProgression(r.Progression with { UnspentAttributePoints = 3 }));
        Assert.Null(arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Agility)));
        Assert.Null(arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Precision)));
        Assert.Contains("not in play", arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Insight)));
    }
}
