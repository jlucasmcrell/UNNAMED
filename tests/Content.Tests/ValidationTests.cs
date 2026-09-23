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
                "config.base_speeds", "config.level_cap", "config.progression", "config.simulation_tiers", "config.time", "config.xp_curve",
                "location.den_mouth", "location.herb_patch", "location.iron_shelf", "location.outpost",
                "region.ashen_hollow", "skill.athletics", "skill.one_hand_blade", "skill.survival",
                "world.hollow.forge_shed_door_open", "world.hollow.longhouse_door_open",
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
            "damage_type: physical_slash\nhands: one\nskill_ref: skill.one_hand_blade\n");
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
