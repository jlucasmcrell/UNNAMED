// UNNAMED Presentation - resource nodes and crafting stations in the greybox (M3f)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws the region's resource nodes and crafting stations: ore in its rock, a stand of young ash, a hearth, an anvil. A
/// node looks spent while the simulation's view says it cannot be harvested - read each frame, never kept - so a stand
/// grows back on screen when its day turns. Stations are scenery only: the simulation needs one of a kind within reach.
/// </summary>
public partial class CraftingView : Node3D
{
    private readonly Dictionary<string, (Node3D Ready, Node3D Spent)> _nodes = new(StringComparer.Ordinal);
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

    /// <summary>The asset library and its bindings (the Phase-1 asset integration); without them, greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public void Build(RegionLayout layout)
    {
        var terrain = layout.Space.Terrain;
        foreach (var site in layout.Nodes)
        {
            var root = new Node3D
            {
                Name = site.Name,
                Position = new Vector3(site.XMm / 1000f, terrain.HeightAtMm(site.XMm, site.ZMm) / 1000f, site.ZMm / 1000f),
            };
            // The look follows the node's category (node.<category>.<name>), until the asset pipeline delivers real ones.
            var (ready, spent) = site.NodeDefId.Split('.')[1] == "wood" ? Stand() : Seam();
            // The asset library's look for a ready node, where the bindings give one; a seam whose rock already shows its vein draws nothing more.
            if (_bindings.Nodes.GetValueOrDefault(site.NodeDefId) is { } look)
            {
                if (look.HideReady)
                {
                    ready.Free();
                    ready = new Node3D();
                }
                else if (Art.Fitting.Site(_art, look, Vector3.Zero) is { } model)
                {
                    ready.Free();
                    ready = model;
                }
            }
            root.AddChild(ready);
            root.AddChild(spent);
            spent.Visible = false;
            _nodes[site.Name] = (ready, spent);
            AddChild(root);
        }
        foreach (var station in layout.Stations)
        {
            var look = _bindings.Stations.GetValueOrDefault(station.Kind);
            var root = (look is not null ? Art.Fitting.Site(_art, look, Vector3.Zero) : null) ?? station.Kind switch
            {
                "forge" => Hearth(),
                "anvil" => Anvil(),
                _ => Part(new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.6f) }, Palette.Wood, new Vector3(0, 0.4f, 0)),
            };
            // Something the station's scenery wants beside it (the bellows at the hearth).
            if (look?.Beside is { } beside && Art.Fitting.Site(_art, new Art.Placement(beside, Offset: look.Offset), Vector3.Zero) is { } extra)
                root.AddChild(extra);
            root.Name = station.Key;
            root.Position = new Vector3(station.XMm / 1000f, terrain.HeightAtMm(station.XMm, station.ZMm) / 1000f, station.ZMm / 1000f);
            AddChild(root);
        }
    }

    /// <summary>Show each node as the simulation has it now.</summary>
    public void Refresh(IEnumerable<NodeView> nodes)
    {
        foreach (var node in nodes)
        {
            if (!_nodes.TryGetValue(node.Name, out var look))
                continue;
            look.Ready.Visible = node.Ready;
            look.Spent.Visible = !node.Ready;
        }
    }

    /// <summary>Rust-streaked lumps standing out of the rock's face; worked out, only grey scars remain.</summary>
    private static (Node3D Ready, Node3D Spent) Seam()
    {
        Node3D Lumps(StandardMaterial3D material, float scale)
        {
            var group = new Node3D();
            foreach (var (size, at) in new[]
                     {
                         (new Vector3(0.45f, 0.35f, 0.3f), new Vector3(-0.3f, 0.45f, 0.1f)),
                         (new Vector3(0.35f, 0.3f, 0.35f), new Vector3(0.25f, 0.7f, 0.05f)),
                         (new Vector3(0.3f, 0.25f, 0.25f), new Vector3(0.05f, 1.0f, 0.15f)),
                     })
                group.AddChild(Part(new BoxMesh { Size = size * scale }, material, at));
            return group;
        }
        return (Lumps(Palette.Ore, 1f), Lumps(Palette.WorkedOut, 0.6f));
    }

    /// <summary>Three straight young trunks; cut, three stumps.</summary>
    private static (Node3D Ready, Node3D Spent) Stand()
    {
        var ready = new Node3D();
        var spent = new Node3D();
        foreach (var at in new[] { new Vector3(-0.35f, 0, 0.2f), new Vector3(0.3f, 0, 0.3f), new Vector3(0, 0, -0.35f) })
        {
            ready.AddChild(Part(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 2.8f }, Palette.AshBark, at + new Vector3(0, 1.4f, 0)));
            ready.AddChild(Part(new SphereMesh { Radius = 0.5f, Height = 0.9f }, Palette.Leaves, at + new Vector3(0, 2.8f, 0)));
            spent.AddChild(Part(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.25f }, Palette.AshBark, at + new Vector3(0, 0.125f, 0)));
        }
        return (ready, spent);
    }

    /// <summary>A stone hearth with its fire banked in the top, and a little warm light.</summary>
    private static Node3D Hearth()
    {
        var root = new Node3D();
        root.AddChild(Part(new BoxMesh { Size = new Vector3(1.2f, 0.9f, 1.0f) }, Palette.Hearth, new Vector3(0, 0.45f, 0)));
        root.AddChild(Part(new BoxMesh { Size = new Vector3(0.8f, 0.08f, 0.6f) }, Palette.Embers, new Vector3(0, 0.92f, 0)));
        root.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.25f), LightEnergy = 1.2f, OmniRange = 4f, Position = new Vector3(0, 1.3f, 0) });
        return root;
    }

    /// <summary>A dark iron block on a wooden stump.</summary>
    private static Node3D Anvil()
    {
        var root = new Node3D();
        root.AddChild(Part(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = 0.5f }, Palette.Wood, new Vector3(0, 0.25f, 0)));
        root.AddChild(Part(new BoxMesh { Size = new Vector3(0.62f, 0.24f, 0.26f) }, Palette.Iron, new Vector3(0, 0.62f, 0)));
        var horn = Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.12f, Height = 0.22f }, Palette.Iron, new Vector3(0.4f, 0.64f, 0));
        horn.RotationDegrees = new Vector3(0, 0, 90);
        root.AddChild(horn);
        return root;
    }

    private static MeshInstance3D Part(Mesh mesh, Material material, Vector3 position) =>
        new() { Mesh = mesh, MaterialOverride = material, Position = position };
}
