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

    /// <summary>The Phase-1 technical audit, L-05: a slot's pre-migration copies are its own, not every slot whose name ends the same.</summary>
    [Fact]
    public void DeletingASlot_LeavesAnotherSlotsPreMigrationCopies()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        string other = SaveSlots.Manual("x_quick");
        store.Save(SaveSlots.Quick, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 1));
        store.Save(other, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 2));
        Directory.CreateDirectory(store.PreMigrationPath(SaveSlots.Quick, 13));
        Directory.CreateDirectory(store.PreMigrationPath(other, 13));

        Assert.Equal(new[] { store.PreMigrationPath(SaveSlots.Quick, 13) }, store.PreMigrationBackups(SaveSlots.Quick));
        store.Delete(SaveSlots.Quick);

        Assert.False(Directory.Exists(store.PreMigrationPath(SaveSlots.Quick, 13)));
        Assert.True(Directory.Exists(store.PreMigrationPath(other, 13)));
        Assert.Equal(new[] { store.PreMigrationPath(other, 13) }, store.PreMigrationBackups(other));
        Assert.Equal(2, store.Load(other, M2Fixtures.Context(new Registry())).Manifest.WorldTick);
    }

    [Fact]
    public void Autosaves_RollThroughFiveSlots_OverwritingTheOldest()
    {
        using var profile = new TempProfile();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new SaveStore(profile.Root, clock: () => now);

        var written = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            string slot = store.NextAutosaveSlot();
            store.Save(slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: i));
            written.Add(slot);
            now = now.AddMinutes(5);
        }

        Assert.Equal(new[] { "auto_01", "auto_02", "auto_03", "auto_04", "auto_05", "auto_01", "auto_02" }, written);
    }

    [Fact]
    public void AnUnreadableAutosave_IsReplacedFirst()
    {
        using var profile = new TempProfile();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new SaveStore(profile.Root, clock: () => now);
        for (int i = 1; i <= SaveSlots.AutosaveCount; i++)
        {
            store.Save(SaveSlots.Auto(i), M2Fixtures.Document(M2Fixtures.OldWorld(new Registry())));
            now = now.AddMinutes(5);
        }

        File.WriteAllText(Path.Combine(store.SlotPath("auto_04"), SaveFormat.Manifest), "{");

        Assert.Equal("auto_04", store.NextAutosaveSlot());
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(299.9, 0, false)]
    [InlineData(300, 0, true)]
    [InlineData(1_000, 800, false)]
    [InlineData(1_100, 800, true)]
    public void Autosave_IsDueEveryFiveMinutesOfPlaytime(double playtime, double lastAutosave, bool due) =>
        Assert.Equal(due, AutosaveCadence.IsDue(playtime, lastAutosave));

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
