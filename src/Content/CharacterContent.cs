// UNNAMED Content - peoples and Calls from content: what the character creator offers (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.2, WP2)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Character;
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
/// least one body of each type it defines.
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
        if (peoples is not null && loader.GetByKind("call").Count > 0 && !peoples.Values.Any(p => p.Playable))
            errors.Add(Error("calls: the pack defines Calls but no playable people to make a character from"));
        return errors;
    }

    /// <summary>The creator's peoples and Calls; empty for a pack with none (test fixtures).</summary>
    public static CharacterSetup Build(ContentLoader loader) => new(BuildPeoples(loader), BuildCalls(loader)) { Bodies = BuildBodies(loader) };

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
        var peoples = loader.GetByKind("people");
        var catalog = peoples.Count == 0 ? ItemCatalog.Empty : ItemContent.BuildCatalog(loader);
        var built = peoples.Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            var names = List(map, "suggest_names").Select((name, i) =>
                name as string ?? throw new FormatException($"{definition.Id} suggest_names[{i}] must be a name")).ToList();
            if (names.Count == 0)
                throw new FormatException($"{definition.Id} lists no names for Suggest");
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
                throw new FormatException($"{definition.Id} lists a name twice");
            bool playable = Flag(map, "playable");
            var heritage = map.ContainsKey("heritage") ? Heritage(definition.Id, map, catalog) : ImmutableArray<StartingItem>.Empty;
            if (playable && heritage.IsEmpty)
                throw new FormatException($"{definition.Id} is playable and gives no heritage");
            return new PeopleDefinition(definition.Id, playable, new NameSource(names), heritage);
        }).ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);
        // An item marked as heritage is one its people gives, so none is left that no people's keeper can re-issue.
        foreach (var item in catalog.Definitions.Values.Where(i => i.Heritage is not null))
        {
            if (!built.TryGetValue(item.Heritage!, out var people) || people.Heritage.All(h => h.ItemId != item.Id))
                throw new FormatException($"{item.Id} is marked heritage: {item.Heritage}, which no people gives as its heritage");
        }
        return built;
    }

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

        return new CallDefinition(id, Text(map, "tagline"), Text(map, "description"), new StartingPackage(bias, skills, ImmutableArray<string>.Empty), kit,
            new CallContact(Defined(loader, Text(contact, "npc_ref"), "npc", $"{id} contact"), trust));
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
