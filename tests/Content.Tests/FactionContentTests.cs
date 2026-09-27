using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The factions from content and FAC001 (M7 design §5.13-§5.14): the ladder, members at their seat and never a companion, who may be
/// asked about or told what (R1, R3, R4), a gate on the trader's own faction (R5), reactions only to single-instance acts of built
/// kinds, and no field the data model names that M7 does not build.
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

    /// <summary>The FAC001 messages content with these edits gets; the game itself gets none.</summary>
    private static List<string> Refusals(params (string File, string Find, string Replace)[] edits)
    {
        using var content = new EditedContent(GameContent, edits);
        return Load(content.Root).Errors.Where(e => e.Code == FactionContent.Code).Select(e => e.Message).ToList();
    }

    private static void Refused(string expected, params (string File, string Find, string Replace)[] edits)
    {
        var refusals = Refusals(edits);
        Assert.True(refusals.Any(r => r.Contains(expected, StringComparison.Ordinal)),
            $"expected a FAC001 refusal containing \"{expected}\"; got:\n" + string.Join("\n", refusals));
    }

    private const string Kera = "dialogue/ashen_hollow/kera_voss.yaml";
    private const string Sel = "dialogue/ashen_hollow/sel_arien.yaml";
    private const string Waystation = "factions/ashen_hollow/waystation.yaml";
    private const string ArmourReaction = "act: creature_killed, creature_ref: creature.construct.animated_armour, delta: 100";

    [Fact]
    public void TheGame_PassesFac001_AndBuildsItsTwoFactions()
    {
        var loader = Load(GameContent);
        Assert.DoesNotContain(loader.Errors, e => e.Code == FactionContent.Code);
        var setup = FactionContent.Build(loader);
        Assert.Equal(new[] { "faction.ashen_hollow.survey", "faction.ashen_hollow.waystation" }, setup.Factions.Keys);
        Assert.Equal(256, setup.LogCapacity);
        Assert.True(StandingLadder.Default.Tiers.SequenceEqual(setup.Ladder.Tiers), "config.factions builds the shipped ladder");
        Assert.Equal((-1000, 1000, -999), (setup.Ladder.MinPoints, setup.Ladder.MaxPoints, setup.Ladder.OrdinaryFloor));
        Assert.Equal(new[] { "faction.ashen_hollow.survey", "faction.ashen_hollow.waystation" },
            setup.ReactorsTo(ActKinds.CreatureKilled, "creature.construct.animated_armour"));
        Assert.True(setup.IsRelevant(ActKinds.SwitchSet, "world.foldscar.steadied"));
        Assert.False(setup.IsRelevant(ActKinds.CreatureKilled, "creature.beast.wolf_grey"));
        var npcs = SocialContent.BuildNpcs(loader);
        Assert.Equal(("faction.ashen_hollow.waystation", "faction.ashen_hollow.waystation", "faction.ashen_hollow.survey", (string?)null),
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
        Refused("points.ordinary_floor", ("config/factions.yaml", "ordinary_floor: -999", "ordinary_floor: -1000"));
        Refused("acts.log_capacity is at least 16", ("config/factions.yaml", "log_capacity: 256", "log_capacity: 8"));
    }

    [Fact]
    public void ACompanionCannotBeAMember() =>
        Refused("a companion belongs to no faction",
            ("npcs/ashen_hollow/tavar_orr.yaml", "unique: true\n", "unique: true\nfaction_ref: faction.ashen_hollow.survey\n"));

    [Fact]
    public void AMemberStandsInsideTheSeat() =>
        Refused("npc.ashen_hollow.sel_arien: stands 28.6 m from location.outpost, outside its 27 m: a member stands inside the seat",
            ("locations/outpost.yaml", "radius_m: 30", "radius_m: 27"));

    [Fact]
    public void AStandingCondition_NamesTheSpeakersOwnFaction() =>
        Refused("a reputation condition names only the faction every participant belongs to",
            (Sel, "{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }",
                "{ kind: reputation, faction_ref: faction.ashen_hollow.waystation, min_tier: accepted }"));

    [Fact]
    public void ActDone_OnlyBesideAReportOfTheSameAct() =>
        Refused("stands only beside a report_act of the same act",
            (Kera, "{ kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }",
                "{ kind: act_done, act: switch_set, flag_ref: world.foldscar.steadied }"));

    [Fact]
    public void AReport_GoesOnlyToTheSpeakersReactingFaction() =>
        Refused("faction.ashen_hollow.waystation has no reaction to switch_set world.foldscar.steadied",
            (Kera, "{ command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }",
                "{ command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }"));

    [Fact]
    public void AGate_BelongsToTheTradersFaction()
    {
        const string cap = "  - { item_ref: item.armor.hide_cap, count: 1, price_bias: 1.0 }";
        const string merchant = "merchants/ashen_hollow/kera_voss.yaml";
        Refused("is gated by faction.ashen_hollow.survey, the faction of every NPC who trades from it", (merchant, cap,
            cap + "\n  - { item_ref: item.material.iron_ore, count: 2, requires: { faction_ref: faction.ashen_hollow.survey, min_tier: accepted } }"));
        Refused("a gated item appears in exactly one stock row", (merchant, cap,
            cap + "\n  - { item_ref: item.ammo.arrow_rough, count: 5, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }"));
        Assert.DoesNotContain(Refusals((merchant, cap,
                cap + "\n  - { item_ref: item.material.iron_ore, count: 2, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }")),
            r => r.Contains("merchant.ashen_hollow.kera_voss", StringComparison.Ordinal));
    }

    [Fact]
    public void APlacedPieceReaction_IsRefused()
    {
        foreach (string kind in new[] { "piece_placed", "piece_destroyed" })
            Refused($"act '{kind}' is not built: a repeat rule", (Waystation, ArmourReaction, $"act: {kind}, piece_ref: piece.none, delta: 100"));
    }

    [Fact]
    public void ARespawningCreatureReaction_IsRefused() =>
        Refused("spawn.hollow.east_pack brings it back",
            (Waystation, ArmourReaction, "act: creature_killed, creature_ref: creature.beast.wolf_grey, delta: 100"));

    [Fact]
    public void AReactionToAFlagContentCanReset_IsRefused()
    {
        Refused($"needs a flag only its switch sets; dialogue.ashen_hollow.kera_voss writes it too",
            (Kera, "          - { command: give_recipe, recipe_ref: recipe.smithing.march_spear }\n          - { command: record_relationship_event",
                "          - { command: give_recipe, recipe_ref: recipe.smithing.march_spear }\n          - { command: set_world_flag, flag_ref: world.foldscar.steadied, value: 1 }\n          - { command: record_relationship_event"));
        Refused("needs a switch that sets it",
            (Waystation, ArmourReaction, "act: switch_set, flag_ref: world.hollow.forge_shed_door_open, delta: 100"));
    }

    [Fact]
    public void NoContent_TradesCurrencyForStanding()
    {
        // Structurally: no quest reward, objective or act kind turns coin, goods or a quest into standing.
        Assert.Contains("reputation", RewardKinds.NotBuilt);
        Assert.DoesNotContain("reputation", RewardKinds.Built);
        Assert.Contains("faction_reputation", ObjectiveTypes.NotBuilt.Keys);
        Assert.Contains("faction_state", ObjectiveTypes.NotBuilt.Keys);
        Assert.DoesNotContain(ActKinds.Built, k => k.Contains("trade", StringComparison.Ordinal) || k.Contains("buy", StringComparison.Ordinal)
            || k.Contains("sell", StringComparison.Ordinal) || k.Contains("quest", StringComparison.Ordinal));

        // And a dialogue that tries is refused, with its reason.
        using var content = new EditedContent(GameContent, (Kera, "        consequences: [{ command: open_service, service: trade }]",
            "        consequences: [{ command: open_service, service: trade }, { command: add_reputation, faction_ref: faction.ashen_hollow.waystation, delta: 10 }]"));
        Assert.Contains(Load(content.Root).Errors, e => e.Message.Contains("add_reputation is refused: reputation moves only through acts a faction learns of (M7)",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("members: [npc.ashen_hollow.kera_voss]", "members is refused: membership is the NPC's faction_ref")]
    [InlineData("player_start_reputation: 50", "player_start_reputation is refused")]
    [InlineData("reputation_tiers: []", "reputation_tiers is refused")]
    [InlineData("laws: []", "laws is refused: crime is not built")]
    [InlineData("enemy_of: [faction.ashen_hollow.survey]", "enemy_of is refused")]
    [InlineData("attitude_default: 0.5", "attitude_default is refused")]
    [InlineData("territory: []", "territory is refused")]
    [InlineData("services_gated: []", "services_gated is refused")]
    [InlineData("joinable: true", "joinable is refused")]
    [InlineData("join_requirements: []", "join_requirements is refused")]
    public void FactionContent_RefusesDataModelFieldsItDoesNotBuild(string field, string expected) =>
        Refused(expected, (Waystation, "seat_location_ref: location.outpost\n", "seat_location_ref: location.outpost\n" + field + "\n"));

    [Fact]
    public void TheWitnessBlock_IsRefused_ItIsM9() =>
        Refused("witness is not built: the witnessed channel is M9",
            ("config/factions.yaml", "acts:\n", "witness: { sight_m: 30, fov_deg: 140, identify_m: 15 }\nacts:\n"));
}
