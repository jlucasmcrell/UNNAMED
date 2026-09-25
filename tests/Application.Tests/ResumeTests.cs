using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, B-01: a relaunch resumes. The start screen offers Continue - the newest save that loads, an autosave as
/// readily as any - and a new game only when chosen. A quicksave never destroys the save it replaces outright, and a damaged save is
/// passed over in the open, with its backups there to load.
/// </summary>
public class ResumeTests
{
    [Fact]
    public void ABoot_StartsNothing_AndAnEmptyProfile_OffersNothingToContinue()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Null(session.Simulation);
        var choice = session.StartChoice();
        Assert.Null(choice.Continue);
        Assert.Empty(choice.Saves);
        Assert.False(choice.NewGameAsksFirst);
    }

    [Fact]
    public void Relaunch_OffersContinue_FromTheNewestSave_AnAutosaveIncluded()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame("Wanderer", seed: 42);
        Harness.Ticks(first, 100);
        first.Save(SaveSlots.Quick);
        Harness.Ticks(first, 100);
        first.Save(SaveSlots.Auto(2));
        var (tick, digest) = (first.Simulation!.WorldTick, first.Simulation.StateDigest());

        var second = Harness.Boot(profile);
        Assert.Null(second.Simulation);
        var choice = second.StartChoice();
        Assert.Equal(new[] { "auto_02", "quick" }, choice.Saves.Select(s => s.Slot));
        Assert.Equal((SaveSlots.Auto(2), SaveCopy.Current), (choice.Continue!.Slot, choice.Continue.Copy));
        Assert.Empty(choice.PassedOver);
        Assert.True(choice.NewGameAsksFirst);

        Assert.True(second.Load(choice.Continue.Slot, choice.Continue.Copy).IsComplete);
        Assert.Equal((tick, digest), (second.Simulation!.WorldTick, second.Simulation.StateDigest()));
    }

    /// <summary>The audit's probe: a new game's first quicksave deleted the last session's quick save, which no load had proven.</summary>
    [Fact]
    public void AQuicksaveAfterARelaunch_KeepsThePreviousQuickSaveRecoverable()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame("Wanderer", seed: 42);
        Harness.Ticks(first, 200);
        first.Save(SaveSlots.Quick);
        string before = first.Simulation!.StateDigest();

        var second = Harness.Boot(profile);
        second.NewGame("Wanderer", seed: 7);
        second.Save(SaveSlots.Quick);

        var previous = Assert.Single(second.OtherCopies(SaveSlots.Quick));
        Assert.Equal((SaveCopy.Previous, (string?)null, 200L), (previous.Copy, previous.Problem, previous.WorldTick));
        Assert.True(second.Load(SaveSlots.Quick, SaveCopy.Previous).IsComplete);
        Assert.Equal(before, second.Simulation!.StateDigest());
        // Loading it proves nothing and moves nothing: the quick save is still the new game's.
        Assert.Equal(0, second.StartChoice().Saves.Single(s => s.Slot == SaveSlots.Quick).WorldTick);
        Assert.Equal(SaveCopy.Previous, Assert.Single(second.OtherCopies(SaveSlots.Quick)).Copy);
    }

    [Fact]
    public void Continuing_ThenQuicksaving_KeepsTheSaveContinuedFrom_AsABackup()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame("Wanderer", seed: 42);
        Harness.Ticks(first, 200);
        first.Save(SaveSlots.Quick);

        var second = Harness.Boot(profile);
        var target = second.StartChoice().Continue!;
        second.Load(target.Slot, target.Copy);
        Harness.Ticks(second, 50);
        second.Save(SaveSlots.Quick);

        Assert.Equal(new[] { SaveCopy.Backup1 }, second.OtherCopies(SaveSlots.Quick).Select(c => c.Copy));
        Assert.Equal(200, second.Load(SaveSlots.Quick, SaveCopy.Backup1).Manifest.WorldTick);
    }

    [Fact]
    public void ADamagedSave_IsPassedOverByContinue_InTheOpen_AndItsBackupLoads()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame("Wanderer", seed: 42);
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Auto(1));
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Quick);
        first.Load(SaveSlots.Quick);   // proven by a load, so the next quicksave keeps it as backup 1
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Quick);
        string player = Path.Combine(profile.Root, SaveSlots.Quick, SaveFormat.Player);
        byte[] bytes = File.ReadAllBytes(player);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(player, bytes);

        var second = Harness.Boot(profile);
        var choice = second.StartChoice();
        var damaged = Assert.Single(choice.PassedOver);
        Assert.Equal(SaveSlots.Quick, damaged.Slot);
        Assert.StartsWith("it is damaged", damaged.Problem);
        Assert.Equal(SaveSlots.Auto(1), choice.Continue!.Slot);

        var refused = Assert.Throws<SaveCorruptionException>(() => second.Load(SaveSlots.Quick));
        Assert.Equal(new[] { 1 }, refused.BackupGenerations);
        Assert.Null(second.Simulation);
        var backup = Assert.Single(second.OtherCopies(SaveSlots.Quick));
        Assert.Equal((SaveCopy.Backup1, (string?)null, 80L), (backup.Copy, backup.Problem, backup.WorldTick));
        Assert.True(second.Load(SaveSlots.Quick, SaveCopy.Backup1).IsComplete);
        Assert.Equal(80, second.Simulation!.WorldTick);
    }
}
