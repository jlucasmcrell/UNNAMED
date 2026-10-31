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
        public EditedContent(string relativePath, string find, string replace, params (string Path, string Find, string Replace)[] more)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var (relative, from, to) in more.Prepend((relativePath, find, replace)))
            {
                string path = Path.Combine(Root, relative);
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                // A raw string literal in this file has the checkout's line endings: CRLF on Windows.
                string lf = from.Replace("\r\n", "\n");
                Assert.Contains(lf, text);
                File.WriteAllText(path, text.Replace(lf, to.Replace("\r\n", "\n")));
            }
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-qst-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static IReadOnlyList<ValidationError> ErrorsWith(string relativePath, string find, string replace,
        params (string Path, string Find, string Replace)[] more)
    {
        using var content = new EditedContent(relativePath, find, replace, more);
        var loader = new ContentLoader();
        loader.LoadAll(content.Root);
        return loader.Errors;
    }

    private const string Quest1 = "quests/ashen_hollow/iron_under_ash.yaml";

    /// <summary>
    /// The ore spoil made a one-off, so that nothing renews iron ore: in the game it renews daily (node.ore.loose_iron). (Billets also come
    /// back with the animated armours that drop them, so the test for billets keeps those dead as well.)
    /// </summary>
    private static readonly (string, string, string) FiniteSpoil = ("nodes/ore/loose_iron.yaml", "respawn: daily", "respawn: none");

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
        Assert.Equal(new[] { "item.armor.lame_coif", "item.armor.lame_sabatons", "item.armor.lamework_jack", "item.material.iron_ingot", "item.weapon.march_spear", "item.weapon.rusted_sword" }, Assert.IsType<AcquireItem>(quest.Objectives["o_ore"].Condition).OrItems);
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
                "type: solve_puzzle\n    description: Reach Blackvein Cut, the old quarry south of the waystation.\n    params: { puzzle_ref: location.blackvein_cut }"),
            e => e.Code == "QST001" && e.Message.Contains("'solve_puzzle' is not built in Phase 1 (it waits for puzzles (M8))", StringComparison.Ordinal));

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
        Assert.Contains(ErrorsWith(Quest1, "params: { item_ref: item.material.iron_ore, or_item_refs: [item.armor.lame_coif, item.armor.lame_sabatons, item.armor.lamework_jack, item.material.iron_ingot, item.weapon.march_spear, item.weapon.rusted_sword], count: 1 }",
                "params: { item_ref: item.material.iron_ore, count: 1 }", FiniteSpoil),
            e => e.Code == "QST001" && e.Message.Contains("nothing renews item.material.iron_ore, and recipe.smithing.iron_billet makes it into item.material.iron_ingot",
                StringComparison.Ordinal));

    [Fact]
    public void WhatTheItemIsMadeInto_MustBeCountedAllTheWayDown() =>
        // Count the new armour routes so this deliberately missing route is still the March Spear.
        Assert.Contains(ErrorsWith(Quest1, "or_item_refs: [item.armor.lame_coif, item.armor.lame_sabatons, item.armor.lamework_jack, item.material.iron_ingot, item.weapon.march_spear, item.weapon.rusted_sword]",
                "or_item_refs: [item.armor.lame_coif, item.armor.lame_sabatons, item.armor.lamework_jack, item.material.iron_ingot, item.weapon.rusted_sword]", FiniteSpoil,
                ("spawns/hollow/iron_shelf_armour.yaml", "kind: timer", "kind: none"), ("spawns/moor/barrow_sentinel.yaml", "kind: timer", "kind: none"),
                ("spawns/field/breach_west.yaml", "kind: timer", "kind: none"), ("spawns/field/breach_northeast.yaml", "kind: timer", "kind: none"),
                ("spawns/field/breach_south.yaml", "kind: timer", "kind: none")),
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
        Assert.Contains(ErrorsWith(Quest1, "or_item_refs: [item.armor.lame_coif, item.armor.lame_sabatons, item.armor.lamework_jack, item.material.iron_ingot, item.weapon.march_spear, item.weapon.rusted_sword]",
                "or_item_refs: [item.material.iron_ore, item.armor.lame_coif, item.armor.lame_sabatons, item.armor.lamework_jack, item.material.iron_ingot, item.weapon.march_spear, item.weapon.rusted_sword]"),
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
        Assert.Contains("reward kind 'ability' is not built in Phase 1", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut } }""",
            "rewards: [{ kind: ability, amount: 5 }]"));
    }
    [Fact]
    public void AFactionReputationObjective_AndAReputationReward_Build()
    {
        var quest = Parse(
            """  - { id: o_a, type: faction_reputation, description: A., params: { faction_ref: faction.ashen_hollow.wayhold, min_tier: accepted } }""",
            "rewards: [{ kind: reputation, faction_ref: faction.ashen_hollow.wayhold, amount: 50 }]");
        Assert.Equal(new FactionReputation("faction.ashen_hollow.wayhold", 1, 5), quest.Objectives["o_a"].Condition);
        Assert.Equal(new ReputationReward("faction.ashen_hollow.wayhold", 50), Assert.Single(quest.Rewards));

        Assert.Contains("names min_tier, max_tier or both", Refused(
            """  - { id: o_a, type: faction_reputation, description: A., params: { faction_ref: faction.ashen_hollow.wayhold } }"""));
        Assert.Contains("'hostile' is not a standing tier", Refused(
            """  - { id: o_a, type: faction_reputation, description: A., params: { faction_ref: faction.ashen_hollow.wayhold, max_tier: hostile } }"""));
        Assert.Contains("min_tier is above max_tier", Refused(
            """  - { id: o_a, type: faction_reputation, description: A., params: { faction_ref: faction.ashen_hollow.wayhold, min_tier: trusted, max_tier: wary } }"""));
        Assert.Contains("moves standing by 1 to 250 points", Refused(
            """  - { id: o_a, type: visit_location, description: A., params: { location_ref: location.blackvein_cut } }""",
            "rewards: [{ kind: reputation, faction_ref: faction.ashen_hollow.wayhold, amount: 400 }]"));
        Assert.Contains("'faction_state' is not built in Phase 1", Refused(
            """  - { id: o_a, type: faction_state, description: A., params: { faction_ref: faction.ashen_hollow.wayhold } }"""));
    }

    // ── the story framework (WP4) ───────────────────────────────────────────

    [Fact]
    public void EveryQuest_SaysItsTrack_TheBountiesBeingBounties()
    {
        var quests = QuestContent.BuildQuests(Game.Value);
        Assert.All(quests.Values.Where(q => q.Id.StartsWith("quest.bounty.", StringComparison.Ordinal)), q => Assert.Equal(QuestTrack.Bounty, q.Track));
        Assert.All(quests.Values.Where(q => q.Id.StartsWith("quest.main.", StringComparison.Ordinal)), q => Assert.Equal((QuestTrack.Main, (int?)1), (q.Track, q.Act)));
        Assert.All(quests.Values.Where(q => !q.Id.StartsWith("quest.bounty.", StringComparison.Ordinal) && !q.Id.StartsWith("quest.main.", StringComparison.Ordinal)),
            q => Assert.Equal(QuestTrack.Side, q.Track));
        // The opening (WP2) is the one quest every new character starts, beside their people's heritage questline (owner, 2026-10-24). The hunting bounties are competitive (WP13): an adventurer may
        // finish one first; the gathering and smithing ones are the character's alone.
        // The Quiet Crossing (D23 S6) joins them: it is in the journal from the first minute, not offered at a board.
        Assert.Equal(new[] { "quest.ashen_hollow.quiet_crossing", "quest.heritage.unfinished_joint", "quest.heritage.what_was_kept", "quest.main.the_road_in" }, quests.Values.Where(q => q.OnNewGame).Select(q => q.Id));
        Assert.Equal(new[] { "quest.bounty.crag_walker", "quest.bounty.lodge_supplies", "quest.bounty.rim_road", "quest.bounty.road_wolves", "quest.bounty.winter_hides" },
            quests.Values.Where(q => q.Competitive).Select(q => q.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void TheStoryFields_Parse()
    {
        var quest = Parse("""
              - id: o_a
                type: defeat_creature
                description: Put the wolf down.
                params: { creature_ref: creature.beast.wolf_grey, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost }
                next: [o_b]
                effects:
                  - { kind: choice, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost, value: 2, line: You kept it. }
              - id: o_b
                type: companion_present
                description: With Tavar.
                params: { npc_ref: npc.ashen_hollow.tavar_orr, order: follow }
                next: [o_c]
              - id: o_c
                type: dialogue_choice
                description: Take the wolves.
                params: { npc_ref: npc.ashen_hollow.renn_vale, node: notices, choice: take_wolves }
                next: [o_d]
              - id: o_d
                type: waystone_known
                description: Touch the stone.
                params: { waystone_ref: waystone.ashen }
                next: [o_e]
              - id: o_e
                type: travel_to
                description: Go by the stones.
                params: { waystone_ref: waystone.rim }
            """, "track: main\nact: 1\non_new_game: true");

        Assert.Equal((QuestTrack.Main, (int?)1, true, false), (quest.Track, quest.Act, quest.OnNewGame, quest.Competitive));
        Assert.Equal(new DefeatCreature("creature.beast.wolf_grey", "world.hollow.halda_tally_kept", "location.outpost", false), quest.Objectives["o_a"].Condition);
        Assert.Equal(new ChoiceEffect("world.hollow.halda_tally_kept", "location.outpost", 2, "You kept it."), Assert.Single(quest.Objectives["o_a"].Effects));
        Assert.Equal(new CompanionPresent("npc.ashen_hollow.tavar_orr", UNNAMED.Domain.Companions.CompanionOrder.Follow), quest.Objectives["o_b"].Condition);
        Assert.Equal(new ChoiceGiven("npc.ashen_hollow.renn_vale", "dialogue.ashen_hollow.renn_vale", "notices", "take_wolves"), quest.Objectives["o_c"].Condition);
        Assert.Equal(new WaystoneKnown("waystone.ashen"), quest.Objectives["o_d"].Condition);
        Assert.Equal(new TravelTo("waystone.rim"), quest.Objectives["o_e"].Condition);
    }

    private const string OneStep = """
          - id: o_a
            type: wait_until
            description: Wait.
            params: { after_min: 1 }
        """;

    [Fact]
    public void WhatTheStoryFieldsSay_IsChecked()
    {
        Assert.Contains("track is main, side or bounty", Refused(OneStep, "track: story"));
        Assert.Contains("a main-track quest names its act", Refused(OneStep, "track: main"));
        Assert.Contains("a main-track quest names its act", Refused(OneStep, "act: 1"));
        Assert.Contains("a bounty is standing work", Refused(OneStep, "track: bounty"));
        Assert.Contains("only a bounty (track: bounty) is competitive", Refused(OneStep, "competitive: true"));
        Assert.Contains("taken once", Refused(OneStep, "on_new_game: true\nrepeat_policy: { repeatable: true, cooldown_min: 10 }"));
        Assert.Contains("on_new_game is true or false", Refused(OneStep, "on_new_game: yes"));
        Assert.True(Parse(OneStep, "track: bounty\ncompetitive: true\nrepeat_policy: { repeatable: true, cooldown_min: 10 }").Competitive);
    }

    [Fact]
    public void WhatTheNewObjectivesAndEffectsSay_IsChecked()
    {
        string With(string type, string p, string extra = "") => $$"""
              - id: o_a
                type: {{type}}
                description: A step.
                params: {{p}}
            {{extra}}
            """;
        Assert.Contains("is not a Waystone key", Refused(With("travel_to", "{ waystone_ref: location.outpost }")));
        Assert.Contains("order is follow or wait", Refused(With("companion_present", "{ npc_ref: npc.ashen_hollow.tavar_orr, order: lead }")));
        Assert.Contains("'count' is not a parameter of defeat_boss",
            Refused(With("defeat_boss", "{ creature_ref: creature.beast.wolf_grey, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost, count: 1 }")));
        Assert.Contains("world.test.nothing is not a defined world_flag",
            Refused(With("defeat_creature", "{ creature_ref: creature.beast.wolf_grey, flag_ref: world.test.nothing, location_ref: location.outpost }")));
        string choice(string effect) => With("wait_until", "{ after_min: 1 }", $"    effects:\n      - {effect}");
        Assert.Contains("is not an objective effect", Refused(choice("{ kind: set_flag, flag_ref: world.hollow.halda_tally_kept }")));
        Assert.Contains("a choice has a journal line",
            Refused(choice("{ kind: choice, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost, line: ' ' }")));
        Assert.Contains("'note' is not part of a choice",
            Refused(choice("{ kind: choice, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost, line: x, note: y }")));
    }

    private const string Halda = "quests/ashen_hollow/haldas_tally.yaml";

    private const string HaldaFirst = """
          - id: o_tally
            type: talk_to
            description: Ask Renn whose tally this is.
            params: { npc_ref: npc.ashen_hollow.renn_vale, nodes: [tally] }
        """;

    private static IReadOnlyList<ValidationError> HaldaWith(string objective, string fields = "") =>
        ErrorsWith(Halda, HaldaFirst, objective, ("quests/ashen_hollow/haldas_tally.yaml", "track: side", "track: side" + fields));

    private static string Step(string type, string p) => $$"""
          - id: o_tally
            type: {{type}}
            description: A step.
            params: {{p}}
        """;

    private const string HaldaKeep = "    type: world_state\n    description: Or keep it, and tell Renn you will walk Halda's line.\n" +
                                     "    params: { flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost, value: 1 }";

    [Fact]
    public void ABoss_DoesNotReturn_AndOneDefeatFlagRecordsOneCreatureInOnePlace()
    {
        Assert.Contains(HaldaWith(Step("defeat_boss",
                "{ creature_ref: creature.beast.wolf_grey, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost }")),
            e => e.Code == "QST001" && e.Message.Contains("a boss does not return, and creature.beast.wolf_grey returns", StringComparison.Ordinal));
        Assert.Contains(ErrorsWith(Halda, HaldaFirst,
                Step("defeat_creature", "{ creature_ref: creature.beast.wolf_grey, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost }"),
                (Halda, HaldaKeep, "    type: defeat_creature\n    description: Again.\n" +
                                   "    params: { creature_ref: creature.beast.wolf_grey, flag_ref: world.hollow.halda_tally_kept, location_ref: location.den_mouth }")),
            e => e.Code == "QST001" && e.Message.Contains("already records creature.beast.wolf_grey at location.outpost", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewObjective_NamingWhatContentLacks_IsRefused()
    {
        Assert.Contains(HaldaWith(Step("companion_present", "{ npc_ref: npc.ashen_hollow.renn_vale }")),
            e => e.Code == "QST001" && e.Message.Contains("cannot join the character", StringComparison.Ordinal));
        Assert.Contains(HaldaWith(Step("dialogue_choice", "{ npc_ref: npc.ashen_hollow.renn_vale, node: notices, choice: take_bears }")),
            e => e.Code == "QST001" && e.Message.Contains("offers no reply 'take_bears'", StringComparison.Ordinal));
        Assert.Contains(HaldaWith(Step("dialogue_choice", "{ npc_ref: npc.ashen_hollow.renn_vale, node: elsewhere, choice: back }")),
            e => e.Code == "QST001" && e.Message.Contains("has no line 'elsewhere'", StringComparison.Ordinal));
        Assert.Contains(ErrorsWith(Halda, HaldaFirst,
                Step("defeat_creature", "{ creature_ref: creature.grazer.barrowback, flag_ref: world.hollow.halda_tally_kept, location_ref: location.outpost }"),
                // Both barrowback spawns become fellhorns, each in the fellhorn's own band, so the spawns stay valid and the quest's
                // objective is the only thing wrong.
                ("spawns/wildlife/foldscar_barrowbacks.yaml", "creature_ref: creature.grazer.barrowback", "creature_ref: creature.grazer.fellhorn"),
                ("spawns/wildlife/foldscar_barrowbacks.yaml", "level_band: [5, 5]", "level_band: [3, 3]"),
                ("spawns/old_late/brute.yaml", "creature_ref: creature.grazer.barrowback", "creature_ref: creature.grazer.fellhorn"),
                ("spawns/old_late/brute.yaml", "level_band:\n- 5\n- 5", "level_band:\n- 3\n- 3")),
            e => e.Code == "QST001" && e.Message.Contains("no spawn places creature.grazer.barrowback", StringComparison.Ordinal));
        Assert.Contains(HaldaWith(Step("waystone_known", "{ waystone_ref: waystone.far_north }")),
            e => e.Code == "QST001" && e.Message.Contains("no region places the Waystone waystone.far_north", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOpeningQuest_NeedsNothingElseToStartIt() =>
        Assert.Empty(ErrorsWith("quests/ashen_hollow/end_of_playtest.yaml", "track: side", "track: main\nact: 1\non_new_game: true",
                ("dialogue/ashen_hollow/renn_vale.yaml", "consequences: [{ command: start_quest, quest_ref: quest.ashen_hollow.end_of_playtest }]", "consequences: []")));

    // ── WP15b: a companion's personal quest ─────────────────────────────────

    private const string Talk = """
          - id: o_a
            type: talk_to
            description: Hear him out.
            params: { npc_ref: npc.ashen_hollow.tavar_orr }
        """;

    /// <summary>WP15b: a personal quest names its companion, the trust at which they confide it, and what it does to their regard.</summary>
    [Fact]
    public void APersonalQuest_NamesItsCompanion_ItsConfidesGate_AndAnApprovalChange()
    {
        var quest = Parse(Talk, """
            companion_ref: npc.ashen_hollow.tavar_orr
            confides_at: 50
            rewards: [{ kind: approval_change, changes: { trust: 5, loyalty: 10 } }]
            """);

        Assert.Equal(("npc.ashen_hollow.tavar_orr", 50), (quest.CompanionId, quest.ConfidesAt));
        var reward = Assert.IsType<ApprovalChangeReward>(Assert.Single(quest.Rewards));
        Assert.Equal(new[] { ("loyalty", 10), ("trust", 5) }, reward.Changes.Select(c => (c.Key, c.Value)));
        Assert.False(QuestRules.Confides(quest, _ => 49));
        Assert.True(QuestRules.Confides(quest, _ => 50));
    }

    [Fact]
    public void AConfidesGate_WithoutACompanion_IsRefused() =>
        Assert.Contains("confides_at gates a personal quest", Refused(Talk, "confides_at: 20"));

    [Fact]
    public void AnApprovalChange_WithoutACompanion_IsRefused() =>
        Assert.Contains("an approval_change reward moves the personal quest's companion's regard", Refused(Talk, "rewards: [{ kind: approval_change, changes: { trust: 5 } }]"));

    [Fact]
    public void APersonalQuest_OfSomeoneWhoCannotJoin_IsRefused() =>
        Assert.Contains("cannot join the character as a companion", Refused(Talk, "companion_ref: npc.ashen_hollow.sel_arien"));

    [Fact]
    public void AnApprovalChange_OnADimensionThereIsNot_IsRefused() =>
        Assert.Contains("'attraction' is not a relationship dimension",
            Refused(Talk, "companion_ref: npc.ashen_hollow.tavar_orr\nrewards: [{ kind: approval_change, changes: { attraction: 5 } }]"));
}
