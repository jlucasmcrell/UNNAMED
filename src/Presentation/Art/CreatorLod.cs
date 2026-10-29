// UNNAMED Presentation - a HumGen creator body and its parts drawn at the level of detail the camera is far enough away for.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The four-level ladder the creator export ships: every piece arrives as <c>_LOD0</c>..<c>_LOD3</c>, and drawing one level at a time
/// is both the quality band and the z-fight fix (the copies are near-coincident, so more than one drawn anywhere fights). A level is
/// chosen from how far the camera is from the body and held until the distance crosses the next threshold, so a face sheet at 2 m sees
/// LOD0 and a town seen from across the road does not.
/// </summary>
public partial class CreatorLod : Node
{
    /// <summary>Where each level starts, in metres: LOD0 under 8 m, LOD1 to 18, LOD2 to 40, LOD3 beyond.</summary>
    private static readonly float[] Starts = { 0f, 8f, 18f, 40f };

    private static readonly Regex Level = new("_LOD(?<level>[0-3])(?![0-9])", RegexOptions.Compiled);

    private readonly List<(Node3D Owner, MeshInstance3D Mesh, int Level)> _meshes = new();
    private readonly List<MeshInstance3D> _plain = new();
    private Node3D? _body;
    private double _since;
    private int _shown = -1;

    /// <summary>Attach the ladder to <paramref name="model"/>, which is a creator body with its parts already joined to it.</summary>
    public static CreatorLod Attach(Node3D model)
    {
        var lod = model.GetNodeOrNull<CreatorLod>("CreatorLod") ?? new CreatorLod { Name = "CreatorLod" };
        if (lod.GetParent() is null)
            model.AddChild(lod);
        lod._body = model;
        lod.Scan();
        return lod;
    }

    /// <summary>Take the model's pieces in again after a part was put on or taken off.</summary>
    public void Scan()
    {
        _meshes.Clear();
        _plain.Clear();
        if (_body is null)
            return;
        foreach (var mesh in _body.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (mesh is not MeshInstance3D drawn)
                continue;
            var match = Level.Match(drawn.Name.ToString());
            if (match.Success && int.TryParse(match.Groups["level"].Value, out int level))
                _meshes.Add((drawn, drawn, level));
            else
                _plain.Add(drawn);
        }
        _shown = -1;
        _since = 0;
    }

    public override void _Process(double delta)
    {
        // Four times a second is plenty: a level change is only worth a frame of lag, and the walk through every piece is not free.
        _since += delta;
        if (_since < .25 || _body is null || _meshes.Count == 0)
            return;
        _since = 0;
        var camera = GetViewport().GetCamera3D();
        if (camera is null)
            return;
        float distance = camera.GlobalPosition.DistanceTo(_body.GlobalPosition);
        int level = 0;
        for (int i = Starts.Length - 1; i > 0; i--)
            if (distance >= Starts[i])
            {
                level = i;
                break;
            }
        if (level == _shown)
            return;
        _shown = level;
        foreach (var (_, mesh, at) in _meshes)
            if (GodotObject.IsInstanceValid(mesh))
                mesh.Visible = at == level;
        // A piece with no level of its own (wings, brows, a clasp) is drawn at every distance.
        foreach (var mesh in _plain)
            if (GodotObject.IsInstanceValid(mesh))
                mesh.Visible = true;
    }
}
