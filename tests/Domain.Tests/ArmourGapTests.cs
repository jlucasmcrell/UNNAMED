// UNNAMED Domain tests - the gaps, and the pieces made to close them (CMB-04).

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// COMBAT §13-§14 as a mechanic: a placed blow finds the seam where two plates meet, an uncovered seam is where the plate is not, and each
/// piece in the doc closes the seam it was made for - including the four exotic ones, each with its own narrow job.
/// </summary>
public class ArmourGapTests
{
    private static readonly CombatConstants Constants = new()
    {
        RegionWeights = ImmutableSortedDictionary<BodyRegion, int>.Empty
            .Add(BodyRegion.Head, 1).Add(BodyRegion.Torso, 1).Add(BodyRegion.Limbs, 1),
        RegionMultipliers = ImmutableSortedDictionary<BodyRegion, double>.Empty
            .Add(BodyRegion.Head, 1.0).Add(BodyRegion.Torso, 1.0).Add(BodyRegion.Limbs, 1.0),
    };

    /// <summary>A roll sequence, one value per sample index the resolver asks for.</summary>
    private static Func<uint, double> Rolls(params double[] bySample) =>
        index => index < bySample.Length ? bySample[index] : 0.5;

    private static Strike Blow(string damageType = "physical_slash", int gapFind = 0, bool phase = false,
        BodyRegion? region = null) =>
        new(new AttackProfile("test", 10, 10, damageType, 1_200, 10, 4, 10, 10), 1.0, 0, 1.0)
        {
            GapFindPercent = gapFind,
            Phase = phase,
            ForcedRegion = region,
        };

    private static DefenseProfile Armoured(int plate = 20, ImmutableSortedDictionary<string, int>? gaps = null) =>
        new(100, ImmutableSortedDictionary<BodyRegion, int>.Empty
                .Add(BodyRegion.Head, plate).Add(BodyRegion.Torso, plate).Add(BodyRegion.Limbs, plate),
            ImmutableSortedDictionary<string, double>.Empty, false, false)
        {
            GapArmour = gaps ?? ImmutableSortedDictionary<string, int>.Empty,
        };

    [Fact]
    public void APlacedBlow_FindsTheSeam_AndThePlateDoesNotAnswerIt()
    {
        // the same roll twice: one blown onto the plate, one placed into the seam
        var onThePlate = CombatRules.Resolve(Blow(), Armoured(), Constants, Rolls(.5, .5, .5, .5, .5));
        var inTheSeam = CombatRules.Resolve(Blow(gapFind: 100), Armoured(), Constants, Rolls(.5, .5, .5, .5, .5, 0.0, 0.0, 0.0));

        Assert.Null(onThePlate.Gap);
        Assert.NotNull(inTheSeam.Gap);
        Assert.True(inTheSeam.Final > onThePlate.Final,
            $"a gap should hurt more than the plate: {inTheSeam.Final} in the seam vs {onThePlate.Final} on the plate");
    }

    [Fact]
    public void TheGapFound_IsAlwaysASeamOfTheRegionStruck()
    {
        foreach (var region in Enum.GetValues<BodyRegion>())
        {
            for (int pick = 0; pick < 4; pick++)
            {
                var strike = new Strike(new AttackProfile("test", 10, 10, "physical_slash", 1_200, 10, 4, 10, 10), 1.0, 0, 1.0)
                {
                    GapFindPercent = 100,
                    ForcedRegion = region,
                };
                var hit = CombatRules.Resolve(strike, Armoured(), Constants, Rolls(.5, .5, .5, .5, .5, 0.0, pick * 0.25, 0.0));
                Assert.NotNull(hit.Gap);
                Assert.Equal(region, ArmourGaps.Get(hit.Gap!)!.Region);
            }
        }
    }

    [Fact]
    public void AGorget_ClosesTheThroat_AndTheSeamStopsHurting()
    {
        var uncovered = CombatRules.Resolve(Blow(gapFind: 100, region: BodyRegion.Head), Armoured(), Constants,
            Rolls(.5, .5, .5, .5, .5, 0.0, 0.0, 0.0));
        var withGorget = CombatRules.Resolve(Blow(gapFind: 100, region: BodyRegion.Head),
            Armoured(gaps: Covers.Armour(new[] { "item.armor.gorget" })), Constants, Rolls(.5, .5, .5, .5, .5, 0.0, 0.0, 0.0));

        // a slash to the head finds the face; the gorget closes the face and the throat both
        Assert.Equal("face", uncovered.Gap);
        Assert.True(Covers.For("item.armor.gorget")!.Gaps.Contains("face"));
        Assert.True(withGorget.Final < uncovered.Final,
            $"a gorget should soften the throat: {withGorget.Final} worn vs {uncovered.Final} bare");
        Assert.True(withGorget.Final > 0, "a blow that lands always wounds (COMBAT §32)");
    }

    [Fact]
    public void VoidersAndBesagews_BothCloseTheArmpit()
    {
        var gaps = Covers.Armour(new[] { "item.armor.voiders", "item.armor.besagews" });
        Assert.True(gaps.ContainsKey("armpit"));
        Assert.True(gaps["armpit"] > 0);
        // and two pieces over one seam do not add up to two pieces' worth
        Assert.Equal(Math.Max(Covers.For("item.armor.voiders")!.Protection, Covers.For("item.armor.besagews")!.Protection),
            gaps["armpit"]);
    }

    [Fact]
    public void AWhispergorget_AnswersTheNarrowBlade_AndAnOrdinaryGorgetDoesNotHaveTo()
    {
        var whisper = Covers.Armour(new[] { "item.armor.whispergorget" });
        var mail = Covers.Armour(new[] { "item.armor.mail_standard" });
        var plain = Covers.Armour(new[] { "item.armor.gorget" });

        foreach (var set in new[] { whisper, mail, plain })
        {
            Assert.True(set.ContainsKey("throat"));
            Assert.True(set["throat"] > 0);
        }
        // a bodkin finds the throat, and a narrow-blade piece is what stands in it
        var pierced = CombatRules.Resolve(Blow("physical_pierce", gapFind: 100, region: BodyRegion.Head), Armoured(), Constants,
            Rolls(.5, .5, .5, .5, .5, 0.0, 0.0, 0.0));
        Assert.Equal("throat", pierced.Gap);
    }

    [Fact]
    public void TheJointwardMesh_ClosesEveryArticulationSeam_AndNothingElse()
    {
        var mesh = Covers.Armour(new[] { "item.armor.jointward_mesh" });
        foreach (string seam in new[] { "armpit", "elbow", "knee" })
            Assert.True(mesh.ContainsKey(seam), $"the mesh should cover the {seam}");
        Assert.False(mesh.ContainsKey("throat"));
        Assert.False(mesh.ContainsKey("groin"));
    }

    [Fact]
    public void PhaseHarm_PassesThePlate_UnlessAVeilCollarOrAPhaseLaminateAnswersIt()
    {
        var plateOnly = CombatRules.Resolve(Blow("arcane", phase: true), Armoured(), Constants, Rolls(.5, .5, .5, .5, .5));
        var veil = CombatRules.Resolve(Blow("arcane", phase: true),
            Armoured(gaps: Covers.Armour(new[] { "item.armor.veil_collar" })), Constants, Rolls(.5, .5, .5, .5, .5));
        var laminate = CombatRules.Resolve(Blow("arcane", phase: true),
            Armoured(gaps: Covers.Armour(new[] { "item.armor.phase_laminate" })), Constants, Rolls(.5, .5, .5, .5, .5));

        Assert.True(veil.Final < plateOnly.Final, $"a veil collar should answer phase harm: {veil.Final} vs {plateOnly.Final}");
        Assert.True(laminate.Final < plateOnly.Final, $"a phase laminate should answer phase harm: {laminate.Final} vs {plateOnly.Final}");
        Assert.Equal(veil.Final, laminate.Final);   // both answer it, neither better than the other
    }

    [Fact]
    public void AnOrdinaryBlow_IsUntouchedByGaps()
    {
        // the default strike: no gap finding, no phase - the resolver must behave exactly as it did before CMB-04
        var bare = CombatRules.Resolve(Blow(), Armoured(), Constants, Rolls(.5, .5, .5, .5, .5));
        var wearing = CombatRules.Resolve(Blow(), Armoured(gaps: Covers.Armour(Covers.All.Select(c => c.ItemDefId))),
            Constants, Rolls(.5, .5, .5, .5, .5));

        Assert.Null(bare.Gap);
        Assert.Null(wearing.Gap);
        Assert.Equal(bare.Final, wearing.Final);
        Assert.Equal(bare.Raw, wearing.Raw);
    }

    [Fact]
    public void EveryGap_HasAPieceMadeForIt()
    {
        foreach (var gap in ArmourGaps.All)
            Assert.True(Covers.Covering(gap.Id).Any(), $"no piece in COMBAT §13-§14 covers {gap.Id}");
    }

    [Fact]
    public void EveryPiece_ClosesSomethingTheGapsKnow()
    {
        foreach (var cover in Covers.All)
            foreach (string seam in cover.Gaps)
                Assert.True(ArmourGaps.Get(seam) is not null, $"{cover.Name} claims a seam the gaps do not know: {seam}");
    }

    [Fact]
    public void TheTwoDozenSeams_CoverTheThreeRegions()
    {
        Assert.NotEmpty(ArmourGaps.OfRegion(BodyRegion.Head));
        Assert.NotEmpty(ArmourGaps.OfRegion(BodyRegion.Torso));
        Assert.NotEmpty(ArmourGaps.OfRegion(BodyRegion.Limbs));
        // phase is not a seam in a region: it is the harm plate cannot cover at all
        Assert.True(ArmourGaps.Get("phase")!.Phase);
        Assert.DoesNotContain(ArmourGaps.Findable(BodyRegion.Torso, narrowBlade: false, phase: false), g => g.Phase);
    }
}
