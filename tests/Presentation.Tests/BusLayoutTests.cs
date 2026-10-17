using System.Text.RegularExpressions;

namespace UNNAMED.Presentation.Tests;

/// <summary>WP10 A3: the committed bus layout carries the five buses AudioMix plays on, each sending to Master, so the engine starts with them.</summary>
public class BusLayoutTests
{
    [Fact]
    public void TheLayout_HasTheFiveBuses_UnderMaster()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "src", "UNNAMED.sln")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Repository root not found");
        string layout = File.ReadAllText(Path.Combine(root, "src", "Presentation", "default_bus_layout.tres"));

        Assert.StartsWith("[gd_resource type=\"AudioBusLayout\"", layout);
        var names = Regex.Matches(layout, @"bus/(\d+)/name = &""(\w+)""").ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
        Assert.Equal(new[] { "Music", "Effects", "Ambience", "Interface", "Voice" }, names.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        foreach (int bus in names.Keys)
            Assert.Contains($"bus/{bus}/send = &\"Master\"", layout);
    }
}
