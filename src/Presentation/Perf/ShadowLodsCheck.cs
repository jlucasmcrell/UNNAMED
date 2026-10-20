using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Perf;

// Engine-level regression: real transform notifications, live spawns and authored mode changes.
public partial class ShadowLodsCheck : Node3D
{
    private readonly ShadowLods _lods = new();
    private readonly Node3D _holder = new();
    private readonly MeshInstance3D _near = Cube(0, 10), _far = Cube(10, 0);
    private MeshInstance3D? _spawn;
    private int _stage;
    private static MeshInstance3D Cube(float begin, float end) => new()
    {
        Mesh = new BoxMesh(), VisibilityRangeBegin = begin, VisibilityRangeEnd = end,
        GIMode = GeometryInstance3D.GIModeEnum.Static,
    };
    public override void _Ready()
    {
        AddChild(_holder); _holder.AddChild(_near); _holder.AddChild(_far);
        _lods.Watch(this);
    }
    private static void Require(bool condition, string why)
    {
        if (!condition) throw new InvalidOperationException(why);
    }
    public override void _Process(double delta)
    {
        try
        {
            switch (_stage++)
            {
                case 0:
                    _lods.Update(Vector3.Zero);
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.On && _far.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, "Initial ranges");
                    _holder.Position = new Vector3(20, 0, 0);
                    break;
                case 1:
                    // No polling Update yet: a fixed camera must notice a moving caster before rendering.
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off && _far.CastShadow == GeometryInstance3D.ShadowCastingSetting.On, "Transform notification did not update shadow bands");
                    ShadowLods.SetMode(_far, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
                    Require(_far.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, "Authored shadow-only mode");
                    break;
                case 2:
                    _lods.Update(new Vector3(20, 0, 0));
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.On, "Camera change");
                    Require(_far.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly && _far.Layers == 0,
                        "Out-of-band shadow proxy became visible colour geometry");
                    _holder.Position = Vector3.Zero;
                    break;
                case 3:
                    Require(_far.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, "Shadow-only mode lost after moving back into range");
                    Require(_far.Layers == 1, "Shadow proxy layers not restored in range");
                    Require(_near.Visible && _near.GIMode == GeometryInstance3D.GIModeEnum.Static, "Visibility/GI changed");
                    _far.QueueFree();
                    _spawn = Cube(0, 10); _spawn.Position = new Vector3(20, 0, 0); AddChild(_spawn);
                    break;
                case 4:
                    _lods.Update(new Vector3(20, 0, 0));
                    Require(_spawn!.HasMeta("shadow_lod_bounds") && _spawn.CastShadow == GeometryInstance3D.ShadowCastingSetting.On, "Late spawn not registered");
                    _holder.Visible = false;
                    _lods.Update(Vector3.Zero);
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, "Hidden parent still casts");
                    _holder.Visible = true;
                    _lods.Update(Vector3.Zero);
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.On, "Shown parent did not regain casting");
                    _lods.Unwatch();
                    Require(_near.CastShadow == GeometryInstance3D.ShadowCastingSetting.On && !_near.HasMeta("shadow_lod_bounds"), "Unwatch did not restore/release");
                    GD.Print("SHADOW LOD CHECK PASS: transforms, camera, shadow-only, GI, spawn, disposal");
                    GetTree().Quit(0);
                    break;
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    public override void _ExitTree() => _lods.Unwatch();
}
