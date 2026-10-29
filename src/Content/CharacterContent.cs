// UNNAMED Content - peoples and Calls from content: what the character creator offers (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.2, WP2)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the creator's <see cref="CharacterSetup"/> and lints what the reference pass cannot see (CHR001): a people lists the names its
/// Suggest draws, each a name a character could take; a Call spends exactly <see cref="CallDefinition.BiasPoints"/> attribute points,
/// starts exactly <see cref="CallDefinition.SkillCount"/> defined skills at <see cref="CallDefinition.SkillLevel"/>, adds kit that is
/// one stack each and wears nothing over the common starting kit, and names a defined NPC as its contact. A pack with Calls needs a
/// playable people, and a playable people gives a heritage kit of keepsakes that carry no power. Calls are built only from items, skills and NPCs that already exist (C11): nothing here is new lore.
/// A body (WP17) names a defined people, a type and a build with their labels, a build order unique within its people and type, its
/// tape measurements in whole millimetres, its joints' rest offsets and its arm splay (0-30 degrees); every playable people offers at
/// least one body of each type it defines. A people's traits (RACE_ABILITIES_PROPOSAL.md) net the same creation grant as every other
/// people's, name only the aptitude groups <c>config.aptitudes</c> defines, and its racial abilities (<c>ability</c> of <c>class: people</c>)
/// name it. Origins (<c>config.origins</c>) name the default origin.
/// </summary>
public static class CharacterContent
{
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        ImmutableSortedDictionary<string, PeopleDefinition>? peoples = null;
        Try(() => peoples = BuildPeoples(loader), "people", errors);
        Try(() => BuildCalls(loader), "calls", errors);
        Try(() => BuildBodies(loader), "bodies", errors);
        Try(() => BuildOrigins(loader), "config.origins", errors);
        if (peoples is not null && loader.GetByKind("call").Count > 0 && !peoples.Values.Any(p => p.Playable))
            errors.Add(Error("calls: the pack defines Calls but no playable people to make a character from"));
        return errors;
    }

    /// <summary>The creator's peoples and Calls; empty for a pack with none (test fixtures).</summary>
    public static CharacterSetup Build(ContentLoader loader) => new(BuildPeoples(loader), BuildCalls(loader))
    {
        Bodies = BuildBodies(loader),
        Abilities = BuildPeopleAbilities(loader),
        Origins = BuildOrigins(loader),
    };

    /// <summary>The origins a character can come from; empty for a pack without <c>config.origins</c>.</summary>
    public static ImmutableSortedDictionary<string, OriginDefinition> BuildOrigins(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.origins"))
            return ImmutableSortedDictionary.Create<string, OriginDefinition>(StringComparer.Ordinal);
        var origins = Map(Config(loader, "config.origins"), "origins").ToImmutableSortedDictionary(kv =>
        {
            string id = kv.Key as string ?? "";
            return UNNAMED.Domain.DefinitionId.IsValid(id) && id.StartsWith("origin.", StringComparison.Ordinal) ? id : throw new FormatException($"'{id}' is not an origin ID (origin.*)");
        }, kv =>
        {
            var origin = kv.Value as Dictionary<object, object> ?? throw new FormatException($"{kv.Key} must be a map");
            return new OriginDefinition((string)kv.Key, Text(origin, "name"), Text(origin, "description"))
            {
                StartLocationId = origin.ContainsKey("start_location")
                    ? Defined(loader, Text(origin, "start_location"), "location", $"{kv.Key} start_location")
                    : null,
            };
        }, StringComparer.Ordinal);
        if (!origins.ContainsKey(CharacterSpec.DefaultOrigin))
            throw new FormatException($"config.origins must define {CharacterSpec.DefaultOrigin}, the origin every older character has");
        return origins;
    }

    /// <summary>
    /// The racial abilities (<c>ability</c> of <c>class: people</c>): a <c>people_ref</c>, an <c>action</c>, a <c>description</c>, and the
    /// numbers its action reads - <c>cost</c> (stamina, focus), <c>stamina_per_s</c>, <c>cooldown_s</c>, <c>duration_s</c>, <c>range_m</c>,
    /// <c>speed_m_s</c>, <c>sink_m_s</c>, <c>apex_bonus_m</c>, <c>distance_percent</c>, <c>toll</c>, <c>effect_ref</c>, <c>chance_percent</c>,
    /// <c>sprint_s</c>.
    /// </summary>
    public static ImmutableSortedDictionary<string, PeopleAbility> BuildPeopleAbilities(ContentLoader loader)
    {
        var racial = loader.GetByKind("ability").Values
            .Select(d => (Definition: d, Map: Read(d.YamlSource)))
            .Where(a => a.Map.GetValueOrDefault("class") as string == "people")
            .ToList();
        int tickMs = racial.Count > 0 ? WorldContent.TickMilliseconds(loader) : 0;
        return racial
            .Select(a =>
            {
                string id = a.Definition.Id;
                var map = a.Map;
                double Opt(string key, double fallback = 0) => map.ContainsKey(key) ? Number(map, key) : fallback;
                var cost = map.GetValueOrDefault("cost") as Dictionary<object, object> ?? new Dictionary<object, object>();
                if (cost.Keys.Any(k => k as string is not ("stamina" or "focus")))
                    throw new FormatException($"{id}: a racial ability costs stamina and focus, nothing else - there is no mana");
                var action = Enum.TryParse<PeopleAction>(Text(map, "action").Replace("_", ""), ignoreCase: true, out var parsed)
                    ? parsed
                    : throw new FormatException($"{id}: action '{Text(map, "action")}' is not built ({string.Join(", ", Enum.GetNames<PeopleAction>())})");
                var ability = new PeopleAbility(id, Defined(loader, Text(map, "people_ref"), "people", $"{id} people_ref"), Text(map, "name"),
                    Text(map, "description"), action)
                {
                    StaminaCost = cost.ContainsKey("stamina") ? Int(cost, "stamina") : 0,
                    FocusCost = cost.ContainsKey("focus") ? Int(cost, "focus") : 0,
                    StaminaPerSecond = map.ContainsKey("stamina_per_s") ? Int(map, "stamina_per_s") : 0,
                    CooldownTicks = ToTicks(Opt("cooldown_s"), tickMs),
                    DurationTicks = ToTicks(Opt("duration_s"), tickMs),
                    RangeMm = (long)Math.Round(Opt("range_m") * 1000),
                    SpeedMmPerSecond = (long)Math.Round(Opt("speed_m_s") * 1000),
                    SinkMmPerSecond = (long)Math.Round(Opt("sink_m_s") * 1000),
                    ApexBonusMm = (long)Math.Round(Opt("apex_bonus_m") * 1000),
                    DistancePercent = (int)Opt("distance_percent", 100),
                    Toll = map.ContainsKey("toll") ? Int(map, "toll") : 0,
                    ChancePercent = map.ContainsKey("chance_percent") ? Int(map, "chance_percent") : 0,
                    SprintTicks = ToTicks(Opt("sprint_s"), tickMs),
                    EffectId = map.ContainsKey("effect_ref") ? Defined(loader, Text(map, "effect_ref"), "effect", $"{id} effect_ref") : null,
                };
                if (!id.StartsWith(ability.PeopleId.Replace("people.", "ability.people.") + ".", StringComparison.Ordinal))
                    throw new FormatException($"{id}: a racial ability is named ability.people.<people>.<name> after its people_ref");
                return ability.Problem() is { } problem ? throw new FormatException($"{id}: {problem}") : ability;
            })
            .ToImmutableSortedDictionary(a => a.Id, a => a, StringComparer.Ordinal);
    }

    public static ImmutableSortedDictionary<string, BodyPreset> BuildBodies(ContentLoader loader)
    {
        var bodies = loader.GetByKind("body").Values.Select(definition =>
        {
            string id = definition.Id;
            var map = Read(definition.YamlSource);
            int order = Positive(map.GetValueOrDefault("order"), $"{id} order");
            int splay = int.TryParse(map.GetValueOrDefault("arm_splay_deg") as string, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int deg) && deg is >= 0 and <= 30
                ? deg
                : throw new FormatException($"{id} arm_splay_deg must be a whole number of degrees, 0 to 30");
            var measured = Map(map, "measurements_mm").ToImmutableSortedDictionary(
                kv => kv.Key as string ?? "", kv => Positive(kv.Value, $"{id} measurements_mm.{kv.Key}"), StringComparer.Ordinal);
            var offsets = Map(map, "rest_offsets_mm").ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv =>
                kv.Value is List<object> { Count: 3 } xyz
                    ? new BoneOffset(IntOf(xyz, 0, $"{id} rest_offsets_mm.{kv.Key}"), IntOf(xyz, 1, $"{id} rest_offsets_mm.{kv.Key}"), IntOf(xyz, 2, $"{id} rest_offsets_mm.{kv.Key}"))
                    : throw new FormatException($"{id} rest_offsets_mm.{kv.Key} must be [x, y, z]"), StringComparer.Ordinal);
            return new BodyPreset(id, Defined(loader, Text(map, "people_ref"), "people", $"{id} people_ref"), Text(map, "type"), Text(map, "type_label"),
                Text(map, "build"), Text(map, "build_label"), order, Flag(map, "offered"), splay, measured, offsets);
        }).ToImmutableSortedDictionary(b => b.Id, b => b, StringComparer.Ordinal);
        foreach (var body in bodies.Values)
        {
            if (body.Id != $"body.{body.Type}.{body.Build}")
                throw new FormatException($"{body.Id} must be named body.<type>.<build> (body.{body.Type}.{body.Build})");
        }
        foreach (var clash in bodies.Values.GroupBy(b => (b.PeopleId, b.Type, b.Order)).Where(g => g.Count() > 1))
            throw new FormatException($"{string.Join(" and ", clash.Select(b => b.Id))} share build order {clash.Key.Order}");
        foreach (var type in bodies.Values.GroupBy(b => (b.PeopleId, b.Type)).Where(g => !g.Any(b => b.Offered)))
            throw new FormatException($"{type.Key.PeopleId} offers no {type.Key.Type} body");
        return bodies;
    }

    public static ImmutableSortedDictionary<string, PeopleDefinition> BuildPeoples(ContentLoader loader)
    {
        var groups = AptitudeGroups(loader);
        var abilities = BuildPeopleAbilities(loader);
        var definitions = loader.GetByKind("people");
        var catalog = definitions.Count == 0 ? ItemCatalog.Empty : ItemContent.BuildCatalog(loader);
        var peoples = definitions.Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            var names = List(map, "suggest_names").Select((name, i) =>
                name as string ?? throw new FormatException($"{definition.Id} suggest_names[{i}] must be a name")).ToList();
            if (names.Count == 0)
                throw new FormatException($"{definition.Id} lists no names for Suggest");
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
                throw new FormatException($"{definition.Id} lists a name twice");
            var traits = Traits(definition.Id, map, groups);
            var card = map.GetValueOrDefault("card") is Dictionary<object, object> c
                ? new PeopleCard(Text(c, "epithet"), Text(c, "summary"), Text(c, "body"), Text(c, "leans_toward"), Text(c, "trade_off"))
                : null;
            bool playable = Flag(map, "playable");
            var heritage = map.ContainsKey("heritage") ? Heritage(definition.Id, map, catalog) : ImmutableArray<StartingItem>.Empty;
            if (playable && heritage.IsEmpty)
                throw new FormatException($"{definition.Id} is playable and gives no heritage");
            return new PeopleDefinition(definition.Id, playable, new NameSource(names))
            {
                Heritage = heritage,
                Traits = traits,
                Card = card,
                Abilities = abilities.Values.Where(a => a.PeopleId == definition.Id).Select(a => a.Id).ToImmutableArray(),
            };
        }).ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);
        var grants = peoples.Values.Where(p => p.Traits != PeopleTraits.None).Select(p => p.Traits.GrantTotal).Distinct().ToList();
        if (grants.Count > 1)
            throw new FormatException($"every people's creation grant nets the same points; these net {string.Join(", ", grants)}");
        // An item marked as heritage is one its people gives, so none is left that no people's keeper can re-issue.
        foreach (var item in catalog.Definitions.Values.Where(i => i.Heritage is not null))
        {
            if (!peoples.TryGetValue(item.Heritage!, out var people) || people.Heritage.All(h => h.ItemId != item.Id))
                throw new FormatException($"{item.Id} is marked heritage: {item.Heritage}, which no people gives as its heritage");
        }
        return peoples;
    }

    /// <summary>The skills each aptitude group of <c>config.aptitudes</c> covers; none for a pack without it.</summary>
    private static ImmutableSortedDictionary<string, ImmutableArray<string>> AptitudeGroups(ContentLoader loader) =>
        !loader.Definitions.ContainsKey("config.aptitudes")
            ? ImmutableSortedDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal)
            : Map(Config(loader, "config.aptitudes"), "groups").ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv =>
                (kv.Value as List<object> ?? throw new FormatException($"config.aptitudes groups.{kv.Key} must be a list of skill IDs"))
                .Select(skill => skill as string is { } s && ProgressionKeys.IsSkillId(s) ? s : throw new FormatException($"config.aptitudes groups.{kv.Key}: '{skill}' is not a skill ID"))
                .ToImmutableArray(), StringComparer.Ordinal);

    /// <summary>
    /// A people's <c>traits</c> and <c>learning</c> blocks. <c>damage_taken_percent</c> may name <c>physical</c> for all three physical types; a
    /// skill in several aptitude groups takes the best leaning among them and the worst limit.
    /// </summary>
    private static PeopleTraits Traits(string id, Dictionary<object, object> map, ImmutableSortedDictionary<string, ImmutableArray<string>> groups)
    {
        if (!map.ContainsKey("traits") && !map.ContainsKey("learning"))
            return PeopleTraits.None;
        var t = map.GetValueOrDefault("traits") as Dictionary<object, object> ?? new Dictionary<object, object>();
        var l = map.GetValueOrDefault("learning") as Dictionary<object, object> ?? new Dictionary<object, object>();
        int Percent(Dictionary<object, object> m, string key) => m.ContainsKey(key) ? Int(m, key) : 100;
        var taken = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in t.GetValueOrDefault("damage_taken_percent") as Dictionary<object, object> ?? new Dictionary<object, object>())
        {
            int percent = ParseInt(value, $"{id} damage_taken_percent.{key}");
            foreach (string type in key as string == "physical" ? DamageTypes.All.Where(DamageTypes.IsPhysical) : new[] { Damage(key as string ?? "") })
                taken[type] = percent;
        }
        var leaning = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var byGroup = (l.GetValueOrDefault("aptitude_percent") as Dictionary<object, object> ?? new Dictionary<object, object>())
            .Select(kv => (Skills: groups.GetValueOrDefault(kv.Key as string ?? "")
                    is { IsDefault: false } skills ? skills : throw new FormatException($"{id} aptitude_percent: '{kv.Key}' is not a group config.aptitudes defines"),
                Percent: ParseInt(kv.Value, $"{id} aptitude_percent.{kv.Key}")))
            .ToList();
        foreach (string skill in byGroup.SelectMany(g => g.Skills).Distinct(StringComparer.Ordinal))
        {
            var percents = byGroup.Where(g => g.Skills.Contains(skill)).Select(g => g.Percent).ToList();
            leaning[skill] = Math.Max(100, percents.Max()) * Math.Min(100, percents.Min()) / 100;
        }
        var traits = new PeopleTraits
        {
            HealthPercent = Percent(t, "health_percent"),
            DamageTakenPercent = taken.ToImmutable(),
            SpeedPercent = Percent(t, "speed_percent"),
            CarryGrams = t.ContainsKey("carry_kg") ? Int(t, "carry_kg") * 1000L : 0,
            ResonancePercent = Percent(t, "resonance_percent"),
            StaggerThresholdPercent = Percent(t, "stagger_threshold_percent"),
            ArmorIgnoredPercent = t.ContainsKey("armor_ignored_percent") ? Int(t, "armor_ignored_percent") : 0,
            MantleBonusMm = t.ContainsKey("mantle_bonus_m") ? Mm(t, "mantle_bonus_m") : 0,
            Regenerates = !t.ContainsKey("regenerates") || Flag(t, "regenerates"),
            NoisePercent = Percent(t, "noise_percent"),
            AttributeGrant = (t.GetValueOrDefault("attribute_grant") as Dictionary<object, object> ?? new Dictionary<object, object>())
                .ToImmutableSortedDictionary(kv => ProgressionKeys.ParseAttribute(kv.Key as string ?? ""), kv => ParseInt(kv.Value, $"{id} attribute_grant.{kv.Key}")),
            FreeAttributePoints = t.ContainsKey("free_attribute_points") ? Int(t, "free_attribute_points") : 0,
            FreeAttributeDefault = t.ContainsKey("free_attribute_default") ? ProgressionKeys.ParseAttribute(Text(t, "free_attribute_default")) : CharacterAttribute.Endurance,
            Learning = new LearningAptitude
            {
                RateBands = (l.GetValueOrDefault("rate_by_band") as List<object> ?? new List<object>()).Select((row, i) =>
                    row is List<object> { Count: 2 } pair
                        ? new RateBand(IntOf(pair, 0, $"{id} rate_by_band[{i}]"), IntOf(pair, 1, $"{id} rate_by_band[{i}]"))
                        : throw new FormatException($"{id} rate_by_band[{i}] must be [from_level, percent]")).ToImmutableArray(),
                SkillPercent = leaning.ToImmutable(),
                CommonCeiling = l.ContainsKey("common_ceiling") ? Int(l, "common_ceiling") : null,
                MasteryCeiling = l.ContainsKey("mastery_ceiling") ? Int(l, "mastery_ceiling") : null,
                NoveltyPercent = Percent(l, "novelty_percent"),
            },
        };
        return traits.Problem() is { } problem ? throw new FormatException($"{id} traits: {problem}") : traits;
    }

    private static int ParseInt(object? value, string what) =>
        int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new FormatException($"'{what}' must be a whole number");

    /// <summary>
    /// A people's heritage kit (owner, 2026-10-23 and 2026-10-24): one stack of each keepsake, never worn, marked as that people's heritage
    /// so it changes hands only within the people, never dropped (the keeper re-issues a lost one), and identity rather than power - no
    /// slot, armour, weapon, bag or pet - so no people starts stronger than another.
    /// </summary>
    private static ImmutableArray<StartingItem> Heritage(string id, Dictionary<object, object> map, ItemCatalog catalog) =>
        Rows(map, "heritage", id).Select((line, i) =>
        {
            var item = new StartingItem(Text(line, "item_ref"), Int(line, "count"), Flag(line, "equip"));
            var found = catalog.Find(item.ItemId) ?? throw new FormatException($"{id} heritage[{i}] names {item.ItemId}, which is not an item");
            if (item.Count < 1 || item.Count > found.StackMax)
                throw new FormatException($"{id} heritage[{i}]: {item.Count} of {item.ItemId} is not one stack (1..{found.StackMax})");
            if (found.Heritage != id)
                throw new FormatException($"{id} heritage[{i}]: {item.ItemId} must be marked heritage: {id}");
            if (item.Equip || found.Slot is not null || found.Weapon is not null || found.ArmorValue != 0 || found.Bag is not null || found.PetCreature is not null || !found.NoDrop)
                throw new FormatException($"{id} heritage[{i}]: {item.ItemId} must be identity, not power - no slot, armour, weapon, bag or pet, and never dropped");
            return item;
        }).ToImmutableArray();

    public static ImmutableSortedDictionary<string, CallDefinition> BuildCalls(ContentLoader loader)
    {
        var calls = loader.GetByKind("call");
        if (calls.Count == 0)
            return ImmutableSortedDictionary.Create<string, CallDefinition>(StringComparer.Ordinal);
        var catalog = ItemContent.BuildCatalog(loader);
        var common = ItemContent.BuildStartingKit(loader);
        var worn = common.Where(k => k.Equip).Select(k => catalog.Get(k.ItemId).Slot!.Value).ToHashSet();
        return calls.Values.Select(definition => Call(loader, definition, catalog, worn))
            .ToImmutableSortedDictionary(c => c.Id, c => c, StringComparer.Ordinal);
    }

    private static CallDefinition Call(ContentLoader loader, ContentEnvelope definition, ItemCatalog catalog, HashSet<EquipSlot> worn)
    {
        string id = definition.Id;
        var map = Read(definition.YamlSource);

        var bias = Map(map, "attribute_bias").ToImmutableSortedDictionary(
            kv => ProgressionKeys.ParseAttribute(kv.Key as string ?? ""), kv => Positive(kv.Value, $"{id} attribute_bias.{kv.Key}"));
        if (bias.Values.Sum() != CallDefinition.BiasPoints)
            throw new FormatException($"{id} spends {bias.Values.Sum()} attribute points; a Call spends exactly {CallDefinition.BiasPoints}");

        var skills = Map(map, "skills").ToImmutableSortedDictionary(
            kv => Defined(loader, kv.Key as string ?? "", "skill", $"{id} skills"), kv => Positive(kv.Value, $"{id} skills.{kv.Key}"),
            StringComparer.Ordinal);
        if (skills.Count != CallDefinition.SkillCount || skills.Values.Any(level => level != CallDefinition.SkillLevel))
            throw new FormatException($"{id} must start exactly {CallDefinition.SkillCount} skills, each at {CallDefinition.SkillLevel}");

        var kit = Rows(map, "kit", id).Select((line, i) =>
        {
            var item = new StartingItem(Text(line, "item_ref"), Int(line, "count"), Flag(line, "equip"));
            var found = catalog.Find(item.ItemId) ?? throw new FormatException($"{id} kit[{i}] names {item.ItemId}, which is not an item");
            if (item.Count < 1 || item.Count > found.StackMax)
                throw new FormatException($"{id} kit[{i}]: {item.Count} of {item.ItemId} is not one stack (1..{found.StackMax})");
            if (item.Equip && (found.Slot is not { } slot || worn.Contains(slot) || found.Weapon is { TwoHanded: true } && worn.Contains(EquipSlot.OffHand)))
                throw new FormatException($"{id} kit[{i}]: {item.ItemId} cannot be worn over the common starting kit");
            return item;
        }).ToImmutableArray();
        if (kit.IsEmpty)
            throw new FormatException($"{id} gives no kit");
        var slots = kit.Where(k => k.Equip).Select(k => catalog.Get(k.ItemId).Slot!.Value).ToList();
        if (slots.Distinct().Count() != slots.Count)
            throw new FormatException($"{id} wears two kit items in one slot");

        var contact = Map(map, "contact");
        int trust = Positive(contact.GetValueOrDefault("trust"), $"{id} contact.trust");
        if (trust != Relationships.Clamp(trust))
            throw new FormatException($"{id} contact.trust {trust} is outside {Relationships.Min}..{Relationships.Max}");

        var techniques = (map.ContainsKey("techniques") ? List(map, "techniques") : new List<object>())
            .Select((t, i) => Defined(loader, t as string ?? throw new FormatException($"{id} techniques[{i}] is a definition ID"), "ability", $"{id} techniques[{i}]"))
            .ToImmutableArray();
        if (techniques.Distinct(StringComparer.Ordinal).Count() != techniques.Length)
            throw new FormatException($"{id} names a starting technique twice");
        var recognition = map.GetValueOrDefault("recognition") is Dictionary<object, object> seen
            ? new CallRecognition(Defined(loader, Text(seen, "npc_ref"), "npc", $"{id} recognition"),
                Text(seen, "line").Length > 0 ? Text(seen, "line") : throw new FormatException($"{id} recognition.line is empty"))
            : null;
        return new CallDefinition(id, Text(map, "tagline"), Text(map, "description"),
            new StartingPackage(bias, skills, techniques), kit,
            new CallContact(Defined(loader, Text(contact, "npc_ref"), "npc", $"{id} contact"), trust))
        {
            Recognition = recognition,
        };
    }

    private static bool Flag(Dictionary<object, object> map, string key) => map.GetValueOrDefault(key) switch
    {
        null => false,
        "true" => true,
        "false" => false,
        var other => throw new FormatException($"'{key}' must be true or false, got '{other}'"),
    };

    private static int Positive(object? value, string what) =>
        int.TryParse(value as string, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : throw new FormatException($"'{what}' must be a whole number above 0");

    private static string Defined(ContentLoader loader, string id, string kind, string at) =>
        loader.Definitions.TryGetValue(id, out var definition) && (definition.Kind == kind || definition.Kind.StartsWith(kind + ".", StringComparison.Ordinal))
            ? id
            : throw new FormatException($"{at}: {id} is not a defined {kind}");

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error($"{what}: {e.Message}"));
        }
    }

    private static ValidationError Error(string message) =>
        new() { SeverityLevel = ValidationError.Severity.Error, Code = "CHR001", Message = message };
}
