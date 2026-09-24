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

    public override void _Ready() => AddChild(_ground);

    public void BuildContainers(RegionLayout layout)
    {
        foreach (var site in layout.Containers)
        {
            var size = new Vector3(0.9f, 0.6f, 0.6f);
            var chest = new MeshInstance3D { Name = site.Key, Mesh = new BoxMesh { Size = size }, MaterialOverride = Chest };
            var body = new StaticBody3D { CollisionLayer = HollowView.CameraCollisionLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            chest.AddChild(body);
            chest.Position = new Vector3(site.XMm / 1000f, layout.Space.Terrain.HeightAtMm(site.XMm, site.ZMm) / 1000f + size.Y / 2, site.ZMm / 1000f);
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
            _ground.AddChild(new MeshInstance3D
            {
                Name = item.Id.Value,
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.3f) },
                MaterialOverride = Crate,
                Position = new Vector3(item.XMm / 1000f, terrain.HeightAtMm(item.XMm, item.ZMm) / 1000f + 0.1f, item.ZMm / 1000f),
            });
        }
    }
}
