// UNNAMED Presentation - a model from the asset library stood where the simulation's structure is, at the model's authored size
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Stands a model where the simulation's structure is, at its authored size: an asset's semantic size is authoritative, so nothing here
/// rescales one to a collision shape. It is centred on the structure, its longer side laid along the structure's, and stood on the
/// terrain. The structure the body collides with stays the world's truth, so where the two disagree - the model would leave more than
/// half a metre of invisible wall at a side, or stop more than 0.6 m below the structure's top - the greybox stands instead and the
/// mismatch is reported. A modular piece (<c>tile</c>: a fence panel) repeats at its own length along the run, never stretched. An
/// overhang (a structure with a clearance: a beam to crouch under) stands from its clearance up and is measured by its thickness.
/// </summary>
public static class Fitting
{
    private const float SideMargin = 0.5f;
    private const float HeightShortfall = 0.6f;

    /// <summary>An overhang's model may be this much thinner than the overhang (an invisible bar under it would stop a standing body)...</summary>
    private const float OverhangShortfall = 0.15f;

    /// <summary>...and this much thicker (it rises above the overhang's top; it never hangs below its clearance).</summary>
    private const float OverhangExcess = 0.3f;

    /// <summary>The model for a structure, at its own size and stood on the terrain; null without a model or when it does not fit.</summary>
    public static Node3D? Structure(ArtLibrary art, Placement look, Blocker blocker, TerrainGrid terrain)
    {
        if (look.Model is null || art.ModelWithLods(look.Model) is not { } model)
            return null;
        var (cx, cz) = Footprints.Center(blocker);
        float width, depth;
        bool round = blocker is CircleBlocker;
        switch (blocker)
        {
            case CircleBlocker circle:
                width = depth = 2 * circle.RadiusMm / 1000f;
                break;
            case BoxBlocker box:
                width = (box.MaxXMm - box.MinXMm) / 1000f;
                depth = (box.MaxZMm - box.MinZMm) / 1000f;
                break;
            default:
                model.Free();
                return null;
        }
        float height = blocker.HeightMm / 1000f;
        // An overhang (a beam a crouched body passes under) stands from its clearance up, as its greybox does, and is measured by its thickness.
        float clearance = blocker is BoxBlocker { ClearanceMm: > 0 } overhang ? overhang.ClearanceMm / 1000f : 0;
        var bounds = ArtGallery.Bounds(model);
        // Lay the model's longer side along the structure's (a round structure has no long side).
        bool turn = !round && (bounds.Size.X >= bounds.Size.Z) != (width >= depth);
        var size = turn ? new Vector3(bounds.Size.Z, bounds.Size.Y, bounds.Size.X) : bounds.Size;
        var root = new Node3D { Name = blocker.Id };
        if (look.Fit == "tile")
        {
            float run = Math.Max(width, depth), piece = Math.Max(0.01f, Math.Max(size.X, size.Z));
            int count = Math.Max(1, (int)Math.Floor(run / piece + 0.01f));
            for (int i = 0; i < count; i++)
            {
                var copy = i == 0 ? model : (Node3D)model.Duplicate();
                float along = (i + 0.5f - count / 2f) * piece;
                copy.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
                var holder = new Node3D
                {
                    Position = width >= depth ? new Vector3(along, 0, 0) : new Vector3(0, 0, along),
                    Rotation = new Vector3(0, turn ? Mathf.Pi / 2 : 0, 0),
                };
                holder.AddChild(copy);
                root.AddChild(holder);
            }
        }
        else
        {
            float coveredX = round ? Math.Max(size.X, size.Z) : size.X, coveredZ = round ? coveredX : size.Z;
            const float Tolerance = 0.005f;
            float thickness = height - clearance;
            bool fits = clearance > 0
                ? thickness - size.Y <= OverhangShortfall + Tolerance && size.Y - thickness <= OverhangExcess + Tolerance
                : height - size.Y <= HeightShortfall + Tolerance;
            if (width - coveredX > 2 * SideMargin + Tolerance || depth - coveredZ > 2 * SideMargin + Tolerance || !fits)
            {
                string world = round ? $"{width:0.00} m across" : $"{width:0.00} x {depth:0.00} m";
                string tall = clearance > 0 ? $"{thickness:0.00} m thick from {clearance:0.00} m up" : $"{height:0.00} m tall";
                art.Report($"{look.Model} at {blocker.Id}",
                    $"authored {size.X:0.00} x {size.Z:0.00} m, {size.Y:0.00} m {(clearance > 0 ? "thick" : "tall")}; the world's {blocker.Id} is {world}, {tall}; "
                    + "not rescaled", look.Model);
                model.Free();
                root.Free();
                return null;
            }
            // Centred over the footprint, its lowest point on the ground.
            model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            var holder = new Node3D { Rotation = new Vector3(0, turn ? Mathf.Pi / 2 : 0, 0) };
            holder.AddChild(model);
            root.AddChild(holder);
        }
        root.Position = clearance > 0
            ? new Vector3(cx / 1000f, terrain.HeightAtMm(cx, cz) / 1000f + clearance, cz / 1000f)
            : new Vector3(cx / 1000f, LowestUnder(terrain, blocker), cz / 1000f);
        return root;
    }

    /// <summary>
    /// A whole building over its walls' footprint: authored in the world's frame (its door on the side the world has it), so it is never
    /// turned, and its origin is the footprint's centre at ground level, so it is placed by that origin rather than by its bounds (a
    /// chimney or a porch is not the centre). It must cover the footprint to within the side margin; null without a model or when it
    /// does not.
    /// </summary>
    public static Node3D? Building(ArtLibrary art, Placement look, BoxBlocker footprint, TerrainGrid terrain)
    {
        if (look.Model is null || art.ModelWithLods(look.Model) is not { } model)
            return null;
        float width = (footprint.MaxXMm - footprint.MinXMm) / 1000f, depth = (footprint.MaxZMm - footprint.MinZMm) / 1000f;
        var bounds = ArtGallery.Bounds(model);
        const float Tolerance = 0.005f;
        if (width - bounds.Size.X > 2 * SideMargin + Tolerance || depth - bounds.Size.Z > 2 * SideMargin + Tolerance
            || footprint.HeightMm / 1000f - bounds.Size.Y > HeightShortfall + Tolerance)
        {
            art.Report($"{look.Model} at {footprint.Id}",
                $"authored {bounds.Size.X:0.00} x {bounds.Size.Z:0.00} m, {bounds.Size.Y:0.00} m tall; the world's {footprint.Id} walls enclose "
                + $"{width:0.00} x {depth:0.00} m, {footprint.HeightMm / 1000f:0.00} m tall; not rescaled", look.Model);
            model.Free();
            return null;
        }
        var root = new Node3D { Name = footprint.Id, Position = new Vector3(footprint.CenterXMm / 1000f, LowestUnder(terrain, footprint), footprint.CenterZMm / 1000f) };
        root.AddChild(model);
        return root;
    }

    /// <summary>A model at a site (a container, a station, a node), at its authored size and turned as given; null without a model.</summary>
    public static Node3D? Site(ArtLibrary art, Placement look, Vector3 feet, float yawRadians = 0)
    {
        if (look.Model is null || art.ModelWithLods(look.Model) is not { } model)
            return null;
        var bounds = ArtGallery.Bounds(model);
        model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
        var root = new Node3D { Position = feet + look.Offset, Rotation = new Vector3(0, yawRadians, 0) };
        root.AddChild(model);
        return root;
    }

    private static float LowestUnder(TerrainGrid terrain, Blocker blocker)
    {
        var (cx, cz) = Footprints.Center(blocker);
        if (blocker is not BoxBlocker box)
            return terrain.HeightAtMm(cx, cz) / 1000f;
        return new[]
        {
            terrain.HeightAtMm(box.MinXMm, box.MinZMm), terrain.HeightAtMm(box.MaxXMm, box.MinZMm),
            terrain.HeightAtMm(box.MinXMm, box.MaxZMm), terrain.HeightAtMm(box.MaxXMm, box.MaxZMm), terrain.HeightAtMm(cx, cz),
        }.Min() / 1000f;
    }
}
