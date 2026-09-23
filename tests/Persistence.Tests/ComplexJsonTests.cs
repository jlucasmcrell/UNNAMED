// UNNAMED Persistence Tests - Complex JSON Test
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ComplexJsonTests;

public class ComplexJsonTests
{
    public class TestManifest
    {
        public int Version { get; set; }
    }

    public class TestChild
    {
        public string Value { get; set; } = string.Empty;
    }

    public class TestParent
    {
        public TestManifest Manifest { get; set; } = new();
        public TestChild[] Children { get; set; } = Array.Empty<TestChild>();
    }

    [Fact]
    public void NestedObjectJsonSerializationWorks()
    {
        var parent = new TestParent
        {
            Manifest = new TestManifest { Version = 1 },
            Children = new TestChild[]
            {
                new TestChild { Value = "child1" }
            }
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(parent, options);
        Console.WriteLine($"JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<TestParent>(json, options);
        Console.WriteLine($"Deserialized: Manifest.Version = {deserialized?.Manifest.Version}, Children.Length = {deserialized?.Children.Length}");
        
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Manifest.Version);
        Assert.Equal(1, deserialized.Children.Length);
    }
}
