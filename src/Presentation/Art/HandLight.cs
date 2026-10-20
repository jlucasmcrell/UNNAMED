// UNNAMED Presentation - a light carried in the off hand (owner finding 25: light_radius was a number in a tooltip and nothing more)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The torch's own light. <c>light_radius</c> in the item's content is the one place the light is authored, and this is what turns it into
/// a real <see cref="OmniLight3D"/> in the off hand, burning with the same waver as the hearths (<see cref="FlameFlicker"/>) so it reads as
/// fire rather than as a lamp following the character.
/// </summary>
public static class HandLight
{
    private const string NodeName = "OffHandLight";

    /// <summary>
    /// Show, resize or put out the light hanging from <paramref name="hand"/> (the off hand's attachment): <paramref name="radiusMm"/> 0 puts
    /// it out, which is what an empty off hand or an unlit item does.
    /// </summary>
    public static void Show(Node3D? hand, long radiusMm)
    {
        if (hand is null)
            return;
        var light = hand.GetNodeOrNull<OmniLight3D>(NodeName);
        if (radiusMm <= 0)
        {
            light?.QueueFree();
            return;
        }
        if (light is null)
        {
            light = new OmniLight3D
            {
                Name = NodeName, LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 2.0f, ShadowEnabled = true,
                // The flame sits above the grip rather than in the palm, in the hand's own frame: a torch is carried upright.
                Position = new Vector3(0, 0.32f, 0),
            };
            hand.AddChild(light);
            FlameFlicker.Attach(light, 0.2f);
        }
        light.OmniRange = Mathf.Max(1f, radiusMm / 1000f);
    }
}
