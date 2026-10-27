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
        var roof = _art.WorldMaterial("cw_timber_beam") ?? Palette.Roof;
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
        DressInn(layout, top);
    }

    // The assembled kit's log UVs were stretched across the whole upstairs wall.
    // Keep its relief and native roof/stone maps; sample only the timber in metres.
    private void RepairInnWallMaterials(Node3D model)
    {
        var wood = _art.WorldMaterial("cw_timber_beam");
        if (wood is null) return;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            if (mesh.Mesh is { } shape)
                for (int i = 0; i < shape.GetSurfaceCount(); i++)
                {
                    var material = shape.SurfaceGetMaterial(i);
                    if (material is not null && new[] { "_0", "_1", "_2", "_3", "_4", "_5", "_6", "_8" }
                        .Any(material.ResourceName.EndsWith))
                        mesh.SetSurfaceOverrideMaterial(i, wood);
                }
    }

    private void DressInn(RegionLayout layout, float upper)
    {
        var wood = _art.WorldMaterial("cw_timber_beam") ?? Palette.Wood;
        var linen = new StandardMaterial3D { AlbedoColor = new Color(.69f,.62f,.46f), Roughness = 1 };
        var wool = new StandardMaterial3D { AlbedoColor = new Color(.24f,.28f,.20f), Roughness = 1 };
        float ground = layout.Space.Terrain.HeightAtMm(444_000,728_000)/1000f;
        void Board(string name, Vector3 at, Vector3 size, Material material)
        {
            AddChild(new MeshInstance3D { Name=name, Position=at, Mesh=new BoxMesh { Size=size }, MaterialOverride=material });
        }
        void Prop(string id, Vector3 at, float yaw=0)
        {
            if (Art.Fitting.Site(_art,new Art.Placement(id),at,yaw) is { } prop) AddChild(prop);
        }
        void Warm(Vector3 at, float energy, float range)
        {
            AddChild(new OmniLight3D { Position=at, LightColor=new Color(1,.73f,.46f), LightEnergy=energy,
                OmniRange=range, ShadowEnabled=false });
        }
        // Shuttered stores under the guest rooms make both storeys legible from the road.
        Board("InnStoresDoor",new Vector3(452.015f,ground+1.03f,719.5f),new Vector3(.08f,2.06f,1.2f),wood);
        foreach(float z in new[] {715.4f,717.2f})
            Board("InnStoresShutter"+z,new Vector3(452.02f,ground+1.7f,z),new Vector3(.08f,.65f,.85f),wood);
        Board("InnStoreyBeam",new Vector3(452.04f,upper-.16f,718),new Vector3(.16f,.24f,8),wood);
        // Narrow boards and exposed joists give the hall a human-scale ceiling.
        for (int i=0;i<62;i++)
            Board($"TavernFloorBoard{i}",new Vector3(436.45f+i*.25f,ground+.025f,728),new Vector3(.244f,.05f,7.2f),wood);
        for (int i=0;i<5;i++)
        {
            float x=437.5f+i*3.2f;
            Board($"TavernCeilingBeam{i}",new Vector3(x,ground+2.95f,728),new Vector3(.22f,.28f,7.3f),wood);
            Prop("prop_ph_wooden_lantern_01",new Vector3(x,ground+2.25f,728));
            Board($"LanternDrop{i}",new Vector3(x,ground+2.71f,728),new Vector3(.035f,.4f,.035f),Palette.Iron);
        }
        Warm(new Vector3(440,ground+2.35f,727.3f),2.2f,8);
        Warm(new Vector3(448,ground+2.35f,727.8f),2.2f,8);
        Warm(new Vector3(447.2f,ground+.8f,729.6f),3,6);
        // Renn serves across this counter, with accessible circulation behind it.
        Board("RennCounterFront",new Vector3(438.8f,ground+.5f,725.25f),new Vector3(2.4f,.95f,.52f),wood);
        Board("RennCounterTop",new Vector3(438.8f,ground+1.02f,725.25f),new Vector3(2.6f,.12f,.75f),wood);
        Prop("prop_bp_shelf_ah",new Vector3(438.8f,ground,724.2f));
        Prop("prop_cos_barrel_met_01",new Vector3(437.1f,ground,724.9f));
        Prop("prop_cos_barrel_met_01",new Vector3(440.6f,ground,724.8f));
        // Native benches, candles and tableware dress the four existing tables.
        foreach (var at in new[] { new Vector2(442.6f,725.275f),new Vector2(444,725.275f),new Vector2(438.4f,727.3f),new Vector2(440,727.3f) })
        {
            Prop("prop_cos_bench_tavern_01_l100",new Vector3(at.X,ground,at.Y+(at.Y>727?.8f:1.1f)));
            Prop("prop_bp_table_candles",new Vector3(at.X,ground+.9f,at.Y));
            for (int side=-1;side<=1;side+=2) Mug(new Vector3(at.X+side*.32f,ground+.9f,at.Y));
        }
        for (int i=0;i<4;i++) Mug(new Vector3(438+i*.5f,ground+1.08f,725.25f));
        Board("TavernWovenRug",new Vector3(446,ground+.057f,727.4f),new Vector3(2.2f,.025f,2.8f),wool);
        Board("TavernNoticeBoard",new Vector3(450.5f,ground+1.65f,731.55f),new Vector3(1.3f,.9f,.08f),wood);
        for(int i=0;i<3;i++) Board($"Notice{i}",new Vector3(450.1f+i*.36f,ground+1.67f,731.495f),new Vector3(.28f,.48f,.012f),linen);
        foreach(var bed in layout.Space.Blockers.OfType<BoxBlocker>().Where(b=>b.Id.StartsWith("ah_inn_bed_")))
        {
            float x=bed.CenterXMm/1000f,z=bed.CenterZMm/1000f;
            // Thick ticking, a folded wool cover and a proper pillow over the native timber bed.
            Board(bed.Id+"Mattress",new Vector3(x,upper+.48f,z),new Vector3(1.13f,.23f,1.8f),linen);
            Board(bed.Id+"Blanket",new Vector3(x,upper+.605f,z-.23f),new Vector3(1.15f,.04f,1.25f),wool);
            AddChild(new MeshInstance3D { Name=bed.Id+"Pillow",Position=new Vector3(x,upper+.65f,z+.65f),
                Mesh=new SphereMesh { Radius=.28f,Height=.56f },Scale=new Vector3(1.7f,.3f,.75f),MaterialOverride=linen });
        }
        foreach(var at in new[] { new Vector2(439.8f,715),new Vector2(444.8f,715),new Vector2(449.8f,715),
            new Vector2(437.2f,718.8f),new Vector2(444.9f,718.8f),new Vector2(450.7f,720.7f) })
        {
            Prop("prop_ph_wooden_stool_01",new Vector3(at.X,upper,at.Y));
            Prop("prop_bp_table_candles",new Vector3(at.X,upper+.5f,at.Y));
        }
        void Mug(Vector3 at)
        {
            var cup=new MeshInstance3D { Position=at+Vector3.Up*.06f, Mesh=new CylinderMesh { TopRadius=.045f,BottomRadius=.038f,Height=.12f },MaterialOverride=wood };
            cup.AddChild(new MeshInstance3D { Position=new Vector3(.05f,0,0),RotationDegrees=new Vector3(90,0,0),
                Mesh=new TorusMesh { InnerRadius=.025f,OuterRadius=.04f },MaterialOverride=wood });
            AddChild(cup);
        }
    }
}
