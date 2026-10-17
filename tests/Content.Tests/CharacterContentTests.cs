using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Content.Tests;

/// <summary>Peoples and Calls from content (WP2): the Veth and the four CC-0 Calls build, and the CHR lint refuses what the creator cannot offer.</summary>
public class CharacterContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-chr-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "CHR001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesCreator_OffersTheVeth_AndFourCalls_BuiltFromWhatExists()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.DoesNotContain(loader.Errors, e => e.Code == "CHR001");
        var setup = CharacterContent.Build(loader);

        var veth = Assert.Single(setup.Peoples.Values);
        Assert.Equal(("people.veth", true), (veth.Id, veth.Playable));
        Assert.Contains("Wenna Lind", veth.Names.Names);

        Assert.Equal(new[] { "call.hedge_healer", "call.scholar", "call.smiths_hand", "call.wayfarer" }, setup.Calls.Keys);
        var smith = setup.Calls["call.smiths_hand"];
        Assert.Equal(new[] { (CharacterAttribute.Might, 2), (CharacterAttribute.Endurance, 2) }.OrderBy(a => a.Item1),
            smith.Package.AttributeBias.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(new[] { "skill.polearm", "skill.smithing" }, smith.Package.Skills.Keys);
        Assert.Equal(new CallContact("npc.ashen_hollow.kera_voss", 5), smith.Contact);
        Assert.All(setup.Calls.Values, call =>
        {
            Assert.Equal(CallDefinition.BiasPoints, call.Package.AttributeBias.Values.Sum());
            Assert.Equal(CallDefinition.SkillCount, call.Package.Skills.Count);
            Assert.Empty(call.Package.Techniques);
            Assert.NotEmpty(call.Kit);
            Assert.NotEmpty(call.Tagline);
            Assert.DoesNotContain("class", call.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ACallSpendingTooManyPoints_IsRefused() =>
        AssertRefused("calls/wayfarer.yaml", "endurance: 2, might: 1", "endurance: 3, might: 1", "a Call spends exactly 4");

    [Fact]
    public void ACallStartingASkillAboveFifteen_IsRefused() =>
        AssertRefused("calls/scholar.yaml", "skill.force: 15", "skill.force: 20", "each at 15");

    [Fact]
    public void ACallStartingAnUndefinedSkill_IsRefused() =>
        AssertRefused("calls/scholar.yaml", "skill.force: 15", "skill.astronomy: 15", "skill.astronomy is not a defined skill");

    [Fact]
    public void ACallWearingOverTheCommonKit_IsRefused() =>
        AssertRefused("calls/smiths_hand.yaml", "item.armor.hide_cap, count: 1, equip: true", "item.weapon.march_spear, count: 1, equip: true",
            "cannot be worn over the common starting kit");

    [Fact]
    public void ACallWhoseContactIsNoNpc_IsRefused() =>
        AssertRefused("calls/hedge_healer.yaml", "contact: { npc_ref: npc.ashen_hollow.renn_vale", "contact: { npc_ref: npc.cairnwold.nobody", "is not a defined npc");

    [Fact]
    public void APeopleWithAnUnusableName_IsRefused() =>
        AssertRefused("people/veth.yaml", "  - Teagan Peel", "  - Teagan Peel of the Long Road Beyond the Hills", "A name is at most 24");

    [Fact]
    public void APeopleListingANameTwice_IsRefused() =>
        AssertRefused("people/veth.yaml", "  - Teagan Peel", "  - Teagan Peel\n  - Wenna Lind", "lists a name twice");
}
