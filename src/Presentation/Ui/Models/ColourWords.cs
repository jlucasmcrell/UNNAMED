// UNNAMED Presentation - words that say what a colour would otherwise say alone (canon 26 §9.5: colour never carries meaning by itself)
// Pure: no Godot, so the words are tested directly

namespace UNNAMED.Presentation.Ui.Models;

public static class ColourWords
{
    /// <summary>A requirement's value with whether it is met, so green and red are never the only difference: "10 - met", "10 - you have 7".</summary>
    public static string Needs(int minimum, int have) => have >= minimum ? $"{minimum} - met" : $"{minimum} - you have {have}";
}
