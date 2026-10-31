// UNNAMED Presentation - the shore of an authored water body: the ground its bed is cut into, the graded bank that holds it, and the
// noise that makes its edge a shoreline rather than a rectangle
// Godot presentation only (D-11): the body is the domain's WaterSite; nothing cut here is read back by the simulation

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// One authored water body's shape, as the camera gets it (owner verdict on the Old Late and Foldscar waters, 2026-10-28 ~23:35: they
/// drew as flat pale rectangles lying on the grass). The surface stands at its own <c>level_m</c>, the bed is cut to
/// <c>level - depth_m</c>, and the ground climbs from the bed to a bank over a few metres, so the water sits in a depression and meets
/// the ground along a shoreline that wanders by a metre or two.
/// <para>
/// Two ways a body is held, chosen from the ground it stands on rather than from its key. Where the land already rises above the
/// surface within a few metres of the authored edge - the creek's hand-cut channel, a spring in its basin - the cut only LOWERS what
/// stands above the bed and the banks content cut are left exactly as they are. Where it does not - the Cairnwold millpond, 26 m of
/// ground at 4.00 under a 4.35 m sheet, and the millrace beside it - the ground is GRADED UP to a bank just over the surface, because
/// otherwise the sheet has nothing to sit in and its edge floats above the grass.
/// </para>
/// </summary>
public sealed class WaterShore
{
    /// <summary>How far the bank stands above the surface: enough that the ground is out of the water, too little to be a levee.</summary>
    private const float LipM = 0.13f;

    /// <summary>How far out from the authored edge the ground is tested for a bank of its own (metres).</summary>
    private const float ProbeM = 3f;

    /// <summary>How far past its edge the surface is drawn, where the bank hides the rest of it.</summary>
    private const float MarginM = 0.9f;

    private readonly WaterSite _water;
    private readonly bool _disc;
    private readonly bool _pan;
    private readonly Vector2[] _line;
    private readonly float[] _arc;
    private readonly Vector2 _centre;
    private readonly float _length;
    private readonly float _half;
    private readonly float _wobble;
    private readonly float[] _phase = new float[3];

    /// <summary>The body's own shape, cut from the ground <paramref name="existing"/> reports the body stands on.</summary>
    public WaterShore(WaterSite water, Func<float, float, float> existing)
    {
        _water = water;
        Level = water.LevelMm / 1000f;
        Depth = MathF.Max(0.08f, water.DepthMm / 1000f);
        _line = water.PointsMm.Select(p => new Vector2(p.XMm / 1000f, p.ZMm / 1000f)).ToArray();
        float half = water.WidthMm / 2000f;
        // A disc is one point, or a reach so short that a ribbon of it would be a strip with corners: the springs are two points 1.8 m
        // apart, and the owner asked for a pool, stones and reeds rather than a long thin reach.
        _arc = new float[_line.Length];
        for (int i = 1; i < _line.Length; i++)
            _arc[i] = _arc[i - 1] + _line[i].DistanceTo(_line[i - 1]);
        _length = _arc[^1];
        Pool = water.Kind is "spring" or "well" or "pool" || (_line.Length <= 5 && _length < 20f && half <= 6f);
        _disc = Pool || _line.Length == 1;
        _centre = Middle();
        // A disc's radius is the reach's own run as well as its width, so two points 1.8 m apart make a pool about 4 m across.
        _half = _disc ? MathF.Max(half * 1.15f, _length * 0.5f + half * 0.8f) : half;
        // The edge wanders by a tenth of the body's width: 30 cm on a spring, 2.2 m on the millpond.
        _wobble = Math.Clamp(0.10f * 2f * _half, 0.30f, 2.20f);
        Apron = Math.Clamp(_half * 0.5f, 2.5f, 6.0f);
        Extent = (_disc ? _half : _length * 0.5f) + _wobble + MathF.Max(Bank, Apron) + MarginM;
        Channel = RngChannel.Open(0, CellKey.OfWorld(_centre.X, _centre.Y), "water", water.Key);
        for (int i = 0; i < _phase.Length; i++)
            _phase[i] = Roll((uint)i) * Mathf.Tau;
        _pan = NeedsBanks(existing);
    }

    /// <summary>How far from <see cref="Centre"/> any of the ground this body shapes can lie: the cheap way to skip it entirely.</summary>
    public float Extent { get; }

    /// <summary>
    /// The body's own random channel: every pool's stones and reeds are addressed, not drawn in call order, so a body looks the same
    /// in every run. The world seed is not an input because the body is authored content, not something a seed generated.
    /// </summary>
    public RngChannel Channel { get; }

    /// <summary>A stable value in [0, 1) for one sample of this body's channel.</summary>
    public float Roll(uint sample) => (Channel.UInt64(sample) >> 11) * (1f / 9007199254740992f);

    /// <summary>The authored body this shape belongs to.</summary>
    public WaterSite Water => _water;

    /// <summary>Its own surface height, from <c>level_m</c>.</summary>
    public float Level { get; }

    /// <summary>Its own depth, from <c>depth_m</c> - 0.15 m for a water that names none, the waded ford.</summary>
    public float Depth { get; }

    /// <summary>A small body the owner asked to see as a pool: stones round its rim and reeds at its edge.</summary>
    public bool Pool { get; }

    /// <summary>Its footprint is one round body rather than a reach along a line.</summary>
    public bool Disc => _disc;

    /// <summary>The land keeps this body on its own: the cut only lowers the bed and the banks are left as content cut them.</summary>
    public bool Contained => !_pan;

    /// <summary>How much ground a body in a pan is given, outside its footprint, to settle back from the bank to the land.</summary>
    public float Apron { get; }

    /// <summary>
    /// How far the ground climbs from the bed to the bank, inside the footprint: the water's own shallow shelf. Wide enough that a
    /// shallow body still has a metre of margin where the water thins to nothing, which is what makes its edge soft rather than cut.
    /// </summary>
    public float Bank => Math.Clamp(Depth * 1.5f + 0.9f, 1.0f, 4.0f);

    /// <summary>
    /// How much of the surface's edge fades into the ground, in metres of inset: wide enough to be soft from eye height, never wider
    /// than the body.
    /// </summary>
    public float Band => Math.Clamp(MathF.Min(Depth * 0.9f, _half * 0.5f), 0.5f, 2.60f);

    /// <summary>How deep the water goes before its surface is opaque: the shore band, in metres of depth.</summary>
    public float BandDepth => Math.Clamp(Depth * 0.40f, 0.05f, 0.90f);

    /// <summary>Where the body is: its middle, for a pool or a pond.</summary>
    public Vector2 Centre => _centre;

    /// <summary>How far the surface reaches past the authored edge where the ground, not the mesh, is what ends the water.</summary>
    private float Reach => _pan ? MarginM : Math.Clamp(_half * 0.5f + 0.5f, 0.9f, 4.5f);

    /// <summary>The body's own line in metres, for the mesh that draws its surface.</summary>
    public IReadOnlyList<Vector2> Line => _line;

    /// <summary>The distance along the body at a point of its line.</summary>
    public float Arc(int index) => _arc[index];

    /// <summary>The body's length: zero for a disc.</summary>
    public float Length => _length;

    /// <summary>The footprint's own half-width at <paramref name="t"/> (a fraction of its length or of a turn), wobbled by its noise.</summary>
    public float EdgeAt(float t) => MathF.Max(0.25f, _half + _wobble * Noise(t));

    /// <summary>How far the surface is drawn out at <paramref name="t"/>: the footprint, and a margin where the bank hides the rest.</summary>
    public float SurfaceHalf(float t) => EdgeAt(t) + Reach;

    /// <summary>Smooth noise in [-1, 1] along the body, closing on itself for a disc so the shoreline has no seam.</summary>
    private float Noise(float t)
    {
        // A disc closes on itself, so its harmonics are whole turns of the circle; a reach is open and its noise runs its own length,
        // about one wave every eleven metres.
        float turns = _disc ? 1f : MathF.Max(1f, MathF.Round(_length / 11f));
        float n = 0.62f * MathF.Sin(Mathf.Tau * 3f * turns * t + _phase[0])
            + 0.26f * MathF.Sin(Mathf.Tau * 5f * turns * t + _phase[1])
            + 0.12f * MathF.Sin(Mathf.Tau * 8f * turns * t + _phase[2]);
        return Math.Clamp(n / 0.93f, -1f, 1f);
    }

    /// <summary>Where the body stands: the middle of the ground its own points walk over.</summary>
    private Vector2 Middle()
    {
        Vector2 sum = Vector2.Zero;
        foreach (var p in _line)
            sum += p;
        return sum / _line.Length;
    }

    /// <summary>
    /// How far inside the footprint's edge a point stands, and its parameter along the body: measured from the wobbled edge, so the
    /// ground and the surface that covers it agree. Negative outside.
    /// </summary>
    public (float Inside, float Along) Find(float x, float z)
    {
        var p = new Vector2(x, z);
        if (_disc)
        {
            var away = p - _centre;
            float angle = Mathf.Atan2(away.Y, away.X) / Mathf.Tau;
            return (EdgeAt(angle - MathF.Floor(angle)) - away.Length(), angle - MathF.Floor(angle));
        }
        float best = float.MaxValue, along = 0;
        for (int i = 1; i < _line.Length; i++)
        {
            Vector2 a = _line[i - 1], b = _line[i], span = b - a;
            float square = span.LengthSquared();
            float t = square < 1e-6f ? 0 : Math.Clamp((p - a).Dot(span) / square, 0f, 1f);
            float distance = p.DistanceTo(a + span * t);
            if (distance >= best)
                continue;
            best = distance;
            along = (_arc[i - 1] + span.Length() * t) / MathF.Max(1e-3f, _length);
        }
        return (EdgeAt(along) - best, along);
    }

    /// <summary>
    /// The ground here after the body is cut into it: the bed at <c>level - depth</c>, the bank graded up from it to just over the
    /// surface (<see cref="Contained"/> false) or to the land that already holds it. The cut only lowers ground that stands above the
    /// bed; only a body in a pan is ever raised.
    /// </summary>
    public float Carve(float x, float z, float existing)
    {
        // A sea lies over the land the region authored and never cuts what stands above its surface: the Steps' sound is 150 m
        // wide, and carving its whole ribbon to the bed drowned the quay, so its walls stood as strips over open water (B-021).
        if (_water.Kind == "sea" && existing >= Level)
            return existing;
        var (inside, along) = Find(x, z);
        // The bank is a distance in from the authored edge, never the whole body: a 2 m spring would otherwise have no bed at all.
        float bank = MathF.Min(Bank, _half);
        float bed = MathF.Min(existing, Level - Depth);
        if (inside >= bank)
            return bed;
        if (inside > 0f)
        {
            // From the edge down to the bed: to the land where the land already holds the water, to the lip where it does not.
            float to = _pan ? Level + LipM : existing;
            return Mathf.Lerp(bed, to, 1f - inside / bank);
        }
        if (!_pan || inside <= -Apron)
            return existing;
        // The bank is held just over the surface for a step past the edge, then the land settles back to what it was.
        float lip = MathF.Max(existing, Level + LipM);
        return Mathf.Lerp(lip, existing, GroundField.SmoothStep(0f, 1f, -inside / Apron));
    }

    /// <summary>
    /// The ground at a point where every authored body stands: the body the point is deepest inside shapes it, so a creek crossing a
    /// pond's shore belongs to the pond rather than to whichever was declared last.
    /// </summary>
    public static float Carved(WaterShore[] shores, float x, float z, float ground)
    {
        WaterShore? nearest = null;
        float best = float.NegativeInfinity;
        foreach (var shore in shores)
        {
            Vector2 away = new Vector2(x, z) - shore.Centre;
            if (away.LengthSquared() > shore.Extent * shore.Extent)
                continue;
            float inside = shore.Find(x, z).Inside;
            if (inside <= best)
                continue;
            best = inside;
            nearest = shore;
        }
        return nearest is null ? ground : nearest.Carve(x, z, ground);
    }

    /// <summary>
    /// Whether the land rises above this body's surface within a few metres of its edge, sampled all the way round. The creek's
    /// hand-cut channel and a spring in its basin say yes and are left exactly as content cut them; the millpond's flat pan says no and
    /// is given a bank.
    /// </summary>
    private bool NeedsBanks(Func<float, float, float> existing)
    {
        const int Samples = 16;
        int over = 0, tried = 0;
        for (int i = 0; i < Samples; i++)
        {
            float t = (i + 0.5f) / Samples;
            float outM = EdgeAt(t) + ProbeM;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 p = _disc
                    ? _centre + new Vector2(Mathf.Cos(t * Mathf.Tau), Mathf.Sin(t * Mathf.Tau)) * outM
                    : AlongSide(t, side, outM);
                tried++;
                if (existing(p.X, p.Y) >= Level + LipM)
                    over++;
            }
        }
        // Fewer than two thirds of the samples standing above the surface means the body lies in a pan.
        return over * 3 < tried * 2;
    }

    /// <summary>A point beside the body's line at <paramref name="t"/>, <paramref name="outM"/> metres out to one side.</summary>
    private Vector2 AlongSide(float t, float side, float outM)
    {
        float at = t * _length;
        int i = 1;
        while (i < _arc.Length - 1 && _arc[i] < at)
            i++;
        Vector2 a = _line[i - 1], b = _line[i], span = b - a;
        float length = MathF.Max(1e-3f, span.Length());
        Vector2 normal = new(-span.Y / length, span.X / length);
        return a + span * ((at - _arc[i - 1]) / length) + normal * (outM * side);
    }
}
