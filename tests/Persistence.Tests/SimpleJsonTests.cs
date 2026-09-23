// UNNAMED Persistence Tests - Simple JSON Test
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace SimpleJsonTests;

public class SimpleJsonTests
{
    public class TestManifest
    {
        public int Version { get; set; }
        public long WorldSeed { get; set; }
    }

    [Fact]
    public void BasicJsonSerializationWorks()
    {
        var manifest = new TestManifest { Version = 1, WorldSeed = 12345 };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(manifest, options);
        Console.WriteLine($"JSON: {json}");
        Assert.Contains("\"version\": 1", json);

        var deserialized = JsonSerializer.Deserialize<TestManifest>(json, options);
        Console.WriteLine($"Deserialized: Version = {deserialized?.Version}, WorldSeed = {deserialized?.WorldSeed}");
        
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Version);
        Assert.Equal(12345, deserialized.WorldSeed);
    }
}
