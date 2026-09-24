using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// The boot sweep's individual paths (PERSISTENCE.md §7.1). The crash here is an exception thrown at the
/// step, which leaves the same directories behind as a kill; ExitCriteriaTests does it with a real kill.
/// </summary>
public class RecoveryTests : IDisposable
{
    private readonly TempProfile _profile = new();
    private readonly SaveStore _store;

    public RecoveryTests() => _store = new SaveStore(_profile.Root);

    public void Dispose() => _profile.Dispose();

    private sealed class Crash : Exception { }

    private void Save(long tick) =>
        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: tick));

    private void SaveCrashingAt(SaveStep step, long tick)
    {
        var crashing = new SaveStore(_profile.Root, onStep: s =>
        {
            if (s == step)
                throw new Crash();
        });
        Assert.Throws<Crash>(() =>
            crashing.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: tick)));
    }

    private LoadResult Load() => _store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

    [Fact]
    public void NothingInterrupted_NothingDone()
    {
        Save(1);
        Assert.Empty(_store.RecoverInterruptedCommits());
    }

    [Fact]
    public void AnIncompleteStaging_IsDiscarded_AndTheSlotIsUntouched()
    {
        Save(1);
        SaveCrashingAt(SaveStep.StagingWritten, 2);

        var actions = _store.RecoverInterruptedCommits();

        Assert.Contains(actions, a => a.StartsWith("quick: discarded .staging-quick-", StringComparison.Ordinal));
        Assert.Equal(1, Load().Manifest.WorldTick);
    }

    [Fact]
    public void KilledBetweenTrashAndPromote_PromotesTheNewSave_AndStillRotatesTheProvenOne()
    {
        Save(1);
        Load();   // proven good
        SaveCrashingAt(SaveStep.PreviousMovedToTrash, 2);
        Assert.False(Directory.Exists(_store.SlotPath(M2Fixtures.Slot)));   // §7.1: no slot until the sweep runs

        var actions = _store.RecoverInterruptedCommits();

        Assert.Contains("quick: promoted the complete new save", actions);
        Assert.Equal(2, Load().Manifest.WorldTick);
        Assert.Equal(new[] { 1 }, _store.AvailableBackups(M2Fixtures.Slot));
    }

    [Fact]
    public void KilledAfterVerification_CompletesTheCommit()
    {
        Save(1);
        Load();
        SaveCrashingAt(SaveStep.Verified, 2);

        var actions = _store.RecoverInterruptedCommits();

        Assert.Contains("quick: completed an interrupted commit", actions);
        Assert.Equal(2, Load().Manifest.WorldTick);
        Assert.Equal(new[] { 1 }, _store.AvailableBackups(M2Fixtures.Slot));
    }

    [Fact]
    public void AnUnverifiablePromotedSave_IsRolledBack()
    {
        Save(1);
        SaveCrashingAt(SaveStep.StagingPromoted, 2);
        Tamper.FlipByte(Path.Combine(_store.SlotPath(M2Fixtures.Slot), SaveFormat.Cells));

        var actions = _store.RecoverInterruptedCommits();

        Assert.Contains("quick: rolled back an unverifiable commit", actions);
        var loaded = Load();
        Assert.Equal(1, loaded.Manifest.WorldTick);
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void NoCompleteNewSave_RestoresThePreviousOne()
    {
        Save(1);
        SaveCrashingAt(SaveStep.PreviousMovedToTrash, 2);
        // Damage the staged save too, so there is no complete new save to promote.
        string staging = Directory.EnumerateDirectories(_profile.Root, ".staging-*").Single();
        Tamper.FlipByte(Path.Combine(staging, SaveFormat.Player));

        var actions = _store.RecoverInterruptedCommits();

        Assert.Contains("quick: restored the previous save", actions);
        Assert.Equal(1, Load().Manifest.WorldTick);
        Assert.Empty(Directory.EnumerateDirectories(_profile.Root, ".staging-*"));
    }
}
