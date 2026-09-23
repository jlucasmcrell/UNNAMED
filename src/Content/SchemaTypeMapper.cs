// UNNAMED Content Validation - Schema Type Mapper
// Maps content kinds to their C# schema types
// No Godot references

namespace UNNAMED.Content;

/// <summary>
/// Maps content kinds to their CLR types for deserialization.
/// Uses YamlNetSerialize for dynamic type selection at runtime.
/// </summary>
public static class SchemaTypeMapper
{
    /// <summary>
    /// Get the expected CLR type for a content kind based on its full kind name.
    /// Returns ContentEnvelope base type when the kind is not specifically mapped.
    /// </summary>
    /// <param name="kind">Full kind name</param>
    /// <returns>Expected CLR type for this kind</returns>
    public static Type GetSchemaType(string kind)
    {
        return kind switch
        {
            // Base kinds return ContentEnvelope
            "item" => typeof(ContentEnvelope),
            "creature" => typeof(ContentEnvelope),
            "npc" => typeof(ContentEnvelope),
            "spell" => typeof(ContentEnvelope),
            "ability" => typeof(ContentEnvelope),
            "effect" => typeof(ContentEnvelope),
            "recipe" => typeof(ContentEnvelope),
            "resource" => typeof(ContentEnvelope),
            "quest" => typeof(ContentEnvelope),
            "dialogue" => typeof(ContentEnvelope),
            "faction" => typeof(ContentEnvelope),
            "loot" => typeof(ContentEnvelope),
            "merchant" => typeof(ContentEnvelope),
            "affix" => typeof(ContentEnvelope),
            "node" => typeof(ContentEnvelope),
            "spawn" => typeof(ContentEnvelope),
            "location" => typeof(ContentEnvelope),
            "config" => typeof(ContentEnvelope),
            "species" => typeof(ContentEnvelope),
            "schedule" => typeof(ContentEnvelope),
            "set" => typeof(ContentEnvelope),
            "region" => typeof(ContentEnvelope),
            "anchor" => typeof(ContentEnvelope),
            "fact" => typeof(ContentEnvelope),
            "world_flag" => typeof(ContentEnvelope),
            
            // Sub-kinds with specialized schemas
            "item.weapon" => typeof(ItemWeaponSchema),
            "item.armor" => typeof(ItemArmorSchema),
            
            _ => typeof(ContentEnvelope)
        };
    }
    
    /// <summary>
    /// Check if a kind has a specialized schema type defined.
    /// </summary>
    /// <param name="kind">Full kind name</param>
    /// <returns>True if specialized type exists, false otherwise</returns>
    public static bool HasSpecializedSchema(string kind)
    {
        return kind switch
        {
            "item.weapon" => true,
            "item.armor" => true,
            "creature" => true,
            "npc" => true,
            "spell" => true,
            "ability" => true,
            "effect" => true,
            "recipe" => true,
            "resource" => true,
            "quest" => true,
            "dialogue" => true,
            "faction" => true,
            "loot" => true,
            "merchant" => true,
            "affix" => true,
            "node" => true,
            "spawn" => true,
            "location" => true,
            "config" => true,
            "species" => true,
            "schedule" => true,
            "set" => true,
            "region" => true,
            "anchor" => true,
            "fact" => true,
            "world_flag" => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Get the kind name for a given schema type.
    /// Returns null if the type is not a known schema type.
    /// </summary>
    /// <param name="schemaType">The CLR type</param>
    /// <returns>The kind name or null</returns>
    public static string? GetKind(Type schemaType)
    {
        return schemaType.Name switch
        {
            "ContentEnvelope" => "zone",
            "ItemWeaponSchema" => "item.weapon",
            "ItemArmorSchema" => "item.armor",
            "CreatureSchema" => "creature",
            "NpcSchema" => "npc",
            "SpellSchema" => "spell",
            "AbilitySchema" => "ability",
            "EffectSchema" => "effect",
            "RecipeSchema" => "recipe",
            "ResourceSchema" => "resource",
            "QuestSchema" => "quest",
            "DialogueSchema" => "dialogue",
            "FactionSchema" => "faction",
            "LootSchema" => "loot",
            "MerchantSchema" => "merchant",
            "AffixSchema" => "affix",
            "NodeSchema" => "node",
            "SpawnSchema" => "spawn",
            "LocationSchema" => "location",
            "ConfigSchema" => "config",
            "SpeciesSchema" => "species",
            "ScheduleSchema" => "schedule",
            "SetSchema" => "set",
            "RegionSchema" => "region",
            "AnchorSchema" => "anchor",
            "FactSchema" => "fact",
            "WorldFlagSchema" => "world_flag",
            _ => null
        };
    }
}

/// <summary>
/// Base schema for item.weapon kind.
/// Extended from ContentEnvelope with weapon-specific fields.
/// </summary>
public record ItemWeaponSchema : ContentEnvelope
{
    // Weapon-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for item.armor kind.
/// Extended from ContentEnvelope with armor-specific fields.
/// </summary>
public record ItemArmorSchema : ContentEnvelope
{
    // Armor-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for creature kind.
/// Extended from ContentEnvelope with creature-specific fields.
/// </summary>
public record CreatureSchema : ContentEnvelope
{
    // Creature-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for npc kind.
/// Extended from ContentEnvelope with NPC-specific fields.
/// </summary>
public record NpcSchema : ContentEnvelope
{
    // NPC-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for spell kind.
/// Extended from ContentEnvelope with spell-specific fields.
/// </summary>
public record SpellSchema : ContentEnvelope
{
    // Spell-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for ability kind.
/// Extended from ContentEnvelope with ability-specific fields.
/// </summary>
public record AbilitySchema : ContentEnvelope
{
    // Ability-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for effect kind.
/// Extended from ContentEnvelope with effect-specific fields.
/// </summary>
public record EffectSchema : ContentEnvelope
{
    // Effect-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for recipe kind.
/// Extended from ContentEnvelope with recipe-specific fields.
/// </summary>
public record RecipeSchema : ContentEnvelope
{
    // Recipe-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for resource kind.
/// Extended from ContentEnvelope with resource-specific fields.
/// </summary>
public record ResourceSchema : ContentEnvelope
{
    // Resource-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for quest kind.
/// Extended from ContentEnvelope with quest-specific fields.
/// </summary>
public record QuestSchema : ContentEnvelope
{
    // Quest-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for dialogue kind.
/// Extended from ContentEnvelope with dialogue-specific fields.
/// </summary>
public record DialogueSchema : ContentEnvelope
{
    // Dialogue-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for faction kind.
/// Extended from ContentEnvelope with faction-specific fields.
/// </summary>
public record FactionSchema : ContentEnvelope
{
    // Faction-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for loot kind.
/// Extended from ContentEnvelope with loot-specific fields.
/// </summary>
public record LootSchema : ContentEnvelope
{
    // Loot-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for merchant kind.
/// Extended from ContentEnvelope with merchant-specific fields.
/// </summary>
public record MerchantSchema : ContentEnvelope
{
    // Merchant-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for affix kind.
/// Extended from ContentEnvelope with affix-specific fields.
/// </summary>
public record AffixSchema : ContentEnvelope
{
    // Affix-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for node kind.
/// Extended from ContentEnvelope with node-specific fields.
/// </summary>
public record NodeSchema : ContentEnvelope
{
    // Node-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for spawn kind.
/// Extended from ContentEnvelope with spawn-specific fields.
/// </summary>
public record SpawnSchema : ContentEnvelope
{
    // Spawn-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for location kind.
/// Extended from ContentEnvelope with location-specific fields.
/// </summary>
public record LocationSchema : ContentEnvelope
{
    // Location-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for config kind.
/// Extended from ContentEnvelope with config-specific fields.
/// </summary>
public record ConfigSchema : ContentEnvelope
{
    // Config-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for species kind.
/// Extended from ContentEnvelope with species-specific fields.
/// </summary>
public record SpeciesSchema : ContentEnvelope
{
    // Species-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for schedule kind.
/// Extended from ContentEnvelope with schedule-specific fields.
/// </summary>
public record ScheduleSchema : ContentEnvelope
{
    // Schedule-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for set kind.
/// Extended from ContentEnvelope with set-specific fields.
/// </summary>
public record SetSchema : ContentEnvelope
{
    // Set-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for region kind.
/// Extended from ContentEnvelope with region-specific fields.
/// </summary>
public record RegionSchema : ContentEnvelope
{
    // Region-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for anchor kind.
/// Extended from ContentEnvelope with anchor-specific fields.
/// </summary>
public record AnchorSchema : ContentEnvelope
{
    // Anchor-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for fact kind.
/// Extended from ContentEnvelope with fact-specific fields.
/// </summary>
public record FactSchema : ContentEnvelope
{
    // Fact-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}

/// <summary>
/// Base schema for world_flag kind.
/// Extended from ContentEnvelope with world flag-specific fields.
/// </summary>
public record WorldFlagSchema : ContentEnvelope
{
    // World flag-specific properties would be added here
    // For now, inherits all from ContentEnvelope
}
