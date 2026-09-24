using UNNAMED.Domain.Combat;

namespace UNNAMED.Content.Tests;

/// <summary>Combat from content (M3c): seconds and game minutes become whole ticks, and the CMB lint refuses what cannot build.</summary>
public class CombatContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-cmb-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "CMB001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    /// <summary>
    /// A creature gives up a fight farther than its leash from its spawn, so a patrol point beyond the leash is ground it cannot
    /// fight on. M6's layout move twice left a patrol on M3's coordinates, far from its moved spawn (the east pack, the husk).
    /// </summary>
    [Fact]
    public void EverySpawnersPatrol_StaysOnItsLeash()
    {
        var setup = CombatContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")), "region.ashen_hollow");

        var beyond = setup.Spawns.SelectMany(s => s.Route
            .Where(p => Math.Sqrt(Math.Pow(p.XMm - s.XMm, 2) + Math.Pow(p.ZMm - s.ZMm, 2)) > setup.Constants.LeashMm)
            .Select(p => $"{s.Key} ({p.XMm / 1000.0}, {p.ZMm / 1000.0})"));
        Assert.Empty(beyond);
        Assert.Contains(setup.Spawns, s => !s.Route.IsEmpty);
    }

    [Fact]
    public void TheGamesCombat_BuildsInWholeTicks()
    {
        var setup = CombatContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")), "region.ashen_hollow");

        var wolf = setup.Creatures["creature.beast.wolf_grey"];
        Assert.Equal((2, 50, 4_500L, 450L, 30L), (wolf.Level, wolf.MaxHealth, wolf.MoveSpeedMmPerSecond, wolf.RadiusMm, wolf.XpValue));
        Assert.Equal(2, wolf.Armor[BodyRegion.Torso]);
        // The bite: 0.45 s windup, 0.15 s active, 0.8 s recovery at 20 Hz; bleeding on 30% of bites.
        Assert.Equal((9, 3, 16, 1_400L), (wolf.Attack.WindupTicks, wolf.Attack.ActiveTicks, wolf.Attack.RecoveryTicks, wolf.Attack.ReachMm));
        Assert.Equal(("effect.bleeding", 30), (wolf.Attack.OnHitEffect, wolf.Attack.OnHitEffectPercent));

        // Effect durations are game minutes (config.time: 2 s each): bleeding 6 s ticking each second, weakness 60 s.
        Assert.Equal((120, 20, 1, StackPolicy.StackIntensity), (setup.Effects["effect.bleeding"].DurationTicks,
            setup.Effects["effect.bleeding"].TickIntervalTicks, setup.Effects["effect.bleeding"].DamagePerTick, setup.Effects["effect.bleeding"].Stacking));
        Assert.Equal((1_200, 0.75), (setup.Effects["effect.weakened"].DurationTicks, setup.Effects["effect.weakened"].DamageDealtMultiplier));
        Assert.Equal((120, 3), (setup.Effects["effect.mending"].DurationTicks, setup.Effects["effect.mending"].HealPerTick));

        var constants = setup.Constants;
        Assert.Equal((12, 40, 5, 5, 2_500L), (constants.StaggerTicks, constants.StaggerImmunityTicks, constants.DodgeTicks, constants.DodgeRecoveryTicks, constants.DodgeDistanceMm));
        Assert.Equal((5, 2, 5), (constants.Unarmed.WindupTicks, constants.Unarmed.ActiveTicks, constants.Unarmed.RecoveryTicks));   // 0.6 s
        Assert.Equal("effect.weakened", constants.DeathEffect);
        Assert.Equal(60, constants.RegionWeights[BodyRegion.Torso]);

        var spawns = setup.Spawns.ToDictionary(s => s.Key);
        Assert.Equal(8, spawns.Count);
        var strays = spawns["spawn.hollow.valley_strays"];
        Assert.Equal((2, 118_000L, 128_000L, 0L), (strays.Members.Length, strays.XMm, strays.ZMm, strays.RespawnTicks));
        Assert.All(strays.Members, m => Assert.Equal("stray", m.RoleId));
        Assert.Equal(new[] { "den_guardian", "pack_hunter", "pack_hunter", "sleeper" }, spawns["spawn.hollow.den_pack"].Members.Select(m => m.RoleId));
        Assert.Equal((24_000L, 3), (spawns["spawn.hollow.east_pack"].RespawnTicks, spawns["spawn.hollow.east_pack"].Route.Length));   // back after 20 minutes
        Assert.Equal(new[] { "ambusher", "den_guardian", "hunter", "pack_hunter", "roamer", "sentinel", "sleeper", "stray", "territorial" }, setup.Roles.Keys);

        // The content bible's five archetypes, each a creature of its own with its own body and blow (§10).
        Assert.Equal(new[]
            {
                "creature.beast.ash_ember_hound", "creature.beast.bristleback_boar", "creature.beast.cave_hunting_spider", "creature.beast.wolf_grey",
                "creature.construct.animated_armour", "creature.undead.bone_walker_husk",
            }, setup.Creatures.Keys);
        var hound = setup.Creatures["creature.beast.ash_ember_hound"];
        Assert.Equal(("fire", 2_200L, 6_000L), (hound.Attack.DamageType, hound.Attack.LungeMm, hound.MoveSpeedMmPerSecond));
        var boar = setup.Creatures["creature.beast.bristleback_boar"].Charge!;
        Assert.Equal((9_000L, 5_000L, 14_000L, 40, 120, true), (boar.ChargeSpeedMmPerSecond, boar.ChargeMinRangeMm, boar.ReachMm, boar.StunTicks, boar.CooldownTicks, boar.ForcesStagger));
        Assert.Equal(new WeakPoint(BodyRegion.Head, true), setup.Creatures["creature.construct.animated_armour"].WeakPoint);
        Assert.Contains("undead", setup.Creatures["creature.undead.bone_walker_husk"].Tags);
        Assert.Equal((6_000L, 360_000L, 14_000L), (setup.Creatures["creature.beast.cave_hunting_spider"].Senses.SightMm,
            setup.Creatures["creature.beast.cave_hunting_spider"].Senses.FieldOfViewMdeg, setup.Creatures["creature.beast.cave_hunting_spider"].Senses.HearingMm));
        Assert.Equal(new[] { "construct", "undead" }, setup.Effects["effect.bleeding"].ImmuneTags);
        Assert.True(setup.Roles["ambusher"].PounceOnNoise);
        Assert.Equal((25_000L, 140_000L, 30_000L), (wolf.Senses.SightMm, wolf.Senses.FieldOfViewMdeg, wolf.Senses.HearingMm));
        Assert.Equal((120, 12_000L), (setup.Awareness.SearchTicks, setup.CorpseDecayTicks));
        Assert.Equal("effect.mending", setup.UseEffects["item.consumable.salve_minor"]);
        var passive = Assert.Single(setup.Passives);
        Assert.Equal(("skill.one_hand_blade", 3, "stat.stagger_power", 1.05), (passive.SkillId, passive.FromLevel, passive.Stat, passive.Multiplier));
    }

    [Fact]
    public void AWeaponWithNoTiming_IsRefused()
    {
        using var content = new EditedContent("items/weapon/rusted_sword.yaml", "attack_speed: 1.43", "");
        Assert.Contains(Load(content.Root).Errors, e => e.Code == "ITM001" && e.Message.Contains("attack_speed", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatureAttackThatIsNotACreatureAbility_IsRefused() =>
        AssertRefused("abilities/creature/wolf_bite.yaml", "class: creature", "class: active", "class creature");

    [Fact]
    public void AStackPolicyNotBuiltInPhase1_IsRefused() =>
        AssertRefused("effects/bleeding.yaml", "stack_policy: stack_intensity", "stack_policy: independent", "stack_policy");

    [Fact]
    public void AModifierNotBuiltInPhase1_IsRefused() =>
        AssertRefused("effects/weakened.yaml", "target: stat.damage_dealt", "target: stat.crit_chance", "stat.crit_chance");

    [Fact]
    public void ACreatureAuthoredAcrossALevelBand_IsRefusedUntilTheSpawnerRollsIt() =>
        AssertRefused("creatures/beast/wolf_grey.yaml", "level_band: [2, 2]", "level_band: [2, 4]", "level_band");

    [Fact]
    public void ASpawnWithARolledCount_IsRefusedInPhase1() =>
        AssertRefused("spawns/hollow/valley_strays.yaml", "count: [2, 2]", "count: [1, 3]", "fixed count");

    [Fact]
    public void DamageConstantsOutOfRange_AreRefused() =>
        AssertRefused("config/damage_constants.yaml", "armor_k: 50", "armor_k: 0", "armor_k");

    [Fact]
    public void ADeathEffectThatDoesNotExist_IsAReferenceError()
    {
        using var content = new EditedContent("config/damage_constants.yaml", "effect_ref: effect.weakened", "effect_ref: effect.faint");
        Assert.Contains(Load(content.Root).Errors, e => e.Code == "XREF003" && e.Message.Contains("effect.faint", StringComparison.Ordinal));
    }
}
