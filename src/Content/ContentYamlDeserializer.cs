// UNNAMED Content Validation - YAML Deserializer
// Deserializes YAML content into ContentEnvelope and derived types
// No Godot references

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Core.Events;
using System.Collections;
using System.Reflection;

namespace UNNAMED.Content;

/// <summary>
/// Exception thrown when content loading fails.
/// Includes file path and line number for precise error reporting.
/// </summary>
public class ContentLoadException : Exception
{
    /// <summary>
    /// File path where the error occurred.
    /// </summary>
    public string FilePath { get; set; } = "";
    
    /// <summary>
    /// Line number where the error occurred (1-based).
    /// </summary>
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Create a new ContentLoadException.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="filePath">File path</param>
    /// <param name="lineNumber">Line number (1-based)</param>
    /// <param name="innerException">Inner exception</param>
    public ContentLoadException(string message, string filePath, int lineNumber, Exception? innerException = null) 
        : base(message, innerException)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
    }
    
    /// <summary>
    /// Create a new ContentLoadException without line number.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="filePath">File path</param>
    public ContentLoadException(string message, string filePath) 
        : base(message)
    {
        FilePath = filePath;
        LineNumber = 0;
    }
}

/// <summary>
/// Static class for deserializing YAML content into ContentEnvelope.
/// Sets SourceFile after deserialization for error reporting.
/// </summary>
public static class ContentYamlDeserializer
{
    private static readonly ISerializer _serializer;
    private static readonly IDeserializer _deserializer;
    
    static ContentYamlDeserializer()
    {
        var builder = new SerializerBuilder();
        
        _serializer = builder.Build();
        
        // For YamlDotNet v15.1.0, the way to skip unknown properties is to use
        // a custom TypeConverter or to use dictionary-based deserialization
        // where we first deserialize into Dictionary<string, object> and then
        // manually map the known fields
        
        // Create a deserializer that deserializes to object first
        var deserializerBuilder = new DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance);
        
        _deserializer = deserializerBuilder.Build();
    }
    
    /// <summary>
    /// Extract line number from exception message (format: "line N").
    /// </summary>
    private static int GetLineNumberFromException(Exception ex)
    {
        var message = ex.Message;
        var match = System.Text.RegularExpressions.Regex.Match(message, @"line\s+(\d+)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success && int.TryParse(match.Groups[1].Value, out var lineNumber))
        {
            return lineNumber;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Deserialize YAML string into ContentEnvelope.
    /// Sets SourceFile property for error reporting.
    /// </summary>
    /// <param name="yaml">Raw YAML content</param>
    /// <param name="sourceFile">Source file path (for error reporting)</param>
    /// <returns>ContentEnvelope with SourceFile set</returns>
    /// <exception cref="ContentLoadException">Thrown on YAML parsing error</exception>
    public static ContentEnvelope Deserialize(string yaml, string sourceFile)
    {
        try
        {
            // First deserialize into a dictionary to capture all fields including unknown ones
            var rawDict = _deserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            // Extract known fields from the dictionary
            string id = "";
            string kind = "";
            string displayKey = "";
            string[] tags = Array.Empty<string>();
            
            if (rawDict.TryGetValue("id", out var idObj) && idObj is string idVal)
            {
                id = idVal;
            }
            
            if (rawDict.TryGetValue("kind", out var kindObj) && kindObj is string kindVal)
            {
                kind = kindVal;
            }
            
            if (rawDict.TryGetValue("display_key", out var displayKeyObj) && displayKeyObj is string displayKeyVal)
            {
                displayKey = displayKeyVal;
            }
            
            if (rawDict.TryGetValue("tags", out var tagsObj) && tagsObj is List<object> tagsList)
            {
                tags = tagsList.Where(t => t is string s).Select(s => (string)s).ToArray();
            }
            
            // Now create a ContentEnvelope with all required fields set
            var envelope = new ContentEnvelope
            {
                Id = id,
                Kind = kind,
                DisplayKey = displayKey,
                Tags = tags,
                SourceFile = sourceFile,
                YamlSource = yaml
            };
            
            // Extract remaining optional fields
            if (rawDict.TryGetValue("schema", out var schemaObj) && schemaObj is int schema)
            {
                envelope.Schema = schema;
            }
            
            if (rawDict.TryGetValue("notes", out var notesObj) && notesObj is string notes)
            {
                envelope.Notes = notes;
            }
            
            if (rawDict.TryGetValue("defines", out var definesObj) && definesObj is object[] definesArray)
            {
                envelope.Defines = definesArray.Where(d => d is string s).Select(s => (string)s).ToArray();
            }
            
            if (rawDict.TryGetValue("deprecated", out var deprecatedObj) && deprecatedObj is string deprecated)
            {
                envelope.Deprecated = deprecated;
            }
            
            if (rawDict.TryGetValue("alias_of", out var aliasOfObj) && aliasOfObj is string aliasOf)
            {
                envelope.AliasOf = aliasOf;
            }
            
            return envelope;
        }
        catch (Exception ex)
        {
            throw new ContentLoadException($"YAML parsing error in {sourceFile}: {ex.Message}", sourceFile, GetLineNumberFromException(ex), ex)
            {
                FilePath = sourceFile,
                LineNumber = GetLineNumberFromException(ex)
            };
        }
    }
    
    /// <summary>
    /// Deserialize YAML string into a specific content definition type.
    /// </summary>
    /// <typeparam name="T">The content definition type</typeparam>
    /// <param name="yaml">Raw YAML content</param>
    /// <param name="sourceFile">Source file path</param>
    /// <returns>Content definition with SourceFile set</returns>
    /// <exception cref="ContentLoadException">Thrown on YAML parsing error</exception>
    public static T Deserialize<T>(string yaml, string sourceFile) where T : ContentEnvelope, new()
    {
        try
        {
            // First deserialize into a dictionary to capture all fields including unknown ones
            var rawDict = _deserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            // Extract known fields from the dictionary
            string id = "";
            string kind = "";
            string displayKey = "";
            string[] tags = Array.Empty<string>();
            
            if (rawDict.TryGetValue("id", out var idObj) && idObj is string idVal)
            {
                id = idVal;
            }
            
            if (rawDict.TryGetValue("kind", out var kindObj) && kindObj is string kindVal)
            {
                kind = kindVal;
            }
            
            if (rawDict.TryGetValue("display_key", out var displayKeyObj) && displayKeyObj is string displayKeyVal)
            {
                displayKey = displayKeyVal;
            }
            
            if (rawDict.TryGetValue("tags", out var tagsObj) && tagsObj is List<object> tagsList2)
            {
                tags = tagsList2.Where(t => t is string s).Select(s => (string)s).ToArray();
            }
            
            // Now create the specialized type with all required fields set
            var definition = new T
            {
                Id = id,
                Kind = kind,
                DisplayKey = displayKey,
                Tags = tags,
                SourceFile = sourceFile,
                YamlSource = yaml
            };
            
            // Extract remaining optional fields
            if (rawDict.TryGetValue("schema", out var schemaObj) && schemaObj is int schema)
            {
                definition.Schema = schema;
            }
            
            if (rawDict.TryGetValue("notes", out var notesObj) && notesObj is string notes)
            {
                definition.Notes = notes;
            }
            
            if (rawDict.TryGetValue("defines", out var definesObj) && definesObj is object[] definesArray)
            {
                definition.Defines = definesArray.Where(d => d is string s).Select(s => (string)s).ToArray();
            }
            
            if (rawDict.TryGetValue("deprecated", out var deprecatedObj) && deprecatedObj is string deprecated)
            {
                definition.Deprecated = deprecated;
            }
            
            if (rawDict.TryGetValue("alias_of", out var aliasOfObj) && aliasOfObj is string aliasOf)
            {
                definition.AliasOf = aliasOf;
            }
            
            // Specialized properties will be manually mapped based on kind
            // For now, we just use the base ContentEnvelope fields
            // Specialized properties (use, stats, etc.) are intentionally not mapped
            // since they are handled by the specialized schema types
            
            return definition;
        }
        catch (Exception ex)
        {
            throw new ContentLoadException($"YAML parsing error in {sourceFile}: {ex.Message}", sourceFile, GetLineNumberFromException(ex), ex)
            {
                FilePath = sourceFile,
                LineNumber = GetLineNumberFromException(ex)
            };
        }
    }
}
