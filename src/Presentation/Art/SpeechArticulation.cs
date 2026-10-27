namespace UNNAMED.Presentation.Art;

/// <summary>Common lip/jaw amplitude: the native jaw and its FACS always receive the same weight.</summary>
public static class SpeechArticulation
{
    public static float Jaw(string viseme, float envelope)
    {
        float open = Math.Clamp(float.IsFinite(envelope) ? envelope : 0, 0, .65f) * .55f;
        // Bilabials need a closed mouth even on a loud recorded consonant.
        return viseme == "viseme_PP" ? Math.Min(open, .025f) : open;
    }
}
