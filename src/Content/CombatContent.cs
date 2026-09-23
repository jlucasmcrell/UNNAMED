// UNNAMED Content - combat from content: the damage constants, status effects, creatures and their attacks, spawners,
// behaviour roles, skill passives and item uses (DATA_MODEL.md §4.4, §4.7, §4.8, §4.17, §4.19, §4.21; PROTOTYPE.md §4.1,
// §4.4; M3c, M3d)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.World.Runtime;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// Builds the simulation's <see cref="CombatSetup"/> and lints what the reference and semantic passes cannot see (CMB001):
/// that every effect, creature attack, spawn site and passive builds, in whole ticks. Seconds in content become ticks
/// through <c>config.time</c>; effect durations are in game minutes (DATA_MODEL.md §4.8, §4.19). A pack without
/// <c>config.time</c> has no tick, and so no combat to check.
/// </summary>
public static class CombatContent
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    /// <summary>The stat targets Phase 1's effects and passives may name, and what each does.</summary>
    private static readonly string[] Stats = { "stat.damage_dealt", "stat.stamina_regen", "stat.armor", "stat.stagger_power" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.ContainsKey("config.time"))
            return errors;
        int tickMs;
        try
        {
            tickMs = WorldContent.TickMilliseconds(loader);
        }
        catch (Exception e) when (e is FormatException or KeyNotFoundException)
        {
            return errors;   // WLD008 reports a broken config.time
        }
        Try(() => BuildEffects(loader, tickMs), "effects", errors);
        Try(() => BuildCreatures(loader, tickMs), "creatures", errors);
        if (loader.Definitions.ContainsKey("config.damage_constants"))
            Try(() => BuildConstants(loader, tickMs), "config.damage_constants", errors);
        if (loader.Definitions.ContainsKey("config.creature_behaviour"))
            Try(() => BuildBehaviour(loader, tickMs), "config.creature_behaviour", errors);
        foreach (string region in loader.GetByKind("region").Keys.OrderBy(k => k, StringComparer.Ordinal))
            Try(() => BuildSpawns(loader, region), "spawns", errors);
        Try(() => BuildPassives(loader), "skills", errors);
        Try(() => BuildUseEffects(loader), "items", errors);
        return errors;
    }

    /// <summary>Everything combat needs for one region.</summary>
    public static CombatSetup Build(ContentLoader loader, string regionId)
    {
        int tickMs = WorldContent.TickMilliseconds(loader);
        var setup = new CombatSetup(BuildConstants(loader, tickMs), BuildEffects(loader, tickMs), BuildCreatures(loader, tickMs), BuildSpawns(loader, regionId))
        {
            UseEffects = BuildUseEffects(loader),
            Passives = BuildPassives(loader),
        };
        return loader.Definitions.ContainsKey("config.creature_behaviour") ? BuildBehaviour(loader, tickMs)(setup) : setup;
    }

    /// <summary>
    /// <c>config.creature_behaviour</c> (M3d): how awareness grows (STEALTH §3), how far sounds carry (§7), how long a
    /// corpse lies, and the behaviour roles - as a change to apply to a setup.
    /// </summary>
    public static Func<CombatSetup, CombatSetup> BuildBehaviour(ContentLoader loader, int tickMs)
    {
        var map = Config(loader, "config.creature_behaviour");
        var awareness = Map(map, "awareness");
        var noise = Map(map, "noise_m");
        var corpse = Map(map, "corpse");
        var rules = new AwarenessRules(Int(awareness, "suspicious"), Int(awareness, "sight_per_s_at_range"), Int(awareness, "sight_per_s_close"),
            Int(awareness, "decay_per_s"), Int(awareness, "heard_noise"), Int(awareness, "heard_call"), ToTicks(Number(awareness, "search_s"), tickMs));
        if (rules.Suspicious is <= 0 or >= Perception.Full || rules.HeardNoise < rules.Suspicious || rules.HeardCall < rules.HeardNoise
            || rules.HeardCall >= Perception.Full || rules.SightPerSecondAtRange <= 0 || rules.SightPerSecondClose < rules.SightPerSecondAtRange)
            throw new FormatException($"awareness needs 0 < suspicious <= heard_noise <= heard_call < {Perception.Full}, and sight rates rising with closeness");
        var sounds = new NoiseRules(Mm(noise, "walk"), Mm(noise, "run"), Mm(noise, "sprint"), Mm(noise, "swing"), Mm(noise, "blow"), Mm(noise, "call"));
        var roles = Map(map, "roles").ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv => Role(kv.Key as string ?? "", kv.Value), StringComparer.Ordinal);
        long decay = ToTicks(Number(corpse, "decay_s"), tickMs);
        int slots = Int(corpse, "stack_slots");
        if (decay < 1 || slots < 1)
            throw new FormatException("corpse needs decay_s above 0 and at least one stack slot");
        return setup => setup with { Awareness = rules, Noise = sounds, Roles = roles, CorpseDecayTicks = decay, CorpseStackSlots = slots };
    }

    private static CreatureRole Role(string id, object value)
    {
        var map = value as Dictionary<object, object> ?? throw new FormatException($"roles.{id} must be a map");
        var unaware = Text(map, "unaware") switch
        {
            "hold" => UnawareBehaviour.Hold,
            "wander" => UnawareBehaviour.Wander,
            "patrol" => UnawareBehaviour.Patrol,
            "sleep" => UnawareBehaviour.Sleep,
            var other => throw new FormatException($"roles.{id}: unaware '{other}' is not hold, wander, patrol or sleep"),
        };
        long Metres(string key) => map.ContainsKey(key) ? Mm(map, key) : 0;
        var role = new CreatureRole(id, unaware)
        {
            TerritoryMm = Metres("territory_m"),
            WanderMm = Metres("wander_m"),
            CallsForHelp = map.GetValueOrDefault("calls_for_help") as string == "true",
            AnswersCalls = map.GetValueOrDefault("answers_calls") as string == "true",
            KeepDistanceMm = Metres("keep_distance_m"),
            StrikeWithinMm = Metres("strike_within_m"),
            FleeBelowPercent = map.ContainsKey("flee_below_percent") ? Int(map, "flee_below_percent") : 0,
            SleepHearingPercent = map.ContainsKey("sleep_hearing_percent") ? Int(map, "sleep_hearing_percent") : 100,
            FlankMm = Metres("flank_m"),
            PounceOnNoise = map.GetValueOrDefault("pounce_on_noise") as string == "true",
        };
        if (role.Unaware == UnawareBehaviour.Wander && role.WanderMm <= 0)
            throw new FormatException($"roles.{id}: a wandering role gives wander_m");
        if (role.KeepDistanceMm > 0 && role.StrikeWithinMm >= role.KeepDistanceMm)
            throw new FormatException($"roles.{id}: strike_within_m is inside keep_distance_m");
        if (role.FleeBelowPercent is < 0 or >= 100 || role.SleepHearingPercent is < 0 or > 100)
            throw new FormatException($"roles.{id}: percentages are in [0, 100)");
        return role;
    }

    public static CombatConstants BuildConstants(ContentLoader loader, int tickMs)
    {
        var map = Config(loader, "config.damage_constants");
        int Ticks(Dictionary<object, object> group, string key) => ToTicks(Number(group, key), tickMs);
        var regions = Map(map, "regions");
        var weights = ImmutableSortedDictionary.CreateBuilder<BodyRegion, int>();
        var multipliers = ImmutableSortedDictionary.CreateBuilder<BodyRegion, double>();
        foreach (var (key, value) in regions)
        {
            var region = BodyRegions.Parse(key as string ?? string.Empty);
            var row = value as Dictionary<object, object> ?? throw new FormatException($"regions.{key} must be a map");
            weights[region] = Int(row, "weight");
            multipliers[region] = Number(row, "multiplier");
        }
        if (weights.Count != Enum.GetValues<BodyRegion>().Length || weights.Values.Any(w => w < 0) || weights.Values.Sum() <= 0)
            throw new FormatException("regions names head, torso and limbs, each with a non-negative weight, and the weights sum above 0");

        var stagger = Map(map, "stagger");
        var block = Map(map, "block");
        var dodge = Map(map, "dodge");
        var stamina = Map(map, "stamina");
        var health = Map(map, "health");
        var melee = Map(map, "melee");
        var ranged = Map(map, "ranged");
        var unarmedMap = Map(map, "unarmed");
        var unarmedDamage = List(unarmedMap, "damage");
        int unarmedTotal = Math.Max(3, ToTicks(Number(unarmedMap, "attack_time"), tickMs));
        int windupPercent = Int(melee, "windup_percent"), activePercent = Int(melee, "active_percent");
        int unarmedWindup = Math.Max(1, (int)Math.Round(unarmedTotal * windupPercent / 100.0, MidpointRounding.AwayFromZero));
        int unarmedActive = Math.Max(1, (int)Math.Round(unarmedTotal * activePercent / 100.0, MidpointRounding.AwayFromZero));
        var constants = new CombatConstants
        {
            ArmorK = Number(map, "armor_k"),
            PierceArmorIgnored = Number(map, "pierce_armor_ignored"),
            CritMultiplier = Number(map, "crit_multiplier"),
            BaseCritPercent = Int(map, "base_crit_percent"),
            RegionWeights = weights.ToImmutable(),
            RegionMultipliers = multipliers.ToImmutable(),
            StaggerThresholdPercent = Number(stagger, "threshold_percent"),
            StaggerTicks = Ticks(stagger, "duration_s"),
            StaggerImmunityTicks = Ticks(stagger, "immunity_s"),
            BlockMitigationPercent = Int(block, "mitigation_percent"),
            BlockStaminaPerHit = Int(block, "stamina_per_hit"),
            BlockArcMdeg = (long)Math.Round(Number(block, "arc_deg") * 1000),
            DodgeStaminaCost = Int(dodge, "stamina"),
            DodgeTicks = Ticks(dodge, "iframes_s"),
            DodgeRecoveryTicks = Ticks(dodge, "recovery_s"),
            DodgeDistanceMm = Mm(dodge, "distance_m"),
            StaminaRegenPerSecond = Int(stamina, "regen_per_s"),
            StaminaRegenDelayTicks = Ticks(stamina, "regen_delay_s"),
            SprintStaminaPerSecond = Int(stamina, "sprint_per_s"),
            DefaultStaminaCost = Int(stamina, "attack_default"),
            HealthRegenPerSecond = Int(health, "regen_per_s"),
            OutOfCombatTicks = Ticks(health, "out_of_combat_s"),
            MightDamagePercentPerPoint = Number(map, "might_damage_percent_per_point"),
            MeleeArcMdeg = (long)Math.Round(Number(melee, "arc_deg") * 1000),
            WindupPercent = windupPercent,
            ActivePercent = activePercent,
            RangedRangeMm = Mm(ranged, "range_m"),
            BowRecoveryTicks = Ticks(ranged, "recovery_s"),
            LeashMm = Mm(map, "creature_leash_m"),
            DeathEffect = Text(Map(map, "death"), "effect_ref"),
            Unarmed = new AttackProfile("unarmed", IntOf(unarmedDamage, 0, "unarmed.damage"), IntOf(unarmedDamage, 1, "unarmed.damage"),
                Damage(Text(unarmedMap, "damage_type")), Mm(unarmedMap, "reach"), unarmedWindup, unarmedActive,
                Math.Max(0, unarmedTotal - unarmedWindup - unarmedActive), Int(unarmedMap, "stamina_cost")),
        };
        if (constants.ArmorK <= 0 || constants.CritMultiplier < 1 || constants.BaseCritPercent is < 0 or > 100
            || constants.PierceArmorIgnored is < 0 or > 1 || constants.BlockMitigationPercent is < 0 or > 100
            || windupPercent + activePercent >= 100 || windupPercent <= 0 || activePercent <= 0 || constants.DodgeTicks < 1)
            throw new FormatException("config.damage_constants holds a value out of range (armor_k > 0, crit_multiplier >= 1, percentages in [0, 100], windup + active < 100, dodge i-frames of at least one tick)");
        return constants;
    }

    public static ImmutableSortedDictionary<string, EffectDefinition> BuildEffects(ContentLoader loader, int tickMs)
    {
        double secondsPerMinute = GameMinuteSeconds(loader);
        return loader.GetByKind("effect").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            var policy = Text(map, "stack_policy") switch
            {
                "refresh" => StackPolicy.Refresh,
                "stack_intensity" => StackPolicy.StackIntensity,
                var other => throw new FormatException($"{id}: stack_policy '{other}' is not built in Phase 1 (refresh, stack_intensity)"),
            };
            int Ticks(string key) => ToTicks(Number(map, key) * secondsPerMinute, tickMs);
            var effect = new EffectDefinition(id, Text(map, "category"), policy, Int(map, "max_stacks"), Ticks("duration_min"), Ticks("tick_interval_min"));
            if (effect.MaxStacks < 1 || effect.DurationTicks < 1 || effect.TickIntervalTicks < 0)
                throw new FormatException($"{id}: max_stacks >= 1 and a duration of at least one tick (permanent effects are not built in Phase 1)");
            foreach (var row in Rows(map, "on_tick", id))
            {
                effect = Text(row, "type") switch
                {
                    "damage" => effect with { DamagePerTick = Int(row, "amount"), DamageType = Damage(Text(row, "damage_type")) },
                    "heal" => effect with { HealPerTick = Int(row, "amount") },
                    var other => throw new FormatException($"{id}: on_tick type '{other}' is not built in Phase 1 (damage, heal)"),
                };
            }
            if ((effect.DamagePerTick > 0 || effect.HealPerTick > 0) && effect.TickIntervalTicks < 1)
                throw new FormatException($"{id}: an effect that ticks needs tick_interval_min");
            foreach (var row in Rows(map, "modifiers", id))
            {
                string target = Text(row, "target");
                double value = Number(row, "value");
                effect = (target, Text(row, "op")) switch
                {
                    ("stat.damage_dealt", "multiply") => effect with { DamageDealtMultiplier = value },
                    ("stat.stamina_regen", "multiply") => effect with { StaminaRegenMultiplier = value },
                    ("stat.armor", "add") => effect with { ArmorBonus = (int)Math.Round(value, MidpointRounding.AwayFromZero) },
                    var (t, op) => throw new FormatException($"{id}: modifier {t} {op} is not built in Phase 1 ({string.Join(", ", Stats)})"),
                };
            }
            return effect with { ImmuneTags = Tags(map, "immunity_tags") };
        }).ToImmutableSortedDictionary(e => e.Id, e => e, StringComparer.Ordinal);
    }

    private static ImmutableSortedSet<string> Tags(Dictionary<object, object> map, string key) =>
        (map.GetValueOrDefault(key) as List<object> ?? new List<object>()).OfType<string>().ToImmutableSortedSet(StringComparer.Ordinal);

    /// <summary>
    /// Creatures and their attacks: <c>attack_set</c>'s first creature ability is its blow, with its timing in ticks; a
    /// second, if it is a charge, is the charge it opens with.
    /// </summary>
    public static ImmutableSortedDictionary<string, CreatureDefinition> BuildCreatures(ContentLoader loader, int tickMs)
    {
        var abilities = loader.GetByKind("ability");
        return loader.GetByKind("creature").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            var band = List(map, "level_band");
            var set = List(map, "attack_set").OfType<string>().ToList();
            string abilityId = set.FirstOrDefault() ?? throw new FormatException($"{id}: attack_set names the creature's attack");
            var ability = abilities.GetValueOrDefault(abilityId) ?? throw new FormatException($"{id}: {abilityId} is not an ability");
            AttackProfile? charge = null;
            if (set.Count > 1)
            {
                var second = abilities.GetValueOrDefault(set[1]) ?? throw new FormatException($"{id}: {set[1]} is not an ability");
                charge = Attack(second, tickMs);
                if (!charge.IsCharge)
                    throw new FormatException($"{id}: a second attack is a charge (Phase 1 builds one blow and one charge)");
            }
            if (set.Count > 2)
                throw new FormatException($"{id}: Phase 1 builds at most a blow and a charge");
            var armor = map.ContainsKey("armor") ? Map(map, "armor") : new Dictionary<object, object>();
            var resistances = map.ContainsKey("resistances") && map["resistances"] is Dictionary<object, object> r ? r : new Dictionary<object, object>();
            var creature = new CreatureDefinition(id, Text(map, "family"), IntOf(band, 0, "level_band"), Int(Map(map, "pools"), "health"),
                armor.ToImmutableSortedDictionary(kv => BodyRegions.Parse(kv.Key as string ?? ""), kv => ParseInt(kv.Value, $"{id} armor.{kv.Key}")),
                resistances.ToImmutableSortedDictionary(kv => Damage(kv.Key as string ?? ""), kv => ParseNumber(kv.Value, $"{id} resistances.{kv.Key}"), StringComparer.Ordinal),
                Attack(ability, tickMs), Mm(map, "move_speed_m_s"), Mm(map, "body_radius_m"), map.ContainsKey("xp_value") ? Long(map, "xp_value") : 0)
            {
                LootTableId = map.GetValueOrDefault("loot_table") as string,
                Senses = Senses(id, map),
                Charge = charge,
                TurnMdegPerSecond = map.ContainsKey("turn_deg_s") ? (long)Math.Round(Number(map, "turn_deg_s") * 1000) : 720_000,
                WeakPoint = map.GetValueOrDefault("weak_point") is Dictionary<object, object> weak
                    ? new WeakPoint(BodyRegions.Parse(Text(weak, "region")), weak.GetValueOrDefault("from_behind") as string == "true")
                    : null,
                Tags = Tags(map, "tags"),
            };
            if (creature.Attack.IsCharge)
                throw new FormatException($"{id}: its first attack is a blow; a charge comes second");
            if (IntOf(band, 0, "level_band") != IntOf(band, 1, "level_band"))
                throw new FormatException($"{id}: Phase 1 authors one level per creature (level_band [n, n]); rolling in a band arrives with the spawner (M3d)");
            if (creature.MaxHealth < 1 || creature.MoveSpeedMmPerSecond <= 0 || creature.RadiusMm <= 0 || creature.XpValue < 0)
                throw new FormatException($"{id}: pools.health, move_speed_m_s and body_radius_m are positive, xp_value is not negative");
            return creature;
        }).ToImmutableSortedDictionary(c => c.Id, c => c, StringComparer.Ordinal);
    }

    private static AttackProfile Attack(ContentEnvelope ability, int tickMs)
    {
        var map = Read(ability.YamlSource);
        string id = ability.Id;
        if (Text(map, "class") != "creature")
            throw new FormatException($"{id}: a creature's attack is an ability of class creature");
        var payload = Rows(map, "payload", id);
        var damage = payload.FirstOrDefault(p => p.GetValueOrDefault("type") as string == "damage")
            ?? throw new FormatException($"{id}: payload needs a damage entry");
        var amount = List(damage, "amount");
        var effect = payload.FirstOrDefault(p => p.GetValueOrDefault("type") as string == "apply_effect");
        int stamina = map.GetValueOrDefault("cost") is Dictionary<object, object> cost && cost.ContainsKey("stamina") ? Int(cost, "stamina") : 0;
        var run = map.GetValueOrDefault("charge") as Dictionary<object, object>;
        var attack = new AttackProfile(id, IntOf(amount, 0, "amount"), IntOf(amount, 1, "amount"), Damage(Text(damage, "damage_type")),
            run is null ? Mm(map, "range_m") : Mm(run, "max_distance_m"), Math.Max(1, ToTicks(Number(map, "windup_s"), tickMs)),
            run is null ? Math.Max(1, ToTicks(Number(map, "active_s"), tickMs)) : 1, ToTicks(Number(map, "recovery_s"), tickMs), stamina)
        {
            OnHitEffect = effect is null ? null : Text(effect, "effect_ref"),
            OnHitEffectPercent = effect is null ? 0 : (int)Math.Round(Number(effect, "chance") * 100, MidpointRounding.AwayFromZero),
            LungeMm = map.ContainsKey("lunge_m") ? Mm(map, "lunge_m") : 0,
            ChargeSpeedMmPerSecond = run is null ? 0 : Mm(run, "speed_m_s"),
            ChargeMinRangeMm = run is null ? 0 : Mm(run, "min_range_m"),
            StunTicks = run is null ? 0 : ToTicks(Number(run, "stun_s"), tickMs),
            CooldownTicks = map.ContainsKey("cooldown_s") ? ToTicks(Number(map, "cooldown_s"), tickMs) : 0,
            ForcesStagger = map.GetValueOrDefault("forces_stagger") as string == "true",
            Advances = map.GetValueOrDefault("advance") as string == "true",
        };
        if (attack.DamageMin < 1 || attack.DamageMin > attack.DamageMax || attack.ReachMm <= 0 || attack.LungeMm < 0)
            throw new FormatException($"{id}: amount is [min, max] with 1 <= min <= max, range_m (or a charge's max_distance_m) is positive, lunge_m is not negative");
        if (run is not null && (attack.ChargeSpeedMmPerSecond <= 0 || attack.ChargeMinRangeMm >= attack.ReachMm || attack.StunTicks < 1))
            throw new FormatException($"{id}: a charge has a speed, a min_range_m below its max_distance_m, and a stun");
        return attack;
    }

    /// <summary>A region's spawn sites (Phase 1: a fixed count at a point, no respawn).</summary>
    public static ImmutableArray<SpawnSite> BuildSpawns(ContentLoader loader, string regionId)
    {
        var creatures = loader.GetByKind("creature");
        var roles = loader.Definitions.TryGetValue("config.creature_behaviour", out var behaviour)
            ? (Read(behaviour.YamlSource).GetValueOrDefault("roles") as Dictionary<object, object>)?.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal)
            : null;
        return loader.GetByKind("spawn").Values
            .Select(definition => (Definition: definition, Map: Read(definition.YamlSource)))
            .Where(s => s.Map.GetValueOrDefault("region_ref") as string == regionId)
            .OrderBy(s => s.Definition.Id, StringComparer.Ordinal)
            .Select(s =>
            {
                string id = s.Definition.Id;
                var at = Map(s.Map, "at");
                var position = List(at, "position_m");
                var members = Rows(s.Map, "creatures", id).SelectMany(row =>
                {
                    string creature = Text(row, "creature_ref");
                    if (!creatures.ContainsKey(creature))
                        throw new FormatException($"{id}: {creature} is not a creature");
                    var count = List(row, "count");
                    if (IntOf(count, 0, "count") != IntOf(count, 1, "count") || IntOf(count, 0, "count") < 1)
                        throw new FormatException($"{id}: Phase 1 places a fixed count (count [n, n], n >= 1)");
                    string role = row.GetValueOrDefault("role") as string ?? "hold";
                    if (roles is not null && !roles.Contains(role))
                        throw new FormatException($"{id}: role '{role}' is not a role config.creature_behaviour defines");
                    return Enumerable.Repeat(new SpawnMember(creature, role), IntOf(count, 0, "count"));
                }).ToImmutableArray();
                long respawn = 0;
                if (s.Map.GetValueOrDefault("respawn") is Dictionary<object, object> timer)
                {
                    respawn = Text(timer, "kind") switch
                    {
                        "none" => 0,
                        "timer" => Long(timer, "window_ticks"),
                        var other => throw new FormatException($"{id}: respawn kind '{other}' is not built in Phase 1 (timer, none)"),
                    };
                    if (respawn < 0)
                        throw new FormatException($"{id}: window_ticks is not negative");
                }
                var route = (s.Map.ContainsKey("route_m") ? List(s.Map, "route_m") : new List<object>()).Select((point, i) =>
                {
                    var xz = point as List<object> ?? throw new FormatException($"{id} route_m[{i}] must be [x, z]");
                    return xz.Count == 2 ? (ToMm(xz[0], "route_m"), ToMm(xz[1], "route_m")) : throw new FormatException($"{id} route_m[{i}] must be [x, z]");
                }).ToImmutableArray();
                return new SpawnSite(id, ToMm(position[0], "position_m"), ToMm(position[1], "position_m"), Mm(at, "radius_m"), members)
                {
                    RespawnTicks = respawn,
                    Route = route,
                };
            }).ToImmutableArray();
    }

    /// <summary>A creature's senses (DATA_MODEL.md §4.4 <c>perception</c>): sight and hearing in metres, the field of view in degrees.</summary>
    private static Senses Senses(string id, Dictionary<object, object> map)
    {
        if (map.GetValueOrDefault("perception") is not Dictionary<object, object> perception)
            return new Senses(25_000, 140_000, 30_000);
        var senses = new Senses(Mm(perception, "sight_m"), perception.ContainsKey("fov_deg") ? (long)Math.Round(Number(perception, "fov_deg") * 1000) : 140_000,
            Mm(perception, "hearing_m"));
        if (senses.SightMm < 0 || senses.HearingMm < 0 || senses.FieldOfViewMdeg is <= 0 or > 360_000)
            throw new FormatException($"{id}: perception needs non-negative ranges and a field of view in (0, 360]");
        return senses;
    }

    /// <summary>Skill passives (<c>passives: [{at, modifiers}]</c>): the one-hand blade's steadier stagger at level 3.</summary>
    public static ImmutableArray<SkillPassive> BuildPassives(ContentLoader loader) =>
        loader.GetByKind("skill").Values.OrderBy(d => d.Id, StringComparer.Ordinal).SelectMany(definition =>
        {
            var map = Read(definition.YamlSource);
            return Rows(map, "passives", definition.Id).SelectMany(passive => Rows(passive, "modifiers", definition.Id).Select(modifier =>
            {
                string target = Text(modifier, "target");
                if (!Stats.Contains(target) || Text(modifier, "op") != "multiply")
                    throw new FormatException($"{definition.Id}: passive modifier {target} {modifier.GetValueOrDefault("op")} is not built in Phase 1");
                return new SkillPassive(definition.Id, Int(passive, "at"), target, Number(modifier, "value"));
            }));
        }).ToImmutableArray();

    /// <summary>What using a consumable applies: <c>use: { effect_ref }</c>.</summary>
    public static ImmutableSortedDictionary<string, string> BuildUseEffects(ContentLoader loader) =>
        loader.GetByKind("item").Values
            .Select(d => (d.Id, Use: Read(d.YamlSource).GetValueOrDefault("use") as Dictionary<object, object>))
            .Where(d => d.Use?.GetValueOrDefault("effect_ref") is string)
            .ToImmutableSortedDictionary(d => d.Id, d => (string)d.Use!["effect_ref"], StringComparer.Ordinal);

    // ── reading (shared with MagicContent) ─────────────────────────────────

    private static double GameMinuteSeconds(ContentLoader loader) => Number(Config(loader, "config.time"), "seconds_per_game_minute");

    internal static int ToTicks(double seconds, int tickMs) =>
        seconds >= 0 ? (int)Math.Round(seconds * 1000 / tickMs, MidpointRounding.AwayFromZero) : throw new FormatException("durations are never negative");

    internal static string Damage(string type) =>
        DamageTypes.All.Contains(type) ? type : throw new FormatException($"'{type}' is not a damage type ({string.Join(", ", DamageTypes.All)})");

    internal static IEnumerable<Dictionary<object, object>> Rows(Dictionary<object, object> map, string key, string id) =>
        map.TryGetValue(key, out var value) && value is List<object> rows
            ? rows.Select((row, i) => row as Dictionary<object, object> ?? throw new FormatException($"{id} {key}[{i}] must be a map"))
            : Enumerable.Empty<Dictionary<object, object>>();

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "CMB001", Message = $"{what}: {e.Message}" });
        }
    }

    internal static Dictionary<object, object> Config(ContentLoader loader, string id) =>
        loader.Definitions.TryGetValue(id, out var definition)
            ? Read(definition.YamlSource)
            : throw new KeyNotFoundException($"{id} is missing (DATA_MODEL.md §4.19)");

    internal static Dictionary<object, object> Read(string? yaml) =>
        string.IsNullOrEmpty(yaml) ? new Dictionary<object, object>() : Yaml.Deserialize<Dictionary<object, object>>(yaml) ?? new Dictionary<object, object>();

    internal static Dictionary<object, object> Map(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is Dictionary<object, object> inner ? inner : throw new KeyNotFoundException($"'{key}' must be a map");

    internal static List<object> List(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is List<object> list ? list : throw new KeyNotFoundException($"'{key}' must be a list");

    internal static string Text(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : throw new KeyNotFoundException($"'{key}' is missing");

    internal static int Int(Dictionary<object, object> map, string key) => ParseInt(map.GetValueOrDefault(key), key);

    private static long Long(Dictionary<object, object> map, string key) =>
        long.TryParse(map.GetValueOrDefault(key) as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FormatException($"'{key}' must be a whole number");

    internal static double Number(Dictionary<object, object> map, string key) => ParseNumber(map.GetValueOrDefault(key), key);

    /// <summary>Metres in content, whole millimetres in the domain.</summary>
    internal static long Mm(Dictionary<object, object> map, string key) => (long)Math.Round(Number(map, key) * 1000, MidpointRounding.AwayFromZero);

    private static long ToMm(object value, string what) => (long)Math.Round(ParseNumber(value, what) * 1000, MidpointRounding.AwayFromZero);

    internal static int IntOf(List<object> cells, int i, string what) =>
        i < cells.Count ? ParseInt(cells[i], what) : throw new FormatException($"'{what}' needs {i + 1} values");

    private static int ParseInt(object? value, string what) =>
        int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new FormatException($"'{what}' must be a whole number");

    private static double ParseNumber(object? value, string what) =>
        double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : throw new FormatException($"'{what}' must be a number");
}
