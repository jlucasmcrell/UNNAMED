using UNNAMED.Domain;
using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// The Phase-1 technical audit, L-03 and M-07: a disk that refuses, a file held open elsewhere, a second copy of the game. Each ends in
/// something the game can say - a <see cref="SaveException"/>, a warning, a refusal - and never costs the save already there.
/// </summary>
public class SaveFailureTests : IDisposable
{
    private readonly TempProfile _profile = new();

    public void Dispose() => _profile.Dispose();

    private static SaveDocument Document(long tick) => M2Fixtures.Document(M2Fixtures.OldWorld(new Registry()), tick: tick);

    [Fact]
    public void ASaveThatCannotBeWritten_IsASaveException()
    {
        var store = new SaveStore(_profile.Root);
        Directory.Delete(_profile.Root, recursive: true);
        File.WriteAllText(_profile.Root, "a file where the folder was: nothing can be written under it");
        try
        {
            var error = Assert.Throws<SaveException>(() => store.Save(M2Fixtures.Slot, Document(1)));
            Assert.IsAssignableFrom<IOException>(error.InnerException);
            Assert.Contains("The previous save is untouched", error.Message);
        }
        finally
        {
            File.Delete(_profile.Root);
        }
    }

    [WindowsFact]
    public void ASaveWhosePredecessorIsHeldOpen_IsASaveException_AndThePredecessorStillLoads()
    {
        var store = new SaveStore(_profile.Root);
        store.Save(M2Fixtures.Slot, Document(1));
        using (new FileStream(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Player), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<SaveException>(() => store.Save(M2Fixtures.Slot, Document(2)));

        Assert.Equal(1, store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())).Manifest.WorldTick);
        Assert.Empty(Directory.EnumerateDirectories(_profile.Root, ".staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(_profile.Root, ".trash-*"));
    }

    [Fact]
    public void ALoadWhoseProofCannotBeWritten_StillLoads_WithAWarning()
    {
        var store = new SaveStore(_profile.Root);
        store.Save(M2Fixtures.Slot, Document(1));
        store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));   // rotation.json exists now
        store.Save(M2Fixtures.Slot, Document(2));

        LoadResult loaded;
        using (new FileStream(Path.Combine(_profile.Root, "rotation.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

        Assert.Equal(2, loaded.Manifest.WorldTick);
        Assert.Contains(loaded.Report.Warnings, w => w.Contains("could not be recorded", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(_profile.Root, "rotation.json.tmp-*"));
    }

    [Fact]
    public void ALoadsProof_HeldAMomentByAnIndexer_IsWrittenOnARetry()
    {
        var store = new SaveStore(_profile.Root);
        store.Save(M2Fixtures.Slot, Document(1));
        store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));
        store.Save(M2Fixtures.Slot, Document(2));
        string rotation = Path.Combine(_profile.Root, "rotation.json");
        string before = File.ReadAllText(rotation);

        var held = new FileStream(rotation, FileMode.Open, FileAccess.Read, FileShare.Read);   // readable, not replaceable
        var release = Task.Delay(100).ContinueWith(_ => held.Dispose());
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));
        release.Wait();

        Assert.DoesNotContain(loaded.Report.Warnings, w => w.Contains("could not be recorded", StringComparison.Ordinal));
        Assert.NotEqual(before, File.ReadAllText(rotation));   // the new proof is written
        store.Save(M2Fixtures.Slot, Document(3));
        Assert.Equal(2, store.Load(M2Fixtures.Slot, SaveCopy.Backup1, M2Fixtures.Context(new Registry())).Manifest.WorldTick);
    }

    [Fact]
    public void ASecondProcess_RefusesTheProfile_BeforeItsBootSweepTouchesAnything()
    {
        string staging = Path.Combine(_profile.Root, ".staging-quick-" + EntityId.NewId(EntityKind.WorldEvent).Ulid);
        var held = ProfileLock.Acquire(_profile.Root);
        Directory.CreateDirectory(staging);   // this game's commit, in flight

        var (code, output, _) = Probe.Run("boot", _profile.Root);
        Assert.Equal((3, "in use"), (code, output));
        Assert.True(Directory.Exists(staging));

        held.Dispose();
        (code, output, _) = Probe.Run("boot", _profile.Root);
        Assert.Equal((0, "swept"), (code, output));
        Assert.False(Directory.Exists(staging));   // with no game running, the sweep discards the leftover
    }

    [Fact]
    public void AProfile_IsHeldOnce_AndFreeAgainWhenLetGo()
    {
        using (ProfileLock.Acquire(_profile.Root))
            Assert.Throws<ProfileInUseException>(() => ProfileLock.Acquire(_profile.Root));
        using (ProfileLock.Acquire(_profile.Root))
        {
        }
    }
}

/// <summary>A test of Windows file sharing: POSIX lets a directory be moved while a file in it is open, so elsewhere it is skipped, not passed.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows file-sharing semantics: POSIX moves a directory while a file in it is held open";
    }
}
