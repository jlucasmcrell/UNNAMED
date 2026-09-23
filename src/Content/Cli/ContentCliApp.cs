// UNNAMED Content Validation - CLI Infrastructure
// Provides command-line targets for content validation
// No Godot references

namespace UNNAMED.Content.Cli;

/// <summary>
/// CLI application for content validation.
/// Targets: content:lint, content:schema-dump, content:xref
/// </summary>
public static class ContentCliApp
{
    // CLI entry point is in Program.cs to avoid duplicate Main methods
    // This class contains the command implementations only
    
    /// <summary>
    /// content:lint - Validate all content files.
    /// </summary>
    public static int LintContent(string contentRoot, string format)
    {
        var loader = new ContentLoader();
        bool success = loader.LoadAll(contentRoot);
        var summary = loader.GetSummary();
        
        if (format == "json")
        {
            Console.WriteLine(SerializeToJson(summary, loader.Errors));
        }
        else
        {
            Console.WriteLine($"Content Validation Report");
            Console.WriteLine("=========================");
            Console.WriteLine($"Loaded: {summary.TotalLoaded} definitions");
            Console.WriteLine($"Errors: {summary.TotalErrors}");
            Console.WriteLine();
            
            if (summary.Errors.Any())
            {
                Console.WriteLine("Errors:");
                Console.WriteLine("-------");
                foreach (var error in summary.Errors)
                {
                    Console.WriteLine(error.ToString());
                }
                Console.WriteLine();
            }
            
            if (success)
            {
                Console.WriteLine("Validation passed");
                return 0;
            }
            else
            {
                Console.WriteLine("Validation failed");
                return 1;
            }
        }
        
        return success ? 0 : 1;
    }
    
    /// <summary>
    /// content:schema-dump - Dump schema information.
    /// </summary>
    public static int SchemaDump(string contentRoot, string format)
    {
        var kinds = ContentKindRegistry.Kinds.ToDictionary(k => k.Kind, k => k);
        
        if (format == "json")
        {
            var schema = new
            {
                totalKinds = kinds.Count,
                kinds = kinds.Select(kvp => new {
                    kind = kvp.Key,
                    fullKind = kvp.Value.FullKind,
                    directory = kvp.Value.Directory,
                    idPrefix = kvp.Value.IdPrefix
                }).ToList()
            };
            Console.WriteLine(SerializeToJson(schema));
        }
        else
        {
            Console.WriteLine("Content Kind Schema Dump");
            Console.WriteLine("========================");
            Console.WriteLine($"Total kinds: {kinds.Count}");
            Console.WriteLine();
            
            Console.WriteLine("Kind definitions:");
            Console.WriteLine("-----------------");
            foreach (var kvp in kinds)
            {
                var def = kvp.Value;
                Console.WriteLine($"Kind: {def.Kind}");
                Console.WriteLine($"  Full: {def.FullKind}");
                Console.WriteLine($"  Directory: {def.Directory}");
                Console.WriteLine($"  ID Prefix: {def.IdPrefix}");
                Console.WriteLine();
            }
        }
        
        return 0;
    }
    
    /// <summary>
    /// content:xref - Generate cross-reference report.
    /// </summary>
    public static int CrossReferenceReport(string contentRoot, string format)
    {
        var loader = new ContentLoader();
        loader.LoadAll(contentRoot);
        
        if (format == "json")
        {
            var report = new
            {
                totalDefinitions = loader.Definitions.Count,
                definitions = loader.Definitions.Select(k => new {
                    id = k.Key,
                    kind = k.Value.Kind,
                    displayKey = k.Value.DisplayKey,
                    tags = k.Value.Tags?.ToList()
                }).ToList()
            };
            Console.WriteLine(SerializeToJson(report));
        }
        else
        {
            Console.WriteLine("Cross-Reference Report");
            Console.WriteLine("======================");
            Console.WriteLine($"Loaded: {loader.Definitions.Count} definitions");
            Console.WriteLine();
            
            // Group by kind
            var byKind = loader.Definitions.GroupBy(k => k.Value.Kind);
            
            Console.WriteLine("Definitions by kind:");
            Console.WriteLine("--------------------");
            foreach (var group in byKind)
            {
                Console.WriteLine($"\n{group.Key} ({group.Count()} definitions):");
                foreach (var def in group)
                {
                    Console.WriteLine($"  {def.Key} - {def.Value.DisplayKey}");
                }
            }
        }
        
        var summary = loader.GetSummary();
        return summary.HasErrors ? 1 : 0;
    }
    
    /// <summary>
    /// Serialize object to JSON.
    /// </summary>
    private static string SerializeToJson(object obj, IReadOnlyList<ValidationError>? errors = null)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return "{}";
        }
    }
}

/// <summary>
/// Extension methods for ContentKindDefinition.
/// </summary>
public static class ContentKindDefinitionExtensions
{
    /// <summary>
    /// Check if a definition ID matches this kind's pattern.
    /// </summary>
    public static bool MatchesId(this ContentKindDefinition kindDef, string id)
    {
        return id.StartsWith(kindDef.IdPrefix);
    }
}
