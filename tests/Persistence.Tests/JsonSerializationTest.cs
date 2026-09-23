// UNNAMED Persistence Tests - JSON Serialization Test
using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Persistence;
using UNNAMED.Domain;

namespace UNNAMED.Persistence.Tests;

public static class JsonSerializationTest
{
    public static void Run()
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
            SavedAt = DateTime.UtcNow,
            Checksum = ""
        };

        Console.WriteLine($"Before serialization: Version = {manifest.Version}, WorldSeed = {manifest.WorldSeed}");

        // Serialize
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(manifest, options);
        Console.WriteLine($"Serialized JSON:\n{json}");

        // Deserialize
        var deserialized = JsonSerializer.Deserialize<SaveManifest>(json, options);
        Console.WriteLine($"After deserialization: Version = {deserialized?.Version}, WorldSeed = {deserialized?.WorldSeed}");
    }
}
