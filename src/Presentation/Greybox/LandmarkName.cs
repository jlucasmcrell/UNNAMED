using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>A nearby landmark's title, in the world; discovery and the map remain simulation-owned.</summary>
public partial class LandmarkName : Label3D
{
    public override void _Ready()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        FontSize = 64; PixelSize = .0075f; OutlineSize = 12;
        Modulate = Ui.HudStyle.Brass;
        NoDepthTest = true;
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        float distance = camera?.GlobalPosition.DistanceTo(GlobalPosition) ?? float.MaxValue;
        Visible = distance < 70 && camera!.IsPositionInFrustum(GlobalPosition);
        Transparency = Mathf.SmoothStep(50,70,distance);
    }
}
