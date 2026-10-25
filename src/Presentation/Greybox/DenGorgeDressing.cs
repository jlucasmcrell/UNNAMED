// UNNAMED Presentation - rock, roots, trees, the cave mouth and Halda's rope over the gorge behind the wolf den (spec 28)
// Godot presentation only (D-11): non-colliding scenery beyond the walkable edge, placed by a seeded rule on the gorge's own heights

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

public static partial class DenGorge
{
    private const string FaceShell = "env_ph_mountainside";        // 10.2 x 10.5 m shell facing +Z, front at z +1.57, foot at y -0.2
    private const string LongRun = "gorge_cliff_04";                  // 87 m of sea cliff, 11 m tall, 24 m deep, centred
    private const string ShortRun = "gorge_cliff_02";              // 41 m, 10 m tall
    private const string Roots = "gorge_root_cluster", RopeModel = "gorge_rope";
    private static readonly string[] Boulders = { "env_ph_boulder_01", "env_ph_namaqualand_boulder_02", "rock_ph_moss_boulder_l" };
    private static readonly string[] LipStones = { "rock_ph_moss_boulder_m", "rock_ph_moss_boulder_s", "gorge_rock_face", "rock_ph_moss_stone_a" };
    private static readonly string[] Woods = { "flora_pine_tree", "flora_pine_tree_b", "flora_pine_tree_c", "flora_oak_tree" };

    /// <summary>Dress the gorge; returns how many pieces were placed. <paramref name="height"/> is the scenery surface at (x, z).</summary>
    public static int Dress(Node3D parent, ArtLibrary art, Func<float, float, float> height, ArtCoverage coverage)
    {
        var root = new Node3D { Name = "DenGorge" };
        parent.AddChild(root);
        var random = new RandomNumberGenerator { Seed = 0x5EED_D028 };
        int placed = 0;
        float Lip(float x) => height(x, Edge);
        Vector3 At(float x, float d) => new(x, height(x, Edge + d), Edge + d);

        // Where the far wall's ground reaches a height: a piece whose front stands there has the wall behind it for all of its height.
        float FarFront(float x, float y)
        {
            float d = FarFootAt(x) - 2f;
            while (d < 140f && height(x, Edge + d) < y)
                d += 0.25f;
            return d - 0.4f;
        }

        // The near face, upper: shells standing on the ledge against the sheer face, their tops kept under the walkable edge. A gap is
        // left at the cave mouth and at the cleft.
        for (float x = 438f + random.RandfRange(0, 4); x < 662f; x += 8.5f + random.RandfRange(-1.5f, 2f))
        {
            if (x > 531f && x < 547f || x > 552.5f && x < 560.5f)
                continue;
            float s = random.RandfRange(0.95f, 1.25f);
            float top = Lip(x) - random.RandfRange(0.45f, 0.9f);
            var at = new Vector3(x, top - 10.3f * s, Edge + 1.55f - 1.57f * s);
            placed += Place(root, art, FaceShell, at, new Vector3(random.RandfRange(-4, 3), random.RandfRange(-12, 12), random.RandfRange(-4, 4)), s);
        }
        // The near face, lower: taller shells on the river's bank against the face under the ledge, tops just under the ledge's lip.
        for (float x = 440f + random.RandfRange(0, 6); x < 660f; x += 11f + random.RandfRange(-2, 3))
        {
            float s = random.RandfRange(1.9f, 2.4f);
            float front = ShelfEnd(x) + 2.7f;
            float top = Shelf(Lip(x)) - 0.4f;
            var at = new Vector3(x, top - 10.3f * s, Edge + front - 1.57f * s);
            placed += Place(root, art, FaceShell, at, new Vector3(random.RandfRange(-4, 3), random.RandfRange(-14, 14), random.RandfRange(-5, 5)), s);
        }
        // The far wall: sea-cliff runs in tiers up the face, each standing where the ground behind it reaches its foot, so the whole wall
        // from the river to the shelf is rock and none of it is buried. Round the fall the face is left to its shells.
        for (float x = 420f + random.RandfRange(0, 10); x < 700f; x += 30f + random.RandfRange(-4, 6))
        {
            float bed = height(x, Edge + FarFootAt(x) - 3f), step = (height(x, Edge + FarFootAt(x) + 30f) - bed) / 3f;
            for (int tier = 0; tier < 3; tier++)
            {
                bool tall = random.Randf() < 0.6f;
                string id = tall ? LongRun : ShortRun;
                float half = tall ? 43f : 20f, depth = tall ? 12.1f : 4.3f;
                float s = (step + 1.5f) / (tall ? 11f : 10.05f) * random.RandfRange(0.95f, 1.08f);
                float x1 = x + random.RandfRange(-10f, 10f);
                float foot = bed - 1.5f + tier * step + random.RandfRange(-2.5f, 1.5f);
                if (MathF.Abs(x1 - FallX) < half * s * 0.8f + 6f)
                    continue;
                float front = FarFront(x1, foot);
                placed += Place(root, art, id, new Vector3(x1, foot, Edge + front + depth * s),
                    new Vector3(random.RandfRange(-9, 4), 180f + random.RandfRange(-12, 12), random.RandfRange(-5, 5)), s);
            }
        }
        // The far wall's ledges: scrub and small trees catching the light between its steps.
        var scrub = new RandomNumberGenerator { Seed = 0x5EED_D02B };
        for (float x = 426f; x < 698f; x += scrub.RandfRange(5f, 9f))
        {
            if (MathF.Abs(x - FallX) < 14f || scrub.Randf() < 0.35f)
                continue;
            float d = FarFootAt(x) + 22f * (scrub.Randf() < 0.5f ? 0.3f : 0.7f) + scrub.RandfRange(-0.8f, 0.8f);
            if (art.ModelWithLods(Woods[scrub.RandiRange(0, Woods.Length - 1)]) is not { } tree)
                continue;
            var at = At(x + scrub.RandfRange(-2f, 2f), d);
            tree.Position = at - new Vector3(0, 0.2f, 0);
            tree.RotationDegrees = new Vector3(scrub.RandfRange(-6, 6), scrub.RandfRange(0, 360), scrub.RandfRange(-6, 6));
            tree.Scale = Vector3.One * scrub.RandfRange(0.45f, 0.8f);
            root.AddChild(tree);
            placed++;
        }
        // Shells either side of the fall and behind the plunge pool.
        foreach (float dx in new[] { -13f, 13f, -19f, 19f })
        {
            float x = FallX + dx;
            float s = random.RandfRange(1.3f, 1.6f);
            float foot = height(x, Edge + FarFootAt(x) - 3f) - 1f;
            for (int tier = 0; tier < 6; tier++, foot += 9.5f * s)
                placed += Place(root, art, FaceShell, new Vector3(x, foot, Edge + FarFront(x, foot) + 1.57f * s),
                    new Vector3(random.RandfRange(-6, 3), 180f + random.RandfRange(-14, 14) - MathF.Sign(dx) * 18f, random.RandfRange(-5, 5)), s);
        }
        // The river: boulders standing in the run, the water white round them, and shelves of rock along the banks.
        for (float x = 448f + random.RandfRange(0, 5); x < 696f; x += random.RandfRange(6f, 12f))
        {
            if (MathF.Abs(x - SwallowX) < 9f)
                continue;
            float d = RiverOut + random.RandfRange(-RiverHalf * 0.85f, RiverHalf * 0.85f);
            float s = random.RandfRange(1.6f, 3.6f);
            var at = At(x, d);
            placed += Place(root, art, Boulders[random.RandiRange(0, Boulders.Length - 1)], at - new Vector3(0, 0.2f * s, 0),
                new Vector3(random.RandfRange(-12, 12), random.RandfRange(0, 360), random.RandfRange(-12, 12)), s);
        }
        for (float x = 452f; x < 690f; x += random.RandfRange(30f, 44f))
        {
            bool near = random.Randf() < 0.5f;
            float d = near ? ShelfEnd(x) + 7.5f : FarFootAt(x) - 3f;
            placed += Place(root, art, "env_ph_coast_rocks_01", At(x, d) - new Vector3(0, 0.9f, 0),
                new Vector3(0, random.RandfRange(0, 360), 0), random.RandfRange(0.22f, 0.32f));
        }
        // The lip: roots breaking out of the edge and hanging down the face, and the broken stone and moss where the lip is walked -
        // knee-high at most, so nothing stands between a character at the edge and the drop.
        for (float x = 444f + random.RandfRange(0, 6); x < 656f; x += random.RandfRange(16f, 28f))
        {
            if (x > 532f && x < 546f || x > 552.5f && x < 560.5f)
                continue;
            placed += Place(root, art, Roots, new Vector3(x, Lip(x) - 0.7f, Edge + 0.85f),
                new Vector3(random.RandfRange(78, 96), random.RandfRange(-20, 20), random.RandfRange(-8, 8)), random.RandfRange(0.8f, 1.2f));
        }
        for (float x = 442f + random.RandfRange(0, 4); x < 658f; x += random.RandfRange(3f, 7f))
        {
            string id = LipStones[random.RandiRange(0, LipStones.Length - 1)];
            float s = id switch { "gorge_rock_face" => random.RandfRange(0.12f, 0.2f), "rock_ph_moss_stone_a" => random.RandfRange(1.5f, 3f), _ => random.RandfRange(0.2f, 0.35f) };
            placed += Place(root, art, id, new Vector3(x, Lip(x) - 0.12f, Edge + random.RandfRange(0.5f, 1.05f)),
                new Vector3(random.RandfRange(-10, 10), random.RandfRange(0, 360), random.RandfRange(-10, 10)), s);
        }
        // The ledge itself: the same stone and moss underfoot, kept off the place the rope comes down.
        for (int i = 0; i < 9; i++)
        {
            float x = random.RandfRange(524f, 562f);
            if (MathF.Abs(x - RopeX) < 2.5f)
                continue;
            float d = random.RandfRange(3f, ShelfEnd(x) - 0.3f);
            float s = random.RandfRange(0.4f, 1.0f);
            placed += Place(root, art, LipStones[random.RandiRange(0, 1)], At(x, d) - new Vector3(0, 0.3f * s, 0),
                new Vector3(random.RandfRange(-10, 10), random.RandfRange(0, 360), 0), s);
        }
        // The far shelf's wood: pines in clumps with clearings, thicker and taller going back up the slope to the skyline.
        var woods = new RandomNumberGenerator { Seed = 0x5EED_D029 };
        for (float x = 430f; x < 700f; x += woods.RandfRange(6f, 11f))
        {
            if (woods.Randf() < 0.25f || MathF.Abs(x - FallX) < 7f)
                continue;
            for (float d = FarFootAt(x) + 14f + woods.RandfRange(0, 6); d < 300f; d += woods.RandfRange(9f, 22f) * (1f + d / 160f))
            {
                if (art.ModelWithLods(Woods[woods.RandiRange(0, Woods.Length - 1)]) is not { } tree)
                    continue;
                var at = At(x + woods.RandfRange(-3f, 3f), d);
                tree.Position = at - new Vector3(0, 0.3f, 0);
                tree.RotationDegrees = new Vector3(0, woods.RandfRange(0, 360), 0);
                tree.Scale = Vector3.One * woods.RandfRange(1.0f, 1.5f) * (1f + d / 400f);
                root.AddChild(tree);
                if (VisualOptions.Wind)
                {
                    WindField.Ensure(root);
                    Palette.Windblown(tree, 1.3f, Palette.Shifted($"{at.X:F1},{at.Z:F1}", Colors.White));
                }
                placed++;
            }
        }
        placed += CaveMouth(root, art, height);
        if (Rope(root, art, height) is { } rope)
        {
            root.AddChild(rope);
            placed++;
        }
        coverage.Resolved("scenery", "den_gorge", $"{placed} pieces: the gorge behind the wolf den (spec 28)");
        return placed;
    }

    /// <summary>
    /// The way under the lip: a short passage of dark rock at the back of the ledge, where the terrain is cut away (<see cref="InMouth"/>),
    /// framed by broken stone. The passage is drawn, not walked: it is what the rope leads to.
    /// </summary>
    private static int CaveMouth(Node3D root, ArtLibrary art, Func<float, float, float> height)
    {
        const float X = 539f;
        float floor = Shelf(height(X, Edge));
        var mouth = new Node3D { Name = "gorge_cave_mouth", Position = new Vector3(X, floor, Edge) };
        root.AddChild(mouth);
        // A tunnel of the face's own rock running back under the lip, closing to black.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int Around = 14, Back = 8;
        for (int k = 0; k <= Back; k++)
        {
            float t = k / (float)Back;
            float d = 1.5f - t * 7f;
            float shrink = 1f - 0.35f * t;
            for (int a = 0; a <= Around; a++)
            {
                float ang = Mathf.Pi * a / Around;
                float wobble = 0.18f * MathF.Sin(a * 2.3f + k * 1.7f);
                var p = new Vector3(MathF.Cos(ang) * (2.1f + wobble) * shrink, MathF.Sin(ang) * (2.9f + wobble) * shrink - 0.15f, d);
                st.SetNormal(new Vector3(-MathF.Cos(ang), -MathF.Sin(ang), 0));
                st.SetUV(new Vector2(a / (float)Around * 3f, t * 4f));
                st.SetColor(new Color(1 - t, 1 - t, 1 - t));
                st.AddVertex(p);
            }
        }
        for (int k = 0; k < Back; k++)
            for (int a = 0; a < Around; a++)
            {
                int i = k * (Around + 1) + a, j = i + Around + 1;
                st.AddIndex(i); st.AddIndex(j); st.AddIndex(i + 1); st.AddIndex(i + 1); st.AddIndex(j); st.AddIndex(j + 1);
            }
        mouth.AddChild(new MeshInstance3D { Name = "passage", Mesh = st.Commit(), MaterialOverride = PassageRock(art) });
        int placed = 1;
        var random = new RandomNumberGenerator { Seed = 0x5EED_D02A };
        foreach (var (dx, dy, s, yaw) in new[] { (-3.1f, 0.6f, 0.62f, 80f), (3.2f, 0.4f, 0.58f, -80f), (-0.6f, 3.4f, 0.7f, 0f), (1.4f, 3.0f, 0.55f, 15f) })
            placed += Place(mouth, art, "gorge_rock_face", new Vector3(dx, dy - 0.6f, 1.55f), new Vector3(random.RandfRange(-12, 12), yaw, random.RandfRange(-15, 15)), s);
        return placed;
    }

    /// <summary>The passage's wall: the cliff's own rock material, darkened with distance in by the vertex colour.</summary>
    private static Material PassageRock(ArtLibrary art)
    {
        if (art.WorldMaterial("cw_dark_rock")?.Duplicate() is BaseMaterial3D rock)
        {
            rock.VertexColorUseAsAlbedo = true;
            return rock;
        }
        return new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f, AlbedoColor = new Color(0.30f, 0.29f, 0.27f) };
    }

    /// <summary>
    /// Halda's rope: tied round a driven stake on the lip, over the edge and down the face to the ledge, the last of it lying in a loose
    /// coil. Frayed where it runs over the edge - a strand gone, the rest thinned and furred - so the danger is seen before it is felt.
    /// </summary>
    private static Node3D? Rope(Node3D root, ArtLibrary art, Func<float, float, float> height)
    {
        if (art.Model(RopeModel) is not { } source || source.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault() is not { } mesh)
            return null;
        var material = mesh.GetActiveMaterial(0);
        source.QueueFree();
        float lip = height(RopeX, Edge), shelf = Shelf(lip);
        var rope = new Node3D { Name = "gorge_rope" };
        var stake = new Vector3(RopeX + 0.4f, lip, Edge + 0.25f);
        // The rope's line: from the stake over the rounded edge, hanging a hand off the face, to the ledge and a coil.
        var points = new List<Vector3> { stake + new Vector3(0, 0.35f, 0), new(RopeX + 0.1f, lip + 0.05f, Edge + 0.55f), new(RopeX, lip - 0.35f, Edge + 0.9f) };
        for (int i = 1; i <= 8; i++)
        {
            float t = i / 8f;
            float y = Mathf.Lerp(lip - 0.35f, shelf + 0.05f, t);
            points.Add(new Vector3(RopeX + 0.08f * MathF.Sin(i * 1.3f), y, Edge + 0.9f + (2.6f - 0.9f) * GroundField.SmoothStep(0.3f, 1f, t) + 0.35f * MathF.Sin(t * Mathf.Pi)));
        }
        for (int i = 1; i <= 10; i++)
        {
            float a = i * 0.9f;
            points.Add(new Vector3(RopeX + 0.4f + 0.35f * MathF.Cos(a), shelf + 0.05f + 0.025f * i, Edge + 3.2f + 0.35f * MathF.Sin(a)));
        }
        rope.AddChild(new MeshInstance3D { Name = "line", Mesh = Tube(points, 0.022f, FrayAt: 2), MaterialOverride = material });
        // The stake: a short length of split timber driven into the lip.
        rope.AddChild(new MeshInstance3D
        {
            Name = "stake", Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.065f, Height = 0.9f, RadialSegments = 7 },
            Position = stake + new Vector3(0, 0.2f, 0), RotationDegrees = new Vector3(8, 0, -6),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.24f, 0.18f), Roughness = 0.9f },
        });
        return rope;
    }

    /// <summary>
    /// A rope along <paramref name="points"/>: a tube with its twist in the texture, thinned to half over the segment after point
    /// <paramref name="FrayAt"/> with loose fibres standing out of it.
    /// </summary>
    private static ArrayMesh Tube(List<Vector3> points, float radius, int FrayAt)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int Sides = 8, PerSegment = 6;
        var path = new List<Vector3>();
        for (int i = 0; i + 1 < points.Count; i++)
            for (int k = 0; k < PerSegment; k++)
                path.Add(points[i].Lerp(points[i + 1], k / (float)PerSegment));
        path.Add(points[^1]);
        float along = 0;
        for (int i = 0; i < path.Count; i++)
        {
            var forward = (path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]).Normalized();
            var side = forward.Cross(Mathf.Abs(forward.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
            var up = side.Cross(forward).Normalized();
            float segment = i / (float)PerSegment;
            float r = radius * (segment > FrayAt && segment < FrayAt + 1 ? 0.55f + 0.2f * MathF.Sin(segment * 9f) : 1f);
            if (i > 0) along += path[i].DistanceTo(path[i - 1]);
            for (int s = 0; s <= Sides; s++)
            {
                float a = Mathf.Tau * s / Sides;
                var n = side * MathF.Cos(a) + up * MathF.Sin(a);
                st.SetNormal(n);
                st.SetUV(new Vector2(s / (float)Sides, along * 4f));
                st.AddVertex(path[i] + n * r);
            }
        }
        for (int i = 0; i + 1 < path.Count; i++)
            for (int s = 0; s < Sides; s++)
            {
                int a = i * (Sides + 1) + s, b = a + Sides + 1;
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(a + 1); st.AddIndex(a + 1); st.AddIndex(b); st.AddIndex(b + 1);
            }
        // The broken strand's fibres: thin splinters standing off the frayed length.
        var random = new RandomNumberGenerator { Seed = 0xF1B4E };
        int start = FrayAt * PerSegment, end = Math.Min(path.Count - 2, (FrayAt + 1) * PerSegment);
        int next = path.Count * (Sides + 1);
        for (int f = 0; f < 26; f++, next += 3)
        {
            int i = random.RandiRange(start, end);
            var forward = (path[i + 1] - path[i]).Normalized();
            var out_ = forward.Cross(Vector3.Up).Normalized().Rotated(forward, random.RandfRange(0, Mathf.Tau));
            var a = path[i] + out_ * radius * 0.5f;
            var b = a + (out_ * random.RandfRange(0.02f, 0.06f) + forward * random.RandfRange(-0.04f, 0.04f));
            var w = forward * 0.0025f;
            st.SetNormal(out_); st.SetUV(new Vector2(0.5f, 0)); st.AddVertex(a - w);
            st.SetNormal(out_); st.SetUV(new Vector2(0.5f, 0)); st.AddVertex(a + w);
            st.SetNormal(out_); st.SetUV(new Vector2(0.5f, 0.3f)); st.AddVertex(b);
            st.AddIndex(next); st.AddIndex(next + 1); st.AddIndex(next + 2);
        }
        return st.Commit();
    }

    private static int Place(Node3D root, ArtLibrary art, string id, Vector3 position, Vector3 rotationDegrees, float scale)
    {
        if (art.ModelWithLods(id) is not { } model)
            return 0;
        model.Position = position;
        model.RotationDegrees = rotationDegrees;
        model.Scale = Vector3.One * scale;
        Palette.Tinted(model, Palette.Shifted($"{position.X:F1},{position.Z:F1}", Colors.White));
        root.AddChild(model);
        return 1;
    }
}
