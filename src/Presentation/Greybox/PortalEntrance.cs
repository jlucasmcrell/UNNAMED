// UNNAMED Presentation - a doorway at an exterior portal anchor (review 3, B6)
// The way into the ring used to be an interact prompt in mid-air. This is the thing that prompt stands in: a stone
// doorframe with a dark opening, sized to the body, so the entrance reads as an entrance from the road.

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

public static class PortalEntrance
{
    /// <summary>The frame and its opening, standing at the portal's exterior anchor and facing away from it.</summary>
    public static Node3D Build(SpacePortal portal, RegionLayout layout, Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        var root = new Node3D { Name = $"Entrance_{portal.Key}" };
        long xMm = portal.ExteriorXMm, zMm = portal.ExteriorZMm;
        float y = layout.Space.Terrain.HeightAtMm(xMm, zMm) / 1000f;
        root.Position = new Vector3(xMm / 1000f, y, zMm / 1000f);
        if (bindings.Structure(portal.Key) is { Model: not null } look
            && art.ModelWithLods(look.Model) is { } model)
        {
            // The authored origin is the mouth. Recentering an asymmetric berm moves it off the real portal.
            model.Position = look.Offset;
            root.AddChild(model);
            return root;
        }

        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.32f, 0.29f), Roughness = 0.95f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.025f), Roughness = 1f };

        // Two jambs and a lintel: a doorframe a body fits through, 2.2 m high and 1.6 m wide.
        const float halfWidth = 0.95f, height = 2.4f, jamb = 0.35f, thick = 0.5f;
        Add(root, new Vector3(-halfWidth - jamb / 2, height / 2, 0), new Vector3(jamb, height, thick), stone);
        Add(root, new Vector3(halfWidth + jamb / 2, height / 2, 0), new Vector3(jamb, height, thick), stone);
        Add(root, new Vector3(0, height + 0.18f, 0), new Vector3(halfWidth * 2 + jamb * 2, 0.36f, thick), stone);
        // And the opening itself, so the frame does not read as a hole onto the road behind it.
        Add(root, new Vector3(0, height / 2, -0.2f), new Vector3(halfWidth * 2, height, 0.04f), dark);
        return root;
    }

    private static void Add(Node3D root, Vector3 at, Vector3 size, Material material)
    {
        var box = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size, Material = material },
            Position = at,
        };
        root.AddChild(box);
    }
}
