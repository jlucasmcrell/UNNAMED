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
/// playable people. Calls are built only from items, skills and NPCs that already exist (C11): nothing here is new lore.
/// </summary>
public static class CharacterContent
{
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        ImmutableSortedDictionary<string, PeopleDefinition>? peoples = null;
        Try(() => peoples = BuildPeoples(loader), "people", errors);
        Try(() => BuildCalls(loader), "calls", errors);
        if (peoples is not null && loader.GetByKind("call").Count > 0 && !peoples.Values.Any(p => p.Playable))
            errors.Add(Error("calls: the pack defines Calls but no playable people to make a character from"));
        return errors;
    }

    /// <summary>The creator's peoples and Calls; empty for a pack with none (test fixtures).</summary>
    public static CharacterSetup Build(ContentLoader loader) => new(BuildPeoples(loader), BuildCalls(loader));

    public static ImmutableSortedDictionary<string, PeopleDefinition> BuildPeoples(ContentLoader loader) =>
        loader.GetByKind("people").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            var names = List(map, "suggest_names").Select((name, i) =>
                name as string ?? throw new FormatException($"{definition.Id} suggest_names[{i}] must be a name")).ToList();
            if (names.Count == 0)
                throw new FormatException($"{definition.Id} lists no names for Suggest");
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
                throw new FormatException($"{definition.Id} lists a name twice");
            return new PeopleDefinition(definition.Id, Flag(map, "playable"), new NameSource(names));
        }).ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);

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

        return new CallDefinition(id, new StartingPackage(bias, skills, ImmutableArray<string>.Empty), kit,
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
