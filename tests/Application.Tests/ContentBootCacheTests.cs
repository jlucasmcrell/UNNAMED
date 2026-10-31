// UNNAMED Application tests - the content boot is read once per run and shared, and sharing it changes no test's world.

using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

[Collection(SharedContentCollection.Name)]
public sealed class ContentBootCacheTests
{
    private readonly SharedContent _content;

    public ContentBootCacheTests(SharedContent content) => _content = content;

    [Fact]
    public void TheSameRequest_IsBuiltOnce_AndHandedBackIdentically()
    {
        using var profile = new TempProfile();
        var again = ContentBootCache.Get(SharedContent.Options(profile.Root),
            () => throw new InvalidOperationException("the cache rebuilt a request it had already answered"));
        Assert.Same(_content.Boot, again);
        Assert.Same(_content.Boot.Setup, again.Setup);
        Assert.Same(_content.Boot.Generator, again.Generator);
    }

    [Fact]
    public void TwoSessionsOnSharedContent_HaveTheirOwnWorlds()
    {
        using var a = new TempProfile();
        var first = Harness.Boot(a);
        using var b = new TempProfile();
        var second = Harness.Boot(b);

        Assert.Same(first.Setup, second.Setup);   // the content is shared
        Assert.NotSame(first, second);            // the session is not
        Assert.Null(second.Simulation);           // and neither has a world before it starts one

        first.NewGame(Harness.Named("First"));
        Assert.NotNull(first.Simulation);
        Assert.Null(second.Simulation);

        second.NewGame(Harness.Named("Second"));
        Assert.NotNull(second.Simulation);

        // walk the first body and tick both: the second must not move, which is what a shared mutable world would break
        var secondStands = second.Simulation!.Player.Body;
        var firstStands = first.Simulation!.Player.Body;
        first.Submit(new MoveCommand(first.Simulation.PlayerId, new MoveIntent(0, 1000, Gait.Run, 0)));
        Harness.Ticks(first, 1);
        Harness.Ticks(second, 1);

        Assert.NotEqual(firstStands, first.Simulation.Player.Body);   // the intent moved the body it was given
        Assert.Equal(secondStands, second.Simulation.Player.Body);    // and nothing at all moved the other world
    }
}
