// UNNAMED Domain - progression vocabulary (PROGRESSION.md §2-§4)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

/// <summary>The seven canonical attributes (PROGRESSION.md §4.1). Changing the set is a save migration.</summary>
public enum CharacterAttribute { Might, Endurance, Agility, Precision, Will, Insight, Presence }

/// <summary>Where level XP comes from (§3.3). The anti-farm guards key on it.</summary>
public enum XpSource { Discovery, QuestObjective, Combat, Production, Social }

/// <summary>How a technique, formula or recipe entered the knowledge record (§4.4) - the only way knowledge enters.</summary>
public enum LearningSource { StartingPackage, Teacher, Book, Quest, Study, Experiment, Discovery, Artifact, Culture }

/// <summary>A one-time attribute grant's origin. Creation and race are creation-time; the rest count toward §11.3's cap.</summary>
public enum GrantSource { Creation, Race, Trainer, Quest, Item }

public enum PracticeOutcome { Success, Failure }

/// <summary>The progression axes that advance in Phase 1, as telemetry names them (§13.2).</summary>
public enum Axis { Level, Attributes, Skills, Techniques }

/// <summary>Saved and displayed names are stable snake_case keys, never enum ordinals or C# names.</summary>
public static class ProgressionKeys
{
    public static readonly ImmutableArray<CharacterAttribute> Attributes = ImmutableArray.Create(
        CharacterAttribute.Might, CharacterAttribute.Endurance, CharacterAttribute.Agility, CharacterAttribute.Precision,
        CharacterAttribute.Will, CharacterAttribute.Insight, CharacterAttribute.Presence);

    /// <summary>The content kinds whose definitions are known capabilities (§4.4): techniques, formulas, recipes.</summary>
    public static readonly ImmutableArray<string> TechniqueKinds = ImmutableArray.Create("ability", "spell", "recipe");

    public static string Key(CharacterAttribute value) => value switch
    {
        CharacterAttribute.Might => "might",
        CharacterAttribute.Endurance => "endurance",
        CharacterAttribute.Agility => "agility",
        CharacterAttribute.Precision => "precision",
        CharacterAttribute.Will => "will",
        CharacterAttribute.Insight => "insight",
        CharacterAttribute.Presence => "presence",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string Key(XpSource value) => value switch
    {
        XpSource.Discovery => "discovery",
        XpSource.QuestObjective => "quest_objective",
        XpSource.Combat => "combat",
        XpSource.Production => "production",
        XpSource.Social => "social",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string Key(LearningSource value) => value switch
    {
        LearningSource.StartingPackage => "starting_package",
        LearningSource.Teacher => "teacher",
        LearningSource.Book => "book",
        LearningSource.Quest => "quest",
        LearningSource.Study => "study",
        LearningSource.Experiment => "experiment",
        LearningSource.Discovery => "discovery",
        LearningSource.Artifact => "artifact",
        LearningSource.Culture => "culture",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string Key(GrantSource value) => value switch
    {
        GrantSource.Creation => "creation",
        GrantSource.Race => "race",
        GrantSource.Trainer => "trainer",
        GrantSource.Quest => "quest",
        GrantSource.Item => "item",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static CharacterAttribute ParseAttribute(string key) => Parse(key, AttributeByKey, "attribute");
    public static XpSource ParseXpSource(string key) => Parse(key, XpSourceByKey, "XP source");
    public static LearningSource ParseLearningSource(string key) => Parse(key, LearningSourceByKey, "learning source");
    public static GrantSource ParseGrantSource(string key) => Parse(key, GrantSourceByKey, "grant source");

    public static bool TryParseAttribute(string key, out CharacterAttribute value) => AttributeByKey.TryGetValue(key, out value);

    /// <summary>A skill ID: a valid definition ID of the <c>skill</c> kind.</summary>
    public static bool IsSkillId(string? id) => DefinitionId.IsValid(id) && id!.StartsWith("skill.", StringComparison.Ordinal);

    /// <summary>A known-capability ID: a valid definition ID of a technique kind (ability, spell, recipe).</summary>
    public static bool IsTechniqueId(string? id) =>
        DefinitionId.IsValid(id) && TechniqueKinds.Any(kind => id!.StartsWith(kind + ".", StringComparison.Ordinal));

    private static readonly ImmutableDictionary<string, CharacterAttribute> AttributeByKey =
        Enum.GetValues<CharacterAttribute>().ToImmutableDictionary(v => Key(v), v => v, StringComparer.Ordinal);
    private static readonly ImmutableDictionary<string, XpSource> XpSourceByKey =
        Enum.GetValues<XpSource>().ToImmutableDictionary(v => Key(v), v => v, StringComparer.Ordinal);
    private static readonly ImmutableDictionary<string, LearningSource> LearningSourceByKey =
        Enum.GetValues<LearningSource>().ToImmutableDictionary(v => Key(v), v => v, StringComparer.Ordinal);
    private static readonly ImmutableDictionary<string, GrantSource> GrantSourceByKey =
        Enum.GetValues<GrantSource>().ToImmutableDictionary(v => Key(v), v => v, StringComparer.Ordinal);

    private static T Parse<T>(string key, ImmutableDictionary<string, T> table, string what) =>
        table.TryGetValue(key, out var value) ? value : throw new FormatException($"Unknown {what} '{key}'");
}
