namespace UNNAMED.Presentation.Ui.Models;

/// <summary>Restrained default direction for unannotated dialogue; explicit direction always wins.</summary>
public static class DialoguePerformance
{
    public static readonly string[] Expressions = ["neutral", "warm", "curious", "worried", "stern", "sad", "surprised"];

    public static string Mood(string? text, string? direction = null)
    {
        if (direction is not null && Expressions.Contains(direction, StringComparer.Ordinal)) return direction;
        if (string.IsNullOrWhiteSpace(text)) return "neutral";
        var words = text.ToLowerInvariant().Split([' ', '.', ',', '!', '?', ';', ':', '-', '\u2014'], StringSplitOptions.RemoveEmptyEntries);
        bool Has(params string[] choices) => choices.Any(words.Contains);
        if (Has("sorry", "died", "dead", "grief", "lost")) return "sad";
        if (Has("danger", "afraid", "worry", "worried", "careful")) return "worried";
        if (Has("stop", "enough", "thief", "leave")) return "stern";
        if (text.TrimEnd().EndsWith('?')) return "curious";
        if (Has("welcome", "thanks", "thank", "glad", "good")) return "warm";
        return "neutral";
    }
}
