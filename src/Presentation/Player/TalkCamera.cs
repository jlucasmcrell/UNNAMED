// UNNAMED Presentation - the conversation framing: over the character's shoulder onto the speaker's face while a conversation is open
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Player;

/// <summary>
/// While a conversation is open (and the camera is not in first person) the view eases from the gameplay camera to an over-the-shoulder
/// framing of the speaker's face - near enough to read the face as it talks - and back when it closes. Its own camera: the gameplay
/// rig keeps its yaw, pitch and distance untouched underneath, so nothing about control changes.
/// </summary>
public sealed partial class TalkCamera : Camera3D
{
    private float _weight;

    /// <summary>
    /// Frame this frame: <paramref name="speakerHead"/> and <paramref name="characterHead"/> while talking, null otherwise; the gameplay
    /// camera is what it blends from and back to.
    /// </summary>
    public void Frame(Vector3? speakerHead, Vector3? characterHead, Camera3D gameplay, double delta)
    {
        bool on = speakerHead is not null && characterHead is not null;
        _weight = Mathf.MoveToward(_weight, on ? 1f : 0f, (float)delta / 0.6f);
        if (_weight <= 0.001f)
        {
            if (Current)
                gameplay.Current = true;
            return;
        }
        if (on)
        {
            var speaker = speakerHead!.Value;
            var character = characterHead!.Value;
            var toward = speaker - character;
            toward.Y = 0;
            if (toward.LengthSquared() < 0.01f)
                toward = -gameplay.GlobalTransform.Basis.Z;
            toward = toward.Normalized();
            var right = toward.Cross(Vector3.Up).Normalized();
            // Behind and beside the character's head, a little above: the speaker's face at about a third of the frame's width.
            var eye = character - toward * 1.25f + right * 0.62f + Vector3.Up * 0.16f;
            _framing = new Transform3D(Basis.Identity, eye).LookingAt(speaker + Vector3.Down * 0.04f, Vector3.Up);
        }
        float s = _weight * _weight * (3 - 2 * _weight);
        GlobalTransform = gameplay.GlobalTransform.InterpolateWith(_framing, s);
        Fov = Mathf.Lerp(gameplay.Fov, 38f, s);
        Near = gameplay.Near;
        Far = gameplay.Far;
        Environment = gameplay.Environment;
        Attributes = gameplay.Attributes;
        if (!Current)
            Current = true;
    }

    private Transform3D _framing = Transform3D.Identity;
}
