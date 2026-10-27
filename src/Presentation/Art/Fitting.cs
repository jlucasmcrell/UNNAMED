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
    public static Node3D? Structure(ArtLibrary art, Placement look, Blocker blocker, TerrainGrid terrain, WalkSpace? space = null)
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
            case PolygonBlocker hull:
                width = (hull.Corners.Max(c => c.XMm) - hull.Corners.Min(c => c.XMm)) / 1000f;
                depth = (hull.Corners.Max(c => c.ZMm) - hull.Corners.Min(c => c.ZMm)) / 1000f;
                break;
            default:
                model.Free();
                return null;
        }
        float height = blocker.HeightMm / 1000f;
        // An overhang (a beam a crouched body passes under) stands from its clearance up, as its greybox does, and is measured by its thickness.
        float clearance = blocker is BoxBlocker { ClearanceMm: > 0 } overhang ? overhang.ClearanceMm / 1000f : 0;
        var bounds = ArtGallery.Bounds(model);
        // A scattered structure (a boulder, a crag) is turned by its OWN ID when its binding asks for a spread.
        // Without this every copy of a model faced the same way: the region places 240 boulders from three models and
        // 40 crags from two, and NOT ONE of the 280 carried a turn - `facing_deg` is read for NPCs and spawns only,
        // and this method took no yaw at all - so a field of them read as one prop stamped out 80 times. That is the
        // geometry half of the owner's "samey duplicates" complaint; the colour half was already answered in
        // HollowView (a deterministic value shift per blocker id, whose own comment says it is there "so a field of
        // boulders reads as rock rather than as copies of one prop"). Deterministic on purpose: the same world must
        // look the same on every run and in every capture, so it is the placement's own id and never a frame counter
        // or a shared RNG. tools/asset_pipeline/cairnwold/scatter_rocks.py reproduces this exactly.
        var (scatterYaw, scatterLean) = look.YawSpread > 0
            ? Scatter(blocker.Id, look.YawSpread)
            : (0f, 0f);
        // Lay the model's longer side along the structure's (a round structure has no long side). A scattered
        // structure is exempt: its turn already covers every orientation, and swapping the axes as well would make
        // the blocker the tool sizes for it depend on the blocker itself, which has no fixed point.
        bool turn = look.YawSpread <= 0 && !round && (bounds.Size.X >= bounds.Size.Z) != (width >= depth);
        var size = turn ? new Vector3(bounds.Size.Z, bounds.Size.Y, bounds.Size.X) : bounds.Size;
        // AND THE FIT IS MEASURED AT THE TURN IT WILL BE DRAWN AT. Comparing the model's unturned box to a blocker
        // sized for its turned one is comparing two different orientations, and it rejected 89 of the 280 rocks -
        // cw_boulder_b_001 is 2.85 x 1.39 m unturned and 2.50 x 2.42 m at its own yaw, so the 2.42 m depth read as
        // 1.03 m of daylight past a 0.5 m margin and the game drew greybox over a rock that fits perfectly.
        if (look.YawSpread > 0)
        {
            var turned = art.TurnedFootprint(look.Model!, model, scatterYaw);
            size = new Vector3(turned.X, bounds.Size.Y, turned.Y);
        }
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
            // The turn goes on the holder INSIDE the root, so the root - and the collision body HollowView hangs off
            // it - keeps the blocker's own axis-aligned frame and what the player walks into is untouched.
            var holder = new Node3D
            {
                Rotation = new Vector3(scatterLean, (turn ? Mathf.Pi / 2 : 0) + scatterYaw, scatterLean * 0.6f),
            };
            holder.AddChild(model);
            root.AddChild(holder);
        }
        root.Position = clearance > 0
            ? new Vector3(cx / 1000f, (space?.HeightAtMm(cx, cz) ?? terrain.HeightAtMm(cx, cz)) / 1000f + clearance, cz / 1000f)
            : new Vector3(cx / 1000f, LowestUnder(terrain, blocker, space), cz / 1000f);
        // The visible-model offset can align an authored collider with the actual trunk
        // while preserving the accepted mesh position and world-space support data.
        foreach (var holder in root.GetChildren().OfType<Node3D>()) holder.Position += look.Offset + Vector3.Down * look.GroundSinkM;
        return root;
    }

    /// <summary>
    /// A whole building over its walls' footprint: authored in the world's frame (its door on the side the world has it), so it is never
    /// turned, and its origin is the footprint's centre at ground level, so it is placed by that origin rather than by its bounds (a
    /// chimney or a porch is not the centre). It must cover the footprint to within the side margin; null without a model or when it
    /// does not.
    /// </summary>
    public static Node3D? Building(ArtLibrary art, Placement look, BoxBlocker footprint, TerrainGrid terrain, WalkSpace? space = null)
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
        var root = new Node3D { Name = footprint.Id, Position = new Vector3(footprint.CenterXMm / 1000f, LowestUnder(terrain, footprint, space), footprint.CenterZMm / 1000f) };
        root.AddChild(model);
        return root;
    }

    /// <summary>A model at a site (a container, a station, a node), at its authored size and turned as given; null without a model.</summary>
    public static Node3D? Site(ArtLibrary art, Placement look, Vector3 feet, float yawRadians = 0)
    {
        if (look.Model is null || art.ModelWithLods(look.Model) is not { } model)
            return null;
        var bounds = ArtGallery.Bounds(model);
        // A binding's stated scale (a relic the size of a marble) is about the model's own centre and foot, so the body a caller adds to the
        // returned root is not scaled with it.
        float scale = look.Scale > 0 ? look.Scale : 1;
        model.Scale = Vector3.One * scale;
        model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z) * scale;
        if (look.Tint is { } tint)
            Tinted(model, new Color(tint.X, tint.Y, tint.Z));
        var root = new Node3D { Position = feet + look.Offset, Rotation = new Vector3(0, yawRadians, 0) };
        root.AddChild(model);
        return root;
    }

    /// <summary>
    /// Multiplies every surface's albedo by <paramref name="tint"/> through a per-instance material override (the ash-grey of an ashbloom, the
    /// scorch of a spent one): the shared mesh and the source file are untouched, and a surface that is not a standard material keeps its own.
    /// </summary>
    public static void Tinted(Node model, Color tint)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is not { } shape)
                continue;
            for (int surface = 0; surface < shape.GetSurfaceCount(); surface++)
            {
                if ((mesh.GetSurfaceOverrideMaterial(surface) ?? shape.SurfaceGetMaterial(surface)) is not BaseMaterial3D source)
                    continue;
                var worn = (BaseMaterial3D)source.Duplicate();
                worn.AlbedoColor = source.AlbedoColor * tint;
                mesh.SetSurfaceOverrideMaterial(surface, worn);
            }
        }
    }

    /// <summary>
    /// The turn and lean a scattered structure gets from its own ID, as radians: a yaw anywhere in
    /// <paramref name="spreadDegrees"/> CENTRED ON ZERO (so a spread is a band of freedom around the orientation the
    /// region authored, and 360 is every way round) and a lean of up to 3 degrees either way. Deterministic on the ID
    /// alone, so the world looks the same on every run, in every capture and after every reload - never read off a
    /// frame counter, a shared random source or the order things happen to be drawn in.
    /// <c>tools/asset_pipeline/cairnwold/scatter_rocks.py</c> reproduces this exactly to size each blocker to the
    /// model it will actually draw at that turn.
    /// </summary>
    public static (float Yaw, float Lean) Scatter(string id, float spreadDegrees)
    {
        uint hash = 2166136261u;
        foreach (char c in id)
            hash = (hash ^ c) * 16777619u;
        // A finaliser, so the turn is not read off FNV-1a's weaker low bits.
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        hash *= 3266489917u;
        hash ^= hash >> 16;
        float yaw = Mathf.DegToRad(spreadDegrees) * ((hash % 4096u) / 4096f - 0.5f);
        // The lean stays small: the model is turned about the centre of its own base, so a big rock laid over would
        // bury one edge in the ground. Three degrees settles a boulder onto a slope; six would sink a 6.5 m one.
        float lean = Mathf.DegToRad(Mathf.Min(3f, spreadDegrees / 24f)) * (((hash >> 12) % 2048u) / 1024f - 1f);
        return (yaw, lean);
    }

    private static float LowestUnder(TerrainGrid terrain, Blocker blocker, WalkSpace? space = null)
    {
        long Height(long x, long z) => space?.HeightAtMm(x, z) ?? terrain.HeightAtMm(x, z);
        var (cx, cz) = Footprints.Center(blocker);
        if (blocker is not BoxBlocker box)
            return Height(cx, cz) / 1000f;
        return new[]
        {
            Height(box.MinXMm, box.MinZMm), Height(box.MaxXMm, box.MinZMm),
            Height(box.MinXMm, box.MaxZMm), Height(box.MaxXMm, box.MaxZMm), Height(cx, cz),
        }.Min() / 1000f;
    }
}
