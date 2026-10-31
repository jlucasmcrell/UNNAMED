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
    private readonly Dictionary<string, Node3D> _falling = new(StringComparer.Ordinal);
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
        _falling.Clear();
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
                if (Player.PlayerController.IsPickup(site))
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
            if (Player.PlayerController.IsPickup(site))
                _pickups[site.Key] = chest;
        }
    }

    /// <summary>What is drawn for a container, by its key, or for a thing on the ground, by its instance; null when none is (for the outline).</summary>
    public Node3D? Shown(string key) => _containers.GetValueOrDefault(key) ?? _items.GetValueOrDefault(key);

    public void Refresh(Simulation simulation, Node3D? releasedWeapon = null, string? releasedDefinition = null)
    {
        var arriving = releasedWeapon is null ? null : simulation.WorldItems.FirstOrDefault(item =>
            item.DefId == releasedDefinition && !_items.ContainsKey(item.Id.Value));
        foreach (var container in simulation.Containers.Where(c => Player.PlayerController.IsPickup(c.Site)))
            if (_pickups.TryGetValue(container.Site.Key, out var model))
                model.Visible = !container.Items.IsEmpty;
        foreach (var child in _ground.GetChildren())
            child.QueueFree();
        _items.Clear();
        var terrain = simulation.ActiveLayout.Space.Terrain;
        foreach (var item in simulation.WorldItems)
        {
            var feet = new Vector3(item.XMm / 1000f, terrain.HeightAtMm(item.XMm, item.ZMm) / 1000f, item.ZMm / 1000f);
            // A knocked-away weapon remains the weapon that left the hand. A dedicated
            // ground binding still wins; ordinary weapons use their actual native model.
            if (!_bindings.GroundItems.ContainsKey(item.DefId)
                && _bindings.Weapons.TryGetValue(item.DefId, out var weapon)
                && GroundWeapon(weapon.Model) is { } grounded)
            {
                grounded.Name = item.Id.Value;
                grounded.SetMeta("item_def", item.DefId);
                grounded.Position = feet;
                float Height(float dx, float dz) => terrain.HeightAtMm(item.XMm + (long)(dx * 1000), item.ZMm + (long)(dz * 1000)) / 1000f;
                var normal = new Vector3(Height(-.5f, 0) - Height(.5f, 0), 1,
                    Height(0, -.5f) - Height(0, .5f)).Normalized();
                grounded.Basis = new Basis(new Quaternion(Vector3.Up, normal));
                _ground.AddChild(grounded);
                _items[item.Id.Value] = grounded;
                _art.Coverage.Resolved("ground_item", item.DefId, weapon.Model);
                continue;
            }
            // The item's own look where the bindings give one (a hide, a boot, an ingot), else the shared sack.
            var drop = _bindings.GroundItems.GetValueOrDefault(item.DefId) ?? _bindings.GroundItem;
            if (drop is { } look && Art.Fitting.Site(_art, look, feet) is { } model)
            {
                model.Name = item.Id.Value;
                _ground.AddChild(model);
                _items[item.Id.Value] = model;
                _art.Coverage.Resolved("ground_item", item.DefId, look.Model!);
                continue;
            }
            _art.Coverage.Fallback("ground_item", item.DefId, drop?.Model is { } id ? $"{_art.Why(id) ?? "not drawn"}: a small crate" : "no binding: a small crate",
                drop?.Model);
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
        foreach (string id in _falling.Keys.Where(id => !_items.ContainsKey(id)).ToArray())
        {
            _falling[id].QueueFree();
            _falling.Remove(id);
        }
        foreach (string id in _falling.Keys) _items[id].Visible = false;
        if (releasedWeapon is not null)
        {
            if (arriving is not null && _items.TryGetValue(arriving.Id.Value, out var destination)
                && destination.GetNodeOrNull<Node3D>("Turn/Weapon") is { } final)
                Fall(arriving.Id.Value, releasedWeapon, destination, final.GlobalTransform);
            else
                releasedWeapon.QueueFree();
        }
    }

    private void Fall(string id, Node3D model, Node3D destination, Transform3D landing)
    {
        model.Reparent(this, keepGlobalTransform: true);
        var start = model.GlobalTransform;
        destination.Visible = false;
        _falling[id] = model;
        float seconds = Mathf.Clamp(Mathf.Sqrt(2 * MathF.Max(.1f, start.Origin.Y - landing.Origin.Y) / 9.8f), .18f, .7f);
        var tween = model.CreateTween();
        model.SetMeta("ground_fall", tween);
        tween.TweenMethod(Callable.From<float>(through =>
        {
            var pose = start.InterpolateWith(landing, through);
            pose.Origin += Vector3.Up * (.12f * 4 * through * (1 - through));
            model.GlobalTransform = pose;
        }), 0f, 1f, seconds);
        tween.TweenCallback(Callable.From(() =>
        {
            _falling.Remove(id);
            if (_items.TryGetValue(id, out var settled)) settled.Visible = true;
            model.QueueFree();
        }));
    }

    /// <summary>The native prop removed by the authoritative pickup, before Refresh retires its old holder.</summary>
    public Node3D? TakeWeapon(Simulation simulation, string definition)
    {
        var present = simulation.WorldItems.Select(item => item.Id.Value).ToHashSet(StringComparer.Ordinal);
        var removed = _items.FirstOrDefault(pair => !present.Contains(pair.Key)
            && pair.Value.GetMeta("item_def", "").AsString() == definition);
        if (removed.Value is null) return null;
        var prop = _falling.Remove(removed.Key, out var falling)
            ? falling : removed.Value.GetNodeOrNull<Node3D>("Turn/Weapon");
        if (prop is null) return null;
        if (falling is not null && removed.Value.GetNodeOrNull<Node3D>("Turn/Weapon") is { } resting)
            prop.SetMeta("pickup_rest", resting.GlobalTransform);
        if (prop.HasMeta("ground_fall") && prop.GetMeta("ground_fall").AsGodotObject() is Tween tween)
        {
            tween.Kill();
            prop.RemoveMeta("ground_fall");
        }
        prop.Reparent(this, keepGlobalTransform: true);
        _items.Remove(removed.Key);
        return prop;
    }

    private Node3D? GroundWeapon(string id)
    {
        if (_art.Model(id) is not { } model) return null;
        var box = Art.ArtGallery.Bounds(model);
        int along = Enumerable.Range(0, 3).MaxBy(i => box.Size[i]);
        int thin = Enumerable.Range(0, 3).Where(i => i != along).MinBy(i => box.Size[i]);
        Vector3 Axis(int i) => i == 0 ? Vector3.Right : i == 1 ? Vector3.Up : Vector3.Back;
        var up = Axis(thin);
        var forward = Axis(along);
        model.Name = "Weapon";
        var turn = new Node3D { Name = "Turn", Basis = new Basis(up.Cross(forward), up, forward).Inverse() };
        turn.AddChild(model);
        var holder = new Node3D();
        holder.AddChild(turn);
        var flat = Art.ArtGallery.Bounds(holder);
        turn.Position = new Vector3(-flat.GetCenter().X, -flat.Position.Y + .01f, -flat.GetCenter().Z);
        return holder;
    }
}
