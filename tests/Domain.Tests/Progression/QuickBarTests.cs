using System.Collections.Immutable;
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

        Assert.Equal(new string?[] { Bolt, null, null, Salve, null, null, null, null }, QuickBar.Assign(bar, 0, Bolt));
        Assert.Equal(new string?[] { Salve, null, null, null, null, null, null, null }, QuickBar.Assign(bar, 3, null));
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
