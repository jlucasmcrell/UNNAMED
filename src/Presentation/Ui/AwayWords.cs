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
    public ImmutableArray<AwayGroup> DetailGroups { get; init; } = ImmutableArray<AwayGroup>.Empty;
    public IEnumerable<string> JournalLines => (DetailGroups.IsDefaultOrEmpty ? Groups : DetailGroups).SelectMany(g => g.Lines).Prepend(Head);
}

/// <summary>
/// The away log (<see cref="AdvanceLog"/>) in words, grouped as the sheet shows it: the weather, the prices, the character's hirelings and
/// their wages, respawns, and anything else of note. Only what a system logged reaches here, and a system logs only what the character
/// could know; a kind no group claims is still told, under "Notable".
/// </summary>
public static class AwayWords
{
    public const string Weather = "weather", Prices = "prices", Party = "party", Respawns = "respawns", Notable = "notable";

    public const int PreviewLimit=12;
    public const string Adventurers = "adventurers", Quests = "quests";
    public static readonly ImmutableArray<(string Key, string Heading)> Order = ImmutableArray.Create(
        (Adventurers, "Adventurers"), (Party, "Your companions"), (Quests, "Your quests"),
        (Prices, "Market"), (Weather, "The weather"), (Respawns, "Wildlife"), (Notable, "Other news"));

    /// <summary>
    /// The sheet for one absence. <paramref name="capped"/> says the absence was longer than the world honours (DN1); <paramref name="name"/>
    /// turns a definition ID into its display name (presentation's <c>Names.Of</c>).
    /// </summary>
    public static AwaySummary Of(long fromTick, long toTick, IReadOnlyList<AdvanceLine> lines, bool capped, WorldClock clock, CalendarRules rules,
        Func<string, string> name)
    {
        long ticks = Math.Max(0, toTick - fromTick);
        var grouped = Order.ToDictionary(o => o.Key, _ => new List<string>(), StringComparer.Ordinal);
        var details = Order.ToDictionary(o => o.Key, _ => new List<string>(), StringComparer.Ordinal);
        var delivered = new Dictionary<(string Npc, string Item), long>();
        var wages = new Dictionary<string, long>(StringComparer.Ordinal);
        var claimed = new Dictionary<(string Key, string Actor, string? Place), (long Count, string First)>();
        foreach (var line in lines)
        {
            string? said=Line(line,name);
            if(said is not null)details[GroupOf(line.Kind)].Add(said);
            if (line.Kind == PartyAdvanceKinds.Delivered && Split(line.Ref) is (var npc, var item))
            {
                if (!delivered.ContainsKey((npc, item)))grouped[Party].Add(DeliveredMarker(npc, item));
                delivered[(npc, item)] = delivered.GetValueOrDefault((npc, item)) + line.Amount;
                continue;
            }
            if(line.Kind==PartyAdvanceKinds.WagesPaid)
            {
                if(!wages.ContainsKey(line.Ref))grouped[Party].Add("\u0002"+line.Ref);
                wages[line.Ref]=wages.GetValueOrDefault(line.Ref)+line.Amount;continue;
            }
            if(line.Kind==AdventurerAdvanceKinds.Claimed && Split(line.Ref) is (_,var actorKey) && said is not null)
            {
                var key=(actorKey,Actor(line,name),line.PlaceId);
                if(!claimed.TryGetValue(key,out var count))
                {
                    grouped[Adventurers].Add("\u0003"+actorKey+"|"+line.PlaceId);count=(0,said);
                }
                claimed[key]=(count.Count+1,count.First);continue;
            }
            if(said is not null)grouped[GroupOf(line.Kind)].Add(said);
        }
        foreach (var ((npc, item), count) in delivered)
        {
            int at = grouped[Party].IndexOf(DeliveredMarker(npc, item));
            grouped[Party][at] = $"{SafeName(npc,name,"A companion")} brought in {Lower(SafeName(item,name,"supplies"))} ×{count}.";
        }
        foreach(var (npc,count) in wages)
            grouped[Party][grouped[Party].IndexOf("\u0002"+npc)]=$"You paid {SafeName(npc,name,"a companion")} {count} coin in wages.";
        foreach(var (key,count) in claimed)
            grouped[Adventurers][grouped[Adventurers].IndexOf("\u0003"+key.Key+"|"+key.Place)]=count.Count==1 ? count.First
                : $"{key.Actor} claimed {count.Count} contracts{At(key.Place,name)}.";
        ImmutableArray<AwayGroup> Groups(Dictionary<string,List<string>> source,bool collapse) => Order.Where(o=>source[o.Key].Count>0)
            .Select(o=>new AwayGroup(o.Key,o.Heading,(collapse ? source[o.Key].GroupBy(x=>x,StringComparer.Ordinal)
                .Select(g=>g.Count()>1 ? $"{g.Key.TrimEnd('.')} ({g.Count()} times)." : g.Key) : source[o.Key]).ToImmutableArray())).ToImmutableArray();
        var allGroups=Groups(grouped,true);
        var preview=ImmutableArray.CreateBuilder<AwayGroup>();int remaining=PreviewLimit;
        foreach(var group in allGroups)
        {
            if(remaining==0)break;
            var visible=group.Lines.Take(remaining).ToImmutableArray();remaining-=visible.Length;
            preview.Add(group with { Lines=visible });
        }
        var groups=preview.ToImmutable();
        string head=$"You were away {Span(ticks,clock).ToLowerInvariant()}.";
        if(capped)head+=" The world advanced no further than this.";
        string date = CalendarWords.Date(Calendar.Of(toTick, clock, rules), rules);
        string now = $"It is the {(date.StartsWith("The ", StringComparison.Ordinal) ? date[4..] : date)}.";
        bool worth = ticks * clock.HoursPerDay >= clock.TicksPerDay || groups.Any(g => g.Key is not Weather);
        return new AwaySummary(head,now,groups,worth) { DetailGroups=Groups(details,false) };
    }

    /// <summary>The group a kind of line belongs to.</summary>
    public static string GroupOf(string kind) => kind switch
    {
        AdvanceKinds.WeatherNow => Weather,
        _ when kind.StartsWith("adventurer.",StringComparison.Ordinal) => Adventurers,
        _ when kind.StartsWith("quest.",StringComparison.Ordinal) => Quests,
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
            case NpcAdvanceKinds.Arrived:
                return $"{Actor(line,name,"A new resident")} arrived{At(line.PlaceId,name)}.";
            case AdventurerAdvanceKinds.Arrived:
                string arrival=Actor(line,name);
                string newcomer=arrival=="An adventurer" ? "A new adventurer" : $"A new adventurer, {arrival}";
                return $"{newcomer}{(line.Level is { } level ? $" (level {level})" : "")}{(arrival=="An adventurer" ? "" : ",")} arrived{At(line.PlaceId,name)}.";
            case AdventurerAdvanceKinds.Claimed when Split(line.Ref) is (var quest,_):
                return $"{Actor(line,name)} took the “{SafeName(line.SubjectId??quest,name,"unnamed")}” contract{At(line.PlaceId,name)}.";
            case AdventurerAdvanceKinds.Died:
                return $"{Actor(line,name)} died{(line.SubjectId is { } creature ? $" fighting {Lower(SafeName(creature,name,"wild creatures"))}" : "")}{At(line.PlaceId,name," near ")}.";
            case AdventurerAdvanceKinds.Returned:
                string from=line.PlaceId is { } place ? $" from {SafeName(place,name,"the hunting grounds")}" : "";
                string brought=line.Wounded ? ", wounded" : line.Loot.IsDefaultOrEmpty ? "" : " with "+string.Join(", ",line.Loot.GroupBy(i=>i.ItemId,StringComparer.Ordinal)
                    .Select(g=>$"{Lower(SafeName(g.Key,name,"supplies"))} ×{g.Sum(i=>(long)i.Count)}"));
                return $"{Actor(line,name)} came back{from}{brought}.";
            case AdventurerAdvanceKinds.Retired:
                return $"{Actor(line,name)} retired from adventuring{At(line.PlaceId,name)}.";
            case PartyAdvanceKinds.Delivered when Split(line.Ref) is (var worker,var good):
                return $"{SafeName(worker,name,"A companion")} brought in {Lower(SafeName(good,name,"supplies"))} ×{line.Amount}.";
            case AdvanceKinds.TimePassed or AdvanceKinds.DaysPassed:
                return null;
            case AdvanceKinds.WeatherNow:
                return $"The weather now: {CalendarWords.Weather(line.Ref).ToLowerInvariant()}.";
            case AdvanceKinds.EffectExpired:
                return $"{SafeName(line.Ref,name,"An effect")} wore off.";
            case MarketAdvanceKinds.PriceMoved when Split(line.Ref, '/') is (var market, var item):
                return $"{SafeName(item,name,"supplies")} {(line.Amount > 0 ? "costs more" : "costs less")} at {Place(SafeName(market,name,"local market"))}: " +
                       $"{(line.Amount > 0 ? "up" : "down")} {Percent(Math.Abs(line.Amount))} on its usual price.";
            case MarketAdvanceKinds.Short when Split(line.Ref, '/') is (var market, var item):
                return $"{Capitalised(Place(SafeName(market,name,"local market")))} is short of {Lower(SafeName(item,name,"supplies"))}.";
            case PartyAdvanceKinds.WagesPaid:
                return $"You paid {SafeName(line.Ref,name,"a companion")} {line.Amount} coin in wages.";
            case PartyAdvanceKinds.Left when Split(line.Ref) is (var npc, var reason):
                return reason switch
                {
                    "ended" => $"{SafeName(npc,name,"A companion")}'s contract ran its course, and they have gone.",
                    "lapsed" => $"{SafeName(npc,name,"A companion")} left: the wages went unpaid.",
                    "dismissed" => $"You let {SafeName(npc,name,"A companion")} go.",
                    _ => $"{SafeName(npc,name,"A companion")} left the party.",
                };
            case PartyAdvanceKinds.Died:
                return $"{SafeName(line.Ref,name,"A companion")} died.";
            case PartyAdvanceKinds.Wounded:
                return $"{SafeName(line.Ref,name,"A companion")} was gravely wounded, and has gone home to recover.";
            case PartyAdvanceKinds.Recovered:
                return $"{SafeName(line.Ref,name,"A companion")} has recovered.";
            default:
                // A kind with no words of its own yet is told plainly rather than dropped: what it is, and what it concerns.
                string what = Capitalised(line.Kind.Replace('.', ' ').Replace('_', ' '));
                string subject=SafeName(line.Ref,name,"");
                return subject.Length==0 ? $"{what}." : $"{what}: {subject}.";
        }
    }

    /// <summary>The time that passed in days, or hours under a day: "16 days", "1 day", "5 hours", "Less than an hour".</summary>
    public static string Span(long ticks, WorldClock clock)
    {
        long hours=ticks*clock.HoursPerDay/clock.TicksPerDay;
        long days=hours/clock.HoursPerDay,rest=hours%clock.HoursPerDay;
        if(days>0)return $"{days} {(days==1 ? "day" : "days")}"+(rest>0 ? $", {rest} {(rest==1 ? "hour" : "hours")}" : "");
        return hours==0 ? "Less than an hour" : $"{hours} {(hours==1 ? "hour" : "hours")}";
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

    private static string SafeName(string? id,Func<string,string> name,string fallback)
    {
        if(string.IsNullOrWhiteSpace(id) || Names.IsInstanceId(id))return fallback;
        string value=name(id);
        if(string.IsNullOrWhiteSpace(value) || value.StartsWith("Slot ",StringComparison.OrdinalIgnoreCase) ||
            value.Length==1 || (value==id && id.IndexOfAny(new[]{'.','_','#','|'})>=0))return fallback;
        return value;
    }
    private static string Actor(AdvanceLine line,Func<string,string> name,string fallback="An adventurer")
    {
        if(line.ActorName is { } known)return SafeName(known,x=>x,fallback);
        string key=line.Kind==AdventurerAdvanceKinds.Claimed && Split(line.Ref) is (_,var actor) ? actor : line.Ref;
        return key.Contains("slot",StringComparison.OrdinalIgnoreCase) ? fallback : SafeName(key,name,fallback);
    }
    private static string At(string? id,Func<string,string> name,string preposition=" at ") =>
        id is null ? "" : preposition+Place(SafeName(id,name,"inn"));

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
