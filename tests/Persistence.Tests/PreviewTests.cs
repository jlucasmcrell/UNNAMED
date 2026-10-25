using UNNAMED.M2Probe;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// PERSISTENCE.md §3.2 and PJ10 §4.3 (SYS-114): a save carries <c>preview.json</c> - name, level, place, quest - staged and committed with
/// it, and may gain <c>preview.png</c> once committed. Neither is authoritative: the integrity root never covers them, a load never reads
/// them, and a save whose preview is broken or missing still lists and loads whole.
/// </summary>
public class PreviewTests
{
    private static readonly SavePreview Wenna = new("Wenna Lind", 3, "the Starting Frontier", "Iron Under Ash");

    [Fact]
    public void ThePreview_IsCommittedWithTheSave_ListedWithIt_AndOutsideTheIntegrityRoot()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())) with { Preview = Wenna });
        string slot = store.SlotPath(M2Fixtures.Slot);

        Assert.True(File.Exists(Path.Combine(slot, SaveFormat.Preview)));
        Assert.Equal(Wenna, store.Summaries().Single().Preview);
        Assert.DoesNotContain(SaveFormat.Preview, File.ReadAllText(Path.Combine(slot, SaveFormat.IntegrityRoot)));
        Assert.True(store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())).IsComplete);
    }

    [Fact]
    public void ABrokenOrMissingPreview_CostsTheSaveNothing()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())) with { Preview = Wenna });
        File.WriteAllText(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Preview), "{ not json");

        var listed = store.Summaries().Single();
        Assert.Null(listed.Preview);
        Assert.Null(listed.Problem);
        Assert.True(store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry())).IsComplete);

        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));   // a save with no preview writes none
        Assert.False(File.Exists(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Preview)));
        Assert.Null(store.Summaries().Single().Preview);
    }

    [Fact]
    public void APicture_IsAttachedToTheCommittedSave_AndGoesWithItIntoTheBackups()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        Assert.False(store.AttachThumbnail(M2Fixtures.Slot, new byte[] { 1 }));   // no save to put it beside
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())) with { Preview = Wenna });

        Assert.True(store.AttachThumbnail(M2Fixtures.Slot, new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        var listed = store.Summaries().Single();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(listed.ThumbnailPath!));
        Assert.Null(SaveStore.VerifyIntegrity(store.SlotPath(M2Fixtures.Slot)));
        store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));   // proven: the next save keeps this one as backup 1
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.NewWorld(new Registry())));
        Assert.Null(store.Summaries().Single().ThumbnailPath);   // the new save has none until one is attached
        Assert.NotNull(Assert.Single(store.OtherCopies(M2Fixtures.Slot)).ThumbnailPath);
    }
}
