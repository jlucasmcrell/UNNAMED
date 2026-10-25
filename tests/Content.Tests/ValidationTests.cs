// UNNAMED Content Validation Tests
// Tests for M1b: Content Validation Tooling
// No Godot references

using UNNAMED.Content;

namespace UNNAMED.Content.Tests;

public class DefinitionIdValidationTests
{
    [Fact]
    public void IsValidId_Returns_True_For_Valid_Id()
    {
        // Arrange
        string validId = "item.weapon.iron_sword";
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(validId);
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void IsValidId_Returns_False_For_Invalid_Id_With_Uppercase()
    {
        // Arrange
        string invalidId = "Item.Weapon.Iron_Sword";
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(invalidId);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void IsValidId_Returns_False_For_Empty_Id()
    {
        // Arrange
        string invalidId = "";
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(invalidId);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void IsValidId_Returns_False_For_Null_Id()
    {
        // Arrange
        string? invalidId = null;
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(invalidId);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void IsValidId_Returns_True_For_Id_With_Ordinal()
    {
        // Arrange
        string validId = "quest.artifact.shattered_crown.03";
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(validId);
        
        // Assert
        Assert.True(result);
    }
}

public class DuplicateIdTrackerTests
{
    [Fact]
    public void RegisterId_Adds_Id_To_Registry()
    {
        // Arrange
        var tracker = new DuplicateIdTracker();
        string id = "item.weapon.iron_sword";
        string file1 = "content/items/weapon/iron_sword.yaml";
        
        // Act
        tracker.RegisterId(id, file1);
        
        // Assert
        Assert.Null(tracker.GetDuplicateFiles(id));
    }
    
    [Fact]
    public void RegisterId_Detects_Duplicate()
    {
        // Arrange
        var tracker = new DuplicateIdTracker();
        string id = "item.weapon.iron_sword";
        string file1 = "content/items/weapon/iron_sword.yaml";
        string file2 = "content/items/other/duplicate.yaml";
        
        // Act
        tracker.RegisterId(id, file1);
        tracker.RegisterId(id, file2);
        
        // Assert
        var duplicates = tracker.GetDuplicateFiles(id);
        Assert.NotNull(duplicates);
        Assert.Contains(file1, duplicates!);
        Assert.Contains(file2, duplicates!);
        Assert.Equal(2, duplicates!.Count);
    }
    
    [Fact]
    public void GetDuplicateFiles_Returns_Null_When_No_Duplicates()
    {
        // Arrange
        var tracker = new DuplicateIdTracker();
        string id = "item.weapon.iron_sword";
        string file1 = "content/items/weapon/iron_sword.yaml";
        tracker.RegisterId(id, file1);
        
        // Act
        var result = tracker.GetDuplicateFiles(id);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void HasDuplicates_Returns_True_When_Duplicates_Exist()
    {
        // Arrange
        var tracker = new DuplicateIdTracker();
        string id = "item.weapon.iron_sword";
        tracker.RegisterId(id, "file1.yaml");
        tracker.RegisterId(id, "file2.yaml");
        
        // Act
        bool hasDuplicates = tracker.HasDuplicates;
        
        // Assert
        Assert.True(hasDuplicates);
    }
    
    [Fact]
    public void GetAllDuplicates_Returns_Empty_When_No_Duplicates()
    {
        // Arrange
        var tracker = new DuplicateIdTracker();
        tracker.RegisterId("id1", "file1.yaml");
        tracker.RegisterId("id2", "file2.yaml");
        
        // Act
        var duplicates = tracker.GetAllDuplicates();
        
        // Assert
        Assert.Empty(duplicates);
    }
}

public class CrossReferenceValidatorTests
{
    [Fact]
    public void ValidateReference_Returns_True_For_Known_Id()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        validator.RegisterKnownIds(new[] { "item.weapon.iron_sword", "spell.fireball" });
        
        // Act
        bool result = validator.ValidateReference("item.weapon.iron_sword", "test.yaml");
        
        // Assert
        Assert.True(result);
        Assert.False(validator.HasErrors);
    }
    
    [Fact]
    public void ValidateReference_Returns_False_For_Unknown_Id()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        validator.RegisterKnownIds(new[] { "item.weapon.iron_sword" });
        
        // Act
        bool result = validator.ValidateReference("spell.nonexistent", "test.yaml", 42);
        
        // Assert
        Assert.False(result);
        Assert.True(validator.HasErrors);
        
        // Check error details
        Assert.Single(validator.Errors);
        Assert.Equal("XREF001", validator.Errors[0].Code);
        Assert.Contains("dangling", validator.Errors[0].Message.ToLower());
        Assert.Equal(42, validator.Errors[0].LineNumber);
    }
    
    [Fact]
    public void ValidateReferences_Reports_Multiple_Errors()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        validator.RegisterKnownIds(new[] { "item.weapon.iron_sword" });
        
        // Act
        bool result = validator.ValidateReferences(
            new[] { "spell.fireball", "spell.ice_spear" }, 
            "test.yaml");
        
        // Assert
        Assert.False(result);
        Assert.Equal(2, validator.Errors.Count);
    }
}

public class AliasMapValidatorTests
{
    [Fact]
    public void ResolveAlias_Returns_Resolved_Id_When_Alias_Exists()
    {
        // Arrange
        var validator = new AliasMapValidator();
        validator.LoadAliasMap(
            new Dictionary<string, string> { { "old_id", "new_id" } },
            new Dictionary<string, string>());
        
        // Act
        var (resolved, wasAlias, aliasSource) = validator.ResolveAlias("old_id");
        
        // Assert
        Assert.Equal("new_id", resolved);
        Assert.True(wasAlias);
        Assert.Equal("old_id", aliasSource);
    }
    
    [Fact]
    public void ValidateAliasTargets_Reports_Error_For_Missing_Target()
    {
        // Arrange
        var validator = new AliasMapValidator();
        validator.LoadAliasMap(
            new Dictionary<string, string> { { "old_id", "nonexistent" } },
            new Dictionary<string, string>());
        validator.RegisterKnownIds(new[] { "item.weapon.iron_sword" });
        
        // Act
        bool result = validator.ValidateAliasTargets();
        
        // Assert
        Assert.False(result);
        Assert.True(validator.HasErrors);
        Assert.Single(validator.Errors);
        Assert.Equal("ALIAS001", validator.Errors[0].Code);
    }
}

public class ValidationErrorTests
{
    [Fact]
    public void ToString_Includes_Severity_Code_Message_And_File()
    {
        // Arrange
        var error = new ValidationError
        {
            SeverityLevel = ValidationError.Severity.Error,
            Code = "TEST001",
            Message = "Test error message",
            FilePath = "content/test.yaml",
            LineNumber = 10
        };
        
        // Act
        string result = error.ToString();
        
        // Assert
        Assert.Contains("ERROR", result);
        Assert.Contains("TEST001", result);
        Assert.Contains("Test error message", result);
        Assert.Contains("content/test.yaml", result);
        Assert.Contains(":10", result);
    }
}

public class ContentKindRegistryTests
{
    [Fact]
    public void GetKind_Returns_Kind_Definition()
    {
        // Act
        var kindDef = ContentKindRegistry.GetKind("item.weapon");
        
        // Assert
        Assert.NotNull(kindDef);
        Assert.Equal("item.weapon", kindDef!.FullKind);
        Assert.Equal("items", kindDef.Directory);
    }
    
    [Fact]
    public void GetKind_Returns_Null_For_Unknown_Kind()
    {
        // Act
        var kindDef = ContentKindRegistry.GetKind("unknown.kind");
        
        // Assert
        Assert.Null(kindDef);
    }
    
    [Fact]
    public void IsValidKind_Returns_True_For_Known_Kind()
    {
        // Act
        bool result = ContentKindRegistry.IsValidKind("item.weapon");
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void IsValidKind_Returns_False_For_Unknown_Kind()
    {
        // Act
        bool result = ContentKindRegistry.IsValidKind("unknown.kind");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void KnownDirectories_Is_Closed_Set()
    {
        // Act
        var dirs = ContentKindRegistry.KnownDirectories;
        
        // Assert
        Assert.Contains("items", dirs);
        Assert.Contains("creatures", dirs);
        Assert.Contains("npcs", dirs);
        Assert.Contains("spells", dirs);
        Assert.Contains("abilities", dirs);
        Assert.Contains("effects", dirs);
        Assert.Contains("recipes", dirs);
        Assert.Contains("resources", dirs);
        Assert.Contains("quests", dirs);
        Assert.Contains("dialogue", dirs);
        Assert.Contains("factions", dirs);
        Assert.Contains("loot", dirs);
    }
}

public class SchemaTypeMapperTests
{
    [Fact]
    public void GetSchemaType_Returns_Specialized_Type_For_Kind()
    {
        // Act
        var schemaType = SchemaTypeMapper.GetSchemaType("item.weapon");
        
        // Assert - item.weapon has a specialized schema
        Assert.Equal(typeof(ItemWeaponSchema), schemaType);
    }
    
    [Fact]
    public void GetKind_Returns_Kind_For_SchemaType()
    {
        // Act
        var kind = SchemaTypeMapper.GetKind(typeof(ContentEnvelope));
        
        // Assert - ContentEnvelope is the base type, returns "zone" as placeholder
        Assert.NotNull(kind);
        Assert.Equal("zone", kind);
    }
}

public class ContentYamlDeserializerTests
{
    [Fact]
    public void Deserialize_Parses_Minimal_ContentEnvelope()
    {
        // Arrange
        string yaml = """
            id: item.weapon.iron_sword
            kind: item.weapon
            display_key: item.weapon.iron_sword.name
            tags: [weapon, sword, metal]
            """;
        
        // Act
        var envelope = ContentYamlDeserializer.Deserialize(yaml, "test.yaml");
        
        // Assert
        Assert.Equal("item.weapon.iron_sword", envelope.Id);
        Assert.Equal("item.weapon", envelope.Kind);
        Assert.Equal("item.weapon.iron_sword.name", envelope.DisplayKey);
        Assert.Equal(3, envelope.Tags.Length);
        Assert.Contains("weapon", envelope.Tags);
        Assert.Contains("sword", envelope.Tags);
        Assert.Contains("metal", envelope.Tags);
    }
    
    [Fact]
    public void Deserialize_Sets_SourceFile()
    {
        // Arrange
        string yaml = """
            id: test.id
            kind: item
            display_key: test.display
            tags: []
            """;
        
        // Act
        var envelope = ContentYamlDeserializer.Deserialize(yaml, "content/test.yaml");
        
        // Assert
        Assert.Equal("content/test.yaml", envelope.SourceFile);
    }
    
    [Fact]
    public void Deserialize_Detects_Unknown_Kind()
    {
        // Arrange
        string yaml = """
            id: item.weapon.test_item
            kind: item.unknown_kind
            display_key: item.weapon.test_item.name
            tags: []
            """;
        
        // Act
        var envelope = ContentYamlDeserializer.Deserialize(yaml, "test.yaml");
        
        // Assert - unknown kind should still deserialize but ContentKindRegistry will reject it
        Assert.Equal("item.weapon.test_item", envelope.Id);
        Assert.Equal("item.unknown_kind", envelope.Kind);
    }
}

internal static class RepoPaths
{
    /// <summary>The repository root, found from the test binaries so no machine-specific path is baked in.</summary>
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root (src/UNNAMED.sln) not found above " + AppContext.BaseDirectory);
    }

    public static string InvalidFixture => Path.Combine(Root(), "tests", "fixtures", "fixture-invalid");
}

public class ContentLoaderXrefValidationTests
{
    [Fact]
    public void LoadAll_Detects_DanglingReferences_In_RootLevel_File()
    {
        // Arrange
        var loader = new ContentLoader();
        string testPath = RepoPaths.InvalidFixture;

        // Act
        bool success = loader.LoadAll(testPath);
        var summary = loader.GetSummary();

        // Assert - validation should fail due to dangling references
        Assert.False(success);
        Assert.True(summary.HasErrors);
        
        // Check for XREF001 errors
        var xrefErrors = summary.Errors.Where(e => e.Code == "XREF001").ToList();
        Assert.NotEmpty(xrefErrors);
        
        // Verify at least one dangling reference error
        Assert.Contains(xrefErrors, e => e.Message.Contains("spell.nonexistent") || e.Message.Contains("nonexistent"));
    }
    
    [Fact]
    public void LoadAll_Detects_DuplicateIds()
    {
        // Arrange
        var loader = new ContentLoader();
        string testPath = RepoPaths.InvalidFixture;

        // Act
        bool success = loader.LoadAll(testPath);
        var summary = loader.GetSummary();

        // Assert - validation should fail due to duplicate IDs
        Assert.True(summary.HasErrors);
        
        // Check for DUP001/DUP002 errors
        var dupErrors = summary.Errors.Where(e => e.Code == "DUP001" || e.Code == "DUP002").ToList();
        Assert.NotEmpty(dupErrors);
        
        // Verify at least one duplicate ID error for wolf_grey.01
        Assert.Contains(dupErrors, e => e.Message.Contains("wolf_grey.01"));
    }
}

public class ContentLoaderTests
{
    [Fact]
    public void LoadAll_Loads_Yaml_Files()
    {
        var loader = new ContentLoader();

        bool success = loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));

        Assert.True(success, string.Join("\n", loader.Errors));
        Assert.Empty(loader.Errors);
        Assert.Equal(
            new[]
            {
                "ability.blade.beat_the_guard", "ability.blade.gap_thrust", "ability.blade.parry", "ability.blade.power_strike", "ability.blade.quiet_blade",
                "ability.bow.aimed_shot", "ability.bow.steady_draw", "ability.creature.armour_slam", "ability.creature.boar_charge",
                "ability.creature.boar_gore", "ability.creature.gauge_sweep", "ability.creature.grazer_charge", "ability.creature.hoof_kick",
                "ability.creature.horn_butt", "ability.creature.hound_lunge", "ability.creature.husk_slash", "ability.creature.index_grasp",
                "ability.creature.measure_fall", "ability.creature.peck", "ability.creature.spider_bite", "ability.creature.tender_strike", "ability.creature.tusk_sweep", "ability.creature.warden_fall", "ability.creature.warden_grasp", "ability.creature.warden_sweep",
                "ability.creature.wolf_bite", "ability.polearm.giving_ground", "ability.polearm.long_thrust", "ability.polearm.set_against_charge",
                "body.veth_f.average", "body.veth_f.heavy", "body.veth_f.lean", "body.veth_f.very_heavy", "body.veth_f.very_lean",
                "body.veth_m.average", "body.veth_m.heavy", "body.veth_m.lean", "body.veth_m.very_heavy", "body.veth_m.very_lean",
                "call.hedge_healer", "call.scholar",
                "call.smiths_hand", "call.wayfarer", "chatter.b0001", "chatter.b0002",
                "chatter.b0003", "chatter.b0004", "chatter.b0005", "chatter.b0006",
                "chatter.b0007", "chatter.b0008", "chatter.b0009", "chatter.b0010",
                "chatter.lfg", "chatter.news", "config.adventurers", "config.base_speeds",
                "config.chat", "config.companion", "config.crafting", "config.creature_behaviour",
                "config.damage_constants", "config.economy", "config.factions", "config.inventory",
                "config.level_cap", "config.magic", "config.navigation", "config.npc_life",
                "config.party", "config.pets", "config.progression", "config.simulation_tiers", "config.teaching",
                "config.time", "config.travel", "config.weather", "config.xp_curve",
                "creature.beast.ash_ember_hound", "creature.beast.bristleback_boar", "creature.beast.buck", "creature.beast.cave_hunting_spider",
                "creature.beast.chicken", "creature.beast.doe", "creature.beast.hound_pup", "creature.beast.rabbit", "creature.beast.red_fox",
                "creature.beast.rooster", "creature.beast.wolf_grey", "creature.beast.wolf_grey_alpha", "creature.construct.animated_armour", "creature.construct.gate_tender", "creature.construct.gate_warden", "creature.construct.ring_custodian",
                "creature.grazer.barrowback", "creature.grazer.fellhorn", "creature.undead.bone_walker_husk", "dialogue.ashen_hollow.kera_voss",
                "dialogue.ashen_hollow.renn_vale", "dialogue.ashen_hollow.sel_arien", "dialogue.ashen_hollow.tavar_orr", "dialogue.cairnwold.berrin_garth",
                "dialogue.cairnwold.durn_skell", "dialogue.cairnwold.mara_ling", "dialogue.cairnwold.tam_crowden", "effect.bleeding", "effect.braced",
                "effect.coating_paralyse", "effect.coating_root", "effect.coating_sleep", "effect.coating_venom", "effect.ember_mending", "effect.fed", "effect.mending",
                "effect.paralyse", "effect.root", "effect.sleep", "effect.venom", "effect.weakened", "faction.ashen_hollow.long_survey", "faction.ashen_hollow.wayhold",
                "hint.action_bars", "hint.adventurers", "hint.auto_run", "hint.away",
                "hint.away_detail", "hint.bandage_early", "hint.calls", "hint.carrying",
                "hint.chat", "hint.companions_mend", "hint.death", "hint.dodging",
                "hint.downed", "hint.durability", "hint.fighting", "hint.formulas",
                "hint.help", "hint.helping_up", "hint.hireling_risk", "hint.hiring",
                "hint.interact", "hint.journal", "hint.level", "hint.lfg",
                "hint.looting", "hint.mending", "hint.moving", "hint.node_restraint",
                "hint.nodes", "hint.pack", "hint.pack_full", "hint.party", "hint.points_keep",
                "hint.prices", "hint.quality", "hint.reputation", "hint.skills",
                "hint.specialised_bags", "hint.stash", "hint.station_storage", "hint.teacher_practise",
                "hint.trade", "hint.trainers", "hint.waystones", "hint.worn_price",
                "item.ammo.arrow_rough", "item.armor.cloak", "item.armor.hide_boots", "item.armor.hide_cap",
                "item.armor.hide_vest", "item.armor.lame_coif", "item.armor.lame_sabatons", "item.armor.lamework_jack",
                "item.armor.smith_gloves", "item.armor.timber_shield", "item.armor.wool_trousers", "item.artefact.lantern_foot",
                "item.bag.herb_pouch", "item.bag.pack_large", "item.bag.quiver", "item.bag.reagent_satchel",
                "item.bag.satchel", "item.consumable.paralyse_coating", "item.consumable.poison_flask", "item.consumable.roast_meat", "item.consumable.salve_ember",
                "item.consumable.salve_minor", "item.consumable.sleep_coating", "item.consumable.snarebind_resin", "item.heritage.kept_roll", "item.heritage.record_stone", "item.material.alpha_pelt", "item.material.ash_haft", "item.material.barrowback_hide", "item.material.bog_cotton",
                "item.material.boneleaf", "item.material.cinder_sac", "item.material.deer_hide", "item.material.feathers",
                "item.material.fellhorn_hide", "item.material.herb_ashbloom", "item.material.herb_woundmoss", "item.material.iron_ingot",
                "item.material.iron_ore", "item.material.raw_meat", "item.material.small_hide", "item.material.spider_silk",
                "item.material.timber", "item.material.wolf_hide", "item.misc.hound_pup", "item.quest.barrowworks_codex_fragment", "item.quest.firstfold_shard", "item.quest.halda_token",
                "item.story.brass_key", "item.story.folded_note", "item.story.glass_marble", "item.story.lantern_ledger",
                "item.story.stopped_watch", "item.story.unreadable_page", "item.tome.bench_patterns", "item.tome.field_notes",
                "item.tome.herbal_remedies", "item.tome.resonance_primer", "item.tool.fitting_wedge", "item.tool.torch",
                "item.tool.water_flask", "item.trinket.alpha_fang", "item.trinket.wolf_fang", "item.weapon.haldas_bow", "item.weapon.hunting_bow", "item.weapon.iron_dagger",
                "item.weapon.march_spear", "item.weapon.rusted_sword", "location.barrowworks.blind_store", "location.barrowworks.gauge_archive",
                "location.barrowworks.headworks", "location.barrowworks.intake_hall", "location.barrowworks.r01_threshold", "location.barrowworks.r03_wash_passage",
                "location.barrowworks.r04_drain_mouth", "location.barrowworks.r05_sorting_floor", "location.barrowworks.r06_still_bay", "location.barrowworks.r08_return_gallery",
                "location.barrowworks.r11_measuring_court", "location.barrowworks.r13_inspection_bay", "location.barrowworks.service_vestibule", "location.blackvein_cut",
                "location.cairnwold.ash_stand", "location.cairnwold.iron_seam", "location.cairnwold.lodge", "location.cairnwold.the_bog",
                "location.cairnwold.the_crags", "location.cairnwold.the_gorge", "location.cairnwold.village", "location.den_mouth", "location.field.stopped_gate", "location.field.the_spill",
                "location.foldscar", "location.herb_patch", "location.old_late", "location.outpost", "location.road.basin_rim",
                "location.road.lantern_shrine", "location.road.overturned_wain", "location.road.sunk_barrow", "location.ruined_cart",
                "loot.alpha_wolf", "loot.animated_armour", "loot.ash_ember_hound", "loot.barrow_cache", "loot.barrowback",
                "loot.barrowworks.archive_crate", "loot.barrowworks.codex_fragment", "loot.barrowworks.lantern_foot", "loot.bone_walker_husk",
                "loot.bristleback_boar", "loot.cairnwold_cart", "loot.cairnwold_chest", "loot.cave_hunting_spider",
                "loot.chicken", "loot.deer", "loot.den_cache", "loot.fellhorn",
                "loot.fox", "loot.gate_tender", "loot.gate_warden", "loot.merchant_cart", "loot.rabbit", "loot.rim_camp",
                "loot.ring_custodian", "loot.shrine_offerings", "loot.stash_empty", "loot.story.brass_key", "loot.story.glass_marble",
                "loot.story.lantern_ledger", "loot.story.stopped_watch", "loot.story.unreadable_page", "loot.wain_load",
                "loot.waystation_chest", "loot.wolf_grey",
                "lore.animated_armour", "lore.ashbloom", "lore.ashen_hollow", "lore.ashen_waystone",
                "lore.barrowback", "lore.barrowworks", "lore.blackvein", "lore.bog_cotton",
                "lore.bone_walker_husk", "lore.boneleaf", "lore.bristleback_boar", "lore.cairnwold",
                "lore.cave_hunting_spider", "lore.charwood_verge", "lore.cinder_sac", "lore.constructed",
                "lore.crag_walker", "lore.ember_hounds", "lore.fellhorn", "lore.firstfold_iron", "lore.folded_note", "lore.gate_tender", "lore.gate_warden",
                "lore.grey_alpha", "lore.grey_wolf", "lore.halda_crowden", "lore.iron",
                "lore.iron_under_ash_after", "lore.kal", "lore.long_survey", "lore.mor",
                "lore.mor_schism", "lore.north_ash_stand", "lore.old_late", "lore.ondrek",
                "lore.orenth", "lore.otherborn", "lore.otherfold", "lore.othergate",
                "lore.othermarked", "lore.otherway", "lore.otherwhen", "lore.otherwhere",
                "lore.otherwrought", "lore.overturned_wain", "lore.pacha", "lore.pachakuti",
                "lore.quiet_folk", "lore.rim_stone", "lore.ring_custodian", "lore.ruined_cart",
                "lore.siann", "lore.sinking_moss", "lore.spider_silk", "lore.stopped_gate", "lore.sunk_barrow",
                "lore.tavar_and_the_fold", "lore.tavars_afternoon", "lore.the_crags", "lore.the_first_pacha",
                "lore.the_found_pacha", "lore.the_gorge", "lore.the_kal_afterlife", "lore.the_kals_maker",
                "lore.the_lantern_shrine", "lore.the_last_pachakuti", "lore.the_long_lodge", "lore.the_lost_pacha",
                "lore.the_ondrek_record", "lore.the_other", "lore.the_rim", "lore.the_siann_missing_page",
                "lore.the_silence", "lore.the_south_crag_seam", "lore.the_specification", "lore.the_spill", "lore.the_substrate",
                "lore.the_vaskaal_and_pacha", "lore.the_veil", "lore.the_written_pacha", "lore.three_quiet_stones_after",
                "lore.vaskaal", "lore.vessmere", "lore.veth", "lore.veth_keeping",
                "lore.wayhold", "lore.wenna", "lore.what_the_bog_kept_after", "lore.wolves_den",
                "lore.woundmoss",
                "market.ashen_hollow", "market.cairnwold",
                "merchant.ashen_hollow.kera_voss", "merchant.cairnwold.durn_skell", "node.fibre.bog_cotton", "node.firstplay.ashbloom",
                "node.herb.boneleaf", "node.herb.woundmoss", "node.ore.iron_seam", "node.ore.loose_iron",
                "node.trap.larder_snare", "node.trap.pelt_snare", "node.wood.ash_stand", "node.wood.deadfall",
                "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.sel_arien", "npc.ashen_hollow.tavar_orr",
                "npc.cairnwold.berrin_garth", "npc.cairnwold.durn_skell", "npc.cairnwold.mara_ling", "npc.cairnwold.tam_crowden",
                "people.ondrek", "people.veth", "piece.door.timber", "piece.doorway.timber", "piece.pad.timber",
                "piece.roof.timber", "piece.station.anvil", "piece.storage.chest", "piece.wall.timber",
                "quest.ashen_hollow.end_of_playtest", "quest.ashen_hollow.haldas_tally", "quest.ashen_hollow.iron_under_ash", "quest.ashen_hollow.three_quiet_stones",
                "quest.barrowworks.unfinished_measure", "quest.bounty.crag_walker", "quest.bounty.crossing_timber", "quest.bounty.lodge_supplies",
                "quest.bounty.moor_billets", "quest.bounty.rim_road", "quest.bounty.road_wolves", "quest.bounty.shelf_iron", "quest.bounty.survey_herbs",
                "quest.bounty.winter_hides", "quest.bounty.wrong_leaves", "quest.cairnwold.iron_from_the_moor", "quest.cairnwold.what_the_bog_kept", "quest.field.what_stands_on_the_top", "quest.heritage.what_was_kept",
                "quest.main.closed_seam", "quest.main.folded_note", "quest.main.moss_lights", "quest.main.road_east",
                "quest.main.the_road_in", "quest.main.three_accounts", "quest.main.wrong_side", "quest.story.lantern_ledger",
                "quest.story.name_dandelion", "quest.story.name_robin", "recipe.cooking.roast_meat", "recipe.fletching.arrows",
                "recipe.fletching.hunting_bow", "recipe.fletching.hunting_bow_silk", "recipe.herbal.salve_ember", "recipe.herbal.salve_ember_boneleaf",
                "recipe.herbal.salve_minor", "recipe.herbal.salve_minor_ashbloom", "recipe.leatherwork.cloak", "recipe.leatherwork.hide_boots",
                "recipe.leatherwork.hide_cap", "recipe.leatherwork.hide_cap_fellhorn", "recipe.leatherwork.hide_vest", "recipe.leatherwork.hide_vest_barrowback",
                "recipe.leatherwork.smith_gloves", "recipe.leatherwork.timber_shield", "recipe.leatherwork.wool_trousers", "recipe.outfitting.herb_pouch",
                "recipe.outfitting.pack_large", "recipe.outfitting.quiver", "recipe.outfitting.reagent_satchel", "recipe.outfitting.satchel",
                "recipe.smithing.fitting_wedge", "recipe.smithing.iron_billet", "recipe.smithing.lame_coif", "recipe.smithing.lame_sabatons",
                "recipe.smithing.lamework_jack", "recipe.smithing.march_spear", "recipe.smithing.torch", "region.ashen_hollow",
                "region.barrowworks", "region.starting_frontier", "resource.fibre.bog_cotton", "resource.firstplay.ashbloom",
                "resource.herb.boneleaf", "resource.herb.woundmoss", "resource.ore.iron", "resource.trap.larder_snare",
                "resource.trap.pelt_snare", "resource.wood.ash", "resource.wood.deadfall", "routine.ashen_hollow.cook",
                "routine.ashen_hollow.fletcher", "routine.ashen_hollow.guard", "routine.ashen_hollow.hunter", "routine.ashen_hollow.kera_voss",
                "routine.ashen_hollow.miner", "routine.ashen_hollow.porter", "routine.ashen_hollow.renn_vale", "routine.ashen_hollow.sel_arien",
                "routine.ashen_hollow.tavar_orr", "routine.cairnwold.berrin_garth", "routine.cairnwold.carter", "routine.cairnwold.durn_skell",
                "routine.cairnwold.guard", "routine.cairnwold.herder", "routine.cairnwold.hunter", "routine.cairnwold.mara_ling",
                "routine.cairnwold.tam_crowden", "routine.cairnwold.trapper", "routine.cairnwold.woodcutter", "skill.athletics",
                "skill.bow", "skill.force", "skill.handling", "skill.one_hand_blade", "skill.polearm",
                "skill.salvage", "skill.smithing", "skill.stealth", "skill.survival", "skill.vital", "skill.warding",
                "spawn.barrowworks.bend_husk", "spawn.barrowworks.inspection_spiders", "spawn.barrowworks.ring_custodian",
                "spawn.barrowworks.sorting_spider_north", "spawn.barrowworks.sorting_spider_south", "spawn.barrowworks.wash_spider",
                "spawn.cairnwold.barrow_sentinel", "spawn.cairnwold.birch_boar", "spawn.cairnwold.birch_deer", "spawn.cairnwold.crag_walker",
                "spawn.cairnwold.fox", "spawn.cairnwold.gorge_spiders", "spawn.cairnwold.hens", "spawn.cairnwold.moor_hares",
                "spawn.cairnwold.moor_strays", "spawn.cairnwold.outfall_hares", "spawn.field.breach_northeast", "spawn.field.breach_west", "spawn.field.gate_warden", "spawn.field.tenders_east", "spawn.field.tenders_north", "spawn.field.tenders_west", "spawn.hollow.boar_wallow", "spawn.hollow.charwood_deer",
                "spawn.hollow.charwood_fox", "spawn.hollow.charwood_hound", "spawn.hollow.den_pack", "spawn.hollow.east_pack",
                "spawn.hollow.foldscar_barrowbacks", "spawn.hollow.foldscar_fellhorns", "spawn.hollow.hares", "spawn.hollow.hens",
                "spawn.hollow.iron_shelf_armour", "spawn.hollow.iron_shelf_husk", "spawn.hollow.spider_lair", "spawn.hollow.valley_strays",
                "spawn.road.barrow_husks", "spawn.road.charred_pair", "spawn.road.deer", "spawn.road.hares",
                "spawn.road.wayside_pack", "spell.force.impulse_bolt", "spell.vital.mending_thread", "spell.warding.brace_ward",
                "world.barrowworks.blind_store_read", "world.barrowworks.codex_fragment_taken", "world.barrowworks.diagram_rack_read", "world.barrowworks.fitting_note_read",
                "world.barrowworks.gate_g1_open", "world.barrowworks.gate_g2_open", "world.barrowworks.gate_g3_open", "world.barrowworks.headworks_dismantled",
                "world.barrowworks.headworks_preserved", "world.barrowworks.lantern_foot_taken", "world.barrowworks.ring_accessible", "world.barrowworks.ring_custodian_defeated",
                "world.barrowworks.withdrawal_notice_read", "world.cairnwold.cw_house_01_door_open", "world.cairnwold.cw_house_02_door_open", "world.cairnwold.cw_house_03_door_open",
                "world.cairnwold.cw_house_04_door_open", "world.cairnwold.cw_house_05_door_open", "world.cairnwold.cw_house_06_door_open", "world.cairnwold.cw_house_07_door_open",
                "world.cairnwold.forge_shed_door_open", "world.cairnwold.granary_door_open", "world.cairnwold.longhouse_door_open", "world.field.anchor_northeast_stilled", "world.field.anchor_south_stilled", "world.field.anchor_west_stilled", "world.field.stilled", "world.foldscar.steadied",
                "world.foldscar.stone_north_aligned", "world.foldscar.stone_southeast_aligned", "world.foldscar.stone_southwest_aligned", "world.hollow.forge_shed_door_open",
                "world.hollow.granary_door_open", "world.hollow.halda_tally_kept", "world.hollow.longhouse_door_open", "world.main.carrier",
                "world.main.gauge_seen", "world.main.named_dandelion", "world.main.named_robin", "world.road.rim_stone_woken",
                "world.space.elapsed_ticks",
            },
            loader.Definitions.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void LoadAll_ReportsAMalformedFile_InsteadOfSkippingIt()
    {
        // M1b printed a DEBUG line and dropped the file, so validation "passed" without it.
        var loader = new ContentLoader();

        loader.LoadAll(RepoPaths.InvalidFixture);

        Assert.Contains(loader.Errors, e => e.Code == "LOAD003" && e.FilePath.EndsWith("malformed-schema.yaml", StringComparison.Ordinal));
    }
}

public class AliasFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "unnamed-aliases-" + Guid.NewGuid().ToString("N"));

    public AliasFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "items", "weapon"));
        Directory.CreateDirectory(Path.Combine(_root, "skills"));
        File.WriteAllText(Path.Combine(_root, "items", "weapon", "iron_sword.yaml"),
            "id: item.weapon.iron_sword\nkind: item.weapon\nschema: 1\ndisplay_key: item.weapon.iron_sword.name\ntags: [weapon]\n" +
            "category: weapon\nstack_max: 1\nweight: 3.2\nvalue_base: 90\nrarity: common\ndamage: [7, 11]\n" +
            "damage_type: physical_slash\nhands: one\nattack_speed: 1.2\nreach: 1.9\nskill_ref: skill.one_hand_blade\n");
        File.WriteAllText(Path.Combine(_root, "skills", "one_hand_blade.yaml"),
            "id: skill.one_hand_blade\nkind: skill\nschema: 1\ndisplay_key: skill.one_hand_blade.name\ntags: [skill]\nfamily: combat\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ContentLoader Load(string aliases)
    {
        File.WriteAllText(Path.Combine(_root, "_aliases.yaml"), aliases);
        var loader = new ContentLoader();
        loader.LoadAll(_root);
        return loader;
    }

    [Fact]
    public void Aliases_Removals_AndDiscards_AreRead_CommentsAndAll()
    {
        var loader = Load(
            "aliases:                        # renamed IDs, kept forever\n" +
            "  item.weapon.ironsword: item.weapon.iron_sword   # typo fix\n" +
            "removed:\n" +
            "  item.weapon.bronze_sword: item.weapon.iron_sword\n" +
            "  item.weapon.wooden_sword: ~\n");

        Assert.Empty(loader.Errors);
        Assert.Equal("item.weapon.iron_sword", loader.Aliases["item.weapon.ironsword"]);
        Assert.Equal("item.weapon.iron_sword", loader.Removed["item.weapon.bronze_sword"]);
        Assert.Equal(new[] { "item.weapon.wooden_sword" }, loader.Discarded);
    }

    [Fact]
    public void AReplacementThatDoesNotExist_IsAnError() =>
        Assert.Contains(Load("removed:\n  item.weapon.bronze_sword: item.weapon.steel_sword\n").Errors, e => e.Code == "ALIAS003");

    [Fact]
    public void ARenamedIdThatIsStillDefined_IsAnError() =>
        Assert.Contains(Load("aliases:\n  item.weapon.iron_sword: item.weapon.iron_sword\n").Errors, e => e.Code == "ALIAS004");

    [Fact]
    public void AnUnknownSection_IsAnError() =>
        Assert.Contains(Load("renamed:\n  item.weapon.ironsword: item.weapon.iron_sword\n").Errors, e => e.Code == "ALIAS005");
}
