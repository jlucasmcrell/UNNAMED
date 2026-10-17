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
    private readonly Dictionary<string, Node3D> _containers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _pickups = new(StringComparer.Ordinal);
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
        _pickups.Clear();
        foreach (var child in GetChildren().Where(child => child != _ground))
        {
            RemoveChild(child);
            child.QueueFree();
        }
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
                _containers[site.Key] = model;
                if (site.EmptyFlag is not null)
                    _pickups[site.Key] = model;
                _art.Coverage.Resolved("container", site.Key, look.Model!);
                continue;
            }
            _art.Coverage.Fallback("container", site.Key, look?.Model is { } id ? $"{_art.Why(id) ?? "not drawn"}: a brown box" : "no binding: a brown box", look?.Model);
            var chest = new MeshInstance3D { Name = site.Key, Mesh = new BoxMesh { Size = size }, MaterialOverride = Chest };
            body.Position = new Vector3(0, -size.Y / 2, 0);
            chest.AddChild(body);
            chest.Position = feet + new Vector3(0, size.Y / 2, 0);
            AddChild(chest);
            _containers[site.Key] = chest;
            if (site.EmptyFlag is not null)
                _pickups[site.Key] = chest;
        }
    }

    /// <summary>What is drawn for a container, by its key, or for a thing on the ground, by its instance; null when none is (for the outline).</summary>
    public Node3D? Shown(string key) => _containers.GetValueOrDefault(key) ?? _items.GetValueOrDefault(key);

    public void Refresh(Simulation simulation)
    {
        foreach (var container in simulation.Containers.Where(c => c.Site.EmptyFlag is not null))
            if (_pickups.TryGetValue(container.Site.Key, out var model))
                model.Visible = !container.Items.IsEmpty;
        foreach (var child in _ground.GetChildren())
            child.QueueFree();
        _items.Clear();
        var terrain = simulation.ActiveLayout.Space.Terrain;
        foreach (var item in simulation.WorldItems)
        {
            var feet = new Vector3(item.XMm / 1000f, terrain.HeightAtMm(item.XMm, item.ZMm) / 1000f, item.ZMm / 1000f);
            if (_bindings.GroundItem is { } look && Art.Fitting.Site(_art, look, feet) is { } model)
            {
                model.Name = item.Id.Value;
                _ground.AddChild(model);
                _items[item.Id.Value] = model;
                _art.Coverage.Resolved("ground_item", item.DefId, look.Model!);
                continue;
            }
            _art.Coverage.Fallback("ground_item", item.DefId, _bindings.GroundItem?.Model is { } id ? $"{_art.Why(id) ?? "not drawn"}: a small crate" : "no binding: a small crate",
                _bindings.GroundItem?.Model);
            var crate = new MeshInstance3D
            {
                Name = item.Id.Value,
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.3f) },
                MaterialOverride = Crate,
                Position = feet + new Vector3(0, 0.1f, 0),
            };
            _ground.AddChild(crate);
            _items[item.Id.Value] = crate;
        }
    }
}
