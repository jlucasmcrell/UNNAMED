// UNNAMED Application tests - GEN-01, the player's own map pins.

using UNNAMED.Domain;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Pins are the player's own marks on ground they have walked: a place, a few words, and nothing the game decides. They survive a new session,
/// they are refused when they would be unhelpful (no words, two on one patch of ground, more than a map holds), and a save never carries them -
/// they are written beside the saves, keyed by character, like the journal's notes.
/// </summary>
public sealed class MapPinTests
{
    private static EntityId Who() => EntityId.NewId(EntityKind.Character);

    [Fact]
    public void APinIsMarked_AndComesBackInANewSession()
    {
        using var profile = new TempProfile();
        var who = Who();
        var pins = new MapPins(profile.Root, who);

        Assert.Null(pins.Add(430_000, 758_000, "  the ford below the spill  ", tick: 1_200));
        Assert.Single(pins.All);
        Assert.Equal("the ford below the spill", pins.All[0].Label);
        Assert.Equal((430_000L, 758_000L, 1_200L), (pins.All[0].XMm, pins.All[0].ZMm, pins.All[0].Tick));

        // a new session reads what the last one wrote
        var again = new MapPins(profile.Root, who);
        Assert.Single(again.All);
        Assert.Equal("the ford below the spill", again.All[0].Label);
    }

    [Fact]
    public void ACharacterKeepsTheirOwnPins()
    {
        using var profile = new TempProfile();
        var first = Who();
        var second = Who();
        var mine = new MapPins(profile.Root, first);
        var theirs = new MapPins(profile.Root, second);

        Assert.Null(mine.Add(10_000, 20_000, "mine", 5));
        Assert.Empty(theirs.All);
        Assert.Null(theirs.Add(30_000, 40_000, "theirs", 6));
        Assert.Single(new MapPins(profile.Root, first).All);
        Assert.Single(new MapPins(profile.Root, second).All);
    }

    [Fact]
    public void APinWithoutWords_IsRefused()
    {
        using var profile = new TempProfile();
        var pins = new MapPins(profile.Root, Who());

        Assert.Equal("a pin has a few words on it", pins.Add(1_000, 2_000, "   ", 1));
        Assert.Equal($"a pin's words are at most {MapPins.MaxLabel} characters",
            pins.Add(1_000, 2_000, new string('x', MapPins.MaxLabel + 1), 1));
        Assert.Empty(pins.All);
    }

    [Fact]
    public void TwoPinsOnOnePatchOfGround_AreRefused_AndRemovingFreesIt()
    {
        using var profile = new TempProfile();
        var pins = new MapPins(profile.Root, Who());

        Assert.Null(pins.Add(5_000, 6_000, "the gate", 1));
        Assert.Equal("there is already a pin on that ground", pins.Add(5_000, 6_000, "the gate again", 2));
        Assert.Null(pins.Remove(5_000, 6_000));
        Assert.Empty(pins.All);
        Assert.Equal("there is no pin on that ground", pins.Remove(5_000, 6_000));
        Assert.Null(pins.Add(5_000, 6_000, "the gate, again", 3));
    }

    [Fact]
    public void AMapHoldsNoMoreThanItsPins()
    {
        using var profile = new TempProfile();
        var pins = new MapPins(profile.Root, Who());

        for (int i = 0; i < MapPins.Max; i++)
            Assert.Null(pins.Add(i * 1_000, 0, $"mark {i}", i));
        Assert.Equal(MapPins.Max, pins.All.Length);
        Assert.Equal($"a map holds at most {MapPins.Max} pins", pins.Add(999_000, 999_000, "one too many", 99));
    }
}
