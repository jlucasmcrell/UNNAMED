// UNNAMED Presentation - the "while you were away" log in words (WP11; SYSTEMS_COMPLETENESS_PLAN §2.12 item 7)
// No Godot types: the words are tested on their own (Presentation.Tests links this file)

using System.Collections.Immutable;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>One group of the away sheet: its key, its heading, and its lines in the order the world wrote them.</summary>
public sealed record AwayGroup(string Key, string Heading, ImmutableArray<string> Lines);

/// <summary>
/// What the away sheet says: how long the character was gone (in days and tallies), the date now, and the grouped lines. The same words,
/// head first, go to the journal's Recent list.
/// </summary>
public sealed record AwaySummary(string Head, string Now, ImmutableArray<AwayGroup> Groups, bool WorthShowing)
{
    /// <summary>The lines for the journal's Recent list: the head, then every group's lines in the sheet's order.</summary>
    public IEnumerable<string> JournalLines => Groups.SelectMany(g => g.Lines).Prepend(Head);
}

/// <summary>
/// The away log (<see cref="AdvanceLog"/>) in words, grouped as the sheet shows it: the weather, the prices, the character's hirelings and
/// their wages, respawns, and anything else of note. Only what a system logged reaches here, and a system logs only what the character
/// could know; a kind no group claims is still told, under "Notable".
/// </summary>
public static class AwayWords
{
    public const string Weather = "weather", Prices = "prices", Party = "party", Respawns = "respawns", Notable = "notable";

    /// <summary>The groups, in the sheet's order, with their headings.</summary>
    public static readonly ImmutableArray<(string Key, string Heading)> Order = ImmutableArray.Create(
        (Weather, "Weather"), (Prices, "Prices"), (Party, "Your hirelings and wages"), (Respawns, "Respawns"), (Notable, "Notable"));

    /// <summary>
    /// The sheet for one absence. <paramref name="capped"/> says the absence was longer than the world honours (DN1); <paramref name="name"/>
    /// turns a definition ID into its display name (presentation's <c>Names.Of</c>).
    /// </summary>
    public static AwaySummary Of(long fromTick, long toTick, IReadOnlyList<AdvanceLine> lines, bool capped, WorldClock clock, CalendarRules rules,
        Func<string, string> name)
    {
        long ticks = Math.Max(0, toTick - fromTick);
        var grouped = Order.ToDictionary(o => o.Key, _ => new List<string>(), StringComparer.Ordinal);
        // A hireling's work is logged a day at a time: the sheet tells the whole absence's, per worker and good.
        var delivered = new Dictionary<(string Npc, string Item), long>();
        foreach (var line in lines)
        {
            if (line.Kind == PartyAdvanceKinds.Delivered && Split(line.Ref) is (var npc, var item))
            {
                if (!delivered.ContainsKey((npc, item)))
                    grouped[Party].Add(DeliveredMarker(npc, item));
                delivered[(npc, item)] = delivered.GetValueOrDefault((npc, item)) + line.Amount;
                continue;
            }
            if (Line(line, name) is { } said)
                grouped[GroupOf(line.Kind)].Add(said);
        }
        foreach (var ((npc, item), count) in delivered)
        {
            int at = grouped[Party].IndexOf(DeliveredMarker(npc, item));
            grouped[Party][at] = $"{name(npc)} brought in {Lower(name(item))} ×{count}.";
        }
        var groups = Order.Where(o => grouped[o.Key].Count > 0)
            .Select(o => new AwayGroup(o.Key, o.Heading, grouped[o.Key].ToImmutableArray())).ToImmutableArray();

        string head = $"{Span(ticks, clock)} passed while you were away" + (Tallies(ticks, clock, rules) is { } tallies ? $": {tallies}." : ".");
        if (capped)
            head += " You were gone longer than that; the world went on no further.";
        string date = CalendarWords.Date(Calendar.Of(toTick, clock, rules), rules);
        string now = $"It is the {(date.StartsWith("The ", StringComparison.Ordinal) ? date[4..] : date)}.";
        bool worth = ticks * clock.HoursPerDay >= clock.TicksPerDay || groups.Any(g => g.Key is not Weather);
        return new AwaySummary(head, now, groups, worth);
    }

    /// <summary>The group a kind of line belongs to.</summary>
    public static string GroupOf(string kind) => kind switch
    {
        AdvanceKinds.WeatherNow => Weather,
        MarketAdvanceKinds.PriceMoved or MarketAdvanceKinds.Short => Prices,
        _ when kind.StartsWith("hireling.", StringComparison.Ordinal) || kind.StartsWith("companion.", StringComparison.Ordinal) => Party,
        _ when kind.StartsWith("respawn.", StringComparison.Ordinal) || kind.StartsWith("creature.", StringComparison.Ordinal) => Respawns,
        _ => Notable,
    };

    /// <summary>One line in words; null for a line the head already tells (the time that passed).</summary>
    public static string? Line(AdvanceLine line, Func<string, string> name)
    {
        switch (line.Kind)
        {
            case AdvanceKinds.TimePassed or AdvanceKinds.DaysPassed:
                return null;
            case AdvanceKinds.WeatherNow:
                return $"The weather now: {CalendarWords.Weather(line.Ref).ToLowerInvariant()}.";
            case AdvanceKinds.EffectExpired:
                return $"{name(line.Ref)} wore off.";
            case MarketAdvanceKinds.PriceMoved when Split(line.Ref, '/') is (var market, var item):
                return $"{name(item)} {(line.Amount > 0 ? "costs more" : "costs less")} at {Place(name(market))}: " +
                       $"{(line.Amount > 0 ? "up" : "down")} {Percent(Math.Abs(line.Amount))} on its usual price.";
            case MarketAdvanceKinds.Short when Split(line.Ref, '/') is (var market, var item):
                return $"{Capitalised(Place(name(market)))} is short of {Lower(name(item))}.";
            case PartyAdvanceKinds.WagesPaid:
                return $"You paid {name(line.Ref)} {line.Amount} coin in wages.";
            case PartyAdvanceKinds.Left when Split(line.Ref) is (var npc, var reason):
                return reason switch
                {
                    "ended" => $"{name(npc)}'s contract ran its course, and they have gone.",
                    "lapsed" => $"{name(npc)} left: the wages went unpaid.",
                    "dismissed" => $"You let {name(npc)} go.",
                    _ => $"{name(npc)} left the party.",
                };
            case PartyAdvanceKinds.Died:
                return $"{name(line.Ref)} died.";
            case PartyAdvanceKinds.Wounded:
                return $"{name(line.Ref)} was gravely wounded, and has gone home to recover.";
            case PartyAdvanceKinds.Recovered:
                return $"{name(line.Ref)} has recovered.";
            default:
                // A kind with no words of its own yet is told plainly rather than dropped: what it is, and what it concerns.
                string what = Capitalised(line.Kind.Replace('.', ' ').Replace('_', ' '));
                return line.Ref.Length == 0 ? $"{what}." : $"{what}: {name(line.Ref)}.";
        }
    }

    /// <summary>The time that passed in days, or hours under a day: "16 days", "1 day", "5 hours", "Less than an hour".</summary>
    public static string Span(long ticks, WorldClock clock)
    {
        long days = ticks / clock.TicksPerDay;
        if (days > 0)
            return days == 1 ? "1 day" : $"{days} days";
        long hours = ticks * clock.HoursPerDay / clock.TicksPerDay;
        return hours == 0 ? "Less than an hour" : hours == 1 ? "1 hour" : $"{hours} hours";
    }

    /// <summary>The days as tallies, once there is at least one: "a tally and 2 days", "3 tallies", "a whole Reckoning"; null under a tally.</summary>
    public static string? Tallies(long ticks, WorldClock clock, CalendarRules rules)
    {
        long days = ticks / clock.TicksPerDay;
        if (days >= rules.DaysPerReckoning)
            return days == rules.DaysPerReckoning ? "a whole Reckoning" : "more than a Reckoning";
        long tallies = days / rules.DaysPerTally, rest = days % rules.DaysPerTally;
        if (tallies == 0)
            return null;
        string counted = tallies == 1 ? "a tally" : $"{tallies} tallies";
        return rest == 0 ? counted : $"{counted} and {(rest == 1 ? "1 day" : $"{rest} days")}";
    }

    private static string Percent(long permille) => permille % 10 == 0 ? $"{permille / 10}%" : $"{permille / 10.0:0.#}%";

    private static string DeliveredMarker(string npc, string item) => $"\u0001{npc}|{item}";

    private static (string, string)? Split(string value, char at = '|')
    {
        int i = value.IndexOf(at);
        return i <= 0 || i == value.Length - 1 ? null : (value[..i], value[(i + 1)..]);
    }

    /// <summary>A place after "at": "the market" unless its name is already a proper one ("Ashen Hollow").</summary>
    private static string Place(string name) => name.Length > 1 && char.IsUpper(name[0]) && name.Contains(' ') && char.IsUpper(name[name.LastIndexOf(' ') + 1])
        ? name
        : "the " + Lower(name);

    /// <summary>A name inside a sentence: a word in sentence case is lowered ("Iron ingot" is "iron ingot"); "NPC" or "Ashen Hollow" is kept.</summary>
    private static string Lower(string name) =>
        name.Length > 1 && char.IsUpper(name[0]) && char.IsLower(name[1]) && !name.Skip(1).Any(char.IsUpper) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
