using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

public partial class HollowView
{
    // The guest wing reuses the shipped waystation building above sealed stores. Its
    // floor and stair heights come from the same authored supports the bodies walk.
    private void BuildInnWing(RegionLayout layout)
    {
        var floor = layout.Space.WalkableSurfaces.FirstOrDefault(s => s.OriginXMm == 436_000 && s.OriginZMm == 714_000);
        var stair = layout.Space.WalkableSurfaces.FirstOrDefault(s => s.OriginXMm == 452_000 && s.OriginZMm == 717_000);
        if (floor is null || stair is null)
            return;
        var timber = _art.WorldMaterial("cw_timber_beam") ?? Palette.Trunk;
        var stone = _art.WorldMaterial("cw_wet_stone_wall") ?? Palette.Rock;
        var roof = _art.WorldMaterial("cw_thatch") ?? Palette.Roof;
        void Piece(string name, Vector3 centre, Vector3 size, Material material, float pitch = 0)
        {
            var mesh = new MeshInstance3D { Name = name, Position = centre,
                Mesh = new BoxMesh { Size = size }, MaterialOverride = material,
                Rotation = new Vector3(pitch, 0, 0) };
            var body = new StaticBody3D { CollisionLayer = CameraCollisionLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            mesh.AddChild(body);
            AddChild(mesh);
        }
        float top = floor.HeightAt(0, 0) / 1000f;
        float bottom = layout.Space.Terrain.HeightAtMm(444_000, 718_000) / 1000f - .3f;
        Piece("InnSealedStores", new Vector3(444, (top + bottom) / 2, 718), new Vector3(16, top - bottom, 8), stone);
        Piece("InnGuestFloor", new Vector3(444, top - .08f, 718), new Vector3(16, .16f, 8), timber);
        // Shallow timber treads over a continuous navigation support avoid snagging
        // capsules on individual risers. The landing and rails use the same route.
        for (int i = 0; i < 16; i++)
        {
            float z = 718.25f + i * .5f;
            float y = stair.HeightAtMm(453_000, (long)((z - .25f) * 1000)) / 1000f;
            float ground = layout.Space.Terrain.HeightAtMm(453_000, (long)(z * 1000)) / 1000f - .1f;
            Piece($"InnStairTread{i}", new Vector3(453, (y + ground) / 2, z), new Vector3(2, y - ground, .5f), timber);
        }
        Piece("InnStairLanding", new Vector3(453, top - .08f, 717.5f), new Vector3(2, .16f, 1), timber);
        float low = stair.HeightAtMm(453_000, 726_000) / 1000f;
        float pitch = Mathf.Atan2(top - low, 8);
        Piece("InnCoveredStair", new Vector3(453, (top + low) / 2 + 2.8f, 722),
            new Vector3(3, .18f, MathF.Sqrt(64 + (top - low) * (top - low)) + 2), roof, pitch);
        for (int i = 0; i < 4; i++)
        {
            float z = 717.5f + i * 2.5f;
            float y = stair.HeightAtMm(453_000, (long)(z * 1000)) / 1000f;
            Piece($"InnStairPost{i}", new Vector3(454.4f, y + 1.4f, z), new Vector3(.16f, 2.8f, .16f), timber);
        }
    }
}
