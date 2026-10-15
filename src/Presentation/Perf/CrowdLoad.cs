// UNNAMED Presentation - the character budget's load: bound people stood along the camera's view (MetaHuman pipeline, Phase 1.4)
// Godot presentation only: a harness that draws figures and poses them itself; it submits no command and reads no game state (D-11)

using System.Diagnostics;
using Godot;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Perf;

/// <summary>
/// <c>UNNAMED_PERF_CROWD="&lt;person&gt;:&lt;count&gt;[,...]"</c>: that many figures of each bound person (art bindings' <c>people</c>), stood
/// along the active camera's view from 4 to 45 m - so every level of a character's LOD chain is on screen - every other one walking a
/// small circle at the simulation's walk speed, the rest idle and facing the camera. Re-laid out whenever the camera jumps (a new
/// visual-audit shot). Prints, every 120 frames, the median time the figures' own posing took, the process time, and the renderer's
/// buffer and texture memory, so the memo's budget (characters at most 2 ms GPU and 1.5 ms CPU in the busiest village view) is read
/// as the difference between a run with the crowd and one without.
/// </summary>
public sealed partial class CrowdLoad : Node3D
{
    private static readonly float[] Distances = { 4, 6, 8, 10, 12, 15, 18, 22, 26, 30, 35, 40, 45 };
    private const float WalkSpeed = 1.6f, WalkRadius = 1.2f;

    private readonly List<(Figure Figure, string Person)> _figures = new();
    private readonly Func<float, float, float> _ground;
    private readonly List<double> _poseMs = new(), _processMs = new();
    private Vector3[] _spots = Array.Empty<Vector3>();
    private Vector3 _laidOutFrom = new(float.NaN, 0, 0);
    private double _clock;

    public CrowdLoad(string spec, ArtLibrary art, ArtBindings bindings, Func<float, float, float> ground)
    {
        _ground = ground;
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string person = part.Split(':')[0];
            int count = part.Contains(':') ? int.Parse(part.Split(':')[1]) : 1;
            for (int i = 0; i < count; i++)
            {
                if (SkinnedFigure.Create(art, bindings, person) is not { } figure)
                {
                    GD.PrintErr($"UNNAMED perf crowd: {person} could not be drawn ({art.Why(bindings.People.GetValueOrDefault(person)?.Model ?? person) ?? "no binding"})");
                    break;
                }
                figure.Name = $"Crowd{_figures.Count}";
                AddChild(figure);
                if (VisualOptions.BodyModifiers)
                    BodyModifiers.PlantFeet(figure.Skeleton, figure, ground);
                _figures.Add((figure, person));
            }
        }
        GD.Print($"UNNAMED perf crowd: {_figures.Count} figures ({spec})");
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GetCamera3D() is not { } camera || _figures.Count == 0)
            return;
        var eye = camera.GlobalPosition;
        if (float.IsNaN(_laidOutFrom.X) || eye.DistanceTo(_laidOutFrom) > 1f)
            LayOut(camera);
        _clock += delta;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < _figures.Count; i++)
        {
            var spot = _spots[i];
            float speed = 0, facing;
            Vector3 feet;
            if (i % 2 == 1)
            {
                float angle = (float)(_clock * WalkSpeed / WalkRadius) + i;
                feet = spot + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * WalkRadius;
                var ahead = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));
                facing = Mathf.Atan2(ahead.X, ahead.Z);
                speed = WalkSpeed;
            }
            else
            {
                feet = spot;
                facing = Mathf.Atan2(eye.X - spot.X, eye.Z - spot.Z);
            }
            feet.Y = _ground(feet.X, feet.Z);
            _figures[i].Figure.Pose(feet, facing, speed, delta);
        }
        _poseMs.Add(watch.Elapsed.TotalMilliseconds);
        _processMs.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
        if (_poseMs.Count >= 120)
        {
            GD.Print($"UNNAMED perf crowd: {_figures.Count} figures, posing {Median(_poseMs):0.000} ms median, process {Median(_processMs):0.00} ms median, "
                     + $"buffers {Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576:0} MB, "
                     + $"textures {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576:0} MB, "
                     + $"video {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0} MB");
            _poseMs.Clear();
            _processMs.Clear();
        }
    }

    /// <summary>Each figure a spot along the camera's level view, alternately left and right of its line, and which LOD level shows there.</summary>
    private void LayOut(Camera3D camera)
    {
        _laidOutFrom = camera.GlobalPosition;
        var forward = -camera.GlobalTransform.Basis.Z;
        forward = new Vector3(forward.X, 0, forward.Z).Normalized();
        var side = forward.Cross(Vector3.Up).Normalized();
        _spots = new Vector3[_figures.Count];
        var levels = new List<string>();
        for (int i = 0; i < _figures.Count; i++)
        {
            float d = i < Distances.Length ? Distances[i] : Distances[^1] + 5 * (i - Distances.Length + 1);
            var spot = _laidOutFrom + forward * d + side * ((i % 2 == 0 ? 1 : -1) * (1.5f + i % 3));
            _spots[i] = new Vector3(spot.X, _ground(spot.X, spot.Z), spot.Z);
            levels.Add($"{d:0}m:{Level(_figures[i].Figure, _laidOutFrom.DistanceTo(_spots[i]))}");
        }
        GD.Print($"UNNAMED perf crowd: laid out from {_laidOutFrom} - distance:LOD shown {string.Join(" ", levels)}");
    }

    /// <summary>The LOD meshes a figure draws at <paramref name="distance"/>, from their visibility ranges (CharacterMaterials.ApplyLods).</summary>
    private static string Level(Node figure, float distance)
    {
        var shown = figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Where(m => distance >= m.VisibilityRangeBegin && (m.VisibilityRangeEnd <= 0 || distance < m.VisibilityRangeEnd))
            .Select(m => m.Name.ToString() is var n && n.Contains("_LOD") ? n[(n.LastIndexOf("_LOD") + 1)..] : "LOD0")
            .Distinct().OrderBy(n => n);
        return string.Join("+", shown);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
