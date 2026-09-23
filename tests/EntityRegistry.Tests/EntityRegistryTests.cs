using System;
using UNNAMED.Domain;

namespace UNNAMED.EntityRegistry.Tests;

public class DefinitionIdTests
{
    [Fact]
    public void IsValid_ValidDefinitionId_ReturnsTrue()
    {
        Assert.True(DefinitionId.IsValid("item.weapon.iron_sword"));
        Assert.True(DefinitionId.IsValid("creature.beast.wolf_grey.01"));
        Assert.True(DefinitionId.IsValid("quest.main.01"));
    }

    [Fact]
    public void IsValid_InvalidDefinitionId_ReturnsFalse()
    {
        Assert.False(DefinitionId.IsValid(""));
        Assert.False(DefinitionId.IsValid("item")); // Single segment
        Assert.False(DefinitionId.IsValid("ITEM.weapon")); // Uppercase (case-insensitive pattern)
        Assert.False(DefinitionId.IsValid("item-weapon")); // Hyphen in name
        Assert.False(DefinitionId.IsValid("item.123")); // Numeric last segment
    }

    [Fact]
    public void Parse_ValidId_ParsesCorrectly()
    {
        var id = DefinitionId.Parse("item.weapon.iron_sword");
        Assert.Equal("item.weapon.iron_sword", id.ToString());
    }

    [Fact]
    public void Parse_InvalidId_Throws()
    {
        Assert.Throws<ArgumentException>(() => DefinitionId.Parse("item.123"));
    }

    [Fact]
    public void TryParse_ValidId_ReturnsTrue()
    {
        Assert.True(DefinitionId.TryParse("item.weapon.iron_sword", out var id));
        Assert.Equal("item.weapon.iron_sword", id.ToString());
    }

    [Fact]
    public void TryParse_InvalidId_ReturnsFalse()
    {
        Assert.False(DefinitionId.TryParse("item.123", out var id));
    }
}

public class EntityRegistryTests
{
    [Fact]
    public void CreateEntity_AssignsUniqueInstanceId()
    {
        var registry = new EntityRegistry();
        var entity1 = registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword"));
        var entity2 = registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword"));
        
        Assert.NotEqual(entity1.InstanceId, entity2.InstanceId);
    }

    [Fact]
    public void CreateEntity_CanBeRetrievedByInstanceId()
    {
        var registry = new EntityRegistry();
        var entity = registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword"));
        
        Assert.True(registry.TryGetEntity(entity.InstanceId, out var retrieved));
        Assert.Equal(entity.InstanceId, retrieved.InstanceId);
        Assert.Equal(entity.DefinitionId, retrieved.DefinitionId);
    }

    [Fact]
    public void GetEntitiesByDefinition_ReturnsAllInstances()
    {
        var registry = new EntityRegistry();
        var defId = DefinitionId.Parse("item.weapon.iron_sword");
        
        var entity1 = registry.CreateEntity(defId);
        var entity2 = registry.CreateEntity(defId);
        var entity3 = registry.CreateEntity(DefinitionId.Parse("item.weapon.steel_sword"));
        
        var instances = registry.GetEntitiesByDefinition(defId);
        Assert.Equal(2, instances.Count);
        Assert.Contains(entity1.InstanceId, instances);
        Assert.Contains(entity2.InstanceId, instances);
    }

    [Fact]
    public void DestroyEntity_MakesEntityInaccessible()
    {
        var registry = new EntityRegistry();
        var entity = registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword"));
        
        registry.DestroyEntity(entity.InstanceId);
        Assert.False(registry.TryGetEntity(entity.InstanceId, out _));
    }

    [Fact]
    public void Clear_RemovesAllEntities()
    {
        var registry = new EntityRegistry();
        registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword"));
        registry.CreateEntity(DefinitionId.Parse("item.weapon.steel_sword"));
        
        registry.Clear();
        Assert.Equal(0, registry.EntityCount);
    }
}

public class EntityIdTests
{
    [Fact]
    public void NewId_HasTimestampComponent()
    {
        // ULID timestamp has millisecond precision, so we need to wait between calls
        System.Threading.Thread.Sleep(1);

        var id1 = EntityId.NewId(EntityKind.Item);
        System.Threading.Thread.Sleep(1);
        var id2 = EntityId.NewId(EntityKind.Item);

        // ULIDs should be lexicographically sortable
        // id2 was created after id1, so id2 > id1
        Assert.True(id2 > id1, $"id2 ({id2}) should be greater than id1 ({id1})");
    }

    [Fact]
    public void ULID_EncodeDecode_Roundtrip()
    {
        var original = EntityId.NewId(EntityKind.Creature);
        var encoded = original.ToString();
        var decoded = EntityId.Parse(encoded);

        Assert.Equal(original, decoded);
        Assert.Equal(EntityKind.Creature, decoded.Kind);
    }

    [Fact]
    public void Parse_ValidId_ParsesCorrectly()
    {
        var id = EntityId.Parse("itm_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        Assert.Equal("itm_01ARZ3NDEKTSV4RRFFQ69G5FAV", id.ToString());
        Assert.Equal(EntityKind.Item, id.Kind);
        Assert.Equal("01ARZ3NDEKTSV4RRFFQ69G5FAV", id.Ulid);
    }

    [Fact]
    public void TryParse_InvalidId_ReturnsFalse()
    {
        Assert.False(EntityId.TryParse("invalid-ulid", out _));
        Assert.False(EntityId.TryParse("01ARZ3NDEKTSV4RRFFQ69G5FAV", out _));       // no prefix (D-04)
        Assert.False(EntityId.TryParse("xyz_01ARZ3NDEKTSV4RRFFQ69G5FAV", out _));   // unknown prefix
        Assert.False(EntityId.TryParse("itm_81ARZ3NDEKTSV4RRFFQ69G5FAV", out _));   // > 128 bits
        Assert.False(EntityId.TryParse("itm_01ARZ3NDEKTSV4RRFFQ69G5FAU", out _));   // 'U' is not Crockford
        Assert.False(EntityId.TryParse(null, out _));
    }

    [Fact]
    public void Create_EncodesCanonicalUlid()
    {
        // Vector computed independently of this code: the ULID specification's own example.
        byte[] random = Convert.FromHexString("d6764c61efb99302bd5b");
        var id = EntityId.Create(EntityKind.Item, 1469922850259, random);

        Assert.Equal("itm_01ARZ3NDEKTSV4RRFFQ69G5FAV", id.Value);
        Assert.Equal(1469922850259, id.Timestamp);
    }

    [Fact]
    public void NewId_FollowsD04Format()
    {
        var id = EntityId.NewId(EntityKind.Container);

        Assert.StartsWith("cnt_", id.Value);
        Assert.Equal(4 + EntityId.UlidLength, id.Value.Length);
        Assert.InRange(id.Ulid[0], '0', '7');
        Assert.Equal(id.Ulid, id.Ulid.ToUpperInvariant());
    }

    [Fact]
    public void Parse_AcceptsLowercaseUlid_AndNormalizes()
    {
        var id = EntityId.Parse("npc_01arz3ndektsv4rrffq69g5fav");
        Assert.Equal("npc_01ARZ3NDEKTSV4RRFFQ69G5FAV", id.Value);
    }

    [Fact]
    public void Equality_IsByValue_AndNullSafe()
    {
        var a = EntityId.Parse("itm_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var b = EntityId.Parse("itm_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        EntityId? none = null;

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.False(a == none);
        Assert.True(none == default);   // the comparison that threw NullReferenceException in M1
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void InstanceId_Uniqueness()
    {
        var registry = new HashSet<EntityId>();
        for (int i = 0; i < 1000; i++)
        {
            var id = EntityId.NewId(EntityKind.Item);
            Assert.True(registry.Add(id), $"Duplicate ID generated: {id}");
        }
    }

    [Fact]
    public void KindInference_CoversOnlyUnambiguousContentKinds()
    {
        Assert.True(EntityKinds.TryInferFromDefinition(DefinitionId.Parse("item.weapon.iron_sword"), out var k1));
        Assert.Equal(EntityKind.Item, k1);
        Assert.True(EntityKinds.TryInferFromDefinition(DefinitionId.Parse("creature.beast.wolf_grey"), out var k2));
        Assert.Equal(EntityKind.Creature, k2);
        Assert.False(EntityKinds.TryInferFromDefinition(DefinitionId.Parse("node.ore.iron_vein"), out _));
    }

    [Fact]
    public void Registry_RequiresExplicitKind_WhenDefinitionIsAmbiguous()
    {
        var registry = new EntityRegistry();
        var chest = DefinitionId.Parse("item.container.oak_chest");

        Assert.Throws<ArgumentException>(() => registry.CreateEntity(DefinitionId.Parse("node.ore.iron_vein")));

        var asContainer = registry.CreateEntity(chest, EntityKind.Container);
        Assert.Equal(EntityKind.Container, asContainer.InstanceId.Kind);
        Assert.StartsWith("cnt_", asContainer.InstanceId.Value);
    }

    [Fact]
    public void Two_Instances_One_Definition_GetDifferentIds()
    {
        var registry = new EntityRegistry();
        var defId = DefinitionId.Parse("item.weapon.iron_sword");
        
        var instance1 = registry.CreateEntity(defId);
        var instance2 = registry.CreateEntity(defId);
        
        Assert.NotEqual(instance1.InstanceId, instance2.InstanceId);
    }
}
