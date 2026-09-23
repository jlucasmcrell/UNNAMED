using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// T-13 / PERSISTENCE.md §7.2: "Truncated / corrupted / checksum-mismatched fixtures follow §7.2
/// exactly; never silent success."
/// </summary>
public class IntegrityTests : IDisposable
{
    private readonly TempProfile _profile = new();
    private readonly SaveStore _store;

    public IntegrityTests()
    {
        _store = new SaveStore(_profile.Root);
        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));
    }

    public void Dispose() => _profile.Dispose();

    private string SlotFile(string file) => Path.Combine(_store.SlotPath(M2Fixtures.Slot), file);

    private LoadResult Load() => _store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

    [Fact]
    public void TruncatedPlayer_IsFatal()
    {
        byte[] bytes = File.ReadAllBytes(SlotFile(SaveFormat.Player));
        File.WriteAllBytes(SlotFile(SaveFormat.Player), bytes[..(bytes.Length / 2)]);

        var error = Assert.Throws<SaveCorruptionException>(Load);
        Assert.Contains("player.msgpack", error.Message);
    }

    [Fact]
    public void CorruptPlayer_WithABackupAvailable_StillFails_NeverSilentlyLoadsTheBackup()
    {
        // The regression this milestone fixes: the old loader caught corruption and quietly returned the
        // backup - a fresh copy of the same save - so a corrupted save "loaded successfully".
        _store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));                     // proven good...
        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry())));  // ...so it becomes backup 1
        Assert.Equal(new[] { 1 }, _store.AvailableBackups(M2Fixtures.Slot));

        Tamper.FlipByte(SlotFile(SaveFormat.Player), 10);

        var error = Assert.Throws<SaveCorruptionException>(Load);
        Assert.Equal(new[] { 1 }, error.BackupGenerations);   // offered to the player, not loaded behind their back
    }

    [Fact]
    public void CorruptCells_AreQuarantined_TheRestLoads_AndTheLossIsReported()
    {
        Tamper.FlipByte(SlotFile(SaveFormat.Cells));

        var result = Load();

        Assert.Equal(new[] { "cells" }, result.QuarantinedSections);
        Assert.Equal(new[] { "cells" }, result.Manifest.Flags.QuarantinedSections);
        Assert.False(result.IsComplete);
        Assert.Equal(M2Fixtures.Player().Digest, result.Player.Digest);   // partial recovery beats none
    }

    [Fact]
    public void CorruptEntities_AreQuarantined()
    {
        File.WriteAllBytes(SlotFile(SaveFormat.Entities), new byte[] { 0xC1, 0xC1, 0xC1 });

        var result = Load();
        Assert.Equal(new[] { "entities" }, result.QuarantinedSections);
    }

    [Fact]
    public void CorruptManifest_IsAHardError()
    {
        File.WriteAllText(SlotFile(SaveFormat.Manifest), "{ not json");
        Assert.Throws<SaveCorruptionException>(Load);
    }

    [Fact]
    public void ManifestFailingItsHash_IsAHardError()
    {
        string manifest = File.ReadAllText(SlotFile(SaveFormat.Manifest));
        File.WriteAllText(SlotFile(SaveFormat.Manifest), manifest.Replace("\"playtime_seconds\": 321.5", "\"playtime_seconds\": 999"));

        var error = Assert.Throws<SaveCorruptionException>(Load);
        Assert.Contains("integrity", error.Message);
    }

    [Fact]
    public void UnknownSaveFormat_IsRefused()
    {
        string manifest = File.ReadAllText(SlotFile(SaveFormat.Manifest));
        File.WriteAllText(SlotFile(SaveFormat.Manifest), manifest.Replace("\"save_format\": 1", "\"save_format\": 2"));

        Assert.Throws<SaveFormatMismatchException>(Load);
    }

    [Fact]
    public void UnreadableIntegrityRoot_IsRederived_AndReported()
    {
        File.WriteAllText(SlotFile(SaveFormat.IntegrityRoot), "garbage");

        var result = Load();
        Assert.True(result.IntegrityRootRederived);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Manifest_HasNoSelfChecksum_AndTheIntegrityRootCoversIt()
    {
        // §4.2: "No checksum of the manifest itself ... a self-referential checksum is a trap."
        string manifest = File.ReadAllText(SlotFile(SaveFormat.Manifest));
        Assert.DoesNotContain("checksum", manifest);

        string root = File.ReadAllText(SlotFile(SaveFormat.IntegrityRoot));
        foreach (string file in SaveFormat.CheckedFiles)
            Assert.Contains("  " + file + "\n", root);
    }

    [Fact]
    public void IntegrityRoot_IsSha256sumCompatible()
    {
        string line = File.ReadAllLines(SlotFile(SaveFormat.IntegrityRoot)).Single(l => l.EndsWith(SaveFormat.Player));
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SlotFile(SaveFormat.Player)))).ToLowerInvariant();
        Assert.Equal($"{expected}  {SaveFormat.Player}", line);
    }
}
