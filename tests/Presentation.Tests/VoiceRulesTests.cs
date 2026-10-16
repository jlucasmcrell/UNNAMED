using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Tests;

/// <summary>Voice-over and subtitles (WP5): where a line's voice lies, when its words show, the music's duck and a bark's fade.</summary>
public class VoiceRulesTests
{
    [Fact]
    public void ALinesVoice_LiesUnderTheVoiceFolder_ByItsLineId()
    {
        string root = Path.Combine("assets", "ws");
        Assert.Equal(Path.Combine(root, "vo", "dialogue.ashen_hollow.kera_voss", "greet.ogg"), VoiceRules.VoicePath(root, "dialogue.ashen_hollow.kera_voss/greet"));
        Assert.Equal(Path.Combine(root, "vo", "bark.ashen_hollow.kera_voss.2.ogg"), VoiceRules.VoicePath(root, "bark.ashen_hollow.kera_voss.2"));
        Assert.Null(VoiceRules.VoicePath(null, "bark.ashen_hollow.kera_voss.2"));
        Assert.Null(VoiceRules.VoicePath(root, "../secrets"));
        Assert.Null(VoiceRules.VoicePath(root, "/etc/passwd"));
    }

    [Theory]
    [InlineData(false, false, true)]   // no voice: the words are the line
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void Words_ShowWithoutAVoice_AndWithOneWhileSubtitlesAreOn(bool voiced, bool subtitles, bool show) =>
        Assert.Equal(show, VoiceRules.ShowWords(voiced, subtitles));

    [Fact]
    public void TheMusic_DucksUnderAVoice_AndComesBack()
    {
        float db = 0;
        for (int i = 0; i < 60; i++)
            db = VoiceRules.Duck(db, speaking: true, 1 / 60.0);
        Assert.Equal(VoiceRules.DuckDb, db);
        db = VoiceRules.Duck(db, speaking: false, 0.25);
        Assert.InRange(db, VoiceRules.DuckDb + 0.1f, -0.1f);
        for (int i = 0; i < 120; i++)
            db = VoiceRules.Duck(db, speaking: false, 1 / 60.0);
        Assert.Equal(0f, db);
    }

    [Fact]
    public void ABark_FadesIn_FadesWithDistance_AndFadesOut()
    {
        Assert.Equal(0f, VoiceRules.BarkAlpha(2, 0, 3));
        Assert.Equal(1f, VoiceRules.BarkAlpha(2, 1, 3));
        Assert.Equal(1f, VoiceRules.BarkAlpha(VoiceRules.BarkFullM, 1, 3));
        float halfway = VoiceRules.BarkAlpha((VoiceRules.BarkFullM + VoiceRules.BarkGoneM) / 2, 1, 3);
        Assert.InRange(halfway, 0.49f, 0.51f);
        Assert.Equal(0f, VoiceRules.BarkAlpha(VoiceRules.BarkGoneM, 1, 3));
        Assert.True(VoiceRules.BarkAlpha(2, 1, 0.1) < 0.5f);
        Assert.Equal(0f, VoiceRules.BarkAlpha(2, 1, 0));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not json", true)]
    [InlineData("{}", true)]
    [InlineData("{\"subtitles\": false}", false)]
    [InlineData("{\"interface\": {\"subtitles\": false}}", false)]
    [InlineData("{\"interface\": {\"subtitles\": true}}", true)]
    [InlineData("{\"subtitles\": \"no\"}", true)]
    [InlineData("[1, 2]", true)]
    public void Subtitles_AreOnUnlessThePlayersPreferencesSayOtherwise(string? prefs, bool on) =>
        Assert.Equal(on, VoiceRules.SubtitlesFrom(prefs));
}
