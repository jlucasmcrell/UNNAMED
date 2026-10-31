using System.Text.RegularExpressions;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The first-hour audit's durable half: the data invariants whose absence is an obvious bug in the first five
/// minutes - a vendor with no purse, a quest giver no routine ever places anywhere, a dialogue that offers or asks
/// for an item the build does not have. It reads the pack as files, so it guards the data whatever the typed
/// builders do with it later.
/// </summary>
public class FirstHourContentTests
{
    private static string ContentRoot => Path.Combine(RepoPaths.Root(), "content");

    private static IEnumerable<string> Yamls(string folder) =>
        Directory.EnumerateFiles(Path.Combine(ContentRoot, folder), "*.yaml", SearchOption.AllDirectories);

    private static string Read(string path) => File.ReadAllText(path, System.Text.Encoding.UTF8);

    private static HashSet<string> Ids(string folder) =>
        Yamls(folder)
            .Select(path => Regex.Match(Read(path), @"(?m)^id:\s*(\S+)").Groups[1].Value)
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryVendorHasAPurse()
    {
        var without = Yamls("merchants")
            .Where(path => !Regex.IsMatch(Read(path), @"(?m)^gold_reserve:"))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order()
            .ToArray();

        Assert.Empty(without);
    }

    [Fact]
    public void EveryQuestGiver_IsAnNpcWhoseRoutinePlacesThemSomewhere()
    {
        var routines = Yamls("routines").Select(Read).ToArray();
        var missing = new List<string>();
        foreach (var quest in Yamls("quests"))
        {
            var giver = Regex.Match(Read(quest), @"(?m)^giver_ref:\s*(\S+)").Groups[1].Value;
            if (giver.Length == 0)
                continue;

            var body = routines.FirstOrDefault(text => text.Contains("npc_ref: " + giver, StringComparison.Ordinal));
            if (body is null)
            {
                missing.Add($"{Path.GetFileNameWithoutExtension(quest)}: no routine gives {giver} a place");
                continue;
            }
            if (!body.Contains("anchors:", StringComparison.Ordinal) || !body.Contains("work_hours:", StringComparison.Ordinal))
                missing.Add($"{Path.GetFileNameWithoutExtension(quest)}: {giver}'s routine has no anchors or no work hours");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryItemADialogueOffersOrAsksFor_IsAnItemTheBuildHas()
    {
        var items = Ids("items");
        var missing = new List<string>();
        foreach (var line in Yamls("dialogue"))
        {
            foreach (Match match in Regex.Matches(Read(line), @"(?<![\w.])item\.[a-z0-9_.]+"))
            {
                if (!items.Contains(match.Value))
                    missing.Add($"{Path.GetFileNameWithoutExtension(line)}: {match.Value}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryStageADialogueNames_IsAStageItsQuestDefines()
    {
        var stages = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var quest in Yamls("quests"))
        {
            var text = Read(quest);
            var id = Regex.Match(text, @"(?m)^id:\s*(\S+)").Groups[1].Value;
            var objectives = Regex.Matches(text, @"(?m)^\s{2,}- id:\s*(\S+)").Select(m => m.Groups[1].Value);
            stages[id] = new HashSet<string>(objectives.Select(objective => $"{id}.{objective}"), StringComparer.Ordinal) { id };
        }

        var missing = new List<string>();
        foreach (var dialogue in Yamls("dialogue"))
        {
            foreach (Match match in Regex.Matches(Read(dialogue), @"(?<![\w.])quest\.[a-z0-9_.]+"))
            {
                var parts = match.Value.Split('.');
                var questId = string.Join(".", parts.Take(3));
                if (!stages.TryGetValue(questId, out var known) || !known.Contains(match.Value))
                    missing.Add($"{Path.GetFileNameWithoutExtension(dialogue)}: {match.Value}");
            }
        }

        Assert.Empty(missing);
    }
}
