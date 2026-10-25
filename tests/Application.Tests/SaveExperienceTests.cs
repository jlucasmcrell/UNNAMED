using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// What a player sees of saving (PJ10 §5.2, §6.2, §6.3.1; SYS-147, SYS-148, SYS-151): the session says while a save is being written, a
/// failed save comes back as an outcome carrying its trouble in a player's words, and a character can be deleted from the title - every save
/// with them, and never the one in play.
/// </summary>
public class SaveExperienceTests
{
    private static GameSession Boot(TempProfile profile, Action<SaveStep>? hook = null, bool firstFrame = false) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { SaveStepHook = hook, FirstFrameAutosave = firstFrame });

    [Fact]
    public void ASave_SaysWhoseItIs_TheirLevel_AndWhere_InItsPreview()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        session.NewGame(Harness.Named("Wenna Lind"), seed: 42);
        Harness.Ticks(session, 5);
        session.Save(SaveSlots.Quick);

        var preview = session.SaveSlots().Single().Preview!;
        Assert.Equal(("Wenna Lind", 1), (preview.Name, preview.Level));
        Assert.Equal(session.DisplayName(session.Simulation!.ActiveLayout.Id), preview.Place);
        Assert.Equal(session.Simulation.TrackedQuest() is { } quest ? session.DisplayName(quest) : null, preview.Quest);
        Assert.True(session.AttachThumbnail(SaveSlots.Quick, new byte[] { 1, 2, 3 }));
        Assert.NotNull(session.SaveSlots().Single().ThumbnailPath);
    }

    // ── when autosaves are taken (PJ10 §4.2; PERSISTENCE.md §8.1; SYS-143) ─────────

    [Fact]
    public void ANewCharacter_IsAutosavedOnTheirFirstFrame_SoQuittingAtOnceLosesNothing()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile, firstFrame: true);
        session.NewGame(Harness.Named("Wenna Lind"), seed: 42);

        var frame = session.Frame(session.TickSeconds);

        Assert.Equal(SaveSlots.Auto(1), frame.AutosaveTaken);
        session.WaitForSaves(TimeSpan.FromSeconds(30));
        Assert.Equal(SaveSlots.Auto(1), Assert.Single(session.SaveSlots()).Slot);
        Assert.Null(session.Frame(session.TickSeconds).AutosaveTaken);   // once, not every frame
        // A loaded character is not a new one: no first-frame save.
        using var again = Boot(profile, firstFrame: true);
        again.Load(SaveSlots.Auto(1));
        Assert.Null(again.Frame(again.TickSeconds).AutosaveTaken);
    }

    [Fact]
    public void AnAutosaveDueMidFight_WaitsUntilTheFightHasBeenOverAWhile()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wenna Lind"), seed: 42);
        session.Submit(new World.Runtime.AttackCommand(simulation.PlayerId));   // a swing at the air is a fight, as far as saving goes
        PlayUntil(session, () => simulation.Combat.InCombat);
        session.RequestAutosave();

        double askedAt = session.PlaytimeSeconds;
        string? taken = null;
        double fightOver = double.NaN;
        while (taken is null && session.PlaytimeSeconds < askedAt + GameSession.CombatDeferralCapSeconds + 5)
        {
            var frame = session.Frame(GameSession.MaxFrameSeconds);
            if (double.IsNaN(fightOver) && !simulation.Combat.InCombat)
                fightOver = session.PlaytimeSeconds;
            taken = frame.AutosaveTaken;
        }

        Assert.NotNull(taken);
        Assert.False(double.IsNaN(fightOver));
        Assert.True(session.PlaytimeSeconds >= fightOver + GameSession.CombatQuietSeconds - GameSession.MaxFrameSeconds,
            $"taken at {session.PlaytimeSeconds:0.00} s, the fight over at {fightOver:0.00} s");
    }

    [Fact]
    public void AnAutosave_IsNeverDeferredPastTheCap_HoweverLongTheFightGoesOn()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wenna Lind"), seed: 42);
        session.RequestAutosave();
        string? taken = null;
        // An event's autosave falls due EventAutosaveSpacingSeconds after the last one (the start); the cap counts from then.
        double due = Math.Max(session.PlaytimeSeconds, GameSession.EventAutosaveSpacingSeconds);
        while (taken is null && session.PlaytimeSeconds < due + GameSession.CombatDeferralCapSeconds + 5)
        {
            if (simulation.Combat.Phase == World.Runtime.CombatPhase.Idle)
                session.Submit(new World.Runtime.AttackCommand(simulation.PlayerId));   // swinging all the while
            taken = session.Frame(GameSession.MaxFrameSeconds).AutosaveTaken;
        }

        // The swings never left ten quiet seconds, so only the cap could take it - and it did.
        Assert.NotNull(taken);
        Assert.InRange(session.PlaytimeSeconds - due, GameSession.CombatDeferralCapSeconds - GameSession.MaxFrameSeconds,
            GameSession.CombatDeferralCapSeconds + 2 * GameSession.MaxFrameSeconds);
    }

    [Fact]
    public void CrossingIntoASpace_IsAutosaved_AtTheBoundary_ButNoMoreOftenThanTheScopedSpacing()
    {
        using var profile = new TempProfile();
        using var session = GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            RegionId = "region.starting_frontier", Barrowworks = true, StartXMm = 438_000, StartZMm = 663_000, FirstFrameAutosave = false,
        });
        var simulation = session.NewGame(Harness.Named("Wenna Lind"), seed: 42);
        Assert.Null(session.Frame(session.TickSeconds).AutosaveTaken);

        session.Submit(new World.Runtime.EnterSpaceCommand(simulation.PlayerId, "portal.barrowworks.threshold"));
        var inside = session.Frame(session.TickSeconds);
        Assert.False(simulation.PlayerInExterior);
        Assert.Equal(SaveSlots.Auto(1), inside.AutosaveTaken);
        session.WaitForSaves(TimeSpan.FromSeconds(30));

        // Straight back out: the boundary is crossed again inside the spacing, so it waits for it.
        session.Submit(new World.Runtime.LeaveSpaceCommand(simulation.PlayerId));
        var outside = session.Frame(session.TickSeconds);
        Assert.True(simulation.PlayerInExterior);
        Assert.Null(outside.AutosaveTaken);
        string? taken = null;
        while (taken is null && session.PlaytimeSeconds < GameSession.ScopedSaveSpacingSeconds + 5)
            taken = session.Frame(GameSession.MaxFrameSeconds).AutosaveTaken;
        Assert.Equal(SaveSlots.Auto(2), taken);
        Assert.True(session.PlaytimeSeconds >= GameSession.ScopedSaveSpacingSeconds - GameSession.MaxFrameSeconds);
    }

    private static void PlayUntil(GameSession session, Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++)
            session.Frame(session.TickSeconds);
        Assert.True(done());
    }

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
