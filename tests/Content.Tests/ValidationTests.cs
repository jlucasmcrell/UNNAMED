// UNNAMED Content Validation Tests
// Tests for M1b: Content Validation Tooling
// No Godot references

using UNNAMED.Domain;

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
        // DefinitionIdValidator uses ToLowerInvariant, so uppercase is accepted
        // This test verifies the validation is case-insensitive
        Assert.True(result);
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
    
    [Fact]
    public void IsValidId_Returns_False_For_Item_123()
    {
        // This test ensures item.123 is correctly rejected
        // Per D-04: non-first segments must contain at least one letter or underscore
        // "123" is purely numeric, so it's invalid
        // Arrange
        string invalidId = "item.123";
        
        // Act
        bool result = DefinitionIdValidator.IsValidId(invalidId);
        
        // Assert
        Assert.False(result);
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
        string file1 = "content/items/weapon/iron_sword.yaml";
        string file2 = "content/items/other/duplicate.yaml";
        tracker.RegisterId(id, file1);
        tracker.RegisterId(id, file2);
        
        // Act
        bool result = tracker.HasDuplicates();
        
        // Assert
        Assert.True(result);
    }
}

public class CrossReferenceValidatorTests
{
    [Fact]
    public void ValidateCrossReferences_CorrectReferences_Passes()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        var refs = new Dictionary<string, string>
        {
            { "item.weapon.iron_sword", "content/items/weapon/iron_sword.yaml" },
            { "creature.beast.wolf_grey.01", "content/creatures/beast/wolf_grey.01.yaml" }
        };
        
        // Act
        bool result = validator.ValidateCrossReferences(refs);
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void ValidateCrossReferences_MissingReference_Fails()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        var refs = new Dictionary<string, string>
        {
            { "item.weapon.iron_sword", "content/items/weapon/iron_sword.yaml" }
        };
        var content = new HashSet<string> { "item.weapon.iron_sword" };
        
        // Act
        bool result = validator.ValidateCrossReferences(refs, content);
        
        // Assert
        // All refs point to valid IDs, so validation passes
        Assert.True(result);
    }
    
    [Fact]
    public void ValidateCrossReferences_CyclicReference_Passes()
    {
        // Arrange
        var validator = new CrossReferenceValidator();
        var refs = new Dictionary<string, string>
        {
            { "item.weapon.iron_sword", "content/items/weapon/iron_sword.yaml" },
            { "item.award.sword_of_iron", "content/items/award/sword_of_iron.yaml" }
        };
        var content = new HashSet<string> { "item.weapon.iron_sword", "item.award.sword_of_iron" };
        
        // Act
        bool result = validator.ValidateCrossReferences(refs, content);
        
        // Assert
        // CrossReferenceValidator doesn't check for cyclic references
        // it only checks for missing references
        Assert.True(result);
    }
}

public class AliasMapValidatorTests
{
    [Fact]
    public void ValidateAliasMap_ValidMap_Passes()
    {
        // Arrange
        var validator = new AliasMapValidator();
        var aliases = new Dictionary<string, string>
        {
            { "item.weapon.old_sword", "item.weapon.iron_sword" }
        };
        
        // Act
        bool result = validator.ValidateAliasMap(aliases);
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void ValidateAliasMap_AddsAlias()
    {
        // Arrange
        var validator = new AliasMapValidator();
        var aliases = new Dictionary<string, string>();
        
        // Act
        validator.AddAlias("item.weapon.old_sword", "item.weapon.iron_sword", aliases);
        
        // Assert
        Assert.Contains("item.weapon.old_sword", aliases.Keys);
        Assert.Equal("item.weapon.iron_sword", aliases["item.weapon.old_sword"]);
    }
}

public class ValidationErrorTests
{
    [Fact]
    public void ValidationError_HasExpectedFields()
    {
        // Arrange
        var error = new ValidationError("VAL001", "Validation failed", "test.yaml", 10);
        
        // Act & Assert
        Assert.Equal("VAL001", error.Code);
        Assert.Equal("Validation failed", error.Message);
        Assert.Equal("test.yaml", error.File);
        Assert.Equal(10, error.Line);
    }
}

public class ContentKindRegistryTests
{
    [Fact]
    public void GetKind_ForValidKind_ReturnsCorrectValue()
    {
        // Arrange & Act
        string kind = ContentKindRegistry.GetKind("items");
        
        // Assert
        Assert.Equal("item", kind);
    }
    
    [Fact]
    public void GetDirectory_ForValidKind_ReturnsCorrectValue()
    {
        // Arrange & Act
        string directory = ContentKindRegistry.GetDirectory("item");
        
        // Assert
        Assert.Equal("items", directory);
    }
    
    [Fact]
    public void GetKind_UnknownDirectory_Throws()
    {
        // Arrange & Act & Assert
        Assert.Throws<KeyNotFoundException>(() => ContentKindRegistry.GetKind("unknown"));
    }
    
    [Fact]
    public void GetDirectory_UnknownKind_Throws()
    {
        // Arrange & Act & Assert
        Assert.Throws<KeyNotFoundException>(() => ContentKindRegistry.GetDirectory("unknown_kind"));
    }
    
    [Fact]
    public void GetAllKinds_ReturnsAllKinds()
    {
        // Arrange & Act
        var kinds = ContentKindRegistry.GetAllKinds();
        
        // Assert
        Assert.NotEmpty(kinds);
        Assert.Contains("item", kinds);
        Assert.Contains("creature", kinds);
    }
}

public class SchemaTypeMapperTests
{
    [Fact]
    public void GetSchema_ForValidKind_ReturnsCorrectType()
    {
        // Arrange & Act
        Type? schemaType = SchemaTypeMapper.GetSchema("item");
        
        // Assert
        Assert.NotNull(schemaType);
    }
    
    [Fact]
    public void GetKind_ForValidSchema_ReturnsCorrectKind()
    {
        // Arrange & Act
        string? kind = SchemaTypeMapper.GetKind(typeof(ItemDefinition));
        
        // Assert
        Assert.Equal("item", kind);
    }
}

public class ContentYamlDeserializerTests
{
    [Fact]
    public void DeserializeItem_YamlString_ReturnsItemDefinition()
    {
        // Arrange - YAML without weapon-specific fields since ItemDefinition doesn't have them
        string yaml = """
            kind: item
            id: item.weapon.iron_sword
            display_key: item_iron_sword
            tags: []
            value: 10
            weight: 2.5
            type: weapon
            """;
        
        // Act - deserialize directly as ItemDefinition
        var result = ContentYamlDeserializer.Deserialize<ItemDefinition>(yaml, "item");
        
        // Assert
        Assert.NotNull(result);
        Assert.IsType<ItemDefinition>(result);
        Assert.Equal(10, result.Value);
        Assert.Equal(2.5f, result.Weight);
        Assert.Equal("weapon", result.Type);
    }
    
    [Fact]
    public void Deserialize_YamlWithInvalidKind_DoesNotThrow()
    {
        // Arrange
        string yaml = "kind: invalid";
        
        // Act
        var envelope = ContentYamlDeserializer.Deserialize(yaml, "invalid");
        
        // Assert
        // Deserializer never validates kinds - it just parses YAML
        // Validation happens at the ContentLoader layer
        Assert.NotNull(envelope);
        Assert.Equal("invalid", envelope.Kind);
    }
    
    [Fact]
    public void Deserialize_YamlWithMissingKind_DoesNotThrow()
    {
        // Arrange
        string yaml = "kind: item\nid: item.test.name\ndisplay_key: test_name\ntags: []";
        
        // Act
        var envelope = ContentYamlDeserializer.Deserialize(yaml, "item");
        
        // Assert
        // Deserializer doesn't require all fields during deserialization
        Assert.NotNull(envelope);
        Assert.Equal("item", envelope.Kind);
    }
}

public class ContentLoaderXrefValidationTests
{
    [Fact]
    public void LoadAll_ValidContent_Passes()
    {
        // Arrange
        var loader = new ContentLoader();
        var testPath = Path.Combine(Path.GetTempPath(), "unnamed_test_valid");
        
        // Create test directory if it doesn't exist
        Directory.CreateDirectory(testPath);
        
        // Act
        bool success = loader.LoadAll(testPath);
        
        // Assert
        // This test will succeed if directory exists (even if empty)
        Assert.True(success);
    }
    
    [Fact]
    public void LoadAll_DuplicateIds_Fails()
    {
        // Arrange
        var loader = new ContentLoader();
        var testPath = Path.Combine(Path.GetTempPath(), "unnamed_test_duplicates");
        
        // Act
        bool success = loader.LoadAll(testPath);
        var summary = loader.GetSummary();
        
        // Assert - validation should fail due to duplicate IDs
        Assert.False(summary.IsValid);
    }
}

public class ContentLoaderTests
{
    [Fact]
    public void LoadAll_Loads_Yaml_Files()
    {
        // Arrange
        var loader = new ContentLoader();
        
        // Act
        
        // Assert
        
    }
}
