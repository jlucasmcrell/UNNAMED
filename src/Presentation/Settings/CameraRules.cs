// UNNAMED Presentation - how the camera settings act on the rig: the look speed (and while aiming), the starting distance, the shoulder
// Godot-free: Presentation.Tests links this file and tests the rig's seam on its own (D-11)

namespace UNNAMED.Presentation.Settings;

/// <summary>The camera rig's rules, apart from the engine: <c>CameraRig</c> reads them, and a test holds them.</summary>
public static class CameraRules
{
    /// <summary>Radians a pixel of mouse travel turns the camera now: the look sensitivity, and the aim share of it while a bow is aimed.</summary>
    public static float LookSpeed(CameraPrefs prefs, bool aiming) =>
        CameraPrefs.BaseSensitivity * prefs.Sensitivity * (aiming ? prefs.AimSensitivity : 1f);

    /// <summary>
    /// The distance the rig should move to when the settings change: the new starting distance when the player changed it, and none
    /// otherwise - a wheel zoom or first person is the player's, and a change elsewhere on the page never undoes it.
    /// </summary>
    public static float? DistanceFor(CameraPrefs? before, CameraPrefs now) =>
        before is null || before.Distance != now.Distance ? now.Distance : null;

    /// <summary>The shoulder the rig should take when the settings change: the chosen one when it changed (or at the start), else none.</summary>
    public static CameraShoulder? ShoulderFor(CameraPrefs? before, CameraPrefs now) =>
        before is null || before.Shoulder != now.Shoulder ? now.Shoulder : null;

    /// <summary>The shoulder key's order: right, left, centre, and round again.</summary>
    public static CameraShoulder Next(CameraShoulder side) => side switch
    {
        CameraShoulder.Right => CameraShoulder.Left,
        CameraShoulder.Left => CameraShoulder.Center,
        _ => CameraShoulder.Right,
    };

    /// <summary>Which way the shoulder offset goes: +1 right, -1 left, 0 centred.</summary>
    public static float Side(CameraShoulder side) => side switch { CameraShoulder.Right => 1f, CameraShoulder.Left => -1f, _ => 0f };
}
