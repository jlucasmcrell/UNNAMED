// UNNAMED Presentation - items lying in the world and the authored containers (M3b)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws each item lying in the world as a small crate and each authored container as a chest. It redraws from the
/// simulation's views when an item moves; it holds no truth of its own.
/// </summary>
public partial class ItemsView : Node3D
{
    private readonly Node3D _ground = new() { Name = "GroundItems" };
    private static readonly StandardMaterial3D Crate = new() { AlbedoColor = new Color(0.72f, 0.62f, 0.38f) };
    private static readonly StandardMaterial3D Chest = new() { AlbedoColor = new Color(0.38f, 0.26f, 0.14f) };
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

    public override void _Ready() => AddChild(_ground);

    /// <summary>The asset library and its bindings (the Phase-1 asset integration); without them, greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public void BuildContainers(RegionLayout layout)
    {
        foreach (var site in layout.Containers)
        {
            var feet = new Vector3(site.XMm / 1000f, layout.Space.Terrain.HeightAtMm(site.XMm, site.ZMm) / 1000f, site.ZMm / 1000f);
            var size = new Vector3(0.9f, 0.6f, 0.6f);
            var look = _bindings.Containers.GetValueOrDefault(site.Key);
            // A container another model already shows (the cart holding the bow) draws nothing of its own.
            if (look?.Hidden == true)
                continue;
            var body = new StaticBody3D { CollisionLayer = HollowView.CameraCollisionLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2, 0) });
            if (look is not null && Art.Fitting.Site(_art, look, feet) is { } model)
            {
                model.Name = site.Key;
                model.AddChild(body);
                AddChild(model);
                continue;
            }
            var chest = new MeshInstance3D { Name = site.Key, Mesh = new BoxMesh { Size = size }, MaterialOverride = Chest };
            body.Position = new Vector3(0, -size.Y / 2, 0);
            chest.AddChild(body);
            chest.Position = feet + new Vector3(0, size.Y / 2, 0);
            AddChild(chest);
        }
    }

    public void Refresh(Simulation simulation)
    {
        foreach (var child in _ground.GetChildren())
            child.QueueFree();
        var terrain = simulation.Setup.Layout.Space.Terrain;
        foreach (var item in simulation.WorldItems)
        {
            var feet = new Vector3(item.XMm / 1000f, terrain.HeightAtMm(item.XMm, item.ZMm) / 1000f, item.ZMm / 1000f);
            if (_bindings.GroundItem is { } look && Art.Fitting.Site(_art, look, feet) is { } model)
            {
                model.Name = item.Id.Value;
                _ground.AddChild(model);
                continue;
            }
            _ground.AddChild(new MeshInstance3D
            {
                Name = item.Id.Value,
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.3f) },
                MaterialOverride = Crate,
                Position = feet + new Vector3(0, 0.1f, 0),
            });
        }
    }
}
