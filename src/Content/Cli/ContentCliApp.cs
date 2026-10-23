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
        bool success = loader.LoadAll(contentRoot, authoring: true);
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
            
            var gaps = ChatterContent.Lint(loader).Gaps;
            if (gaps.Count > 0)
                Console.WriteLine($"Chatter coverage: {gaps.Count} speaker x place x time-band cells are thin (chatter-coverage lists them; not an error)");

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
    /// lore-coverage - what canon holds against what the player can read (spec 41 pass 2 §1, §6): the counts by status, and every way the
    /// index and the entries disagree. Exit 1 on a disagreement: an index that lies is worse than none.
    /// </summary>
    public static int LoreCoverage(string contentRoot)
    {
        var loader = new ContentLoader();
        loader.LoadAll(contentRoot);
        var entries = LoreContent.Build(loader);
        var coverage = LoreCoverageReport.Read(contentRoot, entries);
        Console.WriteLine(coverage.Text(entries));
        return coverage.Problems.IsEmpty ? 0 : 1;
    }

    /// <summary>
    /// chatter-coverage - The corpus's thin cells (WP16): each speaker type, place and time band with fewer always-sayable lines than
    /// the chatter schema's coverage_min_lines. What the next generated batch should fill. Exit 0 always: coverage is a report.
    /// </summary>
    public static int ChatterCoverage(string contentRoot)
    {
        var loader = new ContentLoader();
        loader.LoadAll(contentRoot);
        var gaps = ChatterContent.Lint(loader).Gaps;
        Console.WriteLine($"Chatter coverage: {gaps.Count} thin cells");
        foreach (var gap in gaps)
            Console.WriteLine($"  {gap.SpeakerType} x {gap.Place} x {gap.TimeBand}: {gap.Lines}");
        return 0;
    }

    /// <summary>
    /// content:schema-dump - Dump schema information.
    /// </summary>
    public static int SchemaDump(string contentRoot, string format)
    {
        var kinds = ContentKindRegistry.Kinds;
        
        if (format == "json")
        {
            var schema = new
            {
                totalKinds = kinds.Count,
                kinds = kinds.Select(k => new {
                    kind = k.Key,
                    fullKind = k.Value.FullKind,
                    directory = k.Value.Directory,
                    idPrefix = k.Value.IdPrefix
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
                Console.WriteLine($"Kind: {kvp.Key}");
                Console.WriteLine($"  Full: {kvp.Value.FullKind}");
                Console.WriteLine($"  Directory: {kvp.Value.Directory}");
                Console.WriteLine($"  ID Prefix: {kvp.Value.IdPrefix}");
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
