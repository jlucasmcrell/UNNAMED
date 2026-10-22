// UNNAMED Content - pets from content (spec 43; owner rulings 2026-10-21): the rules of taming, a pet's own skills, the lure, the
// wound that sends it home, and what a party of them does to an encounter
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Pets;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the pet rules (<c>config.pets</c>) and the species (<c>pet:</c> on a creature), and lints what the reference and semantic
/// passes cannot see (PET001): a creature marked tameable has a <c>pet:</c> block saying what to tame it with and what it becomes;
/// a pet's skills are ones this build has; a food it eats is an item that exists; and the animal it grows into is a creature.
/// </summary>
public static class PetContent
{
    /// <summary>The skills a pet can have in this slice (spec 43's first slice: a fighting bite and a control lure).</summary>
    private static readonly string[] Skills = { "bite", "lure" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        ImmutableSortedDictionary<string, PetSpecies>? species = null;
        Try(() => species = BuildSpecies(loader), "pets", errors);
        if (species is not null && species.Count > 0 && !loader.Definitions.ContainsKey("config.pets"))
            errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error, Code = "PET001",
                Message = "config.pets: an animal can be tamed or bought, so its rules are required",
            });
        if (loader.Definitions.ContainsKey("config.pets"))
            Try(() => BuildRules(loader), "config.pets", errors);
        return errors;
    }

    /// <summary>
    /// The pet rules (<c>config.pets</c>): the taming odds and the offer's channel, what a use of a skill is worth, how far a lure
    /// reaches and what resists it, how long a badly wounded pet stays home, and the encounter scaling a party earns.
    /// </summary>
    public static PetRules BuildRules(ContentLoader loader)
    {
        var map = Config(loader, "config.pets");
        int tickMs = WorldContent.TickMilliseconds(loader);
        var tame = Map(map, "tame");
        var skills = Map(map, "skills");
        var lure = Map(map, "lure");
        var retire = Map(map, "retire");
        var encounter = Map(map, "encounter");
        var rules = new PetRules(
            new PetTamingRules(Int(tame, "base_percent"), Int(tame, "per_handling_level"), Int(tame, "unnoticed_percent"),
                Int(tame, "wounded_percent"), Int(tame, "over_level_percent"), Int(tame, "max_percent"),
                ToTicks(Number(tame, "channel_s"), tickMs),
                tame.ContainsKey("temperament_percent")
                    ? Map(tame, "temperament_percent").ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv => Whole(kv.Value, "config.pets tame temperament_percent"), StringComparer.Ordinal)
                    : ImmutableSortedDictionary<string, int>.Empty),
            new PetSkillRules(List(skills, "level_uses").Select(v => Whole(v, "config.pets skills level_uses")).OrderBy(v => v).ToImmutableArray()),
            new PetLureRules((int)Math.Round(Number(lure, "radius_m") * 1000), (int)Math.Round(Number(lure, "travel_m") * 1000),
                Int(lure, "resist_per_level"), Int(lure, "min_percent"), ToTicks(Number(lure, "immunity_s"), tickMs)),
            new PetRetireRules(Int(retire, "wound_days_min"), Int(retire, "wound_days_max"), 0, 0),
            new EncounterRules(Int(encounter, "percent_per_point"), Int(encounter, "max_percent"), Number(encounter, "level_per_percent")));
        return rules.Problem() is { } problem ? throw new FormatException($"config.pets: {problem}") : rules;
    }

    /// <summary>
    /// The species a creature is a pet as (<c>pet:</c> on its own <c>creature</c> definition): the role, its own skills, what it eats
    /// and what one offer of that is worth, its temperament, the grown animal a young one becomes, and where it mends at home.
    /// </summary>
    public static ImmutableSortedDictionary<string, PetSpecies> BuildSpecies(ContentLoader loader)
    {
        var items = loader.GetByKind("item");
        var creatures = loader.GetByKind("creature");
        var built = new SortedDictionary<string, PetSpecies>(StringComparer.Ordinal);
        foreach (var definition in creatures.Values)
        {
            var map = Read(definition.YamlSource);
            bool tameable = map.GetValueOrDefault("tameable") as string == "true";
            if (map.GetValueOrDefault("pet") is not Dictionary<object, object> pet)
            {
                if (tameable)
                    throw new FormatException($"{definition.Id}: tameable is true, so the creature names a pet: block (what it eats, its role and skills)");
                continue;
            }
            string id = definition.Id;
            var skills = List(pet, "skills").Select(s => PetKeys.ParseSkill(s as string ?? "")
                ?? throw new FormatException($"{id}: '{s}' is not a pet skill ({string.Join(", ", Skills)})")).ToImmutableArray();
            if (skills.IsEmpty)
                throw new FormatException($"{id}: a pet has at least one skill");
            var foods = pet.ContainsKey("foods")
                ? Map(pet, "foods").Select(kv =>
                {
                    string item = kv.Key as string ?? "";
                    if (!items.ContainsKey(item))
                        throw new FormatException($"{id}: it eats {item}, which is not an item");
                    return KeyValuePair.Create(item, Whole(kv.Value, $"{id} pet foods"));
                }).ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
                : ImmutableSortedDictionary<string, int>.Empty;
            string? growsInto = pet.GetValueOrDefault("grows_into") as string;
            if (growsInto is not null && !creatures.ContainsKey(growsInto))
                throw new FormatException($"{id}: it grows into {growsInto}, which is not a creature");
            string home = pet.GetValueOrDefault("home") as string ?? "";
            if (!home.StartsWith("location.", StringComparison.Ordinal))
                throw new FormatException($"{id}: its home is a location.<id> (where it mends when badly wounded)");
            string role = pet.GetValueOrDefault("role") as string ?? "fighter";
            built[id] = new PetSpecies(id, pet.GetValueOrDefault("name") as string ?? definition.Id,
                PetKeys.ParseRole(role) ?? throw new FormatException($"{id}: '{role}' is not a pet role (fighter, support, control, scout)"),
                skills, foods, pet.GetValueOrDefault("temperament") as string ?? "wary", growsInto,
                growsInto is null ? 0 : Whole(pet.GetValueOrDefault("grow_days"), $"{id} grow_days"), home);
        }
        return built.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    private static int Whole(object? value, string what) => value switch
    {
        int i => i,
        long l => checked((int)l),
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        double d when d == Math.Floor(d) => (int)d,
        _ => throw new FormatException($"{what}: '{value}' is not a whole number"),
    };

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or KeyNotFoundException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "PET001", Message = error.Message });
        }
    }
}
