// UNNAMED Domain - availability: can a known technique, formula or recipe be used now, and if not, why? One evaluator for every known row
// (UI_PROGRESSION_AUDIT items B, C and D; canon 26 §4.3 availability words, §5 the reason grammar, and 07 §5.7 the risk read)
// No Godot references - pure C#. The words are templates with named slots (canon 26 §5.4 rule 6), so a line is never built from fragments.

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Magic;

namespace UNNAMED.Domain.Progression;

/// <summary>The availability words of canon 26 §4.3 that a known technique, formula or recipe can carry now.</summary>
public enum AvailabilityKind
{
    /// <summary>Usable now.</summary>
    Ready,

    /// <summary>The item or feature is not in hand.</summary>
    NeedsGear,

    /// <summary>A contextual need is absent: a station within reach.</summary>
    NeedsSituation,

    /// <summary>A pool or supply is too low now.</summary>
    ShortOf,

    /// <summary>Used a moment ago: ready again soon. A short wait, not a want.</summary>
    Recovering,
}

/// <summary>One line of a requirement checklist: what is needed, what the character has, and whether that is enough.</summary>
public sealed record RequirementLine(string Need, string Have, bool Met);

/// <summary>
/// One row's availability: the word, the first reason it is not Ready (canon 26 §5.2 order), the full checklist, and a caution that does
/// not block (working past Strain tolerance costs health but is never refused: "a clear risk rather than a lock").
/// </summary>
public sealed record Availability(AvailabilityKind Kind, string Word, string? Reason, ImmutableArray<RequirementLine> Checklist, string? Caution = null)
{
    public bool Ready => Kind == AvailabilityKind.Ready;
}

/// <summary>
/// What the evaluator needs to know of the character and the moment. Plain values and lookups, never a world: a caller fills it from the
/// player's view, a test fills it by hand. <see cref="Name"/> turns a definition ID into display words (canon 26 §5.4 rule 1).
/// </summary>
public sealed record AvailabilityContext(
    Func<string, int> SkillLevel,
    int Stamina,
    int Focus,
    int Strain,
    int StrainTolerance,
    int Health,
    string? HeldSkillId,
    string? HeldName,
    Func<string, int> Carried,
    string? StationKind,
    Func<string, int> CooldownSeconds,
    Func<string, string> Name);

/// <summary>A formula's risk read, as precise as the domain skill allows (07 §5.7): nothing beyond the band is shown (UI-9).</summary>
public sealed record RiskRead(string Word, ImmutableArray<string> Lines);

/// <summary>The words of every reason, one template each with its slots named.</summary>
public static class ReasonText
{
    public const string Ready = "Ready";
    public const string NeedsSituationWord = "Needs situation";
    public const string RecoveringWord = "Recovering";

    /// <summary>Canon 26 §5.2 kind 9, gear: "Needs {feature} — {what you hold lacks it}".</summary>
    public static string Gear(string feature, string held) => $"Needs {feature} — {held}";

    /// <summary>Kind 11, resource now: "Short of {pool}: {have} of {need}".</summary>
    public static string Short(string pool, long have, long need) => $"Short of {pool}: {have} of {need}";

    /// <summary>Kind 12, situation: "Needs {situation}".</summary>
    public static string Situation(string situation) => $"Needs {situation}";

    public static string Recovering(int seconds) => $"Recovering: ready again in {seconds} s";

    public static string ShortWord(string pool) => $"Short of {pool}";

    /// <summary>A noun phrase with its article, lower-cased: "an anvil", "a one-hand blade".</summary>
    public static string A(string noun, bool capital = false)
    {
        string word = noun.ToLowerInvariant();
        string article = word.Length > 0 && "aeiou".Contains(word[0]) && !word.StartsWith("one", StringComparison.Ordinal) ? "an" : "a";
        string phrase = $"{article} {word}";
        return capital ? char.ToUpperInvariant(phrase[0]) + phrase[1..] : phrase;
    }
}

public static class AvailabilityRules
{
    /// <summary>The domain skill from which a read also names what goes wrong (07 §5.7).</summary>
    public const int ReadsWhatSlips = 15;

    /// <summary>The domain skill from which a read also gives the slip chance.</summary>
    public const int ReadsSlipChance = 30;

    /// <summary>The domain skill from which a read also gives the Strain's severity.</summary>
    public const int ReadsSeverity = 45;

    /// <summary>The margin of skill over complexity that makes a working steady (07 §5.7: margin 5 or more).</summary>
    public const int SteadyMargin = 5;

    // ---- techniques ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A known technique now: a weapon of its family in hand (gear), its Stamina (a resource), and its cooldown. The first unmet one is the
    /// reason, in canon's order: what cannot be had by waiting, then what can be fixed now, then the short wait.
    /// </summary>
    public static Availability Technique(TechniqueDefinition technique, AvailabilityContext c)
    {
        string family = c.Name(technique.SkillId);
        bool armed = c.HeldSkillId == technique.SkillId;
        int wait = c.CooldownSeconds(technique.Id);
        var lines = ImmutableArray.Create(
            new RequirementLine($"{ReasonText.A(family, capital: true)} weapon in hand", armed ? c.HeldName ?? "in hand" : c.HeldName is null ? "nothing in hand" : $"you hold {c.HeldName}", armed),
            new RequirementLine($"{technique.StaminaCost} Stamina", $"{c.Stamina}", c.Stamina >= technique.StaminaCost),
            new RequirementLine("Recovered from its last use", wait > 0 ? $"{wait} s to go" : "yes", wait <= 0));
        if (!armed)
            return new(AvailabilityKind.NeedsGear, "Needs gear",
                ReasonText.Gear($"{ReasonText.A(family)} weapon in hand", c.HeldName is null ? "your hands are empty" : $"you hold {c.HeldName}"), lines);
        if (c.Stamina < technique.StaminaCost)
            return new(AvailabilityKind.ShortOf, ReasonText.ShortWord("Stamina"), ReasonText.Short("Stamina", c.Stamina, technique.StaminaCost), lines);
        if (wait > 0)
            return new(AvailabilityKind.Recovering, ReasonText.RecoveringWord, ReasonText.Recovering(wait), lines);
        return new(AvailabilityKind.Ready, ReasonText.Ready, null, lines);
    }

    /// <summary>What a technique does and costs, in a line a teacher's list and a detail card share.</summary>
    public static string TechniqueSummary(TechniqueDefinition technique, Func<string, string> name)
    {
        var parts = new List<string>
        {
            $"{name(technique.SkillId)} · {technique.StaminaCost} Stamina",
            technique.DamageMultiplier >= 1.05 ? "a harder blow" : "a blow",
        };
        if (technique.WindupMultiplier > 1.05)
            parts.Add("a longer wind-up");
        if (technique.ReachBonusMm > 0)
            parts.Add("reaches further");
        if (technique.OnHitEffect is { } effect)
            parts.Add($"may leave {name(effect)}");
        parts.Add($"{technique.CooldownTicks / 20} s between uses");
        return string.Join(" · ", parts);
    }

    // ---- formulas ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A known formula now. It is Ready when the character has the Focus; a skill short of its complexity only makes it riskier (the read
    /// says so), and working past Strain tolerance is a caution, never a refusal.
    /// </summary>
    public static Availability Formula(FormulaDefinition formula, MagicConstants constants, AvailabilityContext c)
    {
        int skill = c.SkillLevel(formula.DomainSkillId);
        int cost = MagicRules.StrainCost(formula, skill, constants);
        var (_, backlash) = MagicRules.Settle(c.Strain, cost, c.StrainTolerance, constants);
        string domain = c.Name(formula.DomainSkillId);
        var lines = ImmutableArray.Create(
            new RequirementLine($"{formula.FocusCost} Focus", $"{c.Focus}", c.Focus >= formula.FocusCost),
            new RequirementLine($"{domain} {formula.Complexity} to work it without a fizzle", $"{skill}", skill >= formula.Complexity));
        string? caution = backlash > 0 ? $"Past your Strain tolerance now: working it costs {backlash} health in backlash." : null;
        return c.Focus < formula.FocusCost
            ? new(AvailabilityKind.ShortOf, ReasonText.ShortWord("Focus"), ReasonText.Short("Focus", c.Focus, formula.FocusCost), lines, caution)
            : new(AvailabilityKind.Ready, ReasonText.Ready, null, lines, caution);
    }

    /// <summary>
    /// The read of a formula's risk (07 §5.7), by the domain skill. The word is steady (margin 5 or more), uncertain (0 to 4) or beyond you;
    /// from 15 it says what slips; from 30 the slip chance; from 45 the Strain's severity. The health a backlash would cost is told in every
    /// band, with "this could kill you" when it exceeds current health.
    /// </summary>
    public static RiskRead Risk(FormulaDefinition formula, MagicConstants constants, AvailabilityContext c)
    {
        int skill = c.SkillLevel(formula.DomainSkillId);
        int margin = skill - formula.Complexity;
        string word = margin >= SteadyMargin ? "steady" : margin >= 0 ? "uncertain" : "beyond you";
        var lines = ImmutableArray.CreateBuilder<string>();
        int cost = MagicRules.StrainCost(formula, skill, constants);
        var (_, backlash) = MagicRules.Settle(c.Strain, cost, c.StrainTolerance, constants);
        int slip = MagicRules.FizzlePercent(formula, skill, constants);
        if (skill >= ReadsWhatSlips && (margin < SteadyMargin || slip > 0))
            lines.Add("If it slips, it fizzles: the Focus is spent and nothing happens.");
        if (skill >= ReadsSlipChance)
            lines.Add(slip == 0 ? "It will not slip at your skill." : $"Slip chance {slip}%.");
        if (skill >= ReadsSeverity)
            lines.Add(c.StrainTolerance <= 0 ? "Strain: you have no tolerance for it."
                : cost * 100 >= c.StrainTolerance * 60 ? $"Strain {cost} of {c.StrainTolerance}: heavy."
                : cost * 100 >= c.StrainTolerance * 25 ? $"Strain {cost} of {c.StrainTolerance}: moderate."
                : $"Strain {cost} of {c.StrainTolerance}: light.");
        if (backlash > 0)
            lines.Add(backlash >= c.Health
                ? $"Past your Strain tolerance this would cost {backlash} health: this could kill you."
                : $"Past your Strain tolerance this would cost {backlash} health.");
        return new RiskRead(word, lines.ToImmutable());
    }

    // ---- recipes -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A known recipe now: its inputs carried (a supply: Short of …), then a station of its kind within reach (a situation). A skill short
    /// of its complexity never blocks the work: it makes crude work likelier, which the row says in its own words.
    /// </summary>
    public static Availability Recipe(RecipeDefinition recipe, AvailabilityContext c)
    {
        var lines = ImmutableArray.CreateBuilder<RequirementLine>();
        foreach (var each in recipe.Inputs)
            lines.Add(new RequirementLine($"{each.Count} × {c.Name(each.ItemId)}", $"{c.Carried(each.ItemId)}", c.Carried(each.ItemId) >= each.Count));
        string station = c.Name(recipe.StationKind);
        bool at = c.StationKind == recipe.StationKind;
        lines.Add(new RequirementLine($"{ReasonText.A(station)} within reach", at ? "here" : "none within reach", at));
        if (recipe.Inputs.FirstOrDefault(i => c.Carried(i.ItemId) < i.Count) is { } input)
        {
            return new(AvailabilityKind.ShortOf, ReasonText.ShortWord(c.Name(input.ItemId)), ReasonText.Short(c.Name(input.ItemId), c.Carried(input.ItemId), input.Count), lines.ToImmutable());
        }
        return at
            ? new(AvailabilityKind.Ready, ReasonText.Ready, null, lines.ToImmutable())
            : new(AvailabilityKind.NeedsSituation, ReasonText.NeedsSituationWord, ReasonText.Situation($"{ReasonText.A(station)} within reach"), lines.ToImmutable());
    }
}
