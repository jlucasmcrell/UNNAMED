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
    public void TheHollow_IsFourNamedNpcs_EachWithAConversation()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var npcs = SocialContent.BuildNpcs(loader);
        var dialogues = SocialContent.BuildDialogues(loader);

        Assert.Equal(new[] { "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.sel_arien", "npc.ashen_hollow.tavar_orr" }, npcs.Keys);
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

    /// <summary>M6: Tavar can join the character - his spear, his health, his jerkin - and config.companion says how companions behave.</summary>
    [Fact]
    public void Tavar_CanJoin_AndTheRulesCompanionsKeepAreContent()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var npcs = SocialContent.BuildNpcs(loader);

        var tavar = npcs["npc.ashen_hollow.tavar_orr"].Companion!;
        Assert.Equal((100, "item.weapon.march_spear"), (tavar.MaxHealth, tavar.WeaponId));
        Assert.Equal(new[] { 0, 3, 1 }, new[] { UNNAMED.Domain.Combat.BodyRegion.Head, UNNAMED.Domain.Combat.BodyRegion.Torso, UNNAMED.Domain.Combat.BodyRegion.Limbs }
            .Select(r => tavar.Armor[r]));
        Assert.All(npcs.Values.Where(n => n.Id != "npc.ashen_hollow.tavar_orr"), n => Assert.Null(n.Companion));
        // Metres and seconds in the content; millimetres and 20 Hz ticks here.
        Assert.Equal(new UNNAMED.Domain.Companions.CompanionTuning(2_500, 4_000, 8_000, 30_000, 80, 1_000, 48, 10_000, 16_000, 4_000, 12, 1_200, 40, 200, 2),
            SocialContent.BuildCompanionTuning(loader));
    }

    [Fact]
    public void RecruitingSomeoneWhoCannotJoin_IsRefused() =>
        AssertRefused("npcs/ashen_hollow/tavar_orr.yaml", "companion:\n  health: 100", "sidekick:\n  health: 100",
            "recruits or orders a companion, but none of its participants can join");

    [Fact]
    public void ACompanionWithABow_IsRefused() =>
        AssertRefused("npcs/ashen_hollow/tavar_orr.yaml", "weapon_item_ref: item.weapon.march_spear", "weapon_item_ref: item.weapon.hunting_bow",
            "is a ranged weapon");

    [Fact]
    public void CompanionRulesThatDoNotNest_AreRefused() =>
        AssertRefused("config/companion.yaml", "run_beyond_m: 4 ", "run_beyond_m: 2 ", "the follow distances nest");

    [Fact]
    public void AnOrderThatIsNotFollowOrWait_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/tavar_orr.yaml", "order: wait }]", "order: guard }]", "a companion's order is follow or wait");

    [Fact]
    public void AVisitedConditionOnALineAnotherConversationDoesNotHave_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/sel_arien.yaml", "dialogue_ref: dialogue.ashen_hollow.tavar_orr, node: greet",
            "dialogue_ref: dialogue.ashen_hollow.tavar_orr, node: nowhere", "names node 'nowhere' of dialogue.ashen_hollow.tavar_orr");

    [Fact]
    public void AReplyToANodeThatIsNotThere_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "next: place\n", "next: nowhere\n", "names node 'nowhere'");

    [Fact]
    public void AConditionPhaseOneDoesNotBuild_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "{ kind: has_item, item_ref: item.material.iron_ore }",
            "{ kind: time_of_day, is: night }", "condition 'time_of_day' is not built");

    [Fact]
    public void AConsequencePhaseOneDoesNotBuild_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/kera_voss.yaml", "consequences: [{ command: open_service, service: trade }]\n      - id: leave\n        text: \"Another time.\"",
            "consequences: [{ command: start_combat }]\n      - id: leave\n        text: \"Another time.\"", "consequence 'start_combat' is not built");

    [Fact]
    public void ALoopOfSpentLines_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/kera_voss.yaml", "    once: true\n    next_if_exhausted: again\n    choices:\n      - id: back\n        text: \"Thank you.\"",
            "    once: true\n    next_if_exhausted: praised\n    choices:\n      - id: back\n        text: \"Thank you.\"", "spent lines loop");

    /// <summary>
    /// The Phase-1 technical audit, H-01 and L-18: a once line is spent when it is heard, so a reply on it that gives something is lost for
    /// good to a character who leaves before answering - unless the line it falls back to offers the same reply again.
    /// </summary>
    [Fact]
    public void AGiftOnALineSpentOnceHeard_IsRefused_UnlessItsFallbackOffersItAgain()
    {
        // Sel's primer as M4 wrote it: a once line whose only reply hands over the book.
        AssertRefused("dialogue/ashen_hollow/sel_arien.yaml", "    text: \"One. The Resonance Primer",
            "    once: true\n    next_if_exhausted: again\n    text: \"One. The Resonance Primer", "node primer is spent once heard, and its reply take carries consequences");
        // Tavar's thanks offered on his first line only, as M6 wrote it.
        AssertRefused("dialogue/ashen_hollow/tavar_orr.yaml", "      - id: sent\n        text: \"Sel sent me.\"\n        conditions:\n          - { kind: visited, node: caught, not: true }",
            "      - id: sent_again\n        text: \"Sel sent me.\"\n        conditions:\n          - { kind: visited, node: caught, not: true }",
            "node greet is spent once heard, and its reply sent carries consequences");
        // The fallback's reply must do the same thing, not merely share the id.
        AssertRefused("dialogue/ashen_hollow/tavar_orr.yaml", "      - id: found\n        text: \"The stones were out of line. I put them right.\"\n        conditions:\n          - { kind: visited, node: caught, not: true }\n          - { kind: quest_state, quest_ref: quest.ashen_hollow.three_quiet_stones, is: not_started }\n        consequences:\n          - { command: record_relationship_event, npc_ref: npc.ashen_hollow.tavar_orr, dimension: trust, delta: 10",
            "      - id: found\n        text: \"The stones were out of line. I put them right.\"\n        conditions:\n          - { kind: visited, node: caught, not: true }\n          - { kind: quest_state, quest_ref: quest.ashen_hollow.three_quiet_stones, is: not_started }\n        consequences:\n          - { command: record_relationship_event, npc_ref: npc.ashen_hollow.tavar_orr, dimension: trust, delta: 5",
            "its reply found carries consequences");
    }

    /// <summary>Every once line of the game's conversations that offers something offers it again on the line it falls back to.</summary>
    [Fact]
    public void EveryGiftOnAOnceLine_IsOfferedAgainOnItsFallback()
    {
        var dialogues = SocialContent.BuildDialogues(Load(Path.Combine(RepoPaths.Root(), "content")));
        var gifts = dialogues.Values.SelectMany(d => d.Nodes.Values.Where(n => n.Once)
            .SelectMany(n => n.Choices.Where(c => !c.Consequences.IsEmpty).Select(c => (Dialogue: d, Node: n, Choice: c)))).ToList();
        Assert.NotEmpty(gifts);
        foreach (var (dialogue, node, choice) in gifts)
        {
            var fallback = dialogue.Nodes[node.NextIfExhausted ?? throw new Xunit.Sdk.XunitException($"{dialogue.Id} {node.Id} has no fallback")];
            Assert.Contains(fallback.Choices, c => c.Id == choice.Id && c.Consequences.SequenceEqual(choice.Consequences));
        }
    }

    [Fact]
    public void ATraderWithoutStock_IsRefused() =>
        AssertRefused("npcs/ashen_hollow/kera_voss.yaml", "merchant_ref: merchant.ashen_hollow.kera_voss\n", "", "a trader names its merchant_ref");

    [Fact]
    public void AServiceOpenedByOneWhoDoesNotOfferIt_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/renn_vale.yaml", "      - id: leave\n        text: \"Nothing for now.\"",
            "      - id: leave\n        text: \"Nothing for now.\"\n        consequences: [{ command: open_service, service: trade }]", "none of its participants offers");

    [Fact]
    public void ANamelessCrowd_IsNotBuilt() =>
        AssertRefused("npcs/ashen_hollow/renn_vale.yaml", "unique: true", "unique: false", "named NPCs only");

    [Fact]
    public void ARelationshipOnADimensionThereIsNot_IsRefused() =>
        AssertRefused("dialogue/ashen_hollow/sel_arien.yaml", "dimension: trust, delta: 5, event: given_the_primer",
            "dimension: love, delta: 5, event: given_the_primer", "'love' is not a relationship dimension");
}
