using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
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
    /// <summary>
    /// The Ashen Hollow's south-west corner in region.starting_frontier, the game's region: the tests' places are authored in the hollow's
    /// own 200 m square (its M6 layout, which recovery 04f079a moved into the region by this) and measured from here.
    /// </summary>
    public static readonly (double X, double Z) Hollow = (400, 600);

    /// <summary>Where the hollow lies in a region: its south-west corner, in starting_frontier and in the retired region.ashen_hollow alike.</summary>
    public static (double X, double Z) HollowOf(SimulationSetup setup) => (setup.Layout.Space.MinXMm / 1000.0, setup.Layout.Space.MinZMm / 1000.0);

    /// <summary>
    /// The setup with these townsfolk hireable in every role, each with a spear (the WP15 tests' townsfolk). Who can be hired is derived
    /// from town roles (WP12b), and the named are vendors or quest givers, who never are: for the test they keep a role that hires out.
    /// </summary>
    public static SimulationSetup HireableTownsfolk(SimulationSetup setup, params string[] ids) => setup with
    {
        Social = setup.Social with
        {
            Npcs = ids.Aggregate(setup.Social.Npcs, (npcs, id) => npcs.SetItem(id, npcs[id] with
            {
                Hireling = new HirelingProfile(Enum.GetValues<HirelingRole>().ToImmutableArray(),
                    new CompanionProfile(60, "item.weapon.march_spear", ImmutableSortedDictionary<BodyRegion, int>.Empty)),
            })),
        },
        NpcLife = setup.NpcLife with
        {
            Routines = setup.NpcLife.Routines.ToImmutableSortedDictionary(kv => kv.Key,
                kv => kv.Value.NpcId is { } npc && ids.Contains(npc) ? kv.Value with { Roles = ImmutableArray.Create("guard") } : kv.Value, StringComparer.Ordinal),
        },
    };

    /// <summary>The region's own origin, for an arena whose places are given in region metres.</summary>
    public static readonly (double X, double Z) Region = (0, 0);

    /// <summary>A place in the hollow's metres, in the region's millimetres.</summary>
    public static (long XMm, long ZMm) InRegion(double x, double z) => ((long)((x + Hollow.X) * 1000), (long)((z + Hollow.Z) * 1000));

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root (src/UNNAMED.sln) not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The game's own content, booted exactly as the game boots it - or in another of its regions, or with the
    /// Barrowworks gated on (<c>--visual barrowworks=1</c>), which is the only way a run reaches the ring.
    /// </summary>
    public static GameSession Boot(TempProfile profile, string? regionId = null, bool barrowworks = false)
    {
        string root = Environment.GetEnvironmentVariable("UNNAMED_ACCEPTANCE_GAME_ROOT") ?? RepoRoot();
        // These tests count autosaves and slots from zero; the first-frame autosave has tests of its own (SaveExperienceTests).
        var options = new GameOptions(Path.Combine(root, "content"), profile.Root) { FirstFrameAutosave = false };
        if (regionId is not null)
            options = options with { RegionId = regionId };
        if (barrowworks)
            options = options with { Barrowworks = true };
        return GameSession.Boot(options);
    }

    /// <summary>A new game's character with only a name chosen, and no Call: the character every test made before the creator.</summary>
    public static CharacterSpec Named(string name) => CharacterSpec.Quick(name, null);

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
    public static bool WalkTo(GameSession session, long xMm, long zMm, Gait gait = Gait.Run, int maxTicks = 4000, long toleranceMm = 300)
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
                return true;
            }
            session.Submit(new MoveCommand(simulation.PlayerId, Toward(dx, dz, gait)));
            session.Frame(session.TickSeconds);
        }
        return false;
    }

    /// <summary>Walk a list of waypoints in the hollow's metres.</summary>
    public static bool WalkPath(GameSession session, params (double X, double Z)[] waypoints) =>
        waypoints.All(w => WalkTo(session, (long)((w.X + HollowOf(session.Setup).X) * 1000), (long)((w.Z + HollowOf(session.Setup).Z) * 1000)));

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
