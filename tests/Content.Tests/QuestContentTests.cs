using UNNAMED.Domain.Quests;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Quests from content (M5): Iron Under Ash builds as the content bible's Quest 1, and the lint refuses what Phase 1 cannot run -
/// an objective type that is not in the closed set or not built, a reference to an item, NPC or faction that does not exist, an
/// orphaned objective, a loop, a join that can never happen, and a quest nothing starts.
/// </summary>
public class QuestContentTests
{
    private const string IronUnderAsh = "quest.ashen_hollow.iron_under_ash";

    private static readonly Lazy<ContentLoader> Game = new(() =>
    {
        var loader = new ContentLoader();
        Assert.True(loader.LoadAll(Path.Combine(RepoPaths.Root(), "content")), string.Join("\n", loader.Errors));
        return loader;
    });

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

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-qst-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static IReadOnlyList<ValidationError> ErrorsWith(string relativePath, string find, string replace)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = new ContentLoader();
        loader.LoadAll(content.Root);
        return loader.Errors;
    }

    private const string Quest1 = "quests/ashen_hollow/iron_under_ash.yaml";

    /// <summary>A quest parsed against the game's content, from YAML objectives (the quest-level fields filled in).</summary>
    private static QuestDefinition Parse(string objectives, string extra = "", string entry = "o_a") =>
        QuestContent.Parse("quest.test.lint", $$"""
            id: quest.test.lint
            kind: quest
            schema: 1
            title: Lint
            summary: A quest the lint reads.
            entry_objective: {{entry}}
            {{extra}}
            objectives:
            {{objectives}}
            """, Game.Value);

    private static string Refused(string objectives, string extra = "", string entry = "o_a") =>
        Assert.Throws<FormatException>(() => Parse(objectives, extra, entry)).Message;

    [Fact]
    public void IronUnderAsh_IsTheBiblesQuestOne_WithNoKillObjective()
    {
        var quest = QuestContent.BuildQuests(Game.Value)[IronUnderAsh];

        Assert.Equal(("Iron Under Ash", "npc.ashen_hollow.kera_voss", "o_speak"), (quest.Title, quest.GiverId, quest.Entry));
        Assert.Equal(new[] { "talk_to", "visit_location", "acquire_item", "explore_location", "acquire_item", "acquire_item", "talk_to" },
            quest.Order.Select(o => o.Condition.Type));
        Assert.DoesNotContain(quest.Order, o => o.Condition is KillCreature);
        // The seam is finite: what the ore is made into counts as the ore obtained, and a spear as the billet (the audit's C-01).
        Assert.Equal(new[] { "item.material.iron_ingot", "item.weapon.march_spear" }, Assert.IsType<AcquireItem>(quest.Objectives["o_ore"].Condition).OrItems);
        Assert.Equal(new[] { "item.weapon.march_spear" }, Assert.IsType<AcquireItem>(quest.Objectives["o_billet"].Condition).OrItems);
        Assert.Empty(Assert.IsType<AcquireItem>(quest.Objectives["o_spear"].Condition).OrItems);
        // A straight line, and the last step - showing Kera the spear - ends it.
        Assert.Equal(new[] { "o_shelf", "o_ore", "o_return", "o_billet", "o_spear", "o_show", null },
            quest.Order.Select(o => o.Next.IsEmpty ? null : Assert.Single(o.Next)));
        var show = Assert.IsType<TalkTo>(quest.Objectives["o_show"].Condition);
        Assert.Equal(new[] { "praised", "judged" }, show.Nodes);
        Assert.Equal(new[] { "xp", "currency", "relationship" }, quest.Rewards.Select(r => r.Kind));
    }

    // ── the exit criterion: a quest naming what does not exist ───────────────

    [Fact]
    public void AnObjectiveTypeOutsideTheClosedSet_IsRefused() =>
        Assert.Contains(ErrorsWith(Quest1, "type: visit_location", "type: find_the_shelf"),
            e => e.Code == "QST001" && e.Message.Contains("'find_the_shelf' is not an objective type", StringComparison.Ordinal));

    [Fact]
    public void AnObjectiveTypeNotBuiltYet_IsRefused_SayingWhatItWaitsFor() =>
        Assert.Contains(ErrorsWith(Quest1, "type: visit_location\n    description: Reach Blackvein Cut, the old quarry south of the waystation.\n    params: { location_ref: location.blackvein_cut }",
                "type: defeat_boss\n    description: Reach Blackvein Cut, the old quarry south of the waystation.\n    params: { boss_ref: location.blackvein_cut }"),
            e => e.Code == "QST001" && e.Message.Contains("'defeat_boss' is not built in Phase 1 (it waits for bosses (M8))", StringComparison.Ordinal));

    [Fact]
    public void AnItemThatDoesNotExist_IsRefused()
    {
        var errors = ErrorsWith(Quest1, "item_ref: item.material.iron_ore", "item_ref: item.material.star_iron");
        Assert.Contains(errors, e => e.Code.StartsWith("XREF", StringComparison.Ordinal) && e.Message.Contains("item.material.star_iron", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "QST001" && e.Message.Contains("item.material.star_iron is not a defined item", StringComparison.Ordinal));
    }

    [Fact]
    public void AnNpcThatDoesNotExist_IsRefused()
    {
        var errors = ErrorsWith(Quest1, "giver_ref: npc.ashen_hollow.kera_voss", "giver_ref: npc.ashen_hollow.orren");
        Assert.Contains(errors, e => e.Code.StartsWith("XREF", StringComparison.Ordinal) && e.Message.Contains("npc.ashen_hollow.orren", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "QST001" && e.Message.Contains("npc.ashen_hollow.orren is not a defined npc", StringComparison.Ordinal));
    }

    [Fact]
    public void AFactionThatDoesNotExist_IsRefused()
    {
        var errors = ErrorsWith(Quest1, "giver_ref: npc.ashen_hollow.kera_voss", "giver_ref: npc.ashen_hollow.kera_voss\nfaction_ref: faction.hollow.wardens");
        Assert.Contains(errors, e => e.Code.StartsWith("XREF", StringComparison.Ordinal) && e.Message.Contains("faction.hollow.wardens", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "QST001" && e.Message.Contains("faction_ref is not built in Phase 1", StringComparison.Ordinal));
    }

    // ── across content ───────────────────────────────────────────────────────

    [Fact]
    public void AQuestNothingStarts_IsRefused() =>
        Assert.Contains(ErrorsWith("dialogue/ashen_hollow/kera_voss.yaml",
                "          - { command: start_quest, quest_ref: quest.ashen_hollow.iron_under_ash }\n", ""),
            e => e.Code == "QST001" && e.Message.Contains("nothing starts it", StringComparison.Ordinal));

    [Fact]
    public void ALineTheConversationDoesNotHave_IsRefused() =>
        Assert.Contains(ErrorsWith(Quest1, "nodes: [praised, judged]", "nodes: [praised, crowned]"),
            e => e.Code == "QST001" && e.Message.Contains("has no line 'crowned'", StringComparison.Ordinal));

    [Fact]
    public void AnItemNoRecipeMakes_CannotBeACraftObjective() =>
        Assert.Contains(ErrorsWith(Quest1, "type: acquire_item\n    description: Forge a March Spear at the anvil.\n    params: { item_ref: item.weapon.march_spear, count: 1 }",
                "type: craft_item\n    description: Forge a March Spear at the anvil.\n    params: { item_ref: item.material.wolf_hide, count: 1 }"),
            e => e.Code == "QST001" && e.Message.Contains("no recipe makes item.material.wolf_hide", StringComparison.Ordinal));

    // ── a finite item a quest waits for (the Phase-1 technical audit, C-01) ─────

    [Fact]
    public void AFiniteItemARecipeConsumes_MustAlsoCountWhatItIsMadeInto() =>
        Assert.Contains(ErrorsWith(Quest1, "params: { item_ref: item.material.iron_ore, or_item_refs: [item.material.iron_ingot, item.weapon.march_spear], count: 1 }",
                "params: { item_ref: item.material.iron_ore, count: 1 }"),
            e => e.Code == "QST001" && e.Message.Contains("nothing renews item.material.iron_ore, and recipe.smithing.iron_billet makes it into item.material.iron_ingot",
                StringComparison.Ordinal));

    [Fact]
    public void WhatTheItemIsMadeInto_MustBeCountedAllTheWayDown() =>
        Assert.Contains(ErrorsWith(Quest1, "or_item_refs: [item.material.iron_ingot, item.weapon.march_spear]", "or_item_refs: [item.material.iron_ingot]"),
            e => e.Code == "QST001" && e.Message.Contains("nothing renews item.material.iron_ingot, and recipe.smithing.march_spear makes it into item.weapon.march_spear",
                StringComparison.Ordinal));

    [Fact]
    public void ARenewableItem_NeedsNoAlternatives() =>
        Assert.DoesNotContain(ErrorsWith(Quest1, "params: { item_ref: item.weapon.march_spear, count: 1 }", "params: { item_ref: item.material.ash_haft, count: 1 }"),
            e => e.Message.Contains("nothing renews", StringComparison.Ordinal));

    [Fact]
    public void AFiniteItemThatUsingSpends_CannotBeWaitedFor() =>
        Assert.Contains(ErrorsWith(Quest1, "params: { item_ref: item.weapon.march_spear, count: 1 }", "params: { item_ref: item.tome.resonance_primer, count: 1 }"),
            e => e.Code == "QST001" && e.Message.Contains("nothing renews item.tome.resonance_primer, and using it spends it", StringComparison.Ordinal));

    [Fact]
    public void OrItemRefs_NamesEachItemOnce_AndNotTheItemItself() =>
        Assert.Contains(ErrorsWith(Quest1, "or_item_refs: [item.material.iron_ingot, item.weapon.march_spear]",
                "or_item_refs: [item.material.iron_ore, item.material.iron_ingot, item.weapon.march_spear]"),
            e => e.Code == "QST001" && e.Message.Contains("or_item_refs names each item once, and not the item_ref itself", StringComparison.Ordinal));

    [Fact]
    public void AConversationAskingAfterAnObjectiveTheQuestLacks_IsRefused() =>
        Assert.Contains(ErrorsWith("dialogue/ashen_hollow/kera_voss.yaml", "objective: o_ore", "objective: o_gold"),
            e => e.Code == "QST001" && e.Message.Contains("has no objective 'o_gold'", StringComparison.Ordinal));

    // ── the graph ────────────────────────────────────────────────────────────

    [Fact]
    public void AGoodGraph_Parses_WithBranchesJoinsTimersAndHiddenObjectives()
    {
        var quest = Parse("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_b, o_c], branch: first }
              - { id: o_b, type: acquire_item, description: B., params: { item_ref: item.material.iron_ore, count: 2 }, next: [o_d] }
              - { id: o_c, type: kill_creature, description: C., visibility: hidden, params: { creature_ref: creature.beast.wolf_grey, count: 3 }, next: [o_d] }
              - { id: o_d, type: wait_until, description: D., params: { after_min: 1 }, time_limit_min: 2, on_fail: fail_quest }
            """);

        Assert.True(quest.Objectives["o_a"].FirstBranch);
        Assert.True(quest.Objectives["o_c"].Hidden);
        Assert.Equal(80, quest.Objectives["o_d"].TimeLimitTicks);   // 2 game minutes of 40 ticks
        Assert.Equal(40, Assert.IsType<WaitUntil>(quest.Objectives["o_d"].Condition).AfterTicks);
    }

    [Fact]
    public void AnOrphan_ALoop_AndAMissingObjective_AreRefused()
    {
        Assert.Contains("nothing leads to objective o_b (an orphan)", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut } }
              - { id: o_b, type: visit_location, description: B., params: { location_ref: location.outpost } }
            """));
        Assert.Contains("objectives loop through o_a", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_b] }
              - { id: o_b, type: visit_location, description: B., params: { location_ref: location.outpost }, next: [o_a] }
            """));
        Assert.Contains("o_a's next names objective 'o_z'", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_z] }
            """));
    }

    [Fact]
    public void AJoinThatCanNeverHappen_IsRefused()
    {
        Assert.Contains("o_d waits on two alternatives of o_a's branch", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_b, o_c], branch: first }
              - { id: o_b, type: visit_location, description: B., params: { location_ref: location.outpost }, next: [o_d] }
              - { id: o_c, type: visit_location, description: C., params: { location_ref: location.den_mouth }, next: [o_d] }
              - { id: o_d, type: visit_location, description: D., params: { location_ref: location.herb_patch }, all_of: [o_b, o_c] }
            """));
        Assert.Contains("o_c waits on o_b, which does not lead to it", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_b, o_c] }
              - { id: o_b, type: visit_location, description: B., params: { location_ref: location.outpost } }
              - { id: o_c, type: visit_location, description: C., params: { location_ref: location.den_mouth }, all_of: [o_b] }
            """));
    }

    [Fact]
    public void WhatAnObjectiveSays_IsChecked()
    {
        Assert.Contains("a time_limit_min says where failure goes", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, time_limit_min: 5 }"""));
        Assert.Contains("'radius_m' is not a parameter of visit_location", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut, radius_m: 4 } }"""));
        Assert.Contains("branches (branch: first) between at least two objectives", Refused("""
              - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut }, next: [o_b], branch: first }
              - { id: o_b, type: visit_location, description: B., params: { location_ref: location.outpost } }
            """));
        Assert.Contains("hidden is not built in Phase 1 (visibility: hidden)", Refused(
            """  - { id: o_a, type: visit_location, description: A., hidden: true, params: { location_ref: location.blackvein_cut } }"""));
        Assert.Contains("fail_if reads the world as it stands; kill_creature counts deeds", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut } }""",
            "fail_if: [{ type: kill_creature, params: { creature_ref: creature.beast.wolf_grey } }]"));
        Assert.Contains("reward kind 'reputation' is not built in Phase 1", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut } }""",
            "rewards: [{ kind: reputation, amount: 5 }]"));
    }
}
