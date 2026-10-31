// UNNAMED Domain - armour gaps and the pieces made to close them (CMB-04; COMBAT_DAMAGE_ARMOR_AND_DEATH.md §13-§14; CAP05 3.4 CX-04).
// No Godot references - pure C#, integer and table only.
//
// A blow rolled onto a region meets that region's armour. A blow *placed* into the seam where two plates meet meets almost nothing: the
// throat above the collar, the armpit where the arm leaves the body, the inside of the elbow, the back of the knee, the waist under the
// breastplate, the groin. Those seams are the gaps, and every piece in §13 of the combat doc exists to close one of them.
//
// The exotic four in §14 are working vocabulary, not canon: they are modelled here because the checklist names them, and each is given
// one honest, narrow job rather than a stat line. A Whispergorget stops a narrow blade at the throat and nothing else. A Veil collar and
// a Phase laminate answer phase and soul harm, which plate never did.

using System.Collections.Immutable;

namespace UNNAMED.Domain.Combat;

/// <summary>One seam a placed blow can find. <see cref="Region"/> is the body region the gap belongs to.</summary>
public sealed record ArmourGap(string Id, BodyRegion Region, string Name)
{
    /// <summary>A narrow blade - a bodkin, a rondel dagger, a garrote wire - finds this gap more easily than a spear does.</summary>
    public bool NarrowBlade { get; init; }

    /// <summary>The gap opens when the joint moves: an elbow, a knee, a shoulder.</summary>
    public bool Articulation { get; init; }

    /// <summary>The gap is not in the plate at all but in the world: phase and soul harm pass armour unless something else answers it.</summary>
    public bool Phase { get; init; }
}

/// <summary>
/// Every gap a placed blow may find, by the region it belongs to. Kept small on purpose: a gap is only worth modelling if a piece exists
/// to close it, which <see cref="Covers"/> is checked against in the tests.
/// </summary>
public static class ArmourGaps
{
    public static readonly ImmutableArray<ArmourGap> All = ImmutableArray.Create(
        new ArmourGap("throat", BodyRegion.Head, "the throat above the collar") { NarrowBlade = true },
        new ArmourGap("face", BodyRegion.Head, "the face below the brow"),
        new ArmourGap("armpit", BodyRegion.Torso, "the armpit where the arm leaves the body") { Articulation = true, NarrowBlade = true },
        new ArmourGap("waist", BodyRegion.Torso, "the waist under the breastplate"),
        new ArmourGap("groin", BodyRegion.Torso, "the groin below the fauld"),
        new ArmourGap("elbow", BodyRegion.Limbs, "the inside of the elbow") { Articulation = true },
        new ArmourGap("knee", BodyRegion.Limbs, "the back of the knee") { Articulation = true },
        new ArmourGap("ankle", BodyRegion.Limbs, "the ankle above the shoe"),
        // not a seam in plate at all: the harm the exotic pieces are made to answer
        new ArmourGap("phase", BodyRegion.Torso, "the body's own phase, which plate cannot cover") { Phase = true });

    private static readonly ImmutableSortedDictionary<string, ArmourGap> ById =
        All.ToImmutableSortedDictionary(g => g.Id, g => g, StringComparer.Ordinal);

    public static ArmourGap? Get(string id) => ById.TryGetValue(id, out var gap) ? gap : null;

    public static ImmutableArray<ArmourGap> OfRegion(BodyRegion region) =>
        All.Where(g => g.Region == region).ToImmutableArray();

    /// <summary>
    /// The gaps a placed blow may find on this region, narrowed by what the blow is: a narrow blade or an articulation attack reaches
    /// seams a broad one cannot, and phase harm only ever finds the phase gap.
    /// </summary>
    public static ImmutableArray<ArmourGap> Findable(BodyRegion region, bool narrowBlade, bool phase)
    {
        if (phase)
            return All.Where(g => g.Phase).ToImmutableArray();
        return OfRegion(region).Where(g => !g.Phase && (g.NarrowBlade == narrowBlade || !g.NarrowBlade)).ToImmutableArray();
    }
}

/// <summary>
/// One piece made to close gaps. <see cref="Gaps"/> are the seams it covers; <see cref="Protection"/> is the armour it puts in them, which
/// is the same number the piece contributes to the region it sits on. <see cref="NarrowBlade"/> marks the pieces whose whole purpose is a
/// narrow blade (a gorget against a garrote), and <see cref="Phase"/> the pieces that answer phase harm.
/// </summary>
public sealed record GapCover(string ItemDefId, string Name, ImmutableArray<string> Gaps, int Protection)
{
    public bool NarrowBlade { get; init; }
    public bool Phase { get; init; }
}

/// <summary>The pieces of COMBAT §13, and the four exotic ones of §14, each keyed to the content item that is worn.</summary>
public static class Covers
{
    public static readonly ImmutableArray<GapCover> All = ImmutableArray.Create(
        // §13 - the historical set
        new GapCover("item.armor.gorget", "Gorget", ImmutableArray.Create("throat", "face"), 4) { NarrowBlade = true },
        new GapCover("item.armor.bevor", "Bevor", ImmutableArray.Create("throat"), 3) { NarrowBlade = true },
        new GapCover("item.armor.aventail", "Aventail", ImmutableArray.Create("throat", "face"), 2),
        new GapCover("item.armor.mail_standard", "Mail standard", ImmutableArray.Create("throat", "armpit"), 2) { NarrowBlade = true },
        new GapCover("item.armor.voiders", "Voiders", ImmutableArray.Create("armpit"), 3) { NarrowBlade = true },
        new GapCover("item.armor.besagews", "Besagews", ImmutableArray.Create("armpit"), 3),
        new GapCover("item.armor.couters", "Couters", ImmutableArray.Create("elbow"), 3),
        new GapCover("item.armor.poleyns", "Poleyns", ImmutableArray.Create("knee"), 3),
        new GapCover("item.armor.faulds", "Faulds and tassets", ImmutableArray.Create("waist", "groin"), 3),
        new GapCover("item.armor.groin_guard", "Groin guard", ImmutableArray.Create("groin"), 2),
        new GapCover("item.armor.sabatons", "Sabatons", ImmutableArray.Create("ankle"), 3),
        new GapCover("item.armor.gauntlets", "Gauntlets", ImmutableArray.Create("elbow"), 1),
        // §14 - exotic, working vocabulary rather than canon
        new GapCover("item.armor.whispergorget", "Whispergorget", ImmutableArray.Create("throat"), 3) { NarrowBlade = true },
        new GapCover("item.armor.jointward_mesh", "Jointward mesh", ImmutableArray.Create("armpit", "elbow", "knee"), 3),
        new GapCover("item.armor.veil_collar", "Veil collar", ImmutableArray.Create("phase"), 0) { Phase = true },
        new GapCover("item.armor.phase_laminate", "Phase laminate", ImmutableArray.Create("phase"), 0) { Phase = true });

    private static readonly ImmutableSortedDictionary<string, GapCover> ByItem =
        All.ToImmutableSortedDictionary(c => c.ItemDefId, c => c, StringComparer.Ordinal);

    public static GapCover? For(string itemDefId) => ByItem.TryGetValue(itemDefId, out var cover) ? cover : null;

    public static ImmutableArray<GapCover> Covering(string gapId) =>
        All.Where(c => c.Gaps.Contains(gapId)).ToImmutableArray();

    /// <summary>
    /// The armour a body wearing <paramref name="wornItemDefIds"/> has in each gap: the best covering piece the body wears, not the sum of
    /// them, because two gorgets do not make two gorgets' worth of protection.
    /// </summary>
    public static ImmutableSortedDictionary<string, int> Armour(IEnumerable<string> wornItemDefIds)
    {
        var armour = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string item in wornItemDefIds)
        {
            if (For(item) is not { } cover)
                continue;
            foreach (string gap in cover.Gaps)
            {
                int protection = cover.Phase ? PhaseProtection : cover.Protection;
                armour[gap] = Math.Max(armour.GetValueOrDefault(gap), protection);
            }
        }
        return armour.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// What a phase-answering piece is worth against phase harm: it is not armour in the plate sense, so it works as a flat share of the
    /// harm turned aside, expressed here as the armour number the same curve reads (COMBAT §18's <c>armour / (armour + ArmorK)</c>).
    /// </summary>
    public const int PhaseProtection = 25;
}
