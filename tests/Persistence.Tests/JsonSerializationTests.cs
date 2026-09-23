// UNNAMED Persistence Tests - Simple JSON Test
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Persistence;
using Xunit;

namespace JsonSerializationTests;

public class SaveManifestSerializationTests
{
    [Fact]
    public void Serialize_Deserialize_SaveManifest_PreservesData()
    {
        // Create test data
        var manifest = new SaveManifest
        {
            Version = 1,
            WorldSeed = 12345,
            WorldGenVersion = 1,
            ContentHash = "abc123",
            GameTick = 1000,
            ChangedCellCount = 2,
            EntityCount = 5,
            SavedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = ""
        };

        // Serialize
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(manifest, options);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"worldSeed\": 12345", json);

        // Deserialize
        var deserialized = JsonSerializer.Deserialize<SaveManifest>(json, options);
        
        // Debug output
        Console.WriteLine($"JSON: {json}");
        Console.WriteLine($"Version: {deserialized?.Version}, WorldSeed: {deserialized?.WorldSeed}");
        
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Version);
        Assert.Equal(12345, deserialized.WorldSeed);
        Assert.Equal("abc123", deserialized.ContentHash);
    }
}
