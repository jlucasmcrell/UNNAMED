using System.Collections.Immutable;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// CMB-147's memory (CAMERA §320-330): a distance per activity, a manual zoom that rewrites the activity's entry, and a settle back to it.
/// The feel is the owner's to judge; what the rules promise is that the same facts always give the same numbers.
/// </summary>
public class CameraMemoryTests
{
    [Fact]
    public void EachActivityKeepsItsOwnDistance()
    {
        var memory = CameraMemory.Default;
        Assert.Equal(3.5f, memory.For(CameraActivity.Exploring));
        Assert.Equal(4.6f, memory.For(CameraActivity.Combat));
        Assert.Equal(2.1f, memory.For(CameraActivity.Conversation));
        Assert.Equal(1.1f, memory.For(CameraActivity.Crafting));
        Assert.Equal(2.4f, memory.For(CameraActivity.Interior));
    }

    [Fact]
    public void AManualZoom_RewritesOnlyTheActivityItHappenedIn()
    {
        var (distance, memory) = CameraMemoryRules.Zoom(CameraMemory.Default, CameraActivity.Combat, 4.6f, 0.9f);

        Assert.Equal(5.5f, distance);
        Assert.Equal(5.5f, memory.For(CameraActivity.Combat));

        // Every other activity is untouched: this is a memory per kind of work, not one global distance.
        Assert.Equal(3.5f, memory.For(CameraActivity.Exploring));
        Assert.Equal(2.1f, memory.For(CameraActivity.Conversation));
        Assert.Equal(1.1f, memory.For(CameraActivity.Crafting));
        Assert.Equal(2.4f, memory.For(CameraActivity.Interior));
    }

    [Fact]
    public void AZoomIsHeldInsideTheRigsRange()
    {
        var (far, farMemory) = CameraMemoryRules.Zoom(CameraMemory.Default, CameraActivity.Exploring, 5.9f, 5f);
        Assert.Equal(CameraMemoryRules.MaxDistance, far);
        Assert.Equal(CameraMemoryRules.MaxDistance, farMemory.For(CameraActivity.Exploring));

        var (near, nearMemory) = CameraMemoryRules.Zoom(CameraMemory.Default, CameraActivity.Exploring, 0.2f, -5f);
        Assert.Equal(0f, near);
        Assert.Equal(0f, nearMemory.For(CameraActivity.Exploring));
    }

    [Fact]
    public void AHandEditedFile_CannotPutTheCameraSomewhereItCannotGo()
    {
        var wild = new CameraMemory(Exploring: -3f, Combat: 99f, Conversation: float.NaN, Crafting: 1f, Interior: 2f).Normalised();
        Assert.Equal(0f, wild.Exploring);
        Assert.Equal(CameraMemoryRules.MaxDistance, wild.Combat);
        Assert.Equal(CameraMemoryRules.MaxDistance, wild.Conversation);
        Assert.Equal(1f, wild.Crafting);
    }

    [Fact]
    public void TheSettle_ApproachesWithoutOvershooting_AndSnapsAtTheEnd()
    {
        // A third of a second toward a target: closer than it was, never past it.
        float once = CameraMemoryRules.Ease(3.5f, 5.5f, 1.0 / 3.0);
        Assert.InRange(once, 3.5f, 5.5f);
        Assert.True(once > 3.5f);

        // However long it is given, it lands exactly, and it never crosses.
        float settled = 3.5f;
        for (int i = 0; i < 240; i++)
        {
            settled = CameraMemoryRules.Ease(settled, 5.5f, 1.0 / 60.0);
            Assert.InRange(settled, 3.5f, 5.5f);
        }
        Assert.Equal(5.5f, settled);

        // Easing toward where it already is costs nothing.
        Assert.Equal(4.6f, CameraMemoryRules.Ease(4.6f, 4.6f, 1.0 / 60.0));
    }

    [Fact]
    public void TheActivityIsChosenByWhatNarrowsTheFrameMost()
    {
        // A conversation wins over everything: it is the narrowest frame the camera has.
        Assert.Equal(CameraActivity.Conversation, CameraMemoryRules.Classify(inConversation: true, crafting: true, inCombat: true, interior: true));
        // A bench wins over a fight, because the wheel is on the bench.
        Assert.Equal(CameraActivity.Crafting, CameraMemoryRules.Classify(false, crafting: true, inCombat: true, interior: true));
        // A fight wins over the room.
        Assert.Equal(CameraActivity.Combat, CameraMemoryRules.Classify(false, false, inCombat: true, interior: true));
        // Indoors, with nothing else on.
        Assert.Equal(CameraActivity.Interior, CameraMemoryRules.Classify(false, false, false, interior: true));
        // And the open road.
        Assert.Equal(CameraActivity.Exploring, CameraMemoryRules.Classify(false, false, false, interior: false));
    }

    [Fact]
    public void ThePrefsFileKeepsOnePagePerCharacter_AndSanitisesThem()
    {
        var prefs = new ProfilePrefs
        {
            CameraByCharacter = ImmutableSortedDictionary.Create<string, CameraMemory>(StringComparer.Ordinal)
                .SetItem("entity.character.a", CameraMemory.Default.With(CameraActivity.Combat, 5.9f))
                .SetItem("entity.character.b", new CameraMemory(Exploring: 99f))
                .SetItem("", CameraMemory.Default),
        }.Normalised();

        // A blank key is not a character: it is dropped.
        Assert.Equal(2, prefs.CameraByCharacter.Count);

        // Each character keeps its own habit, and a hand-edited value is brought back into range.
        Assert.Equal(5.9f, prefs.CameraByCharacter["entity.character.a"].For(CameraActivity.Combat));
        Assert.Equal(3.5f, prefs.CameraByCharacter["entity.character.a"].For(CameraActivity.Exploring));
        Assert.Equal(CameraMemoryRules.MaxDistance, prefs.CameraByCharacter["entity.character.b"].For(CameraActivity.Exploring));
    }
}
