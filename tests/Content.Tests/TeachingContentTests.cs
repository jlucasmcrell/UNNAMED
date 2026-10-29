using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

/// <summary>
/// WP9 from content: the teachers the brief names and what each teaches, one active technique per weapon family (D11), and the lints -
/// TCH001 (a findable source for everything learnable, sane techniques and teaches entries) and SOC001 (a line teaches only what its
/// speaker gives by conversation, and give_recipe is gone).
/// </summary>
public class TeachingContentTests
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
            : this((relativePath, find, replace))
        {
        }

        public EditedContent(params (string RelativePath, string Find, string Replace)[] edits)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var edit in edits)
            {
                string path = Path.Combine(Root, edit.RelativePath);
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                Assert.Contains(edit.Find, text);
                File.WriteAllText(path, text.Replace(edit.Find, edit.Replace));
            }
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-tch-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string code, string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    private static void AssertRefused(string code, string mentions, params (string RelativePath, string Find, string Replace)[] edits)
    {
        using var content = new EditedContent(edits);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheTeachers_AreKeraDurnTavarSelTamAndBerrin_EachWithTheirOwnLessons()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var npcs = SocialContent.BuildNpcs(loader);

        Assert.Equal(new[]
        {
            "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.sel_arien", "npc.ashen_hollow.tavar_orr",
            "npc.cairnwold.berrin_garth", "npc.cairnwold.durn_skell", "npc.cairnwold.tam_crowden", "npc.road.rim_keeper",
            "npc.sedge.iren_orr", "npc.sedge.nessa_reed", "npc.sedge.oren_pin", "npc.sedge.tess_ferry",
        }, npcs.Values.Where(n => !n.Teaches.IsEmpty).Select(n => n.Id));
        string[] Lessons(string npc, TeachingKind kind) => npcs[npc].Teaches.Where(o => o.Kind == kind).Select(o => o.DefinitionId).ToArray();

        Assert.Equal(new[] { "ability.blade.power_strike" }, Lessons("npc.ashen_hollow.kera_voss", TeachingKind.Technique));
        Assert.Equal(new[] { "ability.polearm.long_thrust" }, Lessons("npc.cairnwold.durn_skell", TeachingKind.Technique));
        Assert.Equal(new[] { "ability.bow.aimed_shot" }, Lessons("npc.ashen_hollow.tavar_orr", TeachingKind.Technique));
        Assert.Equal(new[] { "ability.blade.power_strike" }, Lessons("npc.sedge.iren_orr", TeachingKind.Technique));
        Assert.Equal(new[] { "ability.bow.aimed_shot" }, Lessons("npc.sedge.tess_ferry", TeachingKind.Technique));
        Assert.Equal(new[] { "recipe.herbal.salve_minor", "recipe.herbal.salve_ember" }, Lessons("npc.ashen_hollow.sel_arien", TeachingKind.Recipe));
        Assert.Equal(new[] { "recipe.cooking.roast_meat" }, Lessons("npc.cairnwold.tam_crowden", TeachingKind.Recipe));
        // Berrin teaches every building piece (owner decision); Renn teaches the seven pieces that built the Wayhold.
        Assert.Equal(loader.GetByKind("piece").Keys.OrderBy(k => k, StringComparer.Ordinal),
            Lessons("npc.cairnwold.berrin_garth", TeachingKind.BuildingPiece).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(npcs["npc.cairnwold.berrin_garth"].Teaches, o => Assert.Equal(TeachingKind.BuildingPiece, o.Kind));
        // The conversations' lessons keep their outcomes: free, ungated, given by a line - but for the Barrowworks wedge, which waits on the
        // ring being reachable, as Kera's ring replies do.
        Assert.All(npcs.Values.SelectMany(n => n.Teaches).Where(o => o.Kind == TeachingKind.Recipe),
            o => Assert.True(o.ByDialogue && o.Fee == 0 && (o.Requirements.IsEmpty || o.DefinitionId == "recipe.smithing.fitting_wedge")));
        var wedge = npcs["npc.ashen_hollow.kera_voss"].Teaches.Single(o => o.DefinitionId == "recipe.smithing.fitting_wedge");
        Assert.Equal("world.barrowworks.ring_accessible", Assert.IsType<FlagRequirement>(Assert.Single(wedge.Requirements)).Condition.FlagId);
        // Techniques are asked for, for a fee, once familiar with the family's weapon.
        var paidTechniqueLessons = npcs.Values.SelectMany(n => n.Teaches)
            .Where(o => o.Kind == TeachingKind.Technique && !o.ByDialogue);
        Assert.All(paidTechniqueLessons, o =>
        {
            Assert.True(o.Fee > 0);
            Assert.Equal(15, Assert.IsType<SkillBandRequirement>(Assert.Single(o.Requirements)).MinLevel);
        });
        var dialogueTechniqueLessons = npcs.Values.SelectMany(n => n.Teaches)
            .Where(o => o.Kind == TeachingKind.Technique && o.ByDialogue);
        Assert.All(dialogueTechniqueLessons, o => Assert.Equal(30, o.Fee));
    }

    [Fact]
    public void EachWeaponFamily_HasOneActiveTechnique()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        var techniques = TeachingContent.BuildTechniques(loader);

        Assert.Equal(new[] { ("ability.blade.power_strike", "skill.one_hand_blade"), ("ability.bow.aimed_shot", "skill.bow"),
            ("ability.polearm.long_thrust", "skill.polearm") }, techniques.Values.Select(t => (t.Id, t.SkillId)));
        var strike = techniques["ability.blade.power_strike"];
        Assert.Equal((1.6, 1.5, 0L, 22, 120, "effect.bleeding", 50), (strike.DamageMultiplier, strike.WindupMultiplier, strike.ReachBonusMm,
            strike.StaminaCost, strike.CooldownTicks, strike.OnHitEffect, strike.OnHitEffectPercent));
        Assert.Equal(800, techniques["ability.polearm.long_thrust"].ReachBonusMm);
    }

    [Fact]
    public void SomethingLearnableWithNoSource_IsRefused() =>
        AssertRefused("TCH001", "no findable source - no teacher, book, quest reward, experiment or starting package - teaches piece.storage.chest",
            ("npcs/cairnwold/berrin_garth.yaml", "  - { kind: building_piece, piece_ref: piece.storage.chest, fee: 10 }\n", ""),
            ("npcs/ashen_hollow/renn_vale.yaml", "  - { kind: building_piece, piece_ref: piece.storage.chest, fee: 8 }\n", ""));

    [Fact]
    public void ATechniqueNobodyTeaches_IsRefused() =>
        AssertRefused("TCH001", "teaches ability.bow.aimed_shot",
            ("npcs/ashen_hollow/tavar_orr.yaml", "ability_ref: ability.bow.aimed_shot", "ability_ref: ability.blade.power_strike"),
            ("npcs/sedge/tess_ferry.yaml", "  - kind: technique\n    ability_ref: ability.bow.aimed_shot\n    fee: 30\n    by_dialogue: true\n", ""),
            ("dialogue/sedge/tess_ferry.yaml", "    - id: teach\n      text: \"Show me how you shoot the fen birds.\"\n      consequences:\n      - command: teach\n        refs: [ability.bow.aimed_shot]\n", ""));

    [Fact]
    public void ACreaturesAttack_IsNeverTaught() =>
        AssertRefused("SOC001", "npcs/ashen_hollow/tavar_orr.yaml", "ability_ref: ability.bow.aimed_shot", "ability_ref: ability.creature.wolf_bite",
            "is not an active technique");

    [Fact]
    public void AnEntryNamingItsDefinitionByTheWrongField_IsRefused() =>
        AssertRefused("SOC001", "npcs/cairnwold/berrin_garth.yaml", "{ kind: building_piece, piece_ref: piece.pad.timber, fee: 5 }",
            "{ kind: building_piece, recipe_ref: recipe.cooking.roast_meat, fee: 5 }", "a building_piece entry names its definition by piece_ref");

    [Fact]
    public void ABandConfigTeachingDoesNotName_IsRefused() =>
        AssertRefused("SOC001", "npcs/ashen_hollow/tavar_orr.yaml", "band: familiar", "band: legendary", "'legendary' is not a skill band");

    [Fact]
    public void AQuestRequirementOnAnObjectiveTheQuestDoesNotHave_IsRefused() =>
        AssertRefused("QST001", "npcs/ashen_hollow/tavar_orr.yaml", "requires: { skill: { skill_ref: skill.bow, band: familiar } }",
            "requires: { quest: { quest_ref: quest.ashen_hollow.iron_under_ash, objective: o_no_such, is: satisfied } }",
            "npc.ashen_hollow.tavar_orr teaches[0]: quest.ashen_hollow.iron_under_ash has no objective 'o_no_such'");

    [Fact]
    public void ANegativeFee_IsRefused() =>
        AssertRefused("SOC001", "npcs/cairnwold/berrin_garth.yaml", "piece_ref: piece.pad.timber, fee: 5", "piece_ref: piece.pad.timber, fee: -5",
            "fee is a whole number of coin, 0 or more");

    [Fact]
    public void GiveRecipe_IsRefused_ForTeach() =>
        AssertRefused("SOC001", "dialogue/cairnwold/tam_crowden.yaml", "{ command: teach, refs: [recipe.cooking.roast_meat] }",
            "{ command: give_recipe, recipe_ref: recipe.cooking.roast_meat }", "give_recipe is replaced by teach");

    [Fact]
    public void ALineTeachingWhatItsSpeakerDoesNotGiveByConversation_IsRefused() =>
        AssertRefused("SOC001", "npcs/cairnwold/tam_crowden.yaml", "recipe_ref: recipe.cooking.roast_meat, by_dialogue: true", "recipe_ref: recipe.cooking.roast_meat",
            "teaches recipe.cooking.roast_meat, which npc.cairnwold.tam_crowden, who speaks it, does not give by_dialogue");

    [Fact]
    public void ALessonByConversationThatNoLineGives_IsRefused() =>
        AssertRefused("SOC001", "npcs/cairnwold/berrin_garth.yaml", "{ kind: building_piece, piece_ref: piece.pad.timber, fee: 5 }",
            "{ kind: building_piece, piece_ref: piece.pad.timber, fee: 5, by_dialogue: true }", "teaches piece.pad.timber by_dialogue, but no line of");

    [Fact]
    public void AReplyThatTeachesAndMovesAnItem_IsRefused() =>
        AssertRefused("SOC001", "dialogue/cairnwold/tam_crowden.yaml", "{ command: teach, refs: [recipe.cooking.roast_meat] }",
            "{ command: teach, refs: [recipe.cooking.roast_meat] }\n          - { command: transfer_item, item_ref: item.consumable.roast_meat, count: 1, to: player }",
            "a reply carries at most one consequence that can be refused (transfer_item, hire_hireling, teach)");
}
