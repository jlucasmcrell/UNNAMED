using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// ROADMAP.md M2 exit criteria, one test each, plus the determinism risk spike. docs/M2_STATUS.md
/// cites these tests as the evidence.
/// </summary>
public class ExitCriteriaTests
{
    /// <summary>
    /// "Save in a 10-cell world with 2 changed cells and verify the save contains only the deltas;
    /// load it and verify world equality."
    /// </summary>
    [Fact]
    public void ME1_ME2_TenCellWorld_SavesOnlyTheTwoChangedCells_AndLoadsEqual()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        var world = M2Fixtures.OldWorld(new Registry());   // cells 0 and 3 changed; 1,2,4-9 untouched
        foreach (var cell in M2Fixtures.TenCells)
            _ = world.Baseline(cell);                        // every cell generated: none is a divergence

        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));

        // Only the deltas: exactly the two changed cells, no entities.
        byte[] cells = File.ReadAllBytes(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Cells));
        var records = SectionCodec.DecodeCells(cells);
        Assert.Equal(new[] { "r_0_0:c_00_00", "r_0_0:c_00_03" }, records.Select(r => r.CellKey));
        Assert.Empty(SectionCodec.DecodeEntities(File.ReadAllBytes(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Entities))));

        // World equality on all ten cells, and the player unchanged.
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));
        Assert.True(loaded.IsComplete);
        Assert.Equal(M2Fixtures.WorldDigest(world), M2Fixtures.WorldDigest(loaded.World));
        Assert.Equal(M2Fixtures.Player().Digest, loaded.Player.Digest);
    }

    /// <summary>"Verify (seed, content_version) determinism across two fresh processes."</summary>
    [Fact]
    public void ME3_Determinism_AcrossTwoFreshProcesses()
    {
        string seed = WorldSeed.Format(M2Fixtures.Seed);
        var first = Probe.Run("digest", seed, "5");
        var second = Probe.Run("digest", seed, "5");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.StartsWith("sha256:", first.Output);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(RegionDigest.Compute(M2Fixtures.Generator(), M2Fixtures.Seed, new RegionKey(0, 0)), first.Output);
    }

    /// <summary>
    /// "Verify atomic write survives a simulated kill during write" (and T-14: "kill between every step
    /// of §7.1; every resulting slot is either old-complete or new-complete"). The kill is real: the
    /// probe process terminates itself at the step, with no cleanup code running.
    /// </summary>
    [Theory]
    [InlineData(SaveStep.StagingWritten)]
    [InlineData(SaveStep.IntegrityRootWritten)]
    [InlineData(SaveStep.PreviousMovedToTrash)]
    [InlineData(SaveStep.StagingPromoted)]
    [InlineData(SaveStep.Verified)]
    [InlineData(SaveStep.Rotated)]
    public void ME4_AtomicWrite_SurvivesAKillAtEveryStep(SaveStep killAt)
    {
        using var profile = new TempProfile();
        Assert.Equal(0, Probe.Run("save", profile.Root, "old").ExitCode);

        var killed = Probe.Run("save", profile.Root, "new", killAt.ToString());
        Assert.NotEqual(0, killed.ExitCode);
        Assert.Equal($"KILL {killAt}", killed.Output);   // killed at exactly this step, nothing after it

        var store = new SaveStore(profile.Root);
        store.RecoverInterruptedCommits();
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

        string digest = M2Fixtures.WorldDigest(loaded.World);
        string oldDigest = M2Fixtures.WorldDigest(M2Fixtures.OldWorld(new Registry()));
        string newDigest = M2Fixtures.WorldDigest(M2Fixtures.NewWorld(new Registry()));
        Assert.True(digest == oldDigest || digest == newDigest, $"After a kill at {killAt} the slot is neither the old nor the new save");
        Assert.True(loaded.IsComplete);
        Assert.Empty(Directory.EnumerateDirectories(profile.Root, ".staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(profile.Root, ".trash-*"));
    }

    [Theory]
    [InlineData(SaveStep.StagingWritten)]
    [InlineData(SaveStep.IntegrityRootWritten)]
    [InlineData(SaveStep.StagingPromoted)]
    public void ME4_FirstEverSave_KilledMidWrite_LeavesNoCorruptSlot(SaveStep killAt)
    {
        using var profile = new TempProfile();
        var killed = Probe.Run("save", profile.Root, "new", killAt.ToString());
        Assert.NotEqual(0, killed.ExitCode);
        Assert.Equal($"KILL {killAt}", killed.Output);

        var store = new SaveStore(profile.Root);
        store.RecoverInterruptedCommits();

        // Either no save at all, or the complete new one - never a partial slot.
        if (Directory.Exists(store.SlotPath(M2Fixtures.Slot)))
        {
            var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));
            Assert.Equal(M2Fixtures.WorldDigest(M2Fixtures.NewWorld(new Registry())), M2Fixtures.WorldDigest(loaded.World));
        }
        Assert.Empty(Directory.EnumerateDirectories(profile.Root, ".staging-*"));
    }

    /// <summary>
    /// The M2 risk spike, as M2b resolves it: "generate from (seed, v1) then (seed, v2) and confirm the
    /// version-mismatch path triggers migration rather than silent regeneration". A content-only
    /// change (a new content_hash, same IDs) no longer moves the baseline, so the save loads through the
    /// content check with every changed cell proven against an unchanged baseline.
    /// </summary>
    [Fact]
    public void RiskSpike_ContentOnlyChange_LoadsWithoutReshuffling()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        var world = M2Fixtures.OldWorld(new Registry());
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));

        string v2 = "sha256:" + string.Concat(Enumerable.Repeat("cd", 32));
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry(), M2Fixtures.Content(v2)));

        Assert.True(loaded.Report.ContentChanged);
        Assert.Equal(2, loaded.Report.CellsMatched);
        Assert.Empty(loaded.Report.CellsMismatched);
        Assert.Equal(M2Fixtures.WorldDigest(M2Fixtures.OldWorld(new Registry())), M2Fixtures.WorldDigest(loaded.World));
    }

    /// <summary>A generation change is caught per changed cell, and refused without a registered transition.</summary>
    [Fact]
    public void RiskSpike_GenerationChange_IsRefused_NeverAppliedToAnotherBaseline()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry())));

        var refused = Assert.Throws<SaveCompatibilityException>(() =>
            store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry(), wolfTarget: 4)));

        Assert.Equal(MigrationResult.Blocked, refused.Report.Result);
        Assert.Equal(2, refused.Report.CellsMismatched.Count);
        Assert.Contains(refused.Report.Blockers, b => b.Contains("no transition is registered"));
    }

    [Fact]
    public void ASchemaWithNoRegisteredChain_IsRefused()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry())));

        var future = M2Fixtures.Context(new Registry()) with { SchemaVersion = SaveFormat.SchemaVersion + 1 };
        var refused = Assert.Throws<SaveCompatibilityException>(() => store.Load(M2Fixtures.Slot, future));

        Assert.Contains(refused.Report.Blockers, b => b.Contains("no registered migration chain"));
    }
}
