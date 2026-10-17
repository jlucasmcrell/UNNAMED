using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP11 through the session: every save is stamped <c>saved_utc</c> by the session's clock, Continue logs in to the live world and lets it
/// go on for the absence (capped at one Reckoning), and a restore brings back its moment with no catch-up (DN7).
/// </summary>
public class LoginTests
{
    private const long Reckoning = 183 * 57_600L;
    private static readonly DateTimeOffset Saved = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);

    /// <summary>A session whose clock a test sets: the wall clock, for this test.</summary>
    private sealed class Clocked
    {
        public DateTimeOffset Now = Saved;

        public GameSession Boot(TempProfile profile) =>
            GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { Clock = () => Now });
    }

    private static (Clocked Clock, GameSession Session, long SavedTick) SavedGame(TempProfile profile)
    {
        var clock = new Clocked();
        var session = clock.Boot(profile);
        session.NewGame(Harness.Named("Wanderer"), seed: 7);
        Harness.Ticks(session, 40);
        session.Save(SaveSlots.Quick);
        return (clock, session, session.Simulation!.WorldTick);
    }

    [Fact]
    public void ASave_IsStampedWithTheSessionsClock()
    {
        using var profile = new TempProfile();
        var (_, session, _) = SavedGame(profile);
        var loaded = new SaveStore(session.SaveFolder).Load(SaveSlots.Quick, new LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()));
        Assert.Equal("2026-10-17T18:00:00.000Z", loaded.Manifest.SavedUtc);
        Assert.Equal(0, loaded.Manifest.WorldTimeAdvancedTicks);
        Assert.Empty(loaded.Manifest.AwayLog);
    }

    [Fact]
    public void Continue_LetsTheWorldGoOn_ForTheAbsence_AndTheSaveRemembersIt()
    {
        using var profile = new TempProfile();
        var (clock, session, savedTick) = SavedGame(profile);
        clock.Now = Saved.AddHours(1);   // 72 000 ticks: a game day and a quarter

        var login = session.Continue();

        Assert.Equal(SaveSlots.Quick, login.Slot);
        Assert.Equal((Saved, Saved.AddHours(1)), (login.SavedUtc, login.NowUtc));
        Assert.Equal(new CatchUp(72_000, 72_000), login.CatchUp);
        Assert.Equal(savedTick + 72_000, session.Simulation!.WorldTick);
        Assert.Equal(72_000, session.WorldTimeAdvancedTicks);
        Assert.Contains(login.Away.Lines, l => l.Kind == AdvanceKinds.TimePassed && l.Amount == 72_000);
        Assert.Equal(session.Simulation.WorldTick + session.Setup.Offline.LoginGraceTicks, session.Simulation.LoginGraceUntil);
        Assert.Same(login, session.LastLogin);

        // The next save carries the honoured total and the away log, and the next login adds to it.
        clock.Now = Saved.AddHours(2);
        session.Save(SaveSlots.Quick);
        clock.Now = Saved.AddHours(2).AddMinutes(48);   // one game day
        var again = session.Continue();
        Assert.Equal(57_600, again.CatchUp.HonouredTicks);
        Assert.Equal(72_000 + 57_600, session.WorldTimeAdvancedTicks);
        session.Save(SaveSlots.Quick);
        var manifest = new SaveStore(session.SaveFolder).Load(SaveSlots.Quick,
            new LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry())).Manifest;
        Assert.Equal(72_000 + 57_600, manifest.WorldTimeAdvancedTicks);
        Assert.Contains(manifest.AwayLog, l => l.Kind == AdvanceKinds.TimePassed && l.Amount == 57_600);
    }

    [Fact]
    public void AClockMovedBack_CatchesNothingUp()
    {
        using var profile = new TempProfile();
        var (clock, session, savedTick) = SavedGame(profile);
        clock.Now = Saved.AddDays(-2);

        var login = session.Continue();

        Assert.Equal(CatchUp.None, login.CatchUp);
        Assert.Equal(savedTick, session.Simulation!.WorldTick);
        Assert.Empty(login.Away.Lines);
        Assert.Equal(0, session.WorldTimeAdvancedTicks);
    }

    [Fact]
    public void ALongerAbsence_CountsAsExactlyOneReckoning()
    {
        using var profile = new TempProfile();
        var (clock, session, savedTick) = SavedGame(profile);
        clock.Now = Saved.AddDays(30);

        var login = session.Continue();

        Assert.True(login.CatchUp.Capped);
        Assert.Equal((30L * 86_400 * 20, Reckoning), (login.CatchUp.RequestedTicks, login.CatchUp.HonouredTicks));
        Assert.Equal(savedTick + Reckoning, session.Simulation!.WorldTick);
    }

    [Fact]
    public void ARestore_BringsBackItsMoment_WithNoCatchUp()
    {
        using var profile = new TempProfile();
        var (clock, session, savedTick) = SavedGame(profile);
        clock.Now = Saved.AddDays(3);

        session.Load(SaveSlots.Quick, SaveCopy.Current);

        Assert.Equal(savedTick, session.Simulation!.WorldTick);
        Assert.Equal(0, session.WorldTimeAdvancedTicks);
        Assert.Null(session.LastLogin);
        Assert.True(session.Simulation.LoginGraceUntil <= session.Simulation.WorldTick);
    }

    [Fact]
    public void Continue_WithNothingSaved_SaysSo()
    {
        using var profile = new TempProfile();
        var session = new Clocked().Boot(profile);
        Assert.Throws<SaveException>(() => session.Continue());
    }
}
