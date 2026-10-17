// UNNAMED Presentation - the view models' word tones in the Field Instrument's colours, and the chips and toggles the new screens share
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

public static partial class UiKit
{
    /// <summary>A word's tone as the kit colours it: quiet iron, a brass warning, a wound refusal, lichen for what is well.</summary>
    public static Color Tone(WordTone tone) => tone switch
    {
        WordTone.Warning => HudStyle.Brass,
        WordTone.Refusal => HudStyle.Wound,
        WordTone.Good => HudStyle.Lichen,
        _ => HudStyle.Iron,
    };

    /// <summary>A chip in a word's tone; the quiet tone keeps bone words in its iron frame, so it reads on soot.</summary>
    public static PanelContainer Chip((string Text, WordTone Tone) chip) =>
        chip.Tone == WordTone.Quiet ? Chip(chip.Text, HudStyle.Iron, HudStyle.Bone) : Chip(chip.Text, Tone(chip.Tone));

    /// <summary>
    /// A row of segments (an order, a stance, a tab): one button per choice, the chosen one shown selected, each pressing its own index.
    /// Choosing the one already chosen does nothing.
    /// </summary>
    public static HBoxContainer Segments(string idPrefix, IReadOnlyList<string> words, int chosen, Action<int> choose, bool compact = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        for (int i = 0; i < words.Count; i++)
        {
            int index = i;
            var button = Action(words[i], () =>
            {
                if (index != chosen)
                    choose(index);
            }, compact: compact, id: $"{idPrefix}/{i}");
            Select(button, i == chosen);
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>A toggle: a button that reads as selected while on, its words saying what it does.</summary>
    public static Button Toggle(string words, bool on, Action pressed, string id)
    {
        var button = Action((on ? "On: " : "Off: ") + words, pressed, id: id);
        Select(button, on);
        return button;
    }
}
