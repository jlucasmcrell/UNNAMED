using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>Slot names and layout (PERSISTENCE.md §3.2, §8.2).</summary>
public class SlotTests
{
    [Fact]
    public void SlotNames_FollowTheLayout()
    {
        Assert.Equal("quick", SaveSlots.Quick);
        Assert.Equal("manual_my_base", SaveSlots.Manual("my_base"));
        Assert.Equal("auto_01", SaveSlots.Auto(1));
        Assert.Equal("auto_05", SaveSlots.Auto(SaveSlots.AutosaveCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlots.Auto(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlots.Auto(6));
        Assert.Throws<ArgumentException>(() => SaveSlots.Manual("My Base"));
        Assert.Throws<ArgumentException>(() => SaveSlots.Manual(""));
    }

    [Theory]
    [InlineData("quick", true)]
    [InlineData("manual_a1", true)]
    [InlineData("auto_03", true)]
    [InlineData("auto_06", false)]
    [InlineData("manual_", false)]
    [InlineData("Quick", false)]
    [InlineData("../quick", false)]
    [InlineData(".bak-quick.1", false)]
    [InlineData(".staging-quick-01ARZ3NDEKTSV4RRFFQ69G5FAV", false)]
    public void IsValid(string slot, bool valid) => Assert.Equal(valid, SaveSlots.IsValid(slot));

    [Fact]
    public void Slots_AreIndependent_AndListedWithoutInternals()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Quick, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 1));
        store.Save(SaveSlots.Manual("camp"), M2Fixtures.Document(M2Fixtures.NewWorld(new Registry()), tick: 2));
        store.Save(SaveSlots.Auto(1), M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 3));
        store.Load(SaveSlots.Quick, M2Fixtures.Context(new Registry()));
        store.Save(SaveSlots.Quick, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 4));   // quick now has a backup

        Assert.Equal(new[] { "auto_01", "manual_camp", "quick" }, store.ListSlots());
        Assert.Equal(2, store.Load(SaveSlots.Manual("camp"), M2Fixtures.Context(new Registry())).Manifest.WorldTick);
        Assert.Equal(4, store.Load(SaveSlots.Quick, M2Fixtures.Context(new Registry())).Manifest.WorldTick);
        Assert.Empty(store.AvailableBackups(SaveSlots.Manual("camp")));
    }

    [Fact]
    public void AnInvalidOrEmptySlot_IsRefused()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);

        Assert.Throws<ArgumentException>(() => store.Save("../escape", M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()))));
        Assert.Throws<ArgumentException>(() => store.Load("nope", M2Fixtures.Context(new Registry())));
        var missing = Assert.Throws<SaveException>(() => store.Load(SaveSlots.Quick, M2Fixtures.Context(new Registry())));
        Assert.Contains("no save", missing.Message);
    }
}
