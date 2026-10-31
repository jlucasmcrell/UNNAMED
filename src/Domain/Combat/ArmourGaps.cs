// UNNAMED Domain - armour gaps: where a point can go in past the plate (COMBAT.md §248-276; CAP05 §3.4 CX-04; CMB-04, CMB-24)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;   // EntityId

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The places a plate harness does not close: the points canon names for Gorget, Voiders and Besagews to cover (COMBAT.md §248-276).
/// A gap technique names one and asks how open it is on the body in front of it.
/// </summary>
public enum ArmourGap
{
    /// <summary>Under the chin, above the gorget's reach.</summary>
    Throat,

    /// <summary>The armpit, under the shoulder plate.</summary>
    Armpit,

    /// <summary>The inside of the elbow.</summary>
    Elbow,

    /// <summary>The wrist, where the vambrace ends.</summary>
    Wrist,

    /// <summary>The waist, between breastplate and fauld.</summary>
    Waist,

    /// <summary>Behind the knee.</summary>
    Knee,

    /// <summary>The face, where the visor is not down.</summary>
    Face,
}

/// <summary>
/// How open a gap is on one body, in thousandths: 0 is covered, 1000 is bare flesh. CMB-04 (armour gaps and the pieces that close
/// them) owns the real answer, reading a body's covering per region; until it lands, <see cref="StubArmourGaps"/> stands in, so a gap
/// technique can be built, used and tested now and reads the true coverage the day the reader is swapped.
/// </summary>
public interface IArmourGapReader
{
    /// <summary>How open <paramref name="gap"/> is on the body <paramref name="target"/> is, in thousandths.</summary>
    int OpenPermille(EntityId target, ArmourGap gap);
}

/// <summary>Every gap closed: the reader a world with no coverage model has. A gap thrust against it is an ordinary thrust.</summary>
public sealed class NoArmourGaps : IArmourGapReader
{
    public static readonly NoArmourGaps Instance = new();

    public int OpenPermille(EntityId target, ArmourGap gap) => 0;
}

/// <summary>
/// The seam for CMB-04 (COMBAT.md §248-276): a harness has plate over the chest and back and leaves the joints and the throat open, and
/// these are the working numbers for that shape - throat and face most open, the armpit next, the waist and the joints less. They are
/// target-agnostic because no per-body coverage exists yet; a reader that knows the body it is asked about replaces this and nothing
/// downstream changes. Provenance: the COMBAT §248-276 gap table's shape, with the values provisional.
/// </summary>
public sealed class StubArmourGaps : IArmourGapReader
{
    public static readonly StubArmourGaps Instance = new();

    private static readonly ImmutableDictionary<ArmourGap, int> Canon = new Dictionary<ArmourGap, int>
    {
        [ArmourGap.Throat] = 650,
        [ArmourGap.Face] = 450,
        [ArmourGap.Armpit] = 550,
        [ArmourGap.Elbow] = 350,
        [ArmourGap.Wrist] = 400,
        [ArmourGap.Waist] = 300,
        [ArmourGap.Knee] = 300,
    }.ToImmutableDictionary();

    public int OpenPermille(EntityId target, ArmourGap gap) => Canon.GetValueOrDefault(gap);
}
