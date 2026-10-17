using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The factions from content and FAC001 (M7 design §5.13-§5.14, re-ported by WP6): the ladder, the Wayhold and Long Survey chapters,
/// members at their seat and never a companion, who may be asked about or told what, a quest's reputation paid only by its giver's
/// faction, reactions only to single-instance acts of built kinds, and no field the data model names that is not built.
/// </summary>
public class FactionContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private static string GameContent => Path.Combine(RepoPaths.Root(), "content");

    /// <summary>A copy of a content directory with some files edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string source, params (string File, string Find, string Replace)[] edits)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var (relative, find, replace) in edits)
            {
                string path = Path.Combine(Root, relative);
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                Assert.Contains(find, text);
                File.WriteAllText(path, text.Replace(find, replace));
            }
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-fac-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>Every error content with these edits gets; the game itself gets none.</summary>
    private static List<ValidationError> Errors(params (string File, string Find, string Replace)[] edits)
    {
        using var content = new EditedContent(GameContent, edits);
        return Load(content.Root).Errors.ToList();
    }

    private static void Refused(string expected, params (string File, string Find, string Replace)[] edits)
    {
        var refusals = Errors(edits).Where(e => e.Code == FactionContent.Code).Select(e => e.Message).ToList();
        Assert.True(refusals.Any(r => r.Contains(expected, StringComparison.Ordinal)),
            $"expected a FAC001 refusal containing \"{expected}\"; got:\n" + string.Join("\n", refusals));
    }

    private const string Sel = "dialogue/ashen_hollow/sel_arien.yaml";
    private const string LongSurvey = "factions/ashen_hollow/long_survey.yaml";
    private const string Wayhold = "faction.ashen_hollow.wayhold";
    private const string Survey = "faction.ashen_hollow.long_survey";
    private const string SteadiedReaction = "act: switch_set, flag_ref: world.foldscar.steadied, delta: 100";
    private const string TavarBack = "conditions: [{ kind: visited, dialogue_ref: dialogue.ashen_hollow.tavar_orr, node: greet }]";
    private const string SteadiedReport = "{ command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }";

    [Fact]
    public void TheGame_PassesFac001_AndBuildsTheTwoChapters()
    {
        var loader = Load(GameContent);
        Assert.DoesNotContain(loader.Errors, e => e.Code == FactionContent.Code);
        var setup = FactionContent.Build(loader);
        Assert.Equal(new[] { Survey, Wayhold }, setup.Factions.Keys);
        Assert.Equal(("the Long Survey", "the Wayhold"), (setup.Factions[Survey].Name, setup.Factions[Wayhold].Name));
        Assert.Equal(256, setup.LogCapacity);
        Assert.True(StandingLadder.Default.Tiers.SequenceEqual(setup.Ladder.Tiers), "config.factions builds the shipped ladder");
        Assert.Equal((-1000, 1000, -999), (setup.Ladder.MinPoints, setup.Ladder.MaxPoints, setup.Ladder.OrdinaryFloor));
        Assert.Equal(new[] { Survey }, setup.ReactorsTo(ActKinds.SwitchSet, "world.foldscar.steadied"));
        Assert.False(setup.IsRelevant(ActKinds.CreatureKilled, "creature.construct.animated_armour"));   // it returns now (MMO rules)
        Assert.False(setup.IsRelevant(ActKinds.CreatureKilled, "creature.beast.wolf_grey"));
        var npcs = SocialContent.BuildNpcs(loader);
        Assert.Equal((Wayhold, Wayhold, Survey, (string?)null),
            (npcs["npc.ashen_hollow.renn_vale"].FactionId, npcs["npc.ashen_hollow.kera_voss"].FactionId, npcs["npc.ashen_hollow.sel_arien"].FactionId,
                npcs["npc.ashen_hollow.tavar_orr"].FactionId));
    }

    [Fact]
    public void TheLadder_HasNoHostilityTier()
    {
        Assert.Equal(new[] { "exalted", "allied", "honoured", "trusted", "accepted", "neutral", "wary", "disliked", "despised", "outcast", "anathema" },
            StandingLadder.Keys.Select(k => k.Key));
        Assert.Equal(Enumerable.Range(-5, 11).Reverse(), StandingLadder.Keys.Select(k => k.Level));
        Refused("ladder[6] is wary at level -1", ("config/factions.yaml", "{ tier: wary,     level: -1, min: -249 }", "{ tier: hostile,  level: -1, min: -249 }"));
    }

    [Fact]
    public void TheConfig_HoldsTheFloorAndALogOfSixteen()
    {
        var refusals = Errors(("config/factions.yaml", "ordinary_floor: -999", "ordinary_floor: -1000")).Select(e => e.Message).ToList();
        Assert.Contains(refusals, r => r.Contains("points.ordinary_floor", StringComparison.Ordinal));
        Refused("acts.log_capacity is at least 16", ("config/factions.yaml", "log_capacity: 256", "log_capacity: 8"));
    }

    [Fact]
    public void ACompanionCannotBeAMember() =>
        Refused("a companion belongs to no faction",
            ("npcs/ashen_hollow/tavar_orr.yaml", "unique: true\n", "unique: true\nfaction_ref: faction.ashen_hollow.long_survey\n"));

    [Fact]
    public void AMemberStandsInsideTheSeat() =>
        Refused("outside its 5 m: a member stands inside the seat", ("locations/outpost.yaml", "radius_m: 30", "radius_m: 5"));

    [Fact]
    public void AStandingCondition_NamesTheSpeakersOwnFaction() =>
        Refused("a standing condition names only the faction every participant belongs to, not faction.ashen_hollow.wayhold",
            (Sel, TavarBack, TavarBack.Replace("}]", "}, { kind: standing, faction_ref: faction.ashen_hollow.wayhold, min_tier: accepted }]")));

    [Fact]
    public void ActDone_OnlyBesideAReportOfTheSameAct() =>
        Refused("stands only beside a report_act of the same act",
            (Sel, TavarBack, TavarBack.Replace("}]", "}, { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }]")));

    [Fact]
    public void AReport_GoesOnlyToTheSpeakersReactingFaction() =>
        Refused("faction.ashen_hollow.long_survey has no reaction to creature_killed creature.construct.animated_armour",
            (Sel, SteadiedReport, "{ command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }"));

    [Fact]
    public void APlacedPieceReaction_IsRefused() =>
        Refused("act 'piece_placed' is not built: building (WP8)", (LongSurvey, SteadiedReaction, "act: piece_placed, piece_ref: piece.none, delta: 100"));

    [Fact]
    public void ARespawningCreatureReaction_IsRefused() =>
        Refused("brings it back", (LongSurvey, SteadiedReaction, "act: creature_killed, creature_ref: creature.construct.animated_armour, delta: 100"));

    [Fact]
    public void ACreaturePlacedTwiceReaction_IsRefused() =>
        Refused("needs it placed once in the world; it is placed 2 times",
            (LongSurvey, SteadiedReaction, "act: creature_killed, creature_ref: creature.construct.animated_armour, delta: 100"),
            ("spawns/hollow/iron_shelf_armour.yaml", "  kind: timer\n  window_ticks: 72000", "  kind: none"),
            ("spawns/moor/barrow_sentinel.yaml", "  kind: timer\n  window_ticks: 72000", "  kind: none"));

    [Fact]
    public void AReactionDeltaOfIntMinValue_IsRefused() =>
        Refused("delta is non-zero and at most 250 either way, not -2147483648",
            (LongSurvey, SteadiedReaction, "act: switch_set, flag_ref: world.foldscar.steadied, delta: -2147483648"));

    [Fact]
    public void AReactionToAFlagContentCanReset_IsRefused() =>
        Refused("needs a flag only its switch sets; dialogue.ashen_hollow.sel_arien writes it too",
            (Sel, "          - " + SteadiedReport, "          - { command: set_world_flag, flag_ref: world.foldscar.steadied, value: 1 }\n          - " + SteadiedReport));

    [Fact]
    public void AReactionToAFlagNoSwitchSets_IsRefused() =>
        Refused("needs a switch that sets it", (LongSurvey, SteadiedReaction, "act: switch_set, flag_ref: world.hollow.forge_shed_door_open, delta: 100"));

    [Fact]
    public void Standing_MovesOnlyWhenAMemberIsTold_OrPays()
    {
        // Structurally: no act kind turns coin or goods into standing, and the only reward is the giver's faction's own.
        Assert.Contains("reputation", RewardKinds.Built);
        Assert.Contains("faction_reputation", ObjectiveTypes.Built);
        Assert.Contains("faction_state", ObjectiveTypes.NotBuilt.Keys);
        Assert.DoesNotContain(ActKinds.Built, k => k.Contains("trade", StringComparison.Ordinal) || k.Contains("buy", StringComparison.Ordinal)
            || k.Contains("sell", StringComparison.Ordinal) || k.Contains("quest", StringComparison.Ordinal));

        // A reply that tries to grant standing is refused, with its reason; so is a quest paid by a faction its giver is not of.
        const string rewards = "rewards:\n";
        var errors = Errors(
            (Sel, SteadiedReport, SteadiedReport + "\n          - { command: add_reputation, faction_ref: faction.ashen_hollow.long_survey, delta: 10 }"),
            ("quests/ashen_hollow/iron_under_ash.yaml", rewards, rewards + "  - { kind: reputation, faction_ref: faction.ashen_hollow.long_survey, amount: 25 }\n"));
        Assert.Contains(errors, e => e.Message.Contains("add_reputation is refused: standing moves only when a member is told of an act (WP6)", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == FactionContent.Code && e.Message.Contains(
            "quest.ashen_hollow.iron_under_ash: a reputation reward is paid by the faction of the quest's giver (faction.ashen_hollow.wayhold), not faction.ashen_hollow.long_survey",
            StringComparison.Ordinal));
    }

    [Fact]
    public void AQuestPaidByItsGiversFaction_Passes()
    {
        const string rewards = "rewards:\n";
        using var content = new EditedContent(GameContent,
            ("quests/ashen_hollow/iron_under_ash.yaml", rewards, rewards + "  - { kind: reputation, faction_ref: faction.ashen_hollow.wayhold, amount: 25 }\n"));
        var loader = Load(content.Root);
        Assert.Empty(loader.Errors);
        Assert.Contains(new ReputationReward(Wayhold, 25), QuestContent.BuildQuests(loader)["quest.ashen_hollow.iron_under_ash"].Rewards);
    }

    [Theory]
    [InlineData("members: [npc.ashen_hollow.sel_arien]", "members is refused: membership is the NPC's faction_ref")]
    [InlineData("laws: []", "laws is refused: crime is not built")]
    [InlineData("enemy_of: [faction.ashen_hollow.wayhold]", "enemy_of is refused")]
    [InlineData("joinable: true", "joinable is refused")]
    public void FactionContent_RefusesDataModelFieldsItDoesNotBuild(string field, string expected) =>
        Refused(expected, (LongSurvey, "seat_location_ref: location.outpost\n", "seat_location_ref: location.outpost\n" + field + "\n"));

    [Fact]
    public void TheWitnessBlock_IsRefused_StandingIsReportOnly() =>
        Refused("witness is not built: standing is report-only",
            ("config/factions.yaml", "acts:\n", "witness: { sight_m: 30, fov_deg: 140, identify_m: 15 }\nacts:\n"));
}
