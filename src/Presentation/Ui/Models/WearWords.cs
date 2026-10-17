// UNNAMED Presentation - durability and the laden band in words, for the item rows, the inventory and the character sheet (WP3b; D4, D5)
// Godot-free: Presentation.Tests links this file and tests the words on their own (D-11)

using UNNAMED.Domain.Items;

namespace UNNAMED.Presentation.Ui;

/// <summary>How worn a piece is, in four steps: no wear worth a word, worn (half or less left), badly worn (a quarter or less), worn out.</summary>
public enum WearBand { Sound, Worn, BadlyWorn, WornOut }

/// <summary>How heavy the pack is against the carry limit (D4): under the laden band, in it, or at the limit itself.</summary>
public enum LoadBand { Light, Laden, Full }

/// <summary>A word and the tone family it is shown in: quiet (iron), a warning (brass), or a refusal (wound).</summary>
public enum WordTone { Quiet, Warning, Refusal, Good }

/// <summary>Durability in words (D5): a band, a chip for a row, and a line for a card. An item with no durability has none of them.</summary>
public static class WearWords
{
    /// <summary>The band a stack's wear puts it in; null for an item with no durability.</summary>
    public static WearBand? Band(ItemWear? wear, int durabilityMax)
    {
        if (durabilityMax <= 0)
            return null;
        int left = Durability.Remaining(wear, durabilityMax);
        return left == 0 ? WearBand.WornOut
            : left * 4 <= durabilityMax ? WearBand.BadlyWorn
            : left * 2 <= durabilityMax ? WearBand.Worn
            : WearBand.Sound;
    }

    /// <summary>The chip on an item's row: nothing while sound (a row stays quiet), otherwise the band in words and its tone.</summary>
    public static (string Text, WordTone Tone)? Chip(ItemWear? wear, int durabilityMax) => Band(wear, durabilityMax) switch
    {
        WearBand.Worn => ("Worn", WordTone.Quiet),
        WearBand.BadlyWorn => ("Badly worn", WordTone.Warning),
        WearBand.WornOut => ("Worn out", WordTone.Refusal),
        _ => null,
    };

    /// <summary>"Durability 40 of 120" for a card, or null for an item with none.</summary>
    public static string? Line(ItemWear? wear, int durabilityMax) =>
        durabilityMax <= 0 ? null : $"Durability {Durability.Remaining(wear, durabilityMax)} of {durabilityMax}";
}

/// <summary>The laden band in words (D4): a band, a chip for the inventory's totals and the character sheet, and what it costs.</summary>
public static class LoadWords
{
    public static LoadBand Band(long carriedGrams, long limitGrams, LadenRules rules) =>
        limitGrams > 0 && carriedGrams >= limitGrams ? LoadBand.Full
        : rules.IsLaden(carriedGrams, limitGrams) ? LoadBand.Laden
        : LoadBand.Light;

    /// <summary>The chip beside the load: nothing while light, otherwise the band and its tone.</summary>
    public static (string Text, WordTone Tone)? Chip(long carriedGrams, long limitGrams, LadenRules rules) => Band(carriedGrams, limitGrams, rules) switch
    {
        LoadBand.Laden => ("Laden", WordTone.Warning),
        LoadBand.Full => ("At the limit", WordTone.Refusal),
        _ => null,
    };

    /// <summary>
    /// What the band means now, in one sentence: how much more goes in before the band, or what the band costs (the extra stamina a
    /// sprint and a dodge take, from the rules), or that nothing more can be taken up.
    /// </summary>
    public static string Sentence(long carriedGrams, long limitGrams, LadenRules rules) => Band(carriedGrams, limitGrams, rules) switch
    {
        LoadBand.Full => "At your limit: you can take up nothing more until you put something down.",
        LoadBand.Laden => $"Laden: a sprint costs {rules.SprintDrainPercent - 100}% more stamina and a dodge {rules.DodgeCostPercent - 100}% more.",
        _ => limitGrams <= 0 ? "You can carry nothing."
            : $"{Kg(Math.Max(0, limitGrams * rules.BandPercent / 100 - carriedGrams))} more before the pack weighs on you.",
    };

    /// <summary>Grams as kilograms in words: "1.5 kg", "12 kg".</summary>
    public static string Kg(long grams) => $"{grams / 1000.0:0.#} kg";
}
