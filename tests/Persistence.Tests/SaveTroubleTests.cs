using UNNAMED.Domain;
using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// PJ10 §6.3 and §6.3.1 (SYS-134, SYS-148): a save that could not be written or loaded is said in a player's words - disk full, a file
/// held, a folder the game may not write, a damaged or refused or newer save - each with what to do, and with a report to copy. The kind
/// is judged by error code down the exception chain, never guessed from the words; anything else is "Other", its detail in the report.
/// </summary>
public class SaveTroubleTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070070), SaveTroubleKind.DiskFull)]   // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027), SaveTroubleKind.DiskFull)]   // ERROR_HANDLE_DISK_FULL
    [InlineData(28, SaveTroubleKind.DiskFull)]                            // ENOSPC
    [InlineData(122, SaveTroubleKind.DiskFull)]                           // EDQUOT
    [InlineData(unchecked((int)0x80070020), SaveTroubleKind.InUse)]      // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021), SaveTroubleKind.InUse)]      // ERROR_LOCK_VIOLATION
    [InlineData(16, SaveTroubleKind.InUse)]                               // EBUSY
    [InlineData(unchecked((int)0x80070005), SaveTroubleKind.Other)]      // an IO code this build does not name: not guessed
    public void AnIoErrorCode_DownTheChain_NamesTheKind(int code, SaveTroubleKind kind)
    {
        var wrapped = new SaveException("Save to 'quick' failed: whatever the OS said. The previous save is untouched.", new IOException("the OS's words", code));

        Assert.Equal(kind, SaveTrouble.KindOf(wrapped));
        Assert.Equal(kind, SaveTrouble.OfSave(wrapped).Kind);
    }

    [Fact]
    public void AFolderTheGameMayNotWrite_IsSaid_AndEveryWriteFailureSaysTheLastSaveIsUntouched()
    {
        var denied = SaveTrouble.OfSave(new SaveException("Save to 'quick' failed", new UnauthorizedAccessException("Access to the path is denied")));
        Assert.Equal(SaveTroubleKind.NotAllowed, denied.Kind);
        Assert.Equal("Not saved: the game may not write to its saves folder.", denied.Headline);

        foreach (int code in new[] { unchecked((int)0x80070070), unchecked((int)0x80070020), 5 })
            Assert.Contains("your last save is untouched", SaveTrouble.OfSave(new IOException("x", code)).Advice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not saved: the disk is full.", SaveTrouble.OfSave(new IOException("x", 28)).Headline);
    }

    [Fact]
    public void ADamagedSave_AndOneThatCannotBeRead_AreToldApart_WhenTheyRefuseToLoad()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));
        string player = Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Player);
        byte[] bytes = File.ReadAllBytes(player);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(player, bytes);

        var damaged = SaveTrouble.OfLoad(Assert.Throws<SaveCorruptionException>(() => store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()))));
        Assert.Equal((SaveTroubleKind.Damaged, "This save is damaged."), (damaged.Kind, damaged.Headline));
        // The list says the same of it without loading it.
        var listed = store.Summaries().Single();
        Assert.Equal(SaveTroubleKind.Damaged, SaveTrouble.OfProblem(listed.Problem!).Kind);

        File.Delete(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Manifest));
        var unreadable = SaveTrouble.OfLoad(Assert.Throws<SaveCorruptionException>(() => store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()))));
        Assert.Equal(SaveTroubleKind.Unreadable, unreadable.Kind);
        Assert.Equal(SaveTroubleKind.Unreadable, SaveTrouble.OfProblem(store.Summaries().Single().Problem!).Kind);
    }

    [Fact]
    public void ASaveFromANewerBuild_IsSaidAsOne_InTheListAndOnLoad()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));
        string manifest = Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Manifest);
        string text = File.ReadAllText(manifest).Replace($"\"schema_version\": {SaveFormat.SchemaVersion},", $"\"schema_version\": {SaveFormat.SchemaVersion + 1},");
        File.WriteAllText(manifest, text);
        // Re-sign it, as the newer build would have.
        File.WriteAllText(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.IntegrityRoot), string.Concat(SaveFormat.CheckedFiles.Select(file =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(store.SlotPath(M2Fixtures.Slot), file)))).ToLowerInvariant() + "  " + file + "\n")));

        Assert.Equal(SaveTroubleKind.NewerBuild, SaveTrouble.OfProblem(store.Summaries().Single().Problem!).Kind);
        var refused = Assert.ThrowsAny<SaveException>(() => store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())));
        var trouble = SaveTrouble.OfLoad(refused);
        Assert.Equal(SaveTroubleKind.NewerBuild, trouble.Kind);
        Assert.Contains("newer version", trouble.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedSave_IsSaidAsRefused_AndNothingInItChanged()
    {
        var report = new MigrationReport("quick");
        report.Blockers.Add("unresolved definition ID 'item.gone' (player inventory)");
        var trouble = SaveTrouble.OfLoad(new SaveCompatibilityException(report));

        Assert.Equal(SaveTroubleKind.Refused, trouble.Kind);
        Assert.Contains("Nothing in it was changed", trouble.Advice, StringComparison.Ordinal);
        Assert.Contains("item.gone", trouble.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReport_NamesTheSave_TheProblem_TheFolder_TheBuild_AndEveryMessageDownTheChain()
    {
        var trouble = SaveTrouble.OfSave(new SaveException("Save to 'auto_02' failed: No space left on device", new IOException("No space left on device", 28)));

        string report = trouble.Report("Autosave 2", "/saves/characters/chr_X", "content 0.9.0 (sha256:abc)");

        Assert.Equal(new[]
        {
            "Otherreach save report",
            "Save: Autosave 2",
            "Problem: Not saved: the disk is full. (disk_full)",
            "Saves folder: /saves/characters/chr_X",
            "Build: content 0.9.0 (sha256:abc)",
            "Detail:",
            "SaveException (0x80131500): Save to 'auto_02' failed: No space left on device",
            "IOException (0x0000001C): No space left on device",
        }, report.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void ACharacter_IsDeletedAtomically_AndADeletionCutShort_IsFinishedByTheSweep_NeverListed()
    {
        using var profile = new TempProfile();
        var keep = EntityId.NewId(EntityKind.Character);
        var gone = EntityId.NewId(EntityKind.Character);
        foreach (var id in new[] { keep, gone })
            new SaveStore(CharacterFolders.PathOf(profile.Root, id)).Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));

        Assert.True(CharacterFolders.Delete(profile.Root, gone));
        Assert.Equal(new[] { keep }, CharacterFolders.List(profile.Root));
        Assert.False(Directory.Exists(CharacterFolders.PathOf(profile.Root, gone)));
        Assert.False(CharacterFolders.Delete(profile.Root, gone));   // nothing left to delete

        // A removal a crash cut short: the folder was renamed out of the list, its files still there.
        string leftover = Path.Combine(profile.Root, CharacterFolders.Directory, ".deleted-" + gone.Value + "-01J00000000000000000000000");
        Directory.CreateDirectory(Path.Combine(leftover, "quick"));
        File.WriteAllText(Path.Combine(leftover, "quick", SaveFormat.Manifest), "{}");
        Assert.Equal(new[] { keep }, CharacterFolders.List(profile.Root));
        Assert.Empty(CharacterFolders.SweepDeleted(profile.Root));
        Assert.False(Directory.Exists(leftover));
        Assert.Single(new SaveStore(CharacterFolders.PathOf(profile.Root, keep)).Summaries());
    }
}
