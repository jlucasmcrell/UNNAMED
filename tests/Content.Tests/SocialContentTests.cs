using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

/// <summary>NPCs and conversations from content (M4): the waystation's three build, and the SOC lint refuses what Phase 1 cannot run.</summary>
public class SocialContentTests
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

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-soc-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "SOC001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheWaystation_IsThreeNamedNpcs_EachWithAConversation()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var npcs = SocialContent.BuildNpcs(loader);
        var dialogues = SocialContent.BuildDialogues(loader);

        Assert.Equal(new[] { "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.sel_arien" }, npcs.Keys);
        var kera = npcs["npc.ashen_hollow.kera_voss"];
        Assert.Equal(("Kera Voss", "craftsperson", "merchant.ashen_hollow.kera_voss"), (kera.Name, kera.Role, kera.MerchantId));
        Assert.True(kera.Offers(NpcServices.Trade));
        Assert.False(npcs["npc.ashen_hollow.renn_vale"].Offers(NpcServices.Trade));
        Assert.All(npcs.Values, n => Assert.Contains(n.Id, dialogues[n.DialogueId!].Participants));

        // Kera teaches the two recipes (the starting package no longer does); Sel hands over the primer.
        var teach = dialogues["dialogue.ashen_hollow.kera_voss"].Nodes["greet"].Choices.Single(c => c.Id == "teach");
        Assert.Equal(new[] { "recipe.smithing.iron_billet", "recipe.smithing.march_spear" },
            teach.Consequences.OfType<GiveRecipeConsequence>().Select(g => g.RecipeId));
        var take = dialogues["dialogue.ashen_hollow.sel_arien"].Nodes["primer"].Choices.Single();
        Assert.Equal(new TransferItemConsequence("item.tome.resonance_primer", 1, true), take.Consequences.OfType<TransferItemConsequence>().Single());
    }

    [Fact]
    public void AReplyToANodeThatIsNotThere_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "next: place\n", "next: nowhere\n", "names node 'nowhere'");

    [Fact]
    public void AConditionPhaseOneDoesNotBuild_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "{ kind: has_item, item_ref: item.material.iron_ore }",
            "{ kind: quest_state, quest_ref: quest.none }", "condition 'quest_state' is not built");

    [Fact]
    public void AConsequencePhaseOneDoesNotBuild_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/kera_voss.yaml", "consequences: [{ command: open_service, service: trade }]\n      - id: leave\n        text: \"Another time.\"",
            "consequences: [{ command: start_combat }]\n      - id: leave\n        text: \"Another time.\"", "consequence 'start_combat' is not built");

    [Fact]
    public void ALoopOfSpentLines_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/sel_arien.yaml", "    once: true\n    next_if_exhausted: again\n    choices:\n      - id: take",
            "    once: true\n    next_if_exhausted: primer\n    choices:\n      - id: take", "spent lines loop");

    [Fact]
    public void ATraderWithoutStock_IsRefused() =>
        AssertRefused("npcs/ashen_hollow/kera_voss.yaml", "merchant_ref: merchant.ashen_hollow.kera_voss\n", "", "a trader names its merchant_ref");

    [Fact]
    public void AServiceOpenedByOneWhoDoesNotOfferIt_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "      - id: leave\n        text: \"I'll look around.\"",
            "      - id: leave\n        text: \"I'll look around.\"\n        consequences: [{ command: open_service, service: trade }]", "none of its participants offers");

    [Fact]
    public void ANamelessCrowd_IsNotBuilt() =>
        AssertRefused("npcs/ashen_hollow/renn_vale.yaml", "unique: true", "unique: false", "named NPCs only");

    [Fact]
    public void ARelationshipOnADimensionThereIsNot_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/sel_arien.yaml", "dimension: trust, delta: 5, event: given_the_primer",
            "dimension: love, delta: 5, event: given_the_primer", "'love' is not a relationship dimension");
}
