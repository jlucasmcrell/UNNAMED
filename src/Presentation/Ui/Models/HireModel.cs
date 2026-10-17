// UNNAMED Presentation - the hiring sheet: what hiring a resident or an adventurer would mean, stated before the character agrees, and the
// trip they may be asking company for (WP12b's hireability, WP13's adventurers, WP15a's terms, WP16's offers; DN9, DN11)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>One role someone hires on as: its word and what it does for the character, from the role's terms.</summary>
public sealed record HireRole(HirelingRole Role, string Word, string Does);

/// <summary>
/// The terms of one hire, as the sheet states them before the character agrees (DN9, DN11): the daily wage, what the whole contract
/// costs, the loot rule, that they can die, and why it would be refused now - or null when it would not be.
/// </summary>
public sealed record HireOffer(HirelingRole Role, int Days, string Wage, string Total, string Loot, string Risk, string? Refusal)
{
    /// <summary>The Hire action is offered only with the risk accepted and nothing refusing it.</summary>
    public bool CanHire(bool riskAccepted) => riskAccepted && Refusal is null;
}

/// <summary>
/// The hiring sheet for someone: who they are, whether they can be hired now and why not, the roles they hire on as, how long a contract
/// may run, and the trip they are asking company for, if they are.
/// </summary>
public sealed record HireSheet(string Key, string Name, string Who, string? Refusal, ImmutableArray<HireRole> Roles, int MinDays, int MaxDays, GroupPost? Trip)
{
    public bool Hireable => Refusal is null && !Roles.IsEmpty;
}

public static class HireModel
{
    /// <summary>DN9: the hire says, before it is agreed, that a hireling can die - and what of theirs then stays where they fell.</summary>
    public const string RiskWords = "A hireling can die in your service. If they fall and no one helps them up in time, they are gone; what you loaned them comes back to you.";

    /// <summary>The sheet's default contract: a week, held to what the rules allow.</summary>
    public const int DefaultDays = 7;

    /// <summary>
    /// The sheet for someone the world holds (their hireability answer), with their rules and, for an adventurer, what they are; a resident's
    /// town roles in words otherwise. <paramref name="trip"/> is the group board's post for their open offer, if they have one;
    /// <paramref name="names"/> names a definition (an adventurer's gear).
    /// </summary>
    public static HireSheet Sheet(string name, NpcHireView hire, PartyRules? rules, AdventurerView? adventurer, GroupPost? trip, Func<string, string> names)
    {
        var roles = hire.HiresAs.Distinct().Select(r => new HireRole(r, PartyModel.RoleWord(r), Does(r, rules?.Roles.GetValueOrDefault(r)))).ToImmutableArray();
        string? refusal = hire.Hireable ? null
            : Sentence(name, hire.Refusal ?? "does not hire on");
        if (refusal is null && roles.IsEmpty)
            refusal = $"{name} does not hire on.";
        return new HireSheet(hire.Key, name, Who(hire, adventurer, names), refusal, roles, rules?.ContractDaysMin ?? 1, rules?.ContractDaysMax ?? 1, trip);
    }

    /// <summary>Who they are, in a line: an adventurer's level and that they keep their gear and skill when hired; a resident's town roles.</summary>
    public static string Who(NpcHireView hire, AdventurerView? adventurer, Func<string, string> names)
    {
        if (adventurer is not null)
        {
            string gear = adventurer.Gear.IsEmpty ? "" : $" · carries {string.Join(", ", adventurer.Gear.Select(names))}";
            return $"Adventurer, level {adventurer.Level} · {Caution(adventurer.RiskPercent)}{gear}. Keeps their own gear and level when hired.";
        }
        var roles = hire.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => Names.Words(r)).ToList();
        return roles.Count == 0 ? "Lives here, and hires on for a wage." : $"{Capital(string.Join(", ", roles))} · hires on for a wage.";
    }

    /// <summary>An adventurer's daring in words, never a number: careful, steady, bold or reckless.</summary>
    public static string Caution(int riskPercent) => riskPercent switch
    {
        < 25 => "careful",
        < 50 => "steady",
        < 75 => "bold",
        _ => "reckless",
    };

    /// <summary>What a role does for the character, from its terms: the guard fights, the porter carries, the gatherer and crafter work.</summary>
    public static string Does(HirelingRole role, RoleTerms? terms) => role switch
    {
        HirelingRole.Guard => "Fights beside you.",
        HirelingRole.Porter => terms is { CarryBonusGrams: > 0 } t ? $"Carries {LoadWords.Kg(t.CarryBonusGrams)} more for you while following." : "Carries for you while following.",
        HirelingRole.Gatherer => terms is { YieldsPerDay: > 0 } g ? $"Works a node you choose, {Times(g.YieldsPerDay)} a day, and brings you what it gives." : "Works a node you choose.",
        HirelingRole.Crafter => terms is { CraftsPerDay: > 0 } c ? $"Makes a recipe at a station you choose, {Times(c.CraftsPerDay)} a day, from what you hand them." : "Makes a recipe at a station you choose.",
        _ => "",
    };

    /// <summary>The terms as the sheet states them; the simulation's own refusal (the coin for the first day's wage among them) as a sentence.</summary>
    public static HireOffer Offer(HireTerms terms) => new(
        terms.Role,
        terms.Days,
        $"{terms.DailyWage} coin a day",
        terms.Days == 1 ? $"1 day: {terms.Total} coin, paid now" : $"{terms.Days} days: {terms.Total} coin in all - the first day's now, then each midnight",
        Loot(terms.LootRule),
        terms.CanDie ? RiskWords : "",
        terms.Refusal is null ? null : Capital(terms.Refusal) + ".");

    /// <summary>The loot rule in words (§2.15: wage-only by default).</summary>
    public static string Loot(string rule) => rule == PartyRules.WageOnly
        ? "They take only their wage: what the party finds is yours."
        : Capital(Names.Words(rule)) + ".";

    /// <summary>A contract's days held to the rules' shortest and longest.</summary>
    public static int Days(int days, int min, int max) => Math.Clamp(days, min, Math.Max(min, max));

    /// <summary>A refusal as a sentence after their name: "Bram Hallow is out." A refusal that already names them is kept as it is.</summary>
    public static string Sentence(string name, string refusal)
    {
        string said = refusal.StartsWith(name, StringComparison.Ordinal) ? refusal : $"{name} {refusal}";
        return said.EndsWith('.') ? said : said + ".";
    }

    private static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
