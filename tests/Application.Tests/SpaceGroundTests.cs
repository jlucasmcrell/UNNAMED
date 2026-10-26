// UNNAMED Application tests - a run holds the ground of every space it can reach (B1, first piece)
// Before this the simulation had exactly one WalkSpace - the layout's - so every height and every wall test came
// from the exterior's terrain, which is only correct while one space is loaded. The interior's ground is now in the
// setup when the gate lets it in, and absent when it does not.

using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using Xunit;

namespace UNNAMED.Application.Tests;

public class SpaceGroundTests
{
    private const string Ring = "space.barrowworks";

    [Fact]
    public void WithTheGateOn_TheRunHoldsTheRegionsGroundAndTheInteriors()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, barrowworks: true).Setup;

        Assert.True(setup.Spaces.ContainsKey(setup.Layout.SpaceId));
        Assert.True(setup.Spaces.ContainsKey(SpaceId.Parse(Ring)), "the ring's ground should be loaded with the gate on");

        // And it is not the same ground: the interior's own metres are the ring's, not the Ashen Hollow's.
        var inner = setup.SpaceFor(SpaceId.Parse(Ring));
        Assert.NotEqual(setup.Layout.Space.MinXMm, inner.MinXMm);
    }

    [Fact]
    public void WithTheGateOff_TheRunHoldsOnlyItsOwn()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;

        Assert.Equal(2, setup.Spaces.Count);   // the frontier and the hollow under the den (spec 28), which no gate holds back
        Assert.False(setup.Spaces.ContainsKey(SpaceId.Parse(Ring)));
        // A space it does not hold falls back to its own, which is what an unloaded space can honestly answer.
        Assert.Equal(setup.Layout.Space, setup.SpaceFor(SpaceId.Parse(Ring)));
    }
}
