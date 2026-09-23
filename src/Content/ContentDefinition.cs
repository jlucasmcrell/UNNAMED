// UNNAMED Content Validation - Base Content Definition
// Defines the envelope and common fields for all content kinds
// No Godot references

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

namespace UNNAMED.Content;

/// <summary>
/// Schema version for content definitions. Bump on breaking changes.
/// </summary>
public static class SchemaVersion
{
    public const int Current = 1;
}

/// <summary>
/// Base interface for all content definitions. All definitions must include
/// schema, id, kind, display_key, and tags.
/// </summary>
public interface IContentDefinition
{
    /// <summary>
    /// Schema revision. Bump on incompatible shape change.
    /// </summary>
    int Schema { get; }

    /// <summary>
    /// Unique definition ID. Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    /// Example: item.weapon.iron_sword
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The kind of this definition. Must match the directory and prefix.
    /// Example: item.weapon
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Display key for localization. Never raw English in logic.
    /// </summary>
    string DisplayKey { get; }

    /// <summary>
    /// Tags from content/_tags.yaml (validator-enforced closed set).
    /// </summary>
    string[] Tags { get; }
}

/// <summary>
/// Common envelope for all content definitions.
/// Every definition must include these fields at the top level.
/// </summary>
public record ContentEnvelope
{
    /// <summary>
    /// Schema revision. Bump on incompatible shape change.
    /// Default: 1
    /// </summary>
    [YamlMember(Alias = "schema", ApplyNamingConventions = false)]
    public int Schema { get; set; } = SchemaVersion.Current;

    /// <summary>
    /// Unique definition ID. Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    /// Example: item.weapon.iron_sword
    /// </summary>
    [YamlMember(Alias = "id", ApplyNamingConventions = false)]
    public required string Id { get; set; }

    /// <summary>
    /// The kind of this definition. Must match the directory and prefix.
    /// Example: item.weapon
    /// </summary>
    [YamlMember(Alias = "kind", ApplyNamingConventions = false)]
    public required string Kind { get; set; }

    /// <summary>
    /// Display key for localization. Never raw English in logic.
    /// </summary>
    [YamlMember(Alias = "display_key", ApplyNamingConventions = false)]
    public required string DisplayKey { get; set; }

    /// <summary>
    /// Tags from content/_tags.yaml (validator-enforced closed set).
    /// </summary>
    [YamlMember(Alias = "tags", ApplyNamingConventions = false)]
    public required string[] Tags { get; set; }

    /// <summary>
    /// Optional notes describing the intent or design rationale.
    /// </summary>
    [YamlMember(Alias = "notes", ApplyNamingConventions = false)]
    public string? Notes { get; set; }

    /// <summary>
    /// Optional: IDs defined by this definition (for sub-elements).
    /// </summary>
    [YamlMember(Alias = "defines", ApplyNamingConventions = false)]
    public string[]? Defines { get; set; }

    /// <summary>
    /// Optional: Mark as deprecated with replacement info.
    /// </summary>
    [YamlMember(Alias = "deprecated", ApplyNamingConventions = false)]
    public string? Deprecated { get; set; }

    /// <summary>
    /// Optional: If this is an alias/removal record.
    /// </summary>
    [YamlMember(Alias = "alias_of", ApplyNamingConventions = false)]
    public string? AliasOf { get; set; }
    
    /// <summary>
    /// Internal: Raw YAML source (for debugging).
    /// </summary>
    public string? YamlSource { get; set; }
    
    /// <summary>
    /// Internal: Source file path (for error reporting).
    /// </summary>
    public string? SourceFile { get; set; }
    
    /// <summary>
    /// Internal: Line offset within source file.
    /// </summary>
    public int LineOffset { get; set; }
}

/// <summary>
/// Tag vocabulary. Validator must ensure all tags come from this closed set.
/// This is defined in content/_tags.yaml and loaded separately.
/// </summary>
public static class TagVocabulary
{
    // Common tags for items
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Consumable = "consumable";
    public const string Material = "material";
    public const string Tool = "tool";
    public const string Book = "book";
    public const string QuestItem = "quest_item";
    public const string Container = "container";
    public const string Currency = "currency";
    public const string Misc = "misc";

    // Weapon subtypes
    public const string Sword = "sword";
    public const string Dagger = "dagger";
    public const string Mace = "mace";
    public const string Axe = "axe";
    public const string Spear = "spear";
    public const string Bow = "bow";
    public const string Staff = "staff";

    // Material types
    public const string Metal = "metal";
    public const string Wood = "wood";
    public const string Leather = "leather";
    public const string Cloth = "cloth";
    public const string Stone = "stone";
    public const string Bone = "bone";

    // Rarity
    public const string Common = "common";
    public const string Uncommon = "uncommon";
    public const string Rare = "rare";
    public const string Epic = "epic";
    public const string Legendary = "legendary";
    public const string Artifact = "artifact";

    // Add more as needed for other definition kinds
}

/// <summary>
/// Validation error with severity, code, message, file path, line number, and definition ID.
/// </summary>
public sealed class ValidationError : IEquatable<ValidationError>
{
    /// <summary>
    /// Error severity level.
    /// </summary>
    public enum Severity
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Severity level of this error.
    /// </summary>
    public Severity SeverityLevel { get; set; } = Severity.Error;

    /// <summary>
    /// Error code (e.g., "VAL001").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// File path where error occurred.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File path alias for File property (for test compatibility).
    /// </summary>
    public string File => FilePath;

    /// <summary>
    /// Line number (1-based) where error occurred.
    /// </summary>
    public int LineNumber { get; set; } = 0;

    /// <summary>
    /// Line number alias for Line property (for test compatibility).
    /// </summary>
    public int Line => LineNumber;

    /// <summary>
    /// Definition ID related to this error (optional).
    /// </summary>
    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>
    /// Create a new validation error.
    /// </summary>
    public ValidationError() { }

    /// <summary>
    /// Create a new validation error with all fields.
    /// </summary>
    public ValidationError(string code, string message, string filePath, int lineNumber, Severity severity = Severity.Error, string definitionId = "")
    {
        Code = code;
        Message = message;
        FilePath = filePath;
        LineNumber = lineNumber;
        SeverityLevel = severity;
        DefinitionId = definitionId;
    }

    /// <summary>
    /// Convert to human-readable string.
    /// </summary>
    public override string ToString()
    {
        return $"{SeverityLevel}: {Code}: {Message} ({FilePath}:{LineNumber})";
    }

    /// <summary>
    /// Check equality with another validation error.
    /// </summary>
    public bool Equals(ValidationError? other)
    {
        if (other is null)
            return false;

        return SeverityLevel == other.SeverityLevel &&
               Code == other.Code &&
               Message == other.Message &&
               FilePath == other.FilePath &&
               LineNumber == other.LineNumber &&
               DefinitionId == other.DefinitionId;
    }

    /// <summary>
    /// Check equality with another object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is ValidationError error && Equals(error);
    }

    /// <summary>
    /// Get hash code for this error.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(SeverityLevel, Code, Message, FilePath, LineNumber, DefinitionId);
    }
}

/// <summary>
/// Summary of content validation results.
/// </summary>
public record ContentValidationSummary
{
    /// <summary>
    /// Total number of loaded definitions.
    /// </summary>
    public int TotalLoaded { get; set; }

    /// <summary>
    /// Total number of validation errors.
    /// </summary>
    public int TotalErrors { get; set; }

    /// <summary>
    /// List of all validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; set; } = Array.Empty<ValidationError>();

    /// <summary>
    /// True if there are any validation errors.
    /// </summary>
    public bool HasErrors { get; set; }

    /// <summary>
    /// Check if validation is valid (no errors).
    /// </summary>
    public bool IsValid => !HasErrors;
}

/// <summary>
/// Tracks duplicate definition IDs across files.
/// </summary>
public sealed class DuplicateIdTracker
{
    private readonly Dictionary<string, List<string>> _idToFile = new Dictionary<string, List<string>>();
    private readonly HashSet<string> _duplicateIds = new HashSet<string>();

    /// <summary>
    /// Register a definition ID from a file.
    /// </summary>
    /// <param name="definitionId">The definition ID</param>
    /// <param name="filePath">The file path where it was found</param>
    public void RegisterId(string definitionId, string filePath)
    {
        if (!_idToFile.TryGetValue(definitionId, out var files))
        {
            files = new List<string>();
            _idToFile[definitionId] = files;
        }

        files.Add(filePath);

        if (files.Count == 2)
        {
            _duplicateIds.Add(definitionId);
        }
    }

    /// <summary>
    /// Get files that have the same definition ID (null if no duplicates).
    /// </summary>
    /// <param name="definitionId">The definition ID to check</param>
    /// <returns>List of file paths or null if no duplicates</returns>
    public IReadOnlyList<string>? GetDuplicateFiles(string definitionId)
    {
        if (_idToFile.TryGetValue(definitionId, out var files) && files.Count > 1)
        {
            return files;
        }
        return null;
    }

    /// <summary>
    /// Get all definition IDs that have duplicates.
    /// </summary>
    public IReadOnlyList<string> DuplicateIds => _duplicateIds.ToList();

    /// <summary>
    /// Check if there are any duplicate IDs.
    /// </summary>
    public bool HasDuplicates() => _duplicateIds.Count > 0;

    /// <summary>
    /// Clear all tracked data.
    /// </summary>
    public void Clear()
    {
        _idToFile.Clear();
        _duplicateIds.Clear();
    }

    /// <summary>
    /// Get all duplicate ID entries.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> GetAllDuplicates()
    {
        return _idToFile.Where(kvp => kvp.Value.Count > 1)
                      .ToDictionary(kvp => kvp.Key, kvp => new List<string>(kvp.Value));
    }
}

/// <summary>
/// Validates cross-references between content definitions.
/// </summary>
public sealed class CrossReferenceValidator
{
    private readonly HashSet<string> _missingReferences = new HashSet<string>();

    /// <summary>
    /// Validate all references point to existing definitions.
    /// </summary>
    /// <param name="references">Dictionary of definition ID to file path</param>
    /// <param name="allDefinitions">Set of all valid definition IDs</param>
    /// <returns>True if all references are valid</returns>
    public bool ValidateCrossReferences(IReadOnlyDictionary<string, string> references, IReadOnlySet<string>? allDefinitions = null)
    {
        _missingReferences.Clear();

        foreach (var kvp in references)
        {
            string id = kvp.Key;
            string filePath = kvp.Value;

            // If we have a set of all definitions, check against it
            if (allDefinitions != null && !allDefinitions.Contains(id))
            {
                _missingReferences.Add(id);
            }
        }

        return _missingReferences.Count == 0;
    }

    /// <summary>
    /// Get list of missing references.
    /// </summary>
    public IReadOnlyList<string> MissingReferences => _missingReferences.ToList();

    /// <summary>
    /// Check if there are any missing references.
    /// </summary>
    public bool HasMissingReferences => _missingReferences.Count > 0;

    /// <summary>
    /// Register known IDs for cross-reference validation.
    /// </summary>
    /// <param name="knownIds">Set of known/valid definition IDs</param>
    public void RegisterKnownIds(IReadOnlySet<string> knownIds)
    {
        // This validator doesn't need to track knownIds separately
        // it validates references against them via ValidateCrossReferences
    }
}

/// <summary>
/// Validates and manages alias maps for content definition aliases.
/// </summary>
public sealed class AliasMapValidator
{
    private readonly HashSet<string> _invalidAliases = new HashSet<string>();
    private HashSet<string> _knownIds = new HashSet<string>();
    private Dictionary<string, string> _loadedAliases = new Dictionary<string, string>();

    /// <summary>
    /// Validate that all aliases point to valid definitions.
    /// </summary>
    /// <param name="aliases">Dictionary of alias to target definition ID</param>
    /// <param name="allDefinitions">Set of all valid definition IDs</param>
    /// <returns>True if all aliases are valid</returns>
    public bool ValidateAliasMap(IReadOnlyDictionary<string, string> aliases, IReadOnlySet<string>? allDefinitions = null)
    {
        _invalidAliases.Clear();

        foreach (var kvp in aliases)
        {
            string alias = kvp.Key;
            string target = kvp.Value;

            // Alias must be a valid definition ID
            if (!DefinitionIdValidator.IsValidId(alias))
            {
                _invalidAliases.Add(alias);
                continue;
            }

            // Target must exist if we have a reference set
            if (allDefinitions != null && !allDefinitions.Contains(target))
            {
                _invalidAliases.Add(alias);
            }
        }

        return _invalidAliases.Count == 0;
    }

    /// <summary>
    /// Add an alias to the map (no validation).
    /// </summary>
    /// <param name="alias">The alias ID</param>
    /// <param name="target">The target definition ID</param>
    /// <param name="aliases">The aliases dictionary to modify</param>
    public void AddAlias(string alias, string target, Dictionary<string, string> aliases)
    {
        aliases[alias] = target;
    }

    /// <summary>
    /// Get list of invalid aliases.
    /// </summary>
    public IReadOnlyList<string> InvalidAliases => _invalidAliases.ToList();

    /// <summary>
    /// Get list of validation errors (invalid aliases).
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => InvalidAliases.Select(alias => 
        new ValidationError("ALIAS002", $"Invalid alias: {alias}", "<alias Map>", 0, ValidationError.Severity.Error)).ToList();

    /// <summary>
    /// Load alias map from parsed dictionaries.
    /// </summary>
    /// <param name="aliases">Dictionary of alias to target</param>
    /// <param name="removed">Dictionary of removed aliases to targets</param>
    public void LoadAliasMap(IReadOnlyDictionary<string, string> aliases, IReadOnlyDictionary<string, string> removed)
    {
        // Store aliases for later validation
        foreach (var kvp in aliases)
        {
            _loadedAliases[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Register known IDs for alias validation.
    /// </summary>
    /// <param name="knownIds">Set of known/valid definition IDs</param>
    public void RegisterKnownIds(IReadOnlySet<string> knownIds)
    {
        // This validator doesn't need to track knownIds separately
        // it validates targets against them via ValidateAliasMap
    }

    /// <summary>
    /// Register known IDs for alias validation (alternative signature).
    /// </summary>
    /// <param name="knownIds">Collection of known/valid definition IDs</param>
    public void RegisterKnownIds(IEnumerable<string> knownIds)
    {
        // Store for later validation
        _knownIds = new HashSet<string>(knownIds);
    }

    /// <summary>
    /// Validate alias targets exist.
    /// </summary>
    public void ValidateAliasTargets()
    {
        // Validate against known IDs if available
        if (_knownIds.Count > 0 && _loadedAliases.Count > 0)
        {
            ValidateAliasMap(_loadedAliases, _knownIds);
        }
    }
}

/// <summary>
/// Exception thrown when a content file fails to load.
/// </summary>
public class ContentLoadException : Exception
{
    /// <summary>
    /// Create a new ContentLoadException.
    /// </summary>
    public ContentLoadException() { }

    /// <summary>
    /// Create a new ContentLoadException with a message.
    /// </summary>
    public ContentLoadException(string message) : base(message) { }

    /// <summary>
    /// Create a new ContentLoadException with a message and inner exception.
    /// </summary>
    public ContentLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Content kind registry - maps directory names to kind names and vice versa.
/// </summary>
public static class ContentKindRegistry
{
    private static readonly Dictionary<string, string> DirectoryToKind = new Dictionary<string, string>
    {
        { "items", "item" },
        { "creatures", "creature" },
        { "locations", "location" },
        { "quests", "quest" },
        { "materials", "material" },
        { "skills", "skill" }
    };

    private static readonly Dictionary<string, string> KindToDirectory = new Dictionary<string, string>
    {
        { "item", "items" },
        { "creature", "creatures" },
        { "location", "locations" },
        { "quest", "quests" },
        { "material", "materials" },
        { "skill", "skills" }
    };

    /// <summary>
    /// Get all registered kind definitions.
    /// </summary>
    public static IReadOnlyList<ContentKindDefinition> Kinds =>
        KindToDirectory.Select(kvp => new ContentKindDefinition(kvp.Key, kvp.Value)).ToList();

    /// <summary>
    /// Get all known directories.
    /// </summary>
    public static IReadOnlyList<string> KnownDirectories => DirectoryToKind.Keys.ToList();

    /// <summary>
    /// Check if a kind is valid (exists in the registry).
    /// </summary>
    /// <param name="kind">The kind name to check</param>
    /// <returns>True if the kind is valid</returns>
    public static bool IsValidKind(string kind)
    {
        return KindToDirectory.ContainsKey(kind);
    }

    /// <summary>
    /// Get the kind name for a directory.
    /// </summary>
    /// <param name="directory">The directory name</param>
    /// <returns>The kind name</returns>
    public static string GetKind(string directory)
    {
        if (DirectoryToKind.TryGetValue(directory, out var kind))
        {
            return kind;
        }
        throw new KeyNotFoundException($"Unknown directory: {directory}");
    }

    /// <summary>
    /// Get the directory name for a kind.
    /// </summary>
    /// <param name="kind">The kind name</param>
    /// <returns>The directory name</returns>
    public static string GetDirectory(string kind)
    {
        if (KindToDirectory.TryGetValue(kind, out var directory))
        {
            return directory;
        }
        throw new KeyNotFoundException($"Unknown kind: {kind}");
    }

    /// <summary>
    /// Get all registered kinds.
    /// </summary>
    public static IReadOnlyList<string> GetAllKinds()
    {
        return KindToDirectory.Keys.ToList();
    }

    /// <summary>
    /// Get the ContentKindDefinition for a kind.
    /// </summary>
    /// <param name="kind">The kind name</param>
    /// <returns>The ContentKindDefinition</returns>
    public static ContentKindDefinition GetFromKind(string kind)
    {
        if (KindToDirectory.TryGetValue(kind, out var directory))
        {
            return new ContentKindDefinition(kind, directory);
        }
        throw new KeyNotFoundException($"Unknown kind: {kind}");
    }
}

/// <summary>
/// Schema type mapper - maps kind names to schema types and vice versa.
/// </summary>
public static class SchemaTypeMapper
{
    private static readonly Dictionary<string, Type> KindToType = new Dictionary<string, Type>
    {
        { "item", typeof(ItemDefinition) },
        { "item.weapon", typeof(ItemWeaponSchema) },
        { "item.armor", typeof(ItemArmorSchema) }
    };

    private static readonly Dictionary<Type, string> TypeToKind = new Dictionary<Type, string>
    {
        { typeof(ItemDefinition), "item" },
        { typeof(ItemWeaponSchema), "item.weapon" },
        { typeof(ItemArmorSchema), "item.armor" }
    };

    /// <summary>
    /// Get the schema type for a kind name.
    /// </summary>
    /// <param name="kind">The kind name</param>
    /// <returns>The schema type</returns>
    public static Type GetSchemaType(string kind)
    {
        if (KindToType.TryGetValue(kind, out var type))
        {
            return type;
        }
        throw new KeyNotFoundException($"Unknown kind: {kind}");
    }

    /// <summary>
    /// Get the kind name for a schema type.
    /// </summary>
    /// <param name="type">The schema type</param>
    /// <returns>The kind name</returns>
    public static string GetKind(Type type)
    {
        if (TypeToKind.TryGetValue(type, out var kind))
        {
            return kind;
        }
        throw new KeyNotFoundException($"Unknown type: {type.Name}");
    }

    /// <summary>
    /// Get the schema for a kind name.
    /// </summary>
    /// <param name="kind">The kind name</param>
    /// <returns>The schema type</returns>
    public static Type GetSchema(string kind)
    {
        return GetSchemaType(kind);
    }
}

/// <summary>
/// Represents a content kind definition with directory mapping.
/// </summary>
public sealed class ContentKindDefinition
{
    /// <summary>
    /// The kind name (e.g., "item", "item.weapon").
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// The directory name (e.g., "items", "items/weapon").
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// The full kind name (alias for Kind property).
    /// </summary>
    public string FullKind => Kind;

    /// <summary>
    /// Create a new content kind definition.
    /// </summary>
    /// <param name="kind">The kind name</param>
    /// <param name="directory">The directory name</param>
    public ContentKindDefinition(string kind, string directory)
    {
        Kind = kind;
        Directory = directory;
    }

    /// <summary>
    /// The ID prefix for this kind (kind name ending with a dot).
    /// </summary>
    public string IdPrefix => Kind + '.';

    /// <summary>
    /// Check if this kind definition matches a definition ID.
    /// </summary>
    /// <param name="id">The definition ID to check</param>
    /// <returns>True if the ID starts with this kind</returns>
    public bool MatchesId(string id)
    {
        return id.StartsWith(Kind + '.') || id == Kind;
    }
}

/// <summary>
/// YAML deserializer for content definitions.
/// </summary>
public static class ContentYamlDeserializer
{
    private static readonly IDeserializer Deserializer;

    static ContentYamlDeserializer()
    {
        var serializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitEmptyCollections)
            .Build();

        Deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <summary>
    /// Deserialize YAML content into a ContentEnvelope.
    /// </summary>
    /// <param name="yaml">The YAML content</param>
    /// <param name="filePath">The source file path (for error reporting)</param>
    /// <returns>The deserialized content envelope</returns>
    public static ContentEnvelope Deserialize(string yaml, string filePath)
    {
        var envelope = Deserializer.Deserialize<ContentEnvelope>(yaml);
        envelope.SourceFile = filePath;
        envelope.YamlSource = yaml;
        return envelope;
    }
    
    /// <summary>
    /// Deserialize YAML content into a specific type.
    /// </summary>
    /// <param name="yaml">The YAML content</param>
    /// <param name="filePath">The source file path (for error reporting)</param>
    /// <returns>The deserialized object</returns>
    public static T Deserialize<T>(string yaml, string filePath)
    {
        var result = Deserializer.Deserialize<T>(yaml);
        // Set SourceFile and YamlSource if the type has them (ContentEnvelope does)
        if (result is ContentEnvelope envelope)
        {
            envelope.SourceFile = filePath;
            envelope.YamlSource = yaml;
        }
        return result;
    }
}

/// <summary>
/// Definition ID validator - validates definition IDs against D-04 rules.
/// </summary>
public static class DefinitionIdValidator
{
    // Regex for valid definition ID per D-04
    // Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    // At least 2 segments required
    // First segment: alphanumeric only
    // Non-first segments: must contain at least one letter or underscore
    // Optional 2-digit ordinal suffix
    private static readonly Regex Pattern = new(
        @"^[a-z0-9]+(\.[a-z0-9_]*[a-z_][a-z0-9_]*)+(\.[0-9]{2})?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Check if a string is a valid definition ID.
    /// </summary>
    /// <param name="id">The string to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return Pattern.IsMatch(id.ToLowerInvariant());
    }
}

/// <summary>
/// Base item definition. Can be specialized by sub-kinds like item.weapon, item.armor.
/// Uses ContentEnvelope as base and adds item-specific fields.
/// </summary>
public record ItemDefinition : ContentEnvelope
{
    /// <summary>
    /// Item value in gold/currency
    /// </summary>
    public int Value { get; set; }
    
    /// <summary>
    /// Item weight in kilograms
    /// </summary>
    public float Weight { get; set; }
    
    /// <summary>
    /// Item type (weapon, armor, consumable, etc.)
    /// </summary>
    public string? Type { get; set; }
}

/// <summary>
/// Base schema for item.weapon kind.
/// Extended from ContentEnvelope with weapon-specific fields.
/// </summary>
public record ItemWeaponSchema : ContentEnvelope
{
    /// <summary>
    /// Item value in gold/currency
    /// </summary>
    public int Value { get; set; }
    
    /// <summary>
    /// Item weight in kilograms
    /// </summary>
    public float Weight { get; set; }
    
    /// <summary>
    /// Weapon type (sword, axe, bow, etc.)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// Damage dealt by the weapon
    /// </summary>
    public int Damage { get; set; }
}

/// <summary>
/// Base schema for item.armor kind.
/// Extended from ContentEnvelope with armor-specific fields.
/// </summary>
public record ItemArmorSchema : ContentEnvelope
{
    /// <summary>
    /// Item value in gold/currency
    /// </summary>
    public int Value { get; set; }
    
    /// <summary>
    /// Item weight in kilograms
    /// </summary>
    public float Weight { get; set; }
    
    /// <summary>
    /// Armor type (head, chest, legs, etc.)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// Armor rating/protection value
    /// </summary>
    public int ArmorRating { get; set; }
}
