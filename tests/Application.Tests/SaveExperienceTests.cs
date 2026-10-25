using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// What a player sees of saving (PJ10 §5.2, §6.2, §6.3.1; SYS-147, SYS-148, SYS-151): the session says while a save is being written, a
/// failed save comes back as an outcome carrying its trouble in a player's words, and a character can be deleted from the title - every save
/// with them, and never the one in play.
/// </summary>
public class SaveExperienceTests
{
    private static GameSession Boot(TempProfile profile, Action<SaveStep>? hook = null) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { SaveStepHook = hook });

    [Fact]
    public void WhileASaveIsWrittenInTheBackground_TheSessionSaysItIsSaving_AndStopsOnceItIsCommitted()
    {
        using var profile = new TempProfile();
        using var gate = new ManualResetEventSlim(false);
        using var session = Boot(profile, step =>
        {
            if (step == SaveStep.StagingPromoted)
                gate.Wait(TimeSpan.FromMinutes(5));
        });
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.False(session.IsSaving);

        session.SaveInBackground(SaveSlots.Quick);
        Assert.True(session.IsSaving);
        session.Frame(GameSession.MaxFrameSeconds);
        Assert.True(session.IsSaving);   // still being written: the frame went on regardless

        gate.Set();
        Assert.True(session.WaitForSaves(TimeSpan.FromSeconds(30)));
        Assert.False(session.IsSaving);
        var outcome = Assert.Single(session.Frame(GameSession.MaxFrameSeconds).Saves);
        Assert.Null(outcome.Trouble);
    }

    [Fact]
    public void ASaveOntoAFullDisk_ComesBackAsAnOutcome_SayingTheDiskIsFull_AndTheLastSaveStands()
    {
        using var profile = new TempProfile();
        bool full = false;
        using var session = Boot(profile, step =>
        {
            if (full && step == SaveStep.StagingWritten)
                throw new IOException("No space left on device", 28);
        });
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Harness.Ticks(session, 20);
        session.Save(SaveSlots.Quick);
        long kept = session.Simulation!.WorldTick;

        full = true;
        Harness.Ticks(session, 20);
        session.SaveInBackground(SaveSlots.Quick);
        session.WaitForSaves(TimeSpan.FromSeconds(30));
        var outcome = Assert.Single(session.Frame(GameSession.MaxFrameSeconds).Saves);

        Assert.NotNull(outcome.Failure);
        Assert.Equal(SaveTroubleKind.DiskFull, outcome.Trouble!.Kind);
        Assert.Equal("Not saved: the disk is full.", outcome.Trouble.Headline);
        Assert.Contains("Save: Quick save", outcome.Trouble.Report("Quick save", session.SaveFolder, "test"), StringComparison.Ordinal);
        full = false;
        new SaveStore(session.SaveFolder).RecoverInterruptedCommits();
        Assert.Equal(kept, session.Load(SaveSlots.Quick).Manifest.WorldTick);
    }

    [Fact]
    public void DeletingACharacter_AtTheTitle_TakesEverySaveOfTheirs_AndContinueMovesToTheNext()
    {
        using var profile = new TempProfile();
        var first = Boot(profile);
        first.NewGame(Harness.Named("Wenna Lind"), seed: 1);
        first.Save(SaveSlots.Quick);
        first.Save(SaveSlots.Auto(1));
        string wenna = first.Character!;
        first.NewGame(Harness.Named("Bram Hallow"), seed: 2);
        Harness.Ticks(first, 10);
        first.Save(SaveSlots.Quick);
        string bram = first.Character!;
        first.Dispose();

        using var title = Boot(profile);
        Assert.Equal(bram, title.Character);
        Assert.Equal(2, title.CharacterSaves().Single(c => c.Character == wenna).Saves.Length);

        Assert.True(title.DeleteCharacter(bram));

        Assert.Equal(new[] { wenna }, CharacterFolders.List(profile.Root).Select(id => id.Value));
        Assert.Equal(wenna, title.Character);
        Assert.Equal(wenna, title.StartChoice().Continue!.Character);
        Assert.DoesNotContain(title.StartChoice().Saves, s => s.Character == bram);
        Assert.False(title.DeleteCharacter(bram));
        Assert.Equal("Wenna Lind", title.Load(title.StartChoice().Continue!).Player.Name);
    }

    [Fact]
    public void TheCharacterInPlay_CannotBeDeleted_WhileTheirWorldRuns()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        session.NewGame(Harness.Named("Wenna Lind"), seed: 1);
        session.Save(SaveSlots.Quick);

        Assert.Throws<InvalidOperationException>(() => session.DeleteCharacter(session.Character!));
        Assert.Single(CharacterFolders.List(profile.Root));
        Assert.Throws<ArgumentException>(() => session.DeleteCharacter("not a character"));
    }
}
