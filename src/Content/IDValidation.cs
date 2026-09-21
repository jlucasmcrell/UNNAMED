// UNNAMED Content Validation - Definition ID format and duplicate detection
// No Godot references

using System.Text.RegularExpressions;

namespace UNNAMED.Content;

/// <summary>
/// Definition ID validator. Enforces format per D-04:
/// <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>] lowercase, dots, underscores
/// </summary>
public static class DefinitionIdValidator
{
    // Pattern: lowercase alphanumeric + underscore, dot-separated segments
    // Each segment: [a-z0-9_]+
    // Ordinal (last segment optionally): .00-99
    // Case-sensitive to reject uppercase
    private static readonly Regex IdPattern = new(
        @"^[a-z0-9]+(\.[a-z0-9_]+)*(\.[0-9]{2})?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validate definition ID format. Returns true if valid.
    /// Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    /// Example: item.weapon.iron_sword, quest.artifact.shattered_crown.03
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        
        return IdPattern.IsMatch(id!);
    }
    
    /// <summary>
    /// Validate definition ID format. Returns error message if invalid, null if valid.
    /// </summary>
    public static string? ValidateId(string? id, out bool isValid)
    {
        isValid = IsValidId(id);
        if (isValid)
            return null;
        
        return $"Invalid definition ID format: '{id}'. Expected format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>] (lowercase alphanumeric, underscores, dots. Example: item.weapon.iron_sword)";
    }
}

/// <summary>
/// Duplicate ID detector. Tracks IDs across files and reports duplicates.
/// </summary>
public class DuplicateIdTracker
{
    private readonly Dictionary<string, List<string>> _idToFileMap = new Dictionary<string, List<string>>();
    
    /// <summary>
    /// Register an ID from a file path.
    /// </summary>
    public void RegisterId(string id, string filePath)
    {
        if (!_idToFileMap.ContainsKey(id))
            _idToFileMap[id] = new List<string>();
        _idToFileMap[id].Add(filePath);
    }
    
    /// <summary>
    /// Check if an ID has duplicates. Returns list of files containing this ID (if more than one file).
    /// </summary>
    public List<string>? GetDuplicateFiles(string id)
    {
        return _idToFileMap.TryGetValue(id, out var files) && files.Count > 1 ? files : null;
    }
    
    /// <summary>
    /// Get all IDs with duplicates.
    /// </summary>
    public Dictionary<string, List<string>> GetAllDuplicates()
    {
        return _idToFileMap.Where(kvp => kvp.Value.Count > 1)
                          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    /// <summary>
    /// Check if there are any duplicates.
    /// </summary>
    public bool HasDuplicates => _idToFileMap.Any(kvp => kvp.Value.Count > 1);
}

/// <summary>
/// Error report for content validation.
/// </summary>
public record ValidationError
{
    /// <summary>
    /// Severity level.
    /// </summary>
    public enum Severity
    {
        Error,
        Warning
    }
    
    /// <summary>
    /// Severity level.
    /// </summary>
    public Severity SeverityLevel { get; init; }
    
    /// <summary>
    /// Error code (e.g., "E001", "W001").
    /// </summary>
    public string Code { get; init; } = string.Empty;
    
    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    
    /// <summary>
    /// File path where error occurred.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Line number where error occurred (if known).
    /// </summary>
    public int? LineNumber { get; init; }
    
    /// <summary>
    /// Optional: Affected definition ID.
    /// </summary>
    public string? DefinitionId { get; init; }
    
    /// <summary>
    /// Format as readable error string.
    /// </summary>
    public override string ToString()
    {
        var lineInfo = LineNumber.HasValue ? $":{LineNumber.Value}" : "";
        var severityPrefix = SeverityLevel == Severity.Error ? "ERROR" : "WARNING";
        return $"{severityPrefix} {Code}: {Message} [{FilePath}{lineInfo}]";
    }
}

/// <summary>
/// Cross-reference validator. Ensures all ID references point to existing definitions.
/// </summary>
public class CrossReferenceValidator
{
    private readonly HashSet<string> _knownIds = new HashSet<string>();
    private readonly List<ValidationError> _errors = new List<ValidationError>();
    
    /// <summary>
    /// Register all known definition IDs first (after loading all files).
    /// </summary>
    public void RegisterKnownIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _knownIds.Add(id);
    }
    
    /// <summary>
    /// Validate that a reference ID exists.
    /// </summary>
    public bool ValidateReference(string reference, string sourceFile, int? lineNumber = null)
    {
        if (_knownIds.Contains(reference))
            return true;
        
        _errors.Add(new ValidationError
        {
            SeverityLevel = ValidationError.Severity.Error,
            Code = "XREF001",
            Message = $"Dangling cross-reference: '{reference}' is not defined",
            FilePath = sourceFile,
            LineNumber = lineNumber,
            DefinitionId = reference
        });
        return false;
    }
    
    /// <summary>
    /// Validate multiple references at once.
    /// </summary>
    public bool ValidateReferences(IEnumerable<string> references, string sourceFile, int? lineNumber = null)
    {
        bool allValid = true;
        foreach (var refId in references)
        {
            if (!ValidateReference(refId, sourceFile, lineNumber))
                allValid = false;
        }
        return allValid;
    }
    
    /// <summary>
    /// Get all cross-reference errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors;
    
    /// <summary>
    /// Check if all references are valid.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;
}

/// <summary>
/// Migration/alias map validator (D-04).
/// Ensures aliases map to valid definitions and removed IDs are handled correctly.
/// </summary>
public class AliasMapValidator
{
    private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _removed = new Dictionary<string, string>();
    private readonly HashSet<string> _knownIds = new HashSet<string>();
    private readonly List<ValidationError> _errors = new List<ValidationError>();
    
    /// <summary>
    /// Load alias/removed maps (from content/_aliases.yaml).
    /// </summary>
    public void LoadAliasMap(Dictionary<string, string> aliases, Dictionary<string, string> removed)
    {
        _aliases.Clear();
        _removed.Clear();
        
        foreach (var kvp in aliases)
            _aliases[kvp.Key] = kvp.Value;
        foreach (var kvp in removed)
            _removed[kvp.Key] = kvp.Value;
    }
    
    /// <summary>
    /// Register all known definition IDs.
    /// </summary>
    public void RegisterKnownIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _knownIds.Add(id);
    }
    
    /// <summary>
    /// Resolve an ID through aliases. Returns (resolvedId, wasAlias, aliasSource).
    /// If alias source is returned, log a deprecation warning.
    /// </summary>
    public (string resolvedId, bool wasAlias, string? aliasSource) ResolveAlias(string id)
    {
        if (_aliases.TryGetValue(id, out var resolved))
            return (resolved, true, id);
        return (id, false, null);
    }
    
    /// <summary>
    /// Validate that alias targets exist.
    /// </summary>
    public bool ValidateAliasTargets()
    {
        bool allValid = true;
        
        foreach (var kvp in _aliases)
        {
            if (!_knownIds.Contains(kvp.Value) && !_aliases.ContainsKey(kvp.Value))
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "ALIAS001",
                    Message = $"Alias target '{kvp.Value}' does not exist for alias '{kvp.Key}'",
                    DefinitionId = kvp.Key
                });
                allValid = false;
            }
        }
        
        return allValid;
    }
    
    /// <summary>
    /// Validate that removed IDs are not used in the content (unless they're removed entries themselves).
    /// </summary>
    public bool ValidateRemovedIdsUsed(IEnumerable<string> usedIds)
    {
        bool allValid = true;
        
        foreach (var usedId in usedIds)
        {
            if (_removed.ContainsKey(usedId) && !_knownIds.Contains(_removed[usedId]))
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "ALIAS002",
                    Message = $"Used ID '{usedId}' has been removed. It should map to '{_removed[usedId]}'",
                    DefinitionId = usedId
                });
                allValid = false;
            }
        }
        
        return allValid;
    }
    
    /// <summary>
    /// Get all alias-related errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors;
    
    /// <summary>
    /// Check if there are alias validation errors.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;
}
