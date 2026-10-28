using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A Kal wears its own stone (owner's ruling, 2026-10-28): no head hair, brows, lashes or facial hair from the HumGen library at
/// all. The hair and beard steps offer stone growths instead - a crown crest, ridges over the temples, crystal outcrops, brow horns,
/// and jaw or chin ridges - head-attached meshes tinted to the Kal's chosen stone. The geometry is authored here from simple
/// prisms (the owned rock and crystal packs are separate kit-scale meshes that do not read as growth on a face), and the material
/// reuses the Kal's own stone colour, so growth and skin match on every stone tint.
/// </summary>
public static class KalGrowth
{
    /// <summary>The growths the Kal hair step offers; the first is bare stone, which is the people's own head.</summary>
    public static readonly string[] Styles = { "Bare stone", "Crown crest", "Temple ridges", "Crystal outcrops", "Brow horns" };

    /// <summary>The jaw and chin growths the beard slot offers; the first is none.</summary>
    public static readonly string[] Jaw = { "None", "Jaw ridges", "Calcite fringe" };

    public const string StyleSlider = "look.kal_growth", JawSlider = "look.kal_jaw";
    private const string Holder = "KalGrowthHolder";

    /// <summary>
    /// Draw this Kal's growth on its Head bone: the style and the jaw choice from the look's sliders, tinted by the stone colour
    /// the player chose. The meshes hang off a bone attachment, so they follow every clip without a second skeleton.
    /// </summary>
    public static void Attach(Skeleton3D skeleton, int style, int jaw, Color stone)
    {
        foreach (var old in skeleton.GetChildren().OfType<BoneAttachment3D>().Where(b => b.Name == Holder).ToArray())
        {
            skeleton.RemoveChild(old);
            old.QueueFree();
        }
        if (style <= 0 && jaw <= 0) return;
        var holder = new BoneAttachment3D { Name = Holder };
        skeleton.AddChild(holder);
        // The bone is named once the attachment is in the tree, which is when it resolves the skeleton's bones.
        holder.BoneName = "Head";
        var material = Stone(stone);
        if (style is 1) holder.AddChild(Part("crest", Plates(), material));
        if (style is 2) holder.AddChild(Part("ridges", Ridges(), material));
        if (style is 3) holder.AddChild(Part("crystals", Crystals(), material));
        if (style is 4) holder.AddChild(Part("horns", Horns(), material));
        if (jaw is 1) holder.AddChild(Part("jaw_ridges", JawRidges(), material));
        if (jaw is 2) holder.AddChild(Part("fringe", Fringe(), material));
    }

    private static MeshInstance3D Part(string name, ArrayMesh mesh, Material material) =>
        new() { Name = "KalGrowth_" + name, Mesh = mesh, MaterialOverride = material };

    /// <summary>A matte stone with a faint mineral sheen: the colour the Kal's skin is drawn in, so growth and skin match.</summary>
    private static Material Stone(Color stone) => new StandardMaterial3D
    {
        AlbedoColor = stone,
        Roughness = .78f,
        MetallicSpecular = .4f,
    };

    private static ArrayMesh Build(List<Vector3> points, List<Vector3> normals)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A tapered spike from <paramref name="at"/> to <paramref name="tip"/>: the one shape every growth is made of.</summary>
    private static void Spike(List<Vector3> points, List<Vector3> normals, Vector3 at, Vector3 tip, float radius, int sides)
    {
        var up = (tip - at).Normalized();
        var side = up.Cross(Vector3.Forward).Normalized();
        if (side.Length() < .5f) side = up.Cross(Vector3.Right).Normalized();
        var other = up.Cross(side).Normalized();
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = Mathf.Tau * i / sides;
            ring[i] = at + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius;
        }
        for (int i = 0; i < sides; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % sides];
            var face = (b - a).Cross(tip - a).Normalized();
            points.AddRange(new[] { a, b, tip });
            normals.AddRange(new[] { face, face, face });
        }
    }

    /// <summary>A midline crest: plates standing along the skull from the brow back, the tallest in the middle.</summary>
    private static ArrayMesh Plates()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float fan = Mathf.Sin(t * Mathf.Pi);
            var at = new Vector3(0f, .085f - .02f * t, Mathf.Lerp(.05f, -.055f, t));
            Spike(points, normals, at, at + new Vector3(0f, .008f + .045f * fan, -.004f - .01f * t), .016f + .014f * fan, 4);
        }
        return Build(points, normals);
    }

    /// <summary>Ridges sweeping from the brow over each temple, like a brow ridge carried back into the skull.</summary>
    private static ArrayMesh Ridges()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            var at = new Vector3(side * (.03f + .022f * t), .055f - .012f * t, .04f - .03f * t);
            Spike(points, normals, at, at + new Vector3(side * .01f, .014f - .005f * t, -.012f), .012f - .003f * t, 4);
        }
        return Build(points, normals);
    }

    /// <summary>Crystal outcrops: clusters of short, hard shards breaking through the stone of the scalp.</summary>
    private static ArrayMesh Crystals()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        (float Side, float Up, float Fwd, float Size)[] shards =
        {
            (0f, .095f, -.005f, .022f), (-.034f, .075f, .02f, .017f), (.036f, .072f, .015f, .018f),
            (-.014f, .06f, -.045f, .015f), (.022f, .105f, -.04f, .014f),
        };
        foreach (var shard in shards)
        {
            var at = new Vector3(shard.Side, shard.Up - .02f, shard.Fwd);
            Spike(points, normals, at, at + new Vector3(shard.Side * .3f, shard.Size * 2.4f, -.004f), shard.Size, 3);
        }
        return Build(points, normals);
    }

    /// <summary>Calcified horns at the brow, curving up and back out of the stone.</summary>
    private static ArrayMesh Horns()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int side = -1; side <= 1; side += 2)
        {
            var a = new Vector3(side * .042f, .06f, .05f);
            var b = a + new Vector3(side * .014f, .03f, -.01f);
            Spike(points, normals, a, b, .013f, 4);
            Spike(points, normals, b, b + new Vector3(side * .01f, .028f, -.018f), .008f, 4);
        }
        return Build(points, normals);
    }

    /// <summary>Jaw ridges: two low ridges running forward along the jaw line from the ear to the chin.</summary>
    private static ArrayMesh JawRidges()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 4; i++)
        {
            float t = i / 3f;
            var at = new Vector3(side * (.05f - .012f * t), .01f - .012f * t, .045f - .06f * t);
            Spike(points, normals, at, at + new Vector3(side * .003f, .01f, -.005f), .009f - .002f * t, 4);
        }
        return Build(points, normals);
    }

    /// <summary>A calcite fringe: flat shards hanging under the chin, the mineral version of a beard.</summary>
    private static ArrayMesh Fringe()
    {
        var points = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int i = 0; i < 7; i++)
        {
            float t = i / 6f;
            float side = Mathf.Lerp(-.04f, .04f, t);
            float drop = .022f + .012f * Mathf.Sin(t * Mathf.Pi);
            var at = new Vector3(side, -.005f, .05f - .03f * Mathf.Abs(t - .5f));
            Spike(points, normals, at, at + new Vector3(side * .15f, -drop, -.004f), .008f, 4);
        }
        return Build(points, normals);
    }
}
