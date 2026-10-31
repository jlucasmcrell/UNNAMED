// UNNAMED Presentation tests - GEN-10's settings: the assists live in the settings save, ride its file, and normalise what they cannot take.

using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The accessibility assists as the settings file keeps them: a generous window, a telegraph palette, and - with them, since GEN-10 is one
/// package - the shake slider and the caption size and background, which are only real if a saved choice survives the trip to disk.
/// </summary>
public class AssistPrefsTests
{
    [Fact]
    public void TheAssistsStartAtTheGamesOwnAnswers()
    {
        var prefs = new ClientPrefs();
        Assert.Equal(TimingWindows.Normal, prefs.Profile.Assist.Windows);
        Assert.Equal(TelegraphPalette.Default, prefs.Profile.Assist.Telegraphs);

        // and the held-or-toggled presses: a press is held unless the player says otherwise, aim beside sprint and guard
        Assert.False(prefs.Profile.Controls.IsToggle("sprint"));
        Assert.False(prefs.Profile.Controls.IsToggle("guard"));
        Assert.False(prefs.Profile.Controls.IsToggle("aim"));
        Assert.True(prefs.Profile.Controls.IsToggle("crouch"));
    }

    [Fact]
    public void EveryAssistSurvivesTheSettingsFile()
    {
        var chosen = new ClientPrefs
        {
            Profile = new ClientPrefs().Profile with
            {
                Assist = new AssistPrefs { Windows = TimingWindows.Generous, Telegraphs = TelegraphPalette.Amber },
                Camera = new ClientPrefs().Profile.Camera with { Shake = 0f },
                Interface = new ClientPrefs().Profile.Interface with
                {
                    SubtitleSize = 2,
                    SubtitleBackground = SubtitleBackground.Solid,
                },
                Controls = new ClientPrefs().Profile.Controls with
                {
                    Toggles = new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        ["sprint"] = true, ["guard"] = true, ["aim"] = true,
                    },
                },
            },
        };

        var read = ClientPrefs.FromJson(chosen.ToJson());
        Assert.Equal(TimingWindows.Generous, read.Profile.Assist.Windows);
        Assert.Equal(TelegraphPalette.Amber, read.Profile.Assist.Telegraphs);
        Assert.Equal(0f, read.Profile.Camera.Shake);
        Assert.Equal(2, read.Profile.Interface.SubtitleSize);
        Assert.Equal(SubtitleBackground.Solid, read.Profile.Interface.SubtitleBackground);
        Assert.True(read.Profile.Controls.IsToggle("sprint"));
        Assert.True(read.Profile.Controls.IsToggle("guard"));
        Assert.True(read.Profile.Controls.IsToggle("aim"));
    }

    [Theory]
    [InlineData(9)]     // a hand-edited number that is no timing window
    public void AnUnknownWindowChoice_FallsBackToTheTunedOne(int written)
    {
        var read = ClientPrefs.FromJson($$"""{ "profile": { "assist": { "windows": {{written}} } } }""");
        Assert.Equal(TimingWindows.Normal, read.Profile.Assist.Windows);
    }

    [Theory]
    [InlineData(7)]
    public void AnUnknownPalette_FallsBackToTheDrawnOne(int written)
    {
        var read = ClientPrefs.FromJson($$"""{ "profile": { "assist": { "telegraphs": {{written}} } } }""");
        Assert.Equal(TelegraphPalette.Default, read.Profile.Assist.Telegraphs);
    }

    [Fact]
    public void AToggleTheGameCannotHold_IsDroppedRatherThanKept()
    {
        // aim is a real toggleable action (GEN-10); a made-up one is not kept in the file
        var read = ClientPrefs.FromJson(
            """{ "profile": { "controls": { "toggles": { "aim": true, "teleport": true } } } }""");
        Assert.True(read.Profile.Controls.IsToggle("aim"));
        Assert.False(read.Profile.Controls.Toggles.ContainsKey("teleport"));
    }

    [Fact]
    public void AnUnreadableAssistBlock_GivesTheDefaultsNotACrash()
    {
        var read = ClientPrefs.FromJson("""{ "profile": { "assist": "yes" } }""");
        Assert.Equal(TimingWindows.Normal, read.Profile.Assist.Windows);
        Assert.Equal(TelegraphPalette.Default, read.Profile.Assist.Telegraphs);
    }
}
