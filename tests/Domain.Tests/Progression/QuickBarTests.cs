using System.Collections.Immutable;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>The quick bar's rules (WP9): eight slots, each empty or holding one item, formula or technique, nothing twice.</summary>
public class QuickBarTests
{
    private const string Salve = "item.consumable.salve_minor", Bolt = "spell.force.impulse_bolt";

    [Fact]
    public void Assign_SwapsWithTheSlotItCameFrom_AndNullClears()
    {
        var bar = QuickBar.Assign(QuickBar.Empty, 0, Salve);
        bar = QuickBar.Assign(bar, 3, Bolt);

        Assert.Equal(new string?[] { Bolt, null, null, Salve, null, null, null, null, null, null, null, null }, QuickBar.Assign(bar, 0, Bolt));
        Assert.Equal(new string?[] { Salve, null, null, null, null, null, null, null, null, null, null, null }, QuickBar.Assign(bar, 3, null));
        Assert.Equal(bar, QuickBar.Assign(bar, 3, Bolt));
    }

    [Fact]
    public void FreeSlot_FollowsItsOrder_AndAFullBarHasNone()
    {
        var bar = QuickBar.Assign(QuickBar.Empty, 3, Bolt);
        Assert.Equal(4, QuickBar.FreeSlot(bar, QuickBar.WorkingOrder));
        Assert.Equal(0, QuickBar.FreeSlot(bar, QuickBar.ItemOrder));

        var full = Enumerable.Range(0, QuickBar.Slots).Aggregate(QuickBar.Empty, (b, i) => QuickBar.Assign(b, i, $"item.consumable.thing_{i}"));
        Assert.Equal(-1, QuickBar.FreeSlot(full, QuickBar.WorkingOrder));
        Assert.True(QuickBar.IsValid(full));
    }

    [Fact]
    public void IsValid_RefusesAShortBar_AThingTwice_AndWhatNoSlotHolds()
    {
        Assert.False(QuickBar.IsValid(ImmutableArray.Create<string?>(Salve)));
        Assert.False(QuickBar.IsValid(QuickBar.Empty.SetItem(0, Salve).SetItem(1, Salve)));
        Assert.False(QuickBar.IsValid(QuickBar.Empty.SetItem(0, "recipe.smithing.iron_billet")));
        Assert.True(QuickBar.CanHold("ability.blade.riposte"));
    }
}

/// <summary>Action bars and gear sets: twelve slots, a gear-set switch is a slot's content, and the set rules.</summary>
public class GearSetRulesTests
{
    private static EntityId Item(int n) => EntityId.Create(EntityKind.Item, 1_700_000_000_000 + n, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, (byte)n });

    [Fact]
    public void ABarIsTwelveSlots_AndAGearSetSwitchIsOneOfTenNamedPlaces()
    {
        Assert.Equal(12, QuickBar.Slots);
        Assert.Equal(6, QuickBar.Bars);
        Assert.True(QuickBar.CanHold(QuickBar.GearSetId(10)));
        Assert.False(QuickBar.CanHold("gearset.set_11"));
        Assert.False(QuickBar.CanHold("gearset.set_01"));
        Assert.True(QuickBar.TryGearSet("gearset.set_3", out int set) && set == 3);
    }

    [Fact]
    public void Names_AreCleaned_AndSetsAreValidByPlace()
    {
        Assert.Equal("Blade and board", GearSetRules.CleanName("  Blade   and board "));
        Assert.Null(GearSetRules.CleanName(""));
        Assert.Null(GearSetRules.CleanName(new string('x', 25)));
        Assert.Null(GearSetRules.CleanName("a	b"));
        var one = new GearSet(1, "A", ImmutableArray.Create(new GearSetItem(EquipSlot.Head, Item(1), "item.armor.cap")));
        Assert.True(GearSetRules.IsValid(ImmutableArray.Create(one)));
        Assert.False(GearSetRules.IsValid(ImmutableArray.Create(one, one)));
        Assert.False(GearSetRules.IsValid(ImmutableArray.Create(one with { Index = 11 })));
    }

    [Fact]
    public void Plan_TakesOffWhatTheSetLeavesBare_PutsOnWhatIsCarried_AndNamesWhatIsNot()
    {
        var sword = new GearSetItem(EquipSlot.MainHand, Item(1), "item.weapon.sword");
        var cap = new GearSetItem(EquipSlot.Head, Item(2), "item.armor.cap");
        var set = new GearSet(1, "A", ImmutableArray.Create(sword, cap));
        var worn = new Dictionary<EquipSlot, EntityId> { [EquipSlot.MainHand] = Item(9), [EquipSlot.Chest] = Item(3) };
        var plan = GearSetRules.Plan(set, worn, id => id == sword.Item);
        Assert.Equal(new[] { EquipSlot.Chest }, plan.Unequip);
        Assert.Equal(new[] { sword }, plan.Equip);
        Assert.Equal(new[] { cap }, plan.Missing);
        Assert.False(GearSetRules.Plan(set, new Dictionary<EquipSlot, EntityId> { [EquipSlot.MainHand] = sword.Item, [EquipSlot.Head] = cap.Item }, _ => true).Changes);
    }
}
