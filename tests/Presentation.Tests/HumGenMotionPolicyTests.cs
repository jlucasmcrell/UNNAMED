using UNNAMED.Presentation.Ui.Models;
namespace UNNAMED.Presentation.Tests;

public class HumGenMotionPolicyTests
{
    private static Dictionary<string, string> Sources() => HumGenMotionPolicy.Required.ToDictionary(state => state, _ => "ual1/approved");

    [Fact]
    public void IncompleteComparisonBakeIsRefused() => Assert.Throws<InvalidDataException>(() =>
        HumGenMotionPolicy.Validate(HumGenMotionPolicy.Required.Where(state => state != "walk_back"), Sources(), 1, 1));

    [Fact]
    public void KayKitIsRefusedEvenOutsideLocomotion()
    {
        var states = HumGenMotionPolicy.Required.Append("bow_draw");
        var sources = Sources();
        sources["bow_draw"] = "KayKit/old_bow";
        Assert.Throws<InvalidDataException>(() => HumGenMotionPolicy.Validate(states, sources, 1, 1));
    }

    [Fact]
    public void MissingSourceOrContactReceiptIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => HumGenMotionPolicy.Validate(HumGenMotionPolicy.Required, new Dictionary<string, string>(), 1, 1));
        Assert.Throws<InvalidDataException>(() => HumGenMotionPolicy.Validate(HumGenMotionPolicy.Required, Sources(), 0, 1));
        HumGenMotionPolicy.Validate(HumGenMotionPolicy.Required, Sources(), 1, 1);
    }

    [Fact]
    public void RawTransportWithoutSourcePlaybackTimingIsRefused() => Assert.Throws<InvalidDataException>(() =>
        HumGenMotionPolicy.Validate(HumGenMotionPolicy.Required, Sources(), 1, 0));
}
