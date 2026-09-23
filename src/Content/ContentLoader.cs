// UNNAMED Content Validation - Content Loader with Full Validation Pipeline
// No Godot references

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UNNAMED.Content;

/// <summary>
/// Content loader with full validation pipeline.
/// Implements D-03, D-04: YAML content loading, duplicate detection,
/// cross-reference validation, and alias resolution.
/// </summary>
public class ContentLoader
{
    // Regex for valid line number in YAML (Comment-based tracking)
    private static readonly Regex LineNumberMarker = new(
        @"^# line:\s*(\d+)",
        RegexOptions.Compiled);
    
    private readonly Dictionary<string, ContentEnvelope> _definitions = new Dictionary<string, ContentEnvelope>();
    private readonly List<ValidationError> _errors = new List<ValidationError>();
    private readonly DuplicateIdTracker _duplicateIdTracker = new DuplicateIdTracker();
    private readonly CrossReferenceValidator _crossReferenceValidator = new CrossReferenceValidator();
    private readonly AliasMapValidator _aliasMapValidator = new AliasMapValidator();
    
    private bool _validationEnabled = true;
    private string? _contentRootPath;
    
    /// <summary>
    /// Enable or disable validation.
    /// </summary>
    public bool ValidationEnabled
    {
        get => _validationEnabled;
        set => _validationEnabled = value;
    }
    
    /// <summary>
    /// Set the content root path (for relative file resolution).
    /// </summary>
    public string? ContentRootPath
    {
        get => _contentRootPath;
        set => _contentRootPath = value;
    }
    
    /// <summary>
    /// Get all loaded definitions.
    /// </summary>
    public IReadOnlyDictionary<string, ContentEnvelope> Definitions => _definitions.ToImmutableDictionary();
    
    /// <summary>
    /// Get all validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors.ToImmutableList();
    
    /// <summary>
    /// Get the number of successfully loaded definitions.
    /// </summary>
    public int LoadedCount => _definitions.Count;
    
    /// <summary>
    /// Check if there are any validation errors.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;
    
    /// <summary>
    /// Load all content from a directory.
    /// </summary>
    public bool LoadAll(string rootPath)
    {
        _contentRootPath = rootPath;
        
        // Check if root exists
        if (!Directory.Exists(rootPath))
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD001",
                Message = $"Content root directory does not exist: {rootPath}",
                FilePath = rootPath
            });
            return false;
        }
        
        // Track success for overall validation
        bool success = true;
        
        // Scan YAML files directly in content root (with automatic kind detection)
        // This handles test fixtures and edge cases where content might not be in kind subdirectories
        var yamlFilesInRoot = Directory.EnumerateFiles(rootPath, "*.yaml", SearchOption.TopDirectoryOnly).ToList();
        foreach (string filePath in yamlFilesInRoot)
        {
            string fileName = Path.GetFileName(filePath);
            // Skip underscore-prefixed files (meta files)
            if (fileName.StartsWith("_"))
                continue;
                
            bool fileSuccess = LoadRootLevelFile(filePath);
            if (!fileSuccess)
            {
                // Error already logged in LoadRootLevelFile
            }
        }
        
        // Track overall success - validation should still run even if some files failed
        // Only skip validation if directory check failed (missing content root)
        
        // Load definitions from all kind directories
        // Track which directories have been processed to avoid duplicate processing
        var processedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kindDef in ContentKindRegistry.Kinds.Values)
        {
            string kindPath = Path.Combine(rootPath, kindDef.Directory);
            if (!Directory.Exists(kindPath))
            {
                // Optional directories - don't fail on missing
                continue;
            }
            
            // Skip if this directory has already been processed (by another kind that maps to it)
            if (!processedDirectories.Add(kindPath))
            {
                continue;
            }
            
            // Load all YAML files in this directory
            bool subdirSuccess = LoadKindDirectory(kindDef, kindPath);
           success &= subdirSuccess;
        }
        
        // Track overall success for directory load phase
        bool directoryLoadSuccess = success;

        // Validate loaded content - run validation if at least some files were loaded
        if (_validationEnabled && directoryLoadSuccess)
        {
            // Check for unknown directories
            ValidateUnknownDirectories(rootPath);
            
            // Check for aliases/removed definitions
            LoadAliasMap(Path.Combine(rootPath, "_aliases.yaml"));
            
            // Cross-reference validation
            success &= ValidateCrossReferences();
            
            // Duplicate ID check
            success &= ValidateDuplicateIds();

            // Skill families, and the progression config when the pack has one (PRG codes)
            var progressionErrors = ProgressionContent.Validate(this);
            _errors.AddRange(progressionErrors);
            success &= progressionErrors.Count == 0;
        }
        
        return success;
    }
    
    /// <summary>
    /// Load content from a specific kind directory.
    /// </summary>
    private bool LoadKindDirectory(ContentKindDefinition kindDef, string directoryPath)
    {
        bool success = true;
        
        try
        {
            // Search recursively to support subdirectories like items/weapon, creatures/beast
            var files = Directory.EnumerateFiles(directoryPath, "*.yaml", SearchOption.AllDirectories).ToList();
            foreach (string filePath in files)
            {
                // Skip underscore-prefixed files (meta files like _aliases.yaml, _tags.yaml)
                string fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith("_"))
                    continue;
                
                bool fileSuccess = LoadFile(filePath, kindDef);
                success &= fileSuccess;
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD002",
                Message = $"Failed to read directory {directoryPath}: {ex.Message}",
                FilePath = directoryPath
            });
            success = false;
        }
        
        return success;
    }
    
    /// <summary>
    /// Load a single YAML file from the content root (with automatic kind detection).
    /// Used for test fixtures and edge cases where content might not be in kind subdirectories.
    /// </summary>
    private bool LoadRootLevelFile(string filePath)
    {
        try
        {
            string yaml = File.ReadAllText(filePath);
            
            // Parse YAML to extract kind for validation
            var envelope = ContentYamlDeserializer.Deserialize(yaml, filePath);
            
            // Validate ID format per D-04
            if (!ValidateDefinitionId(envelope.Id, filePath))
            {
                return false;
            }
            
            // Check if kind is in the closed vocabulary
            if (!ValidateKnownKind(envelope.Kind, filePath))
            {
                return false;
            }
            
            // For root-level files, skip directory matching (no directory constraint)
            // But still validate the definition ID format
            
            // Check for duplicate ID
            if (_definitions.ContainsKey(envelope.Id))
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "DUP001",
                    Message = $"Duplicate definition ID '{envelope.Id}' found in both <original> and {filePath}",
                    FilePath = filePath,
                    DefinitionId = envelope.Id
                });
                _duplicateIdTracker.RegisterId(envelope.Id, filePath);
                return false;
            }
            
            // Register for cross-reference validation
            _definitions[envelope.Id] = envelope;
            _duplicateIdTracker.RegisterId(envelope.Id, filePath);
            
            return true;
        }
        catch (ContentLoadException ex)
        {
            // The deserializer is static and cannot record errors itself: a malformed file must be
            // reported here, or it silently drops out of the load and validation still "passes".
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD003",
                Message = ex.Message,
                FilePath = filePath,
                LineNumber = ex.LineNumber
            });
            return false;
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD003",
                Message = $"Failed to load root-level file {filePath}: {ex.Message}",
                FilePath = filePath
            });
            return false;
        }
    }
    
    /// <summary>
    /// Load a single YAML file.
    /// </summary>
    private bool LoadFile(string filePath, ContentKindDefinition? expectedKindDef = null)
    {
        try
        {
            string yaml = File.ReadAllText(filePath);
            
            // Parse YAML
            var envelope = ContentYamlDeserializer.Deserialize(yaml, filePath);
            
            // Validate ID format per D-04
            if (!ValidateDefinitionId(envelope.Id, filePath))
            {
                return false;
            }
            
            // Check if kind is in the closed vocabulary
            if (!ValidateKnownKind(envelope.Kind, filePath))
            {
                return false;
            }
            
            // Check if directory matches kind
            if (expectedKindDef != null && !ValidateKindDirectoryMatch(envelope.Kind, filePath, expectedKindDef))
            {
                return false;
            }
            
            // Check for duplicate ID
            if (_definitions.ContainsKey(envelope.Id))
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "DUP001",
                    Message = $"Duplicate definition ID '{envelope.Id}' found in both <original> and {filePath}",
                    FilePath = filePath,
                    DefinitionId = envelope.Id
                });
                _duplicateIdTracker.RegisterId(envelope.Id, filePath);
                return false;
            }
            
            // Register for cross-reference validation
            _definitions[envelope.Id] = envelope;
            _duplicateIdTracker.RegisterId(envelope.Id, filePath);
            
            return true;
        }
        catch (ContentLoadException ex)
        {
            // The deserializer is static and cannot record errors itself: a malformed file must be
            // reported here, or it silently drops out of the load and validation still "passes".
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD003",
                Message = ex.Message,
                FilePath = filePath,
                LineNumber = ex.LineNumber
            });
            return false;
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "LOAD003",
                Message = $"Failed to Load file {filePath}: {ex.Message}",
                FilePath = filePath
            });
            return false;
        }
    }
    
    /// <summary>
    /// Validate definition ID format per D-04.
    /// </summary>
    private bool ValidateDefinitionId(string id, string sourceFile)
    {
        if (!DefinitionIdValidator.IsValidId(id))
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "ID001",
                Message = $"Invalid definition ID format: '{id}'. Must match pattern: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>] (lowercase alphanumeric, dots, underscores, optional .00-99 ordinal)",
                FilePath = sourceFile,
                DefinitionId = id
            });
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validate that kind is in the closed vocabulary.
    /// </summary>
    private bool ValidateKnownKind(string kind, string sourceFile)
    {
        if (!ContentKindRegistry.IsValidKind(kind))
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "KIND001",
                Message = $"Unknown content kind: '{kind}'. Must be one of the closed vocabulary from DATA_MODEL.md section 1.1",
                FilePath = sourceFile,
                DefinitionId = kind
            });
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validate that kind matches the directory it's in.
    /// </summary>
    private bool ValidateKindDirectoryMatch(string kind, string sourceFile, ContentKindDefinition expectedKindDef)
    {
        var actualKindDef = ContentKindRegistry.GetKind(kind);
        if (actualKindDef == null)
        {
            return false;
        }
        
        // Extract expected directory from kind
        string? actualDir = actualKindDef.Directory;
        if (string.IsNullOrEmpty(actualDir))
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "DIR001",
                Message = $"Kind '{kind}' has no associated directory in ContentKindRegistry",
                FilePath = sourceFile
            });
            return false;
        }
        
        // For files in subdirectories like items/weapon, we need to check
        // that the file is within the expected kind's directory hierarchy
        // The expectedKindDef is based on the immediate parent directory
        string expectedDir = expectedKindDef.Directory;
        
        // Get the relative path from content root to parent directory of file
        string fileDir = Path.GetDirectoryName(sourceFile) ?? "";
        // Normalize paths to handle relative/absolute differences
        string normalizedRoot = Path.GetFullPath(_contentRootPath ?? "");
        string normalizedFileDir = Path.GetFullPath(fileDir);
        string? relativePath = normalizedFileDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedFileDir.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar)
            : fileDir;
        
        // Check if relative path starts with expected directory
        // This allows subdirectories like items/weapon, items/potion under items
        if (!string.IsNullOrEmpty(relativePath) && !relativePath.Equals(expectedDir, StringComparison.OrdinalIgnoreCase))
        {
            // Check if the first segment matches the expected directory
            string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!firstSegment.Equals(expectedDir, StringComparison.OrdinalIgnoreCase))
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "DIR002",
                    Message = $"File '{Path.GetFileName(sourceFile)}' is in directory '{relativePath}' which is not under expected directory '{expectedDir}'",
                    FilePath = sourceFile
                });
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Check for unknown content directories.
    /// Per DATA_MODEL.md, only known directories may exist.
    /// </summary>
    private void ValidateUnknownDirectories(string rootPath)
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(rootPath))
            {
                string dirName = Path.GetFileName(dir);
                
                // Skip underscore-prefixed files/dirs (meta files)
                if (dirName.StartsWith('_'))
                    continue;
                
                // Check if directory is in known set
                if (!ContentKindRegistry.KnownDirectories.Contains(dirName))
                {
                    _errors.Add(new ValidationError
                    {
                        SeverityLevel = ValidationError.Severity.Error,
                        Code = "DIR003",
                        Message = $"Unknown content directory: '{dirName}'. Only known directories from DATA_MODEL.md section 1.1 are allowed",
                        FilePath = dir
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "DIR004",
                Message = $"Failed to scan content root for unknown directories: {ex.Message}",
                FilePath = rootPath
            });
        }
    }
    
    /// <summary>
    /// Load alias map from _aliases.yaml (D-04).
    /// </summary>
    private void LoadAliasMap(string aliasesPath)
    {
        if (!File.Exists(aliasesPath))
            return;
        
        try
        {
            // DATA_MODEL.md §2.1:
            //   aliases:  { old_id: new_id }          renamed IDs, kept forever
            //   removed:  { old_id: replacement }      merged/removed IDs, mapped forward
            //             { old_id: ~ }                removed with no replacement: references are discarded
            var document = new DeserializerBuilder().Build()
                .Deserialize<Dictionary<string, Dictionary<string, string?>?>?>(File.ReadAllText(aliasesPath))
                ?? new Dictionary<string, Dictionary<string, string?>?>();

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var removed = new Dictionary<string, string>(StringComparer.Ordinal);
            var discarded = new List<string>();
            foreach (var (section, entries) in document)
            {
                if (section is not ("aliases" or "removed"))
                {
                    _errors.Add(new ValidationError
                    {
                        SeverityLevel = ValidationError.Severity.Error,
                        Code = "ALIAS005",
                        Message = $"Unknown section '{section}' in {aliasesPath}; only 'aliases' and 'removed' are allowed",
                        FilePath = aliasesPath
                    });
                    continue;
                }
                foreach (var (from, to) in entries ?? new Dictionary<string, string?>())
                {
                    if (!DefinitionIdValidator.IsValidId(from) || (to is not null && !DefinitionIdValidator.IsValidId(to)))
                    {
                        _errors.Add(new ValidationError
                        {
                            SeverityLevel = ValidationError.Severity.Error,
                            Code = "ALIAS005",
                            Message = $"'{from}: {to ?? "~"}' in {aliasesPath} is not a definition ID mapping",
                            FilePath = aliasesPath,
                            DefinitionId = from
                        });
                        continue;
                    }
                    if (section == "aliases" && to is not null)
                        aliases[from] = to;
                    else if (section == "removed" && to is not null)
                        removed[from] = to;
                    else if (section == "removed")
                        discarded.Add(from);
                    else
                        _errors.Add(new ValidationError
                        {
                            SeverityLevel = ValidationError.Severity.Error,
                            Code = "ALIAS005",
                            Message = $"Alias '{from}' in {aliasesPath} has no target; a removal belongs under 'removed'",
                            FilePath = aliasesPath,
                            DefinitionId = from
                        });
                }
            }

            _aliasMapValidator.LoadAliasMap(aliases, removed, discarded);
            _aliasMapValidator.RegisterKnownIds(_definitions.Keys);
            
            // Validate alias targets exist
            _aliasMapValidator.ValidateAliasTargets();
            
            // Add alias errors to main error list
            foreach (var error in _aliasMapValidator.Errors)
            {
                _errors.Add(error);
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error,
                Code = "ALIAS001",
                Message = $"Failed to parse alias map {aliasesPath}: {ex.Message}",
                FilePath = aliasesPath
            });
        }
    }
    
    /// <summary>
    /// Validate cross-references between definitions.
    /// Every referenced ID must exist in the committed registry.
    /// </summary>
    private bool ValidateCrossReferences()
    {
        // Register all known IDs first
        _crossReferenceValidator.RegisterKnownIds(_definitions.Keys);
        
        bool success = true;
        
        // Check each definition's references
        foreach (var kvp in _definitions)
        {
            string id = kvp.Key;
            ContentEnvelope def = kvp.Value;
            
            // Check tags reference valid tags
            if (def.Tags != null)
            {
                foreach (var tag in def.Tags)
                {
                    // For Phase 1, just warn if tag is unknown
                    // TODO: Load from content/_tags.yaml for full validation
                }
            }
            
            // Check for dangling cross-references (e.g., spell.* references that don't exist)
            // Look for patterns like "spell.*" in tags or other fields
            if (def.Tags != null)
            {
                foreach (var tag in def.Tags)
                {
                    // Check if this looks like a cross-reference to another definition
                    if (tag.StartsWith("spell.") || tag.StartsWith("item.") || tag.StartsWith("creature.") || 
                        tag.StartsWith("npc.") || tag.StartsWith("quest.") || tag.StartsWith("ability.") ||
                        tag.StartsWith("effect.") || tag.StartsWith("recipe.") || tag.StartsWith("resource.") ||
                        tag.StartsWith("faction.") || tag.StartsWith("loot.") || tag.StartsWith("merchant.") ||
                        tag.StartsWith("affix.") || tag.StartsWith("node.") || tag.StartsWith("spawn.") ||
                        tag.StartsWith("location.") || tag.StartsWith("species.") || tag.StartsWith("schedule.") ||
                        tag.StartsWith("set.") || tag.StartsWith("region.") || tag.StartsWith("anchor.") ||
                        tag.StartsWith("fact.") || tag.StartsWith("world_flag."))
                    {
                        if (!_definitions.ContainsKey(tag))
                        {
                            _errors.Add(new ValidationError
                            {
                                SeverityLevel = ValidationError.Severity.Error,
                                Code = "XREF001",
                                Message = $"Dangling cross-reference: '{tag}' is not defined",
                                FilePath = def.SourceFile ?? "<unknown>",
                                DefinitionId = id
                            });
                            success = false;
                        }
                    }
                }
            }
        }
        
        return success;
    }
    
    /// <summary>
    /// Check for duplicate IDs across all loaded files.
    /// </summary>
    private bool ValidateDuplicateIds()
    {
        var duplicates = _duplicateIdTracker.GetAllDuplicates();
        
        bool success = true;
        foreach (var kvp in duplicates)
        {
            string id = kvp.Key;
            List<string> files = kvp.Value;
            
            // Already logged first occurrence in LoadFile
            // Log a warning for additional occurrences (first occurrence was already logged)
            for (int i = 1; i < files.Count; i++)
            {
                _errors.Add(new ValidationError
                {
                    SeverityLevel = ValidationError.Severity.Error,
                    Code = "DUP002",
                    Message = $"Additional occurrence of duplicate definition ID '{id}' in {files[i]}",
                    FilePath = files[i],
                    DefinitionId = id
                });
                success = false;
            }
        }
        
        return success;
    }
    
    /// <summary>
    /// Get definitions by kind.
    /// </summary>
    public IReadOnlyDictionary<string, ContentEnvelope> GetByKind(string kind)
    {
        return _definitions.Where(kvp => kvp.Value.Kind == kind)
                        .ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    /// <summary>
    /// Get validation summary.
    /// </summary>
    public ContentValidationSummary GetSummary()
    {
        return new ContentValidationSummary
        {
            TotalLoaded = _definitions.Count,
            TotalErrors = _errors.Count,
            Errors = _errors,
            HasErrors = _errors.Count > 0
        };
    }

    /// <summary>Renamed definition IDs from content/_aliases.yaml: old -> new.</summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliasMapValidator.Aliases;

    /// <summary>Removed definition IDs mapped forward to a replacement.</summary>
    public IReadOnlyDictionary<string, string> Removed => _aliasMapValidator.Removed;

    /// <summary>Removed definition IDs with no replacement (mapped to <c>~</c>).</summary>
    public IReadOnlyCollection<string> Discarded => _aliasMapValidator.Discarded;

    /// <summary>
    /// <c>content_hash</c> (PERSISTENCE.md §4.2): a digest over the loaded content pack - every
    /// definition's ID and YAML source, in ordinal ID order, then the alias map if the pack has one.
    /// Line endings and a byte-order mark are normalized first, so a checkout that converts LF to
    /// CRLF cannot change the hash. It identifies the content exactly; it is not an input to world
    /// generation (M2b), so a change here alone never moves the world.
    /// </summary>
    public string ComputeContentHash()
    {
        using var hasher = new UNNAMED.Domain.CanonicalHasher();
        hasher.Add("unnamed.content-hash/v1").Add(_definitions.Count);
        foreach (var (id, envelope) in _definitions.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            hasher.Add(id).Add(Normalize(envelope.YamlSource));

        string aliasesPath = Path.Combine(_contentRootPath ?? string.Empty, "_aliases.yaml");
        if (_contentRootPath is not null && File.Exists(aliasesPath))
            hasher.Add("_aliases.yaml").Add(Normalize(File.ReadAllText(aliasesPath)));
        return hasher.Finish();

        static string Normalize(string? text) => (text ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n");
    }
}
