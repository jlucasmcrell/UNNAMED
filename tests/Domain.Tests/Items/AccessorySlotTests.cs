using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>
/// GEN-09's accessory slots: a ring, a belt and a shoulder piece are worn slots like any other, each guards the body region it sits against,
/// and what they guard is summed with the rest of the armour (COMBAT §11).
/// </summary>
public class AccessorySlotTests
{
    private static ItemDefinition Piece(EquipSlot slot, int armor) =>
        new($"item.armor.test_{EquipSlots.Key(slot)}", "armor", 1, 500, 20, ItemClasses.Rarity(ItemClass.Ordinary), false, false, slot,
            Requirements.None, null, armor);

    [Theory]
    [InlineData(EquipSlot.Ring, "ring")]
    [InlineData(EquipSlot.Belt, "belt")]
    [InlineData(EquipSlot.Shoulders, "shoulders")]
    [InlineData(EquipSlot.Amulet, "amulet")]
    public void AnAccessorySlot_WritesAndReadsBackItsOwnKey(EquipSlot slot, string key)
    {
        Assert.Equal(key, EquipSlots.Key(slot));
        Assert.Equal(slot, EquipSlots.Parse(key));
    }

    [Fact]
    public void TheAccessories_GuardTheRegionTheySitAgainst()
    {
        Assert.Equal(BodyRegion.Torso, EquipSlots.Covers(EquipSlot.Shoulders));
        Assert.Equal(BodyRegion.Torso, EquipSlots.Covers(EquipSlot.Belt));
        Assert.Equal(BodyRegion.Limbs, EquipSlots.Covers(EquipSlot.Ring));
        // A hand that holds a weapon guards nothing, and a piece worn nowhere guards nothing.
        Assert.Null(EquipSlots.Covers(EquipSlot.MainHand));
    }

    [Fact]
    public void EveryWornSlot_CoversARegion_AndOnlyTheMainHandDoesNot()
    {
        foreach (var slot in Enum.GetValues<EquipSlot>())
            Assert.Equal(slot == EquipSlot.MainHand, EquipSlots.Covers(slot) is null);
    }

    [Fact]
    public void TheArmourOfAnAccessory_IsSummedOnItsRegion_WithItsQuality()
    {
        var worn = new[]
        {
            (EquipSlot.Chest, Piece(EquipSlot.Chest, 6), 0),
            (EquipSlot.Shoulders, Piece(EquipSlot.Shoulders, 3), 1),     // a fine shoulder piece: a step of quality
            (EquipSlot.Belt, Piece(EquipSlot.Belt, 2), 0),
            (EquipSlot.Ring, Piece(EquipSlot.Ring, 1), 0),
        };
        var armor = EquipmentRules.ArmorByRegion(worn, armorPerStep: 1);
        Assert.Equal(6 + 3 + 1 + 2, armor[BodyRegion.Torso]);
        Assert.Equal(1, armor[BodyRegion.Limbs]);
        Assert.Equal(0, armor[BodyRegion.Head]);
    }

    [Fact]
    public void AWornAccessory_OccupiesOnlyItsOwnSlot()
    {
        Assert.Equal(new[] { EquipSlot.Belt }, EquipmentRules.Occupies(Piece(EquipSlot.Belt, 1)));
        Assert.Equal(new[] { EquipSlot.Shoulders }, EquipmentRules.Occupies(Piece(EquipSlot.Shoulders, 1)));
    }

    [Fact]
    public void AnEmptyEquipment_WearsNoArmourAnywhere() =>
        Assert.All(EquipmentRules.ArmorByRegion(ImmutableArray<(EquipSlot, ItemDefinition, int)>.Empty, 1).Values, armor => Assert.Equal(0, armor));
}
