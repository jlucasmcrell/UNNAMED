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
    }

    [Fact]
    public void Relaunch_OffersContinue_FromTheNewestSave_AnAutosaveIncluded()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame(Harness.Named("Wanderer"), seed: 42);
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

        Assert.True(second.Load(choice.Continue.Slot, choice.Continue.Copy).IsComplete);
        Assert.Equal((tick, digest), (second.Simulation!.WorldTick, second.Simulation.StateDigest()));
    }

    /// <summary>
    /// The audit's probe: a new game's first quicksave deleted the last session's quick save, which no load had proven. Saves are now kept
    /// per character (OD-14), so a new character's quicksave lands in its own folder and the last character's quick save is not touched.
    /// </summary>
    [Fact]
    public void ANewCharacter_SavesInAFolderOfItsOwn_AndTheLastCharactersQuickSaveStandsAsItWas()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame(Harness.Named("Wanderer"), seed: 42);
        Harness.Ticks(first, 200);
        first.Save(SaveSlots.Quick);
        string before = first.Simulation!.StateDigest();
        string firstCharacter = first.Character!;

        var second = Harness.Boot(profile);
        second.NewGame(Harness.Named("Wenna Lind"), seed: 7);
        second.Save(SaveSlots.Quick);

        Assert.NotEqual(firstCharacter, second.Character);
        Assert.Equal(Path.Combine(profile.Root, CharacterFolders.Directory, second.Character!), second.SaveFolder);
        Assert.Empty(second.OtherCopies(SaveSlots.Quick));   // nothing displaced: the slot was new
        var quicks = second.StartChoice().Saves.Where(s => s.Slot == SaveSlots.Quick).ToList();
        Assert.Equal(new[] { (second.Character, 0L), (firstCharacter, 200L) }, quicks.Select(s => (s.Character, s.WorldTick)));
        Assert.Equal(new[] { "Wenna Lind", "Wanderer" }, second.CharacterSaves().Select(c => c.Name));

        Assert.True(second.Load(quicks[1]).IsComplete);
        Assert.Equal(before, second.Simulation!.StateDigest());
        Assert.Equal(firstCharacter, second.Character);   // and the character in play, and the folder it saves to, are the first again
    }

    [Fact]
    public void Continuing_ThenQuicksaving_KeepsTheSaveContinuedFrom_AsABackup()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        first.NewGame(Harness.Named("Wanderer"), seed: 42);
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
        first.NewGame(Harness.Named("Wanderer"), seed: 42);
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Auto(1));
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Quick);
        first.Load(SaveSlots.Quick);   // proven by a load, so the next quicksave keeps it as backup 1
        Harness.Ticks(first, 40);
        first.Save(SaveSlots.Quick);
        string player = Path.Combine(first.SaveFolder, SaveSlots.Quick, SaveFormat.Player);
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
