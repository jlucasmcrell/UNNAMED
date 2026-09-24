using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// PERSISTENCE.md §7.1 steps 6-7 and §7.3: "Two backup generations, retired on a verified load, not a
/// verified write." Saves are told apart by their world tick.
/// </summary>
public class RotationTests : IDisposable
{
    private readonly TempProfile _profile = new();
    private readonly SaveStore _store;

    public RotationTests() => _store = new SaveStore(_profile.Root);

    public void Dispose() => _profile.Dispose();

    private void Save(long tick) =>
        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: tick));

    private long LoadTick() => _store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())).Manifest.WorldTick;

    private long BackupTick(int generation) =>
        _store.LoadBackup(M2Fixtures.Slot, generation, M2Fixtures.Context(new Registry())).Manifest.WorldTick;

    [Fact]
    public void AnUnloadedSave_NeverBecomesABackup()
    {
        Save(1);
        Save(2);

        Assert.Empty(_store.AvailableBackups(M2Fixtures.Slot));
        Assert.Equal(2, LoadTick());
    }

    [Fact]
    public void ASaveProvedByALoad_BecomesBackupOne_WhenDisplaced()
    {
        Save(1);
        Assert.Equal(1, LoadTick());
        Save(2);

        Assert.Equal(new[] { 1 }, _store.AvailableBackups(M2Fixtures.Slot));
        Assert.Equal(1, BackupTick(1));
        Assert.Equal(2, LoadTick());
    }

    [Fact]
    public void TwoGenerations_TheOldestDropsOff()
    {
        for (long tick = 1; tick <= 4; tick++)
        {
            Save(tick);
            Assert.Equal(tick, LoadTick());
        }

        Assert.Equal(new[] { 1, 2 }, _store.AvailableBackups(M2Fixtures.Slot));
        Assert.Equal(3, BackupTick(1));
        Assert.Equal(2, BackupTick(2));
    }

    [Fact]
    public void SavesAfterASilentProblem_NeverPushTheLastGoodSaveOut()
    {
        // §7.3: "a player who saves three times after a silent problem has three bad saves and one good
        // backup that the next write discards" - the failure this rule exists to prevent.
        Save(1);
        LoadTick();
        Save(2);
        Save(3);
        Save(4);

        Assert.Equal(new[] { 1 }, _store.AvailableBackups(M2Fixtures.Slot));
        Assert.Equal(1, BackupTick(1));
    }

    [Fact]
    public void APartialLoad_DoesNotProveTheSave()
    {
        Save(1);
        Tamper.FlipByte(Path.Combine(_store.SlotPath(M2Fixtures.Slot), SaveFormat.Cells));
        Assert.False(_store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())).IsComplete);
        Save(2);

        Assert.Empty(_store.AvailableBackups(M2Fixtures.Slot));
    }

    [Fact]
    public void ACommitThatFailsVerification_RestoresThePreviousSave()
    {
        Save(1);
        var damaging = new SaveStore(_profile.Root, onStep: step =>
        {
            if (step == SaveStep.StagingPromoted)
                Tamper.FlipByte(Path.Combine(_store.SlotPath(M2Fixtures.Slot), SaveFormat.Player));
        });

        var error = Assert.Throws<SaveException>(() =>
            damaging.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: 2)));

        Assert.Contains("previous save was restored", error.Message);
        Assert.Equal(1, LoadTick());
        Assert.Empty(Directory.EnumerateDirectories(_profile.Root, ".trash-*"));
    }

    [Fact]
    public void Delete_RemovesTheSlot_ItsBackups_AndItsProof()
    {
        Save(1);
        LoadTick();
        Save(2);
        Assert.NotEmpty(_store.AvailableBackups(M2Fixtures.Slot));

        _store.Delete(M2Fixtures.Slot);

        Assert.False(Directory.Exists(_store.SlotPath(M2Fixtures.Slot)));
        Assert.Empty(_store.AvailableBackups(M2Fixtures.Slot));
        Assert.DoesNotContain(M2Fixtures.Slot, File.ReadAllText(Path.Combine(_profile.Root, "rotation.json")));
    }
}
