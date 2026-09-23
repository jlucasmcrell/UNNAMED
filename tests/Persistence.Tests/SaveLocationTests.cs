namespace UNNAMED.Persistence.Tests;

/// <summary>PERSISTENCE.md §3.2 / RK-P06: a save root inside a cloud-sync folder is detected, so the game can warn.</summary>
public class SaveLocationTests
{
    private static readonly string Sync = Path.Combine(Path.GetTempPath(), "SyncRoot");

    [Fact]
    public void APathInsideASyncRoot_IsDetected() =>
        Assert.Equal(Sync, SaveLocation.CloudSyncRootOf(Path.Combine(Sync, "Game", "saves", "default"), new[] { Sync }));

    [Fact]
    public void TheSyncRootItself_IsDetected() =>
        Assert.Equal(Sync, SaveLocation.CloudSyncRootOf(Sync, new[] { Sync }));

    [Fact]
    public void ASiblingSharingANamePrefix_IsNotInside() =>
        Assert.Null(SaveLocation.CloudSyncRootOf(Sync + "Backup", new[] { Sync }));

    [Fact]
    public void WithNoSyncRoots_NothingIsDetected() =>
        Assert.Null(SaveLocation.CloudSyncRootOf(Path.Combine(Sync, "Game"), Array.Empty<string>()));
}
