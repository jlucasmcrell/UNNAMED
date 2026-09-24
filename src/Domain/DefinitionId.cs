// UNNAMED Domain - DefinitionId
// Canonical DefinitionId value type for all projects
// Per D-04: Definition IDs are human-authored, stable identifiers for content definitions
// Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
// Must have at least 2 segments, first segment alphanumeric only, non-first segments must contain at least one letter or underscore

using System.Text.RegularExpressions;

namespace UNNAMED.Domain;

/// <summary>
/// A unique identifier for entity definitions (human-authored, stable, dotted strings).
/// Per D-04: Definition IDs are human-authored, stable identifiers for content definitions.
/// Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
/// Example: "item.weapon.iron_sword", "creature.beast.wolf_grey.01"
/// </summary>
public readonly record struct DefinitionId(string Value)
{
    private static readonly Regex IdPattern = new(
        @"^[a-z0-9]+(\.[a-z0-9_]*[a-z_][a-z0-9_]*)+(\.[0-9]{2})?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validate a definition ID format.
    /// Must have at least 2 segments, first segment alphanumeric only,
    /// non-first segments must contain at least one letter or underscore.
    /// Optional .NN ordinal suffix allowed on last segment.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return IdPattern.IsMatch(value);
    }

    /// <summary>
    /// Create a DefinitionId from a string (no validation - for programmatic use).
    /// </summary>
    public static DefinitionId FromString(string value) => new(value);

    /// <summary>
    /// Parse a DefinitionId from a string (with validation).
    /// </summary>
    public static DefinitionId Parse(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException($"Invalid definition ID format: {value}", nameof(value));
        
        return new(value);
    }

    /// <summary>
    /// Try to parse a DefinitionId from a string.
    /// </summary>
    public static bool TryParse(string? value, out DefinitionId result)
    {
        if (IsValid(value))
        {
            result = new(value!);
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Get the string representation.
    /// </summary>
    public override string ToString() => Value;
}
