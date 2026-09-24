using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, M-02 and M-07: an autosave that fails used to throw out of every frame from then on - the world running,
/// the display frozen. It is contained, reported in the frame's result, and tried again sooner, backing off; and a second game on a
/// profile already in use refuses to start.
/// </summary>
public class SaveFailureTests
{
    [Fact]
    public void AFailingAutosave_NeverThrowsOutOfFrame_IsReported_AndIsTriedAgainSooner_BackingOff()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.NewGame("Wanderer", seed: 42);
        Directory.Delete(profile.Root, recursive: true);
        File.WriteAllText(profile.Root, "the disk refuses: nothing can be written under it");

        var failed = new List<(double Playtime, string Why)>();
        long ticks = 0;
        try
        {
            while (session.PlaytimeSeconds < 520)
            {
                var frame = session.Frame(GameSession.MaxFrameSeconds);
                ticks += frame.TicksRun;
                Assert.Null(frame.AutosavedTo);
                if (frame.AutosaveFailed is { } why)
                    failed.Add((session.PlaytimeSeconds, why));
            }
        }
        finally
        {
            File.Delete(profile.Root);
        }

        // Due at five minutes of play; then 30 s, 60 s and 120 s later.
        Assert.Equal(new[] { 300.0, 330.0, 390.0, 510.0 }, failed.Select(f => f.Playtime));
        Assert.All(failed, f => Assert.Contains("failed", f.Why, StringComparison.Ordinal));
        Assert.Equal(ticks, session.Simulation!.WorldTick);   // every frame ran every tick it owed

        // The disk back, the next attempt - 240 s after the last - saves.
        Directory.CreateDirectory(profile.Root);
        string? saved = null;
        while (saved is null && session.PlaytimeSeconds < 800)
            saved = session.Frame(GameSession.MaxFrameSeconds).AutosavedTo;
        Assert.Equal((SaveSlots.Auto(1), 750.0), (saved, session.PlaytimeSeconds));
        Assert.Equal(session.Simulation.WorldTick, session.Load(SaveSlots.Auto(1)).Manifest.WorldTick);
    }

    [Fact]
    public void ASecondGame_OnAProfileInUse_RefusesToStart_AndTheFirstStillSaves()
    {
        using var profile = new TempProfile();
        string content = Path.Combine(Harness.RepoRoot(), "content");
        using var first = GameSession.Boot(new GameOptions(content, profile.Root) { LockProfile = true });
        first.NewGame("Wanderer", seed: 42);

        Assert.Throws<ProfileInUseException>(() => GameSession.Boot(new GameOptions(content, profile.Root) { LockProfile = true }));

        first.Save(SaveSlots.Quick);
        Assert.True(first.Load(SaveSlots.Quick).IsComplete);
    }
}
