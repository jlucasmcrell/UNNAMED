// Presentation only, Godot-free so the rules can be tested: which race looks a people wears (owner picks, 2026-10-24).
namespace UNNAMED.Presentation.Art;

/// <summary>What the wings are doing; the movement code decides, the wings only draw it.</summary>
public enum WingState { Folded, Glide, Flare, Leap }

/// <summary>
/// The chosen race looks. Kal: slate calcification on bare skin, spreading with age, and the folding dorsal wings.
/// Orenth: the shadow lags the body by an instant and a sprint leaves a faint secondary silhouette. Edge doubling and
/// the phase-scar skin were tried and dropped (they read as a rendering fault and as a skin condition).
/// </summary>
public static class PeopleLooks
{
    public const string Kal = "people.kal";
    public const string Orenth = "people.orenth";

    /// <summary>How far the stone has spread at <paramref name="ageYears"/>: 0.5 for a young adult, 0.8 for the oldest.</summary>
    public static float StoneCoverage(int ageYears) => Math.Clamp(.5f + (ageYears - 20) * .006f, .5f, .8f);

    /// <summary>Wing span for a state: 0 folded in the spine channels, 1 full span.</summary>
    public static float WingOpenness(WingState state) => state switch { WingState.Folded => 0f, WingState.Leap => .6f, _ => 1f };

    /// <summary>The flare brake pitches the spread spars forward, in radians.</summary>
    public static float WingPitch(WingState state) => state == WingState.Flare ? -.35f : 0f;

    /// <summary>The trailing silhouette's opacity at a ground speed in m/s: nothing below a run, at most 0.18 at a sprint.</summary>
    public static float AfterimageStrength(float speed) => Math.Clamp((speed - 2.6f) / 2.4f, 0f, 1f) * .18f;
}
