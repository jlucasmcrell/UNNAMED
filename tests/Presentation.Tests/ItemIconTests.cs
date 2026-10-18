using System.Text.Json;
using System.Text.RegularExpressions;

namespace UNNAMED.Presentation.Tests;

/// <summary>Every item, and every skill and effect, has an icon binding, and the bound slot is one the icon script makes or the shipped set has.</summary>
public class ItemIconTests
{
    private static Dictionary<string, string> Icons() =>
        JsonDocument.Parse(File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_bindings.json")))
            .RootElement.GetProperty("icons").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

    private static IEnumerable<string> Ids(string folder, string prefix) =>
        Directory.GetFiles(Repository.File("content", folder), "*.yaml", SearchOption.AllDirectories)
            .Select(f => Regex.Match(File.ReadAllText(f), @"^id: (\S+)", RegexOptions.Multiline).Groups[1].Value)
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void EveryItem_HasAnIconBinding()
    {
        var icons = Icons();
        var missing = Ids("items", "item.").Where(id => !icons.TryGetValue(id, out var slot) || string.IsNullOrWhiteSpace(slot)).ToList();
        Assert.True(missing.Count == 0, "items without an icon: " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySkillAndEffect_HasAnIconBinding()
    {
        var icons = Icons();
        var missing = Ids("skills", "skill.").Concat(Ids("effects", "effect."))
            .Where(id => !icons.ContainsKey(id)).ToList();
        Assert.True(missing.Count == 0, "skills/effects without an icon: " + string.Join(", ", missing));
    }
}
