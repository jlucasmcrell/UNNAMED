// UNNAMED Presentation - the region survey: a scripted pass that stands the character at the region's own places,
// turns the camera, and keeps the frames. Unlike --visual-audit this does NOT build an Environment of its own
// (VisualAudit.cs builds its own, so audit stills cannot judge light, atmosphere, exposure or sky-driven ambient).
// Here the world, the sun, the sky and the camera are the ones ordinary play uses; the only thing added is that a
// script drives the stick. That is the point of it: the owner's acceptance criterion is the player-visible result
// in ordinary continuous gameplay, so the pictures worth looking at are gameplay pictures.
//
//   godot --path src/Presentation -- --region region.cairnwold --content-root <dir> --asset-root <dir> ^
//         --visual tier=ultra,sky=hdri --survey <out-dir>
//
// Every place the region authored (its named locations) is a station, plus the spawn and the basin seen from
// height. Each station is shot from several standpoints and yaws. The report beside the frames records the
// character's ACTUAL position and the frame each picture was taken on, so a frame can be traced to a place and
// looked at again.

using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>Stand at each of the region's places in turn, turn the camera, and keep the frames.</summary>
public sealed class SurveyShots
{
    /// <summary>A place to look at, and the standpoints to look at it from.</summary>
    private sealed record Station(string Name, double X, double Z, string Note, params Standpoint[] Standpoints);

    /// <summary>Where the character stands to take a picture of the place: metres away, looking at it.</summary>
    private sealed record Standpoint(double Away, double YawOffset, float Pitch, int Settle, string Shot);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly List<Station> _stations = new();
    private readonly StringBuilder _report = new();
    private readonly StringBuilder _places = new();

    private int _station;
    private int _standpoint;
    private int _wait;
    private bool _busy;
    private int _frame;
    private int _legsFailed;

    public SurveyShots(GameSession session, PlayerController controller, CameraRig camera, string directory,
        bool oneOnly = false)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        if (!oneOnly)
            BuildStations();
        _report.AppendLine("# Cairnwold survey — every place the region authored, from several standpoints");
        _report.AppendLine();
        _report.AppendLine($"Content {session.Content.Version} ({session.Content.Hash}), world seed {session.Simulation!.World.WorldSeed}, " +
                           $"{session.TickSeconds * 1000:0} ms ticks. The world, sun and sky are gameplay's own; a script drives the stick.");
        _report.AppendLine();
        _report.AppendLine("| # | Station | Target (x, z) m | Stood at (x, z) m | Stood Y m | Distance | Looking | Frame | Shot |");
        _report.AppendLine("|---|---|---|---|---|---|---|---|---|");
    }

    public string Directory { get; }

    /// <summary>
    /// A survey of exactly ONE place, for judging a change in seconds instead of a whole traversal. The owner's
    /// direction: "you do not have to play an entire playthrough of the level to gauge whether grass looks right.
    /// 5-10 seconds at most is plenty." The four-minute nine-station pass was the wrong tool for a look-and-see.
    ///
    /// <paramref name="spec"/> is <c>x,z,yaw_deg[,pitch][,seconds]</c> - the place to stand, the way to face, the
    /// tilt, and how long to hold before the frame is kept. Two standpoints are taken: at the place, and backed
    /// off 12 m behind it, so the same spot is judged near and a little way off.
    /// </summary>
    public static SurveyShots One(GameSession session, PlayerController controller, CameraRig camera,
        string directory, string spec)
    {
        var parts = spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0], out double x) || !double.TryParse(parts[1], out double z))
            throw new ArgumentException($"--shot wants x,z,yaw_deg[,pitch][,seconds]; got '{spec}'", nameof(spec));
        double yawDeg = parts.Length > 2 && double.TryParse(parts[2], out double y) ? y : 0;
        float pitch = parts.Length > 3 && float.TryParse(parts[3], out float p) ? p : -0.08f;
        double seconds = parts.Length > 4 && double.TryParse(parts[4], out double s) ? s : 6;
        int settle = Math.Max(20, (int)Math.Round(seconds / session.TickSeconds));
        var camera0 = Mathf.DegToRad((float)yawDeg);
        var shots = new SurveyShots(session, controller, camera, directory, oneOnly: true);
        // The character STANDS ON the place and the camera LOOKS along the yaw - it does not stand off it. The
        // first cut offset the standpoint by +/- the sight line, so "near" collapsed onto the target itself
        // (stand 0.5 m from the point you are looking at) and every frame came back as a close-up of the ground
        // at the character's feet. Stand on it; the yaw chooses what is in front.
        shots._stations.Add(new Station("shot", x, z, "the one place asked for",
            new Standpoint(0.2, camera0, pitch, settle, "near"),
            new Standpoint(9.0, camera0, pitch, settle, "mid"),
            new Standpoint(26.0, camera0, pitch * 0.5f, settle, "far")));
        return shots;
    }

    /// <summary>Advance one frame. The name of a screenshot to take now, "done" at the end, or null.</summary>
    public string? Update()
    {
        _frame++;
        if (_wait > 0)
        {
            _wait--;
            if (_wait > 0)
                return null;
            // The picture is taken, then the report line is written from where the character actually ended up.
            var station = _stations[_station];
            var standpoint = station.Standpoints[_standpoint];
            var shot = _controller.Authoritative;
            double x = shot.XMm / 1000.0, z = shot.ZMm / 1000.0;
            double range = Math.Sqrt((x - station.X) * (x - station.X) + (z - station.Z) * (z - station.Z));
            _report.AppendLine($"| {_station + 1}.{_standpoint + 1} | {station.Name} | ({station.X:0.0}, {station.Z:0.0}) | " +
                               $"({x:0.0}, {z:0.0}) | {shot.YMm / 1000.0:0.00} | {range:0.0} m | " +
                               $"yaw {_camera.Yaw:0.00} pitch {_camera.Pitch:0.00} | {_frame} | {standpoint.Shot}.png |");
            _standpoint++;
            _busy = false;
            if (_standpoint >= station.Standpoints.Length)
            {
                _station++;
                _standpoint = 0;
            }
            if (_station >= _stations.Count)
                return Finish();
            return standpoint.Shot;
        }

        if (_busy)
            return null;

        var next = _stations[_station];
        var plan = next.Standpoints[_standpoint];
        // Where to stand: the place itself, offset a little along the view so "near/mid/far" are distances the
        // camera looks ALONG rather than stand-off distances from the thing being looked at.
        var sight = new Vector3(-(float)Math.Sin(plan.YawOffset), 0, -(float)Math.Cos(plan.YawOffset));
        var wanted = new Vector3(
            (float)next.X + sight.X * (float)plan.Away,
            0,
            (float)next.Z + sight.Z * (float)plan.Away);
        var here = _controller.Authoritative;
        var to = new Vector3((float)(wanted.X - here.XMm / 1000.0), 0, (float)(wanted.Z - here.ZMm / 1000.0));
        if (to.Length() > 1.5f)
        {
            if (++_legsFailed > 8 * 60 * 20)
            {
                _report.AppendLine($"| {_station + 1}.{_standpoint + 1} | {next.Name} | ({next.X:0.0}, {next.Z:0.0}) | " +
                                   $"**could not reach** ({here.XMm / 1000.0:0.0}, {here.ZMm / 1000.0:0.0}) | - | - | - | - | {plan.Shot} NOT TAKEN |");
                _legsFailed = 0;
                _standpoint++;
                if (_standpoint >= next.Standpoints.Length)
                {
                    _station++;
                    _standpoint = 0;
                }
                return _station >= _stations.Count ? Finish() : null;
            }
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _camera.Pitch = -0.18f;
            return null;
        }

        _legsFailed = 0;
        // Arrived. Stand still, aim along the view, and hold: the camera turns and the frame settles before the
        // picture. The yaw is the standpoint's own view direction, the same one used to walk there - not the
        // bearing to the target plus an offset, which pointed the camera somewhere else entirely.
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        _camera.Yaw = (float)plan.YawOffset;
        _camera.Pitch = plan.Pitch;
        _busy = true;
        _wait = plan.Settle;
        return null;
    }

    /// <summary>The region's own places, from the layout the simulation walks on — not a list typed into this file.</summary>
    private void BuildStations()
    {
        var setup = _session.Setup;
        var layout = setup.Layout;
        var spawn = layout.Spawn;
        double sx = spawn.XMm / 1000.0, sz = spawn.ZMm / 1000.0;
        _places.AppendLine($"Region {layout.Id}: spawn ({sx:0.0}, {sz:0.0}) facing {spawn.FacingMdeg / 1000.0:0} deg, " +
                           $"{layout.Locations.Length} authored places, {layout.CellKeys.Length} cells.");
        foreach (var place in layout.Locations.OrderBy(l => l.Id, StringComparer.Ordinal))
            _places.AppendLine($"  {place.Id} at ({place.XMm / 1000.0:0.0}, {place.ZMm / 1000.0:0.0})");

        // The spawn, looking where a new character looks.
        _stations.Add(new Station("spawn", sx, sz, "where a new character starts",
            new Standpoint(6, 0, -0.10f, 40, "s01_spawn_ahead"),
            new Standpoint(6, Mathf.Pi / 2, -0.08f, 40, "s01_spawn_left"),
            new Standpoint(6, -Mathf.Pi / 2, -0.08f, 40, "s01_spawn_right")));

        // Every place the region authored, shot from near and from a little way off, plus a turned view.
        int n = 2;
        foreach (var place in layout.Locations.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            double x = place.XMm / 1000.0, z = place.ZMm / 1000.0;
            string tag = $"s{n:00}_{Sanitise(place.Id)}";
            _stations.Add(new Station(place.Id, x, z, "an authored place",
                new Standpoint(14, 0, -0.12f, 40, tag + "_near"),
                new Standpoint(45, 0, -0.10f, 40, tag + "_far"),
                new Standpoint(14, -0.9, -0.12f, 40, tag + "_turn")));
            n++;
        }

        // The ground itself, which belongs to no place: the open basin, the low wet corner, and the walls
        // (gen_terrain.py: north wall along z=200, south along z=0, west along x=0, gorge along z=100 at x=200).
        _stations.Add(new Station("basin_floor", 110, 60, "open moor, mid-basin",
            new Standpoint(8, 0, -0.06f, 40, "s90_basin_floor_near"),
            new Standpoint(30, 0, -0.06f, 40, "s90_basin_floor_far")));
        _stations.Add(new Station("bog_north_east", 150, 170, "the low wet corner the shelf cannot drain",
            new Standpoint(10, 0, -0.06f, 40, "s91_bog_near"),
            new Standpoint(40, 0.6, -0.06f, 40, "s91_bog_across")));
        _stations.Add(new Station("cliff_north", 100, 196, "the north wall, the weather side",
            new Standpoint(30, 0, 0.10f, 40, "s92_cliff_north_up"),
            new Standpoint(70, 0, 0.02f, 40, "s92_cliff_north_along")));
        _stations.Add(new Station("cliff_west", 4, 60, "the west wall",
            new Standpoint(30, 0, 0.10f, 40, "s93_cliff_west_up"),
            new Standpoint(12, 0.7, 0.06f, 40, "s93_cliff_west_along")));
        _stations.Add(new Station("cliff_south", 100, 4, "the south wall",
            new Standpoint(30, 0, 0.10f, 40, "s94_cliff_south_up"),
            new Standpoint(70, 0, 0.02f, 40, "s94_cliff_south_along")));

        // The village, from inside it and from the rise above it (gen_region_content.py puts the houses on the
        // dry south-west rise; these four standpoints look along each of its streets and down onto it).
        _stations.Add(new Station("village_above", 60, 30, "the village seen from the ground below it",
            new Standpoint(26, 0, -0.02f, 45, "s95_village_from_below")));
        _stations.Add(new Station("village_inside", 60, 90, "the village from the far side of its square",
            new Standpoint(34, 0, -0.04f, 45, "s95_village_inside_far"),
            new Standpoint(16, 0, -0.04f, 45, "s95_village_inside_near"),
            new Standpoint(16, 1.5, -0.04f, 45, "s95_village_inside_turn")));

        // The basin from height: stand on the village rise and on the walls, which is as high as a character can
        // get. (There is no fly camera in ordinary play, and a shot from one would not be a gameplay shot.)
        _stations.Add(new Station("basin_from_height", 90, 100, "the whole basin from the village rise",
            new Standpoint(55, 0, -0.26f, 45, "s96_basin_from_rise"),
            new Standpoint(55, 2.2, -0.20f, 45, "s96_basin_from_rise_east")));
    }

    private static string Sanitise(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (char c in id)
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        return builder.ToString();
    }

    private string Finish()
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(Directory, "survey.md"), _report.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(Directory, "places.txt"), _places.ToString());
        GD.Print($"UNNAMED survey: {_station} stations written to {Directory}");
        return "done";
    }
}
