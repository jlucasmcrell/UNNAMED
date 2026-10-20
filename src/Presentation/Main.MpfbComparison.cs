// UNNAMED Presentation - the MakeHuman comparison gate's close-up set, photographed from the packaged game
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--mpfb-shots &lt;dir&gt;</c>: the comparison gate's set - the face from the front, both feet at front, 45 and 90 degrees, both wrists
/// and the crotch - on the player and on two of the nearest NPCs, plus one full body each to say which figure it was. Every frame is of
/// the packaged game's own bodies, clips and lighting: the shots are held until <c>--autostart</c>'s Begin has put an avatar in the world,
/// and the figures framed are the very ones the game draws.
/// <para>
/// THE CAMERA LOOKS FROM THE FIGURE'S OWN +Z, because that is the side the face is on. Every MakeHuman export in the cast carries its
/// eyes and eyebrows at z = +0.105..+0.141 and its hair at z = -0.062, and a bound figure faces its own +Z - which is the side
/// <c>Main.MetaHumanReview</c> frames the face from as well. The first pass of this gate put the camera at -Z and photographed
/// the back of the head.
/// </para>
/// </summary>
public partial class Main
{
    private const string MpfbShotsOption = "--mpfb-shots";

    /// <summary>One close-up: what it is called, the bone it is aimed at, which way round the figure, how far, and how far above the bone.</summary>
    private static readonly (string Name, string[] Bones, float Yaw, float Distance, float AimRise)[] MpfbShotList =
    [
        ("face", ["head"], 0f, 0.45f, 0.06f),
        ("feet_front", ["foot_l", "foot.L"], 0f, 0.55f, -0.03f),
        ("feet_45", ["foot_l", "foot.L"], Mathf.Pi / 4, 0.55f, -0.03f),
        ("feet_90", ["foot_l", "foot.L"], Mathf.Pi / 2, 0.55f, -0.03f),
        ("wrist_l", ["hand_l", "hand.L"], Mathf.Pi / 8, 0.30f, 0f),
        ("wrist_r", ["hand_r", "hand.R"], -Mathf.Pi / 8, 0.30f, 0f),
        ("crotch", ["pelvis"], 0f, 0.60f, 0f),
        ("full", ["spine_02", "pelvis"], 0f, 3.4f, 0f),
    ];

    private string? _mpfbShotsDirectory;
    private readonly List<(SkinnedFigure Figure, string Person)> _mpfbFigures = new();
    private int _mpfbFigure, _mpfbShot;
    private ulong _mpfbNext;
    private bool _mpfbBusy;
    private double _mpfbSettle;

    private void InstallMpfbComparison()
    {
        if (_options.GetValueOrDefault(MpfbShotsOption) is not { Length: > 0 } directory)
            return;
        _mpfbShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_mpfbShotsDirectory);
        _mpfbNext = Time.GetTicksMsec() + 3000;
        GD.Print("MPFB_SHOTS installed " + _mpfbShotsDirectory);
    }

    private void TickMpfbComparison()
    {
        if (_mpfbShotsDirectory is null || _mpfbBusy || Time.GetTicksMsec() < _mpfbNext)
            return;
        try { AdvanceMpfbComparison(); }
        catch (Exception error)
        {
            GD.PushError("MPFB_SHOTS failed: " + error);
            if (_mpfbShotsDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            _mpfbShotsDirectory = null;
            GetTree().Quit(3);
        }
    }

    private void AdvanceMpfbComparison()
    {
        if (_mpfbStepZero())
            return;
        if (_mpfbFigure >= _mpfbFigures.Count)
        {
            File.WriteAllText(Path.Combine(_mpfbShotsDirectory!, "complete.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "MPFB_SHOTS_COMPLETE", people = VisualOptions.People,
                figures = _mpfbFigures.Select(f => new { f.Person, model = _bindings.People.GetValueOrDefault(f.Person)?.Model }).ToArray(),
                shots = _mpfbFigures.Count * MpfbShotList.Length,
                avatar = (_avatar as SkinnedFigure) is not null,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MPFB_SHOTS complete " + _mpfbShotsDirectory);
            _mpfbShotsDirectory = null;
            GetTree().Quit();
            return;
        }
        var (figure, person) = _mpfbFigures[_mpfbFigure];
        var shot = MpfbShotList[_mpfbShot];
        if (!FrameMpfbShot(figure, shot, out Vector3 aim))
        {
            _mpfbShot++;
            return;
        }
        // The first frame after a camera move contains the move, not the pose: hold the framing before reading the viewport.
        _mpfbSettle += GetProcessDeltaTime();
        if (_mpfbSettle < 0.6)
            return;
        string name = $"{person}_{shot.Name}";
        SaveScreenshot(_mpfbShotsDirectory!, name);
        File.WriteAllText(Path.Combine(_mpfbShotsDirectory!, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            person, bone = shot.Name, model = _bindings.People.GetValueOrDefault(person)?.Model,
            clip = figure.ClipState, aim = aim.ToString(), camera = _camera.Camera.GlobalPosition.ToString(),
            stature = _bindings.People.GetValueOrDefault(person)?.UniformScale,
            options = string.Join(",", VisualOptions.All.Select(kv => $"{kv.Key}={kv.Value}")),
            world = "packaged game world: normal lighting; diagnostic camera only",
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        GD.Print("MPFB_SHOTS frame " + name);
        _mpfbSettle = 0;
        if (++_mpfbShot >= MpfbShotList.Length)
        {
            _mpfbShot = 0;
            _mpfbFigure++;
        }
    }

    /// <summary>Collects the figures once the world exists, then hands the shots back to <see cref="AdvanceMpfbComparison"/>.</summary>
    private bool _mpfbStepZero()
    {
        if (_mpfbFigures.Count > 0)
            return false;
        if (_session.Simulation is null || _avatar is not SkinnedFigure player || !player.IsInsideTree())
        {
            _mpfbNext = Time.GetTicksMsec() + 500;
            return true;
        }
        player.SetFirstPerson(false);
        _mpfbFigures.Add((player, "player"));
        var actors = _npcs.States()
            .Select(n => (n.Id, Figure: _npcs.FigureOf(n.Id) as SkinnedFigure))
            .Where(n => n.Figure is not null && n.Figure.IsInsideTree())
            .OrderBy(n => n.Figure!.GlobalPosition.DistanceSquaredTo(player.GlobalPosition))
            .Take(2).ToArray();
        if (actors.Length < 2)
        {
            _mpfbNext = Time.GetTicksMsec() + 1000;
            return true;
        }
        foreach (var actor in actors)
            _mpfbFigures.Add((actor.Figure!, actor.Id));
        GD.Print($"MPFB_SHOTS figures: {string.Join(", ", _mpfbFigures.Select(f => f.Person))}");
        return false;
    }

    /// <summary>Puts the camera on the shot's bone from the figure's own +Z, or refuses when the rig lacks the bone.</summary>
    private bool FrameMpfbShot(SkinnedFigure figure, (string Name, string[] Bones, float Yaw, float Distance, float AimRise) shot, out Vector3 aim)
    {
        aim = Vector3.Zero;
        int bone = shot.Bones.Select(figure.Skeleton.FindBone).FirstOrDefault(b => b >= 0, -1);
        if (bone < 0)
        {
            GD.PushWarning($"MPFB_SHOTS: {shot.Name} has no bone {string.Join("/", shot.Bones)} on {figure.Name}; shot skipped");
            return false;
        }
        figure.SetFirstPerson(false);
        _scatter.Visible = shot.Name == "full";   // Grass out of the close-ups, as the MetaHuman review keeps it.
        aim = figure.Skeleton.GlobalTransform * figure.Skeleton.GetBoneGlobalPose(bone).Origin + Vector3.Up * shot.AimRise;
        var direction = figure.GlobalBasis.Z.Rotated(Vector3.Up, shot.Yaw).Normalized();
        _camera.Camera.GlobalPosition = aim + direction * shot.Distance + Vector3.Up * (shot.Name == "face" ? 0f : 0.04f);
        _camera.Camera.LookAt(aim);
        return true;
    }
}
