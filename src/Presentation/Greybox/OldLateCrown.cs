// UNNAMED Presentation - Old Late's crown: a rough slab on the stone giant's head that answers the wind a breath late (grand scale, GS-3)
// Godot presentation only (D-11): nothing here is read by the game

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Canon (lore.old_late): "Its crown is a rough slab, and when the wind comes off the ridge the slab answers it - late, a breath behind the
/// gust, not with it." The slab rocks a fraction of a degree on the world wind (<see cref="WindField"/>), driven by the same gust the trees'
/// shader answers, read <see cref="LateSeconds"/> behind it - so the wood round the giant moves, and then the crown does.
/// </summary>
public sealed partial class OldLateCrown : Node3D
{
    private const string Slab = "gs_old_late_crown";        // Frozen Norse SM_Arch_kit_03, 3.0 x 4.0 x 1.4 m as authored
    private const float LateSeconds = 1.5f;
    private const float RockDegrees = 0.6f;
    private const float CrownScale = 1.8f;
    // The trees' wind shader runs its gust and sway on a 3600 s cycle (WindField's CYCLE); this is the same clock.
    private const float Cycle = Mathf.Tau / 3600f;
    private Node3D _slab = null!;
    private Vector2 _origin;

    /// <summary>
    /// Crown the giant: <paramref name="giant"/> is the placed structure (the fitting's root); the slab goes on the authored model at
    /// <paramref name="head"/>, the top of the head in the model's own metres. Null without the slab model.
    /// </summary>
    public static OldLateCrown? Crown(Node3D giant, ArtLibrary art, Vector3 head)
    {
        if (art.ModelWithLods(Slab) is not { } slab)
            return null;
        var authored = giant.GetChildren().OfType<Node3D>().FirstOrDefault()?.GetChildren().OfType<Node3D>().FirstOrDefault() ?? giant;
        // Laid on its broad face and turned off the shoulders' line, narrower than the helm so the head shows round it: 5.4 m across, 7.2 m long, 2.5 m thick.
        slab.RotationDegrees = new Vector3(90, 0, 0);
        slab.Scale = Vector3.One * CrownScale;
        var crown = new OldLateCrown { Name = "OldLateCrown", Position = head + new Vector3(0, -0.4f, 0), RotationDegrees = new Vector3(3, 14, -4), _slab = slab };
        crown.AddChild(slab);
        authored.AddChild(crown);
        crown._origin = new Vector2(giant.GlobalPosition.X, giant.GlobalPosition.Z);
        art.Coverage.Resolved("scenery", "old_late_crown", Slab);
        return crown;
    }

    public override void _Process(double delta)
    {
        float time = Time.GetTicksMsec() / 1000f - LateSeconds;
        var wind = WindField.Direction;
        float gust = 0.5f + 0.5f * Mathf.Sin(time * 212f * Cycle - (_origin.X * wind.X + _origin.Y * wind.Z) * 0.045f);
        float strength = WindField.Strength * Mathf.Lerp(1f, 0.35f + 1.3f * gust, WindField.Gust);
        float sway = Mathf.Sin(time * 516f * Cycle) * 0.6f + Mathf.Sin(time * 974f * Cycle) * 0.4f;
        // Tipped about the line across the wind: the leeward edge lifts a hair, a breath after the trees.
        var across = new Vector3(-wind.Z, 0, wind.X).Normalized();
        _slab.Transform = new Transform3D(new Basis(across, Mathf.DegToRad(RockDegrees * strength * sway)) * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)).Scaled(Vector3.One * CrownScale), Vector3.Zero);
    }
}
