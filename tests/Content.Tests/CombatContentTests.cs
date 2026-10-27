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
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
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
        var setup = CombatContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")), "region.starting_frontier");

        var beyond = setup.Spawns.SelectMany(s => s.Route
            .Where(p => Math.Sqrt(Math.Pow(p.XMm - s.XMm, 2) + Math.Pow(p.ZMm - s.ZMm, 2)) > setup.Constants.LeashMm)
            .Select(p => $"{s.Key} ({p.XMm / 1000.0}, {p.ZMm / 1000.0})"));
        Assert.Empty(beyond);
        Assert.Contains(setup.Spawns, s => !s.Route.IsEmpty);
    }

    [Fact]
    public void TheGamesCombat_BuildsInWholeTicks()
    {
        var setup = CombatContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")), "region.starting_frontier");

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
        // 29, not 30: the Ring Custodian's spawn is region.barrowworks' and this run is the start area's, so
        // CombatContent correctly does not carry it. A run of the ring has it and not these.
        Assert.Equal(46, spawns.Count);   // the hollow's eight and Cairnwold's four; the road's eleven camps (the population pass); one night walker; 13 of wildlife; the Spill's nine
        var strays = spawns["spawn.hollow.valley_strays"];
        Assert.Equal((2, 518_000L, 728_000L, 24_000L), (strays.Members.Length, strays.XMm, strays.ZMm, strays.RespawnTicks));
        Assert.All(strays.Members, m => Assert.Equal("stray", m.RoleId));
        // The den's leader joins the four (owner finding 29): the pack, plus the animal that makes it a den.
        Assert.Equal(new[] { "den_alpha", "den_guardian", "pack_hunter", "pack_hunter", "sleeper" }, spawns["spawn.hollow.den_pack"].Members.Select(m => m.RoleId));
        Assert.Equal((24_000L, 3), (spawns["spawn.hollow.east_pack"].RespawnTicks, spawns["spawn.hollow.east_pack"].Route.Length));   // back after 20 minutes
        Assert.Equal(new[]
        {
            "ambusher", "den_alpha", "den_guardian", "herd", "herd_shy", "hunter", "night_walker", "pack_hunter", "prowler", "roamer", "sentinel",
            "skittish", "sleeper", "stray", "territorial", "wildlife", "yard_fowl",
        }, setup.Roles.Keys);

        // The content bible's five archetypes, each a creature of its own with its own body and blow (§10) - and the start area's wildlife
        // and its two great grazers. The Ring Custodian is M8 I4's, inside the gated Barrowworks; the gate tender and the Gate Warden keep the Spill; the hunting spider matriarch keeps the hollow under the den.
        Assert.Equal(new[]
            {
                "creature.beast.ash_ember_hound", "creature.beast.bristleback_boar", "creature.beast.buck", "creature.beast.cave_hunting_spider",
                "creature.beast.chicken", "creature.beast.doe", "creature.beast.hound_pup", "creature.beast.hunting_spider_matriarch", "creature.beast.rabbit", "creature.beast.red_fox", "creature.beast.rooster", "creature.beast.sedge_eel_serpent",
                "creature.beast.wolf_grey", "creature.beast.wolf_grey_alpha", "creature.construct.animated_armour", "creature.construct.gate_tender", "creature.construct.gate_warden",
                "creature.construct.ring_custodian",
                "creature.grazer.barrowback", "creature.grazer.fellhorn", "creature.humanoid.back_ledger_raider", "creature.undead.bone_walker_husk", "creature.undead.sedge_drowned",
            }, setup.Creatures.Keys);
        var barrowback = setup.Creatures["creature.grazer.barrowback"];
        Assert.Equal((5, 220, 1_400L, 16_000L), (barrowback.Level, barrowback.MaxHealth, barrowback.RadiusMm, barrowback.Charge!.ReachMm));
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
        AssertRefused("spawns/hollow/valley_strays.yaml", "count:\n  - 2\n  - 2", "count:\n  - 1\n  - 3", "fixed count");

    [Theory]
    [InlineData("spawns/moor/barrow_sentinel.yaml", "level_band:\n- 5\n- 5", "level_band:\n- 3\n- 3")]
    [InlineData("spawns/moor/birch_boar.yaml", "level_band:\n- 4\n- 4", "level_band:\n- 2\n- 2")]
    [InlineData("spawns/moor/moor_strays.yaml", "level_band:\n- 2\n- 2", "level_band:\n- 1\n- 2")]
    [InlineData("spawns/road/barrow_husks.yaml", "level_band: [3, 3]", "level_band: [4, 4]")]
    [InlineData("spawns/hollow/den_pack.yaml", "level_band:\n- 2\n- 3", "level_band:\n- 2\n- 2")]
    [InlineData("spawns/road/wayside_pack.yaml", "level_band: [2, 2]", "level_band: [3, 3]")]
    public void ASpawnBandThatMisstatesItsMembers_IsALintError(string path, string correct, string stale) =>
        AssertRefused(path, correct, stale, "level_band must match its members");

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
