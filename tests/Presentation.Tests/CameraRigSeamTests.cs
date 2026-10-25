using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The camera rig's seam (WP-1E; CMB-137..139): how its settings act on it - the look speed and the aim share of it, the starting distance
/// and the shoulder applied only when they change, and the shoulder key's order. The rig itself is the engine's; these are its rules.
/// </summary>
public class CameraRigSeamTests
{
    [Fact]
    public void TheLookSpeed_IsTheSensitivity_AndTheAimShareOfItWhileAiming()
    {
        var prefs = new CameraPrefs { Sensitivity = 2f, AimSensitivity = 0.5f };
        Assert.Equal(CameraPrefs.BaseSensitivity * 2f, CameraRules.LookSpeed(prefs, aiming: false), 6);
        Assert.Equal(CameraPrefs.BaseSensitivity, CameraRules.LookSpeed(prefs, aiming: true), 6);
        Assert.Equal(CameraRules.LookSpeed(new CameraPrefs(), false), CameraRules.LookSpeed(new CameraPrefs(), true));   // as built by default
    }

    [Fact]
    public void TheStartingDistanceAndShoulder_ApplyAtTheStart_AndOnlyWhenTheyChange()
    {
        var start = new CameraPrefs();
        Assert.Equal(3.5f, CameraRules.DistanceFor(null, start));
        Assert.Equal(CameraShoulder.Right, CameraRules.ShoulderFor(null, start));
        // A change elsewhere on the page (the field of view) leaves a wheel zoom and a swapped shoulder as the player left them.
        var fov = start with { Fov = 90 };
        Assert.Null(CameraRules.DistanceFor(start, fov));
        Assert.Null(CameraRules.ShoulderFor(start, fov));
        Assert.Equal(5f, CameraRules.DistanceFor(fov, fov with { Distance = 5f }));
        Assert.Equal(CameraShoulder.Left, CameraRules.ShoulderFor(fov, fov with { Shoulder = CameraShoulder.Left }));
    }

    [Fact]
    public void TheShoulderKey_GoesRightLeftCentre_AndTheOffsetFollows()
    {
        Assert.Equal(CameraShoulder.Left, CameraRules.Next(CameraShoulder.Right));
        Assert.Equal(CameraShoulder.Center, CameraRules.Next(CameraShoulder.Left));
        Assert.Equal(CameraShoulder.Right, CameraRules.Next(CameraShoulder.Center));
        Assert.Equal((1f, -1f, 0f), (CameraRules.Side(CameraShoulder.Right), CameraRules.Side(CameraShoulder.Left), CameraRules.Side(CameraShoulder.Center)));
    }
}
