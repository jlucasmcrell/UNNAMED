// UNNAMED Presentation - a light lit by an open flame: its energy wavers, the way a real flame's does
// Godot presentation only (D-11): it moves a light the presentation itself owns; nothing here is simulated, saved or read by the game

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// What modulates a light's energy once it is in the world: the waver of an open flame, and how much of the light
/// the sun takes away. One node owns a light's energy, so the two terms cannot fight over it.
///
/// WHY THIS EXISTS, measured 2026-09-29. A Cairnwold lantern post carries a flame - the region's own
/// <c>bindings.json</c> gives <c>structure_prefixes.cw_lantern</c> two particle emitters (torch_flame,
/// embers) - and it carries a light: an OmniLight3D at energy 2.0. The particles move; the light did not.
/// <see cref="LightSpec.Build"/> returned a plain OmniLight3D with a constant energy and no script, so at
/// night, where that light is the key light of the village, the pool it throws on the ground was perfectly
/// steady while the sprite above it flickered. Measured over 60 frames (1.08 s of in-game time) at a frozen
/// hour, with the frame's own noise floor as the control: the whole lantern frame moved by mean |dluma|
/// 0.472 and only 0.060 % of its pixels by more than 32 code values, and the largest local movement
/// anywhere in it was the flame sprite itself (mean 1.424 in the box round the lantern head). Nothing in
/// the picture varied faster than the clock, and the clock takes 48 real minutes to do anything.
///
/// Three sines at incommensurate frequencies, so the waver never settles on a visible beat, summed and
/// scaled to the light's own base energy. The phase is derived from the light's own name, so thirteen posts
/// do not pulse together. The vertical offset is small on purpose: it is the flame's own centre wandering,
/// not the lantern moving.
///
/// THE DAYLIGHT TERM, measured 2026-09-29 in the same round. Every light in the game was built once, at boot,
/// with a constant energy and nothing that read the clock - so a Cairnwold lantern post burned at its full
/// 2.0 at noon. Measured out of a 13.00 h frame's own scene dump, daylight 1.00 and sun altitude 47.6: thirteen
/// <c>Light_cw_lantern_*</c> at 1.78 to 2.35, and the seven cottage lamps at 1.3 and 1.6. A street lantern is
/// lit at dusk, not all day, and the air already runs on this axis - <see cref="Greybox.SkyView"/> scales the
/// fog's light colour and the volumetric ambient by the same number and adds a night fill below 0.3. A light
/// authored <c>"daylight": 1</c> is therefore off in full sun and at full strength after dark, and the default,
/// 0, leaves a hearth or a cottage lamp exactly as it was.
///
/// SHADOWS. These lights are authored <c>"shadows": false</c>, so this re-renders no shadow map and costs
/// one node per flame. If a flame light is ever authored with shadows on, this becomes a shadow-map churn
/// and the amplitude should come down or the waver should drive energy only.
/// </summary>
public partial class FlameFlicker : Node3D
{
    /// <summary>
    /// The default waver: 18 % of the light's own energy, as an AMPLITUDE - so up to 36 % peak to peak, not 18.
    /// A constant, and the one number to tune.
    ///
    /// "PEAK TO PEAK" WAS WRONG HERE AND THE MEASUREMENT SAYS SO. <see cref="_Process"/> computes
    /// <c>_base * (1 + _amount * waver)</c> and <c>waver</c> is a weighted sum of three sines whose weights total
    /// 1, so <c>_amount</c> scales a value that spans about -1 to +1: the energy spans <c>_base * (1 -+ _amount)</c>.
    /// Measured 2026-09-29 out of two captures of the identical command, from the scene dump's own live light
    /// energies: the Cairnwold lodge's hearth light read 2.543879 and 2.349254 against its 2.600000 base, which is
    /// <c>waver</c> = -0.120 and -0.536 - neither anywhere near the -1 the old wording implied was the extent.
    /// </summary>
    public const float DefaultAmount = 0.18f;

    /// <summary>How far the flame's own centre wanders, in metres.</summary>
    private const float Drift = 0.02f;

    private OmniLight3D? _light;
    private float _base;
    private Vector3 _at;
    private float _amount;
    private float _daylight;
    private float _phase;
    private double _seconds;

    /// <summary>Whether a prop's own emitters are a flame (so the light it also carries is lit by it).</summary>
    public static bool Lit(IReadOnlyList<(string Recipe, Vector3 At)>? emitters) =>
        emitters is { Count: > 0 } && emitters.Any(e =>
            e.Recipe.Contains("flame", StringComparison.OrdinalIgnoreCase) ||
            e.Recipe.Contains("torch", StringComparison.OrdinalIgnoreCase) ||
            e.Recipe.Contains("ember", StringComparison.OrdinalIgnoreCase));

    /// <summary>Give <paramref name="light"/> the waver of the flame that lit it, and its daylight term.</summary>
    public static void Attach(OmniLight3D light, float amount = DefaultAmount, float daylight = 0f)
    {
        light.AddChild(new FlameFlicker
        {
            Name = "FlameFlicker", _light = light, _base = light.LightEnergy, _at = light.Position,
            _amount = amount, _daylight = Mathf.Clamp(daylight, 0f, 1f), _phase = Phase(light.Name.ToString()),
        });
    }

    /// <summary>A stable phase (radians) for a light's name, spread over the circle: two posts rarely agree.</summary>
    private static float Phase(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name)
            hash = (hash ^ c) * 16777619u;
        return hash % 6283u / 1000f;   // 0 to 2 pi, in thousandths
    }

    public override void _Process(double delta)
    {
        if (_light is null || !GodotObject.IsInstanceValid(_light))
            return;
        _seconds += delta;
        float t = (float)_seconds;
        // Weighted so the slowest term carries it: a flame leans and recovers rather than buzzing.
        float waver = 0.55f * Mathf.Sin(t * 7.3f + _phase)
                    + 0.30f * Mathf.Sin(t * 11.9f + _phase * 1.7f)
                    + 0.15f * Mathf.Sin(t * 19.7f + _phase * 2.3f);
        // The sun's share, floored at 6 %: a lantern in full sun is not visibly lit, but a light that goes to
        // exactly zero and back is a switch, and the eye reads the last of a flame long before it is out.
        float sun = 1f - _daylight * (1f - 0.06f) * Greybox.SkyView.DaylightLevel;
        _light.LightEnergy = _base * sun * (1f + _amount * waver);
        _light.Position = _at + new Vector3(0f, Drift * waver, 0f);
    }
}
