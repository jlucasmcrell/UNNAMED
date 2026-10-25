// Presentation only, Godot-free so the rules can be tested: which race looks a people wears (owner picks, 2026-10-24).
namespace UNNAMED.Presentation.Art;

/// <summary>What the wings are doing; the movement code decides, the wings only draw it.</summary>
public enum WingState { Folded, Glide, Flare, Leap }

/// <summary>
/// The chosen race looks. Kal: slate calcification on bare skin, spreading with age, and the folding dorsal wings.
/// Orenth: the shadow lags the body by an instant and running leaves a trail of pale spirit ghosts. Edge doubling and
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

    /// <summary>How far behind the body each trailing ghost is, in seconds; the owner asked for a stronger, longer spirit trail.</summary>
    public static readonly double[] AfterimageLags = { .08, .16, .24 };

    /// <summary>
    /// Opacity of trailing ghost <paramref name="ghost"/> (0 nearest) at a ground speed in m/s: nothing at a walk, rising
    /// from a jog to full at a sprint, each later ghost fainter (0.38, 0.24, 0.12 at full speed: a soft echo, owner 2026-10-25).
    /// </summary>
    public static float AfterimageStrength(float speed, int ghost) =>
        Math.Clamp((speed - 2.4f) / 2.0f, 0f, 1f) * .38f * (1f - ghost * .34f);

    /// <summary>A lingering decoy ghost's opacity: a little stronger than the nearest trail ghost, fading out over its last third.</summary>
    public static float DecoyStrength(double age, double seconds) =>
        .42f * (float)Math.Clamp((seconds - age) / (seconds / 3), 0, 1);

    /// <summary>
    /// A wraith-step's silver veil over the body: eases in and out over 0.2 s so it never pops, 0.45 at its height, so the
    /// body reads as half out of reality while staying solid underneath.
    /// </summary>
    public static float PhaseVeil(double age, double seconds) =>
        .45f * (float)Math.Clamp(Math.Min(age, seconds - age) / .2, 0, 1);

    /// <summary>While phased the trail shows even at a walk: each ghost at least this strong.</summary>
    public static float PhaseTrailFloor(int ghost) => .28f * (1f - ghost * .34f);
}
