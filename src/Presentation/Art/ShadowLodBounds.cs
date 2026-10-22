using Godot;

namespace UNNAMED.Presentation.Art;

// A child at the mesh origin receives global-transform changes without polling every mesh.
public partial class ShadowLodBounds : Node3D
{
    public Action<Transform3D>? Changed;
    public Action<bool>? InTree;
    public Action<bool>? Visibility;
    public Action<GeometryInstance3D.ShadowCastingSetting>? ChangeMode;

    public override void _EnterTree() { SetNotifyTransform(true); InTree?.Invoke(true); }
    public override void _Ready()
    {
        Visibility?.Invoke(IsVisibleInTree());
        Changed?.Invoke(GlobalTransform);
    }
    public override void _ExitTree() => InTree?.Invoke(false);
    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged && IsInsideTree()) Changed?.Invoke(GlobalTransform);
        if (what == NotificationVisibilityChanged && IsInsideTree()) Visibility?.Invoke(IsVisibleInTree());
    }
}
