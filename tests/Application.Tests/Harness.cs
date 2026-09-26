using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>A save profile in a temporary directory, removed afterwards.</summary>
internal sealed class TempProfile : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-app-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

internal static class Harness
{
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root (src/UNNAMED.sln) not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The game's own content, booted exactly as the game boots it.</summary>
    public static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(RepoRoot(), "content"), profile.Root));

    /// <summary>Run whole ticks, one per frame, the way a 20 Hz frame loop would.</summary>
    public static void Ticks(GameSession session, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            session.Frame(session.TickSeconds);
    }

    /// <summary>
    /// Walk the player towards a point the way presentation does: turn the wish into a world-space intent every
    /// frame and submit it as a command. Stops within <paramref name="toleranceMm"/> or after the tick budget.
    /// </summary>
    public static bool WalkTo(GameSession session, long xMm, long zMm, Gait gait = Gait.Run, int maxTicks = 4000, long toleranceMm = 300,
        Action? afterFrame = null)
    {
        var simulation = session.Simulation!;
        for (int i = 0; i < maxTicks; i++)
        {
            var body = simulation.Player.Body;
            double dx = xMm - body.XMm, dz = zMm - body.ZMm;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance <= toleranceMm)
            {
                session.Submit(new MoveCommand(simulation.PlayerId, MoveIntent.Idle(body.FacingMdeg)));
                session.Frame(session.TickSeconds);
                afterFrame?.Invoke();
                return true;
            }
            session.Submit(new MoveCommand(simulation.PlayerId, Toward(dx, dz, gait)));
            session.Frame(session.TickSeconds);
            afterFrame?.Invoke();
        }
        return false;
    }

    /// <summary>Walk a list of waypoints in metres.</summary>
    public static bool WalkPath(GameSession session, params (double X, double Z)[] waypoints) =>
        waypoints.All(w => WalkTo(session, (long)(w.X * 1000), (long)(w.Z * 1000)));

    /// <summary>A full-deflection intent along a world-space direction, facing where it goes.</summary>
    public static MoveIntent Toward(double dx, double dz, Gait gait = Gait.Run)
    {
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length == 0)
            return MoveIntent.Idle(0);
        int x = (int)Math.Round(dx / length * MoveIntent.FullDeflection);
        int z = (int)Math.Round(dz / length * MoveIntent.FullDeflection);
        return new MoveIntent(x, z, gait, FacingOf(dx, dz));
    }

    /// <summary>Millidegrees from +Z towards +X, in [0, 360000).</summary>
    public static int FacingOf(double dx, double dz)
    {
        double degrees = Math.Atan2(dx, dz) * 180 / Math.PI;
        int mdeg = (int)Math.Round(degrees * 1000) % MoveIntent.FullTurnMdeg;
        return mdeg < 0 ? mdeg + MoveIntent.FullTurnMdeg : mdeg;
    }

    /// <summary>Collect every event of one type the session publishes.</summary>
    public static List<T> Record<T>(IDomainEvents events)
    {
        var seen = new List<T>();
        events.Subscribe<T>(seen.Add);
        return seen;
    }

    /// <summary>
    /// A session playing the world an arena set up - the character changed as the test needs, the region's own creatures in place - saved
    /// and loaded as the game loads any save, so the session's own bus and frame loop run it.
    /// </summary>
    public static GameSession Playing(TempProfile profile, (double X, double Z) at, int facingDeg, Func<World.PlayerRecord, World.PlayerRecord> change)
    {
        var session = Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, at, facingDeg, Array.Empty<(string, double, double, string)>(), change, keepSpawns: true);
        new Persistence.SaveStore(profile.Root).Save(Persistence.SaveSlots.Manual("playing"), Persistence.SaveDocuments.Capture(arena.Simulation.World,
            arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        Assert.True(session.Load(Persistence.SaveSlots.Manual("playing")).IsComplete);
        return session;
    }
}
