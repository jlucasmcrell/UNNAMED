// UNNAMED World - the weather: regional fronts as a pure function of the world seed, the region and the game hour (WP7;
// SYSTEMS_COMPLETENESS_PLAN §2.8 W3, D6)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.World.Runtime;

/// <summary>What the sky over a region is doing. <see cref="Fold"/> is reserved: named so saves and content can carry it, never drawn yet.</summary>
public enum WeatherState
{
    Fair,
    Overcast,
    Rain,
    Fog,
    Wind,
    Fold,
}

public static class WeatherStates
{
    public static string Key(WeatherState state) => state switch
    {
        WeatherState.Fair => "fair",
        WeatherState.Overcast => "overcast",
        WeatherState.Rain => "rain",
        WeatherState.Fog => "fog",
        WeatherState.Wind => "wind",
        WeatherState.Fold => "fold",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static WeatherState? Parse(string key) => key switch
    {
        "fair" => WeatherState.Fair,
        "overcast" => WeatherState.Overcast,
        "rain" => WeatherState.Rain,
        "fog" => WeatherState.Fog,
        "wind" => WeatherState.Wind,
        "fold" => WeatherState.Fold,
        _ => null,
    };
}

/// <summary>
/// How the weather is tuned (config.weather): fronts last <see cref="FrontHoursMin"/> to <see cref="FrontHoursMax"/> game hours, laid out
/// <see cref="SpanHours"/> at a time, and each state is drawn by its <see cref="Weights"/> (a state without a weight is never drawn; the
/// reserved fold state never has one).
/// </summary>
public sealed record WeatherRules(int FrontHoursMin, int FrontHoursMax, int SpanHours, ImmutableSortedDictionary<WeatherState, int> Weights)
{
    /// <summary>D6's fronts of 4-12 game hours, a day at a time; fair weather most often, fog least.</summary>
    public static readonly WeatherRules Default = new(4, 12, 24, new Dictionary<WeatherState, int>
    {
        [WeatherState.Fair] = 4,
        [WeatherState.Overcast] = 3,
        [WeatherState.Rain] = 2,
        [WeatherState.Fog] = 1,
        [WeatherState.Wind] = 2,
    }.ToImmutableSortedDictionary());

    /// <summary>
    /// The weights of a season where it has its own (config.weather's <c>season_weights</c>; WLD-33): a season moves what the sky does, not
    /// how long a front lasts. A season without its own draws by <see cref="Weights"/>.
    /// </summary>
    public ImmutableSortedDictionary<Season, ImmutableSortedDictionary<WeatherState, int>> SeasonWeights { get; init; } =
        ImmutableSortedDictionary<Season, ImmutableSortedDictionary<WeatherState, int>>.Empty;

    public ImmutableSortedDictionary<WeatherState, int> WeightsIn(Season? season) =>
        season is { } s && SeasonWeights.TryGetValue(s, out var own) ? own : Weights;

    /// <summary>
    /// The rules, or why they cannot lay out fronts. A span always holds at least two fronts and tiles exactly; three states are drawn, so a
    /// front can always differ from both its neighbours.
    /// </summary>
    public WeatherRules Validated()
    {
        foreach (var (season, weights) in SeasonWeights)
            Check(weights, $"in {Seasons.Key(season)} ");
        Check(Weights, "");
        if (FrontHoursMin < 1 || FrontHoursMax < 2 * FrontHoursMin - 1)
            throw new FormatException($"weather fronts of {FrontHoursMin}-{FrontHoursMax} hours: the shortest is at least an hour, and the longest at least twice it less one");
        if (SpanHours <= FrontHoursMax)
            throw new FormatException($"a weather span of {SpanHours} hours must be longer than the longest front ({FrontHoursMax})");
        return this;
    }

    private static void Check(ImmutableSortedDictionary<WeatherState, int> weights, string when)
    {
        if (weights.Values.Any(w => w < 0))
            throw new FormatException($"a weather weight {when}is never negative");
        if (weights.GetValueOrDefault(WeatherState.Fold) != 0)
            throw new FormatException($"the fold state is reserved: it has no weight {when}yet");
        if (weights.Count(w => w.Value > 0) < 3)
            throw new FormatException($"the weather {when}draws at least three states");
    }
}

/// <summary>One front: its state, and the game hours it holds, from <see cref="StartHour"/> up to (not including) <see cref="EndHour"/>.</summary>
public sealed record WeatherFront(WeatherState State, long StartHour, long EndHour);

/// <summary>
/// The weather of the region played, as presentation reads it: the front over it now and the ticks it holds, and the front it took over
/// from (<see cref="Previous"/>), which the sky blends out of by elapsed world ticks (<see cref="Weather.Blend"/>).
/// </summary>
public sealed record WeatherView(string RegionId, WeatherState State, long FrontStartTick, long FrontEndTick)
{
    public WeatherState Previous { get; init; } = State;
}

/// <summary>A new front reached the region (or the first one was seen, <paramref name="From"/> null). Raised by <see cref="WeatherSystem"/>.</summary>
public sealed record WeatherChanged(string RegionId, WeatherState? From, WeatherState To, long FrontStartTick, long FrontEndTick, long Tick);

/// <summary>
/// The weather is a pure function of (world seed, region, game hour) through <see cref="RngChannel"/>: nothing is stepped, so the weather
/// of any hour is read without the hours before it, and an absence of any length is caught up at once (§2.12).
/// </summary>
/// <remarks>
/// Time is cut into spans of <see cref="WeatherRules.SpanHours"/>. Each span's fronts tile it exactly, every length between the shortest
/// and the longest front, drawn from the span's own channel. The first front's state is the span's own draw; each later one differs from
/// the one before, and the last differs from the next span's first too - so no two neighbouring fronts share a state, and every front, at
/// a span boundary as anywhere, lasts as long as its draw said.
/// </remarks>
public static class Weather
{
    private const string Subsystem = "weather";

    /// <summary>
    /// How far the sky has gone from the previous front to this one at <paramref name="tick"/>: 0 as the front arrives, 1 once
    /// <paramref name="fadeTicks"/> world ticks have passed (WLD-38). A function of the tick alone, so a load in the middle of a change draws
    /// the same sky as the run that went through it.
    /// </summary>
    public static double Blend(WeatherView view, long tick, long fadeTicks) =>
        view.Previous == view.State || fadeTicks <= 0 ? 1.0 : Math.Clamp((tick - view.FrontStartTick) / (double)fadeTicks, 0.0, 1.0);

    /// <summary>
    /// The front over a region at a game hour (hour 0 is the first hour of the world's first day). <paramref name="seasonOf"/> names the
    /// season of each span (by its first hour), for <see cref="WeatherRules.SeasonWeights"/>; without it every span draws by the base weights.
    /// </summary>
    public static WeatherFront At(ulong worldSeed, CellKey region, string regionId, long hour, WeatherRules rules, Func<long, Season>? seasonOf = null)
    {
        long span = Math.DivRem(hour, rules.SpanHours, out long within);
        if (within < 0)
            span--;
        foreach (var front in Fronts(worldSeed, region, regionId, span, rules, seasonOf))
        {
            if (hour < front.EndHour)
                return front;
        }
        throw new InvalidOperationException($"the fronts of span {span} do not reach hour {hour}");
    }

    /// <summary>The fronts of one span, in order, tiling it exactly.</summary>
    public static ImmutableArray<WeatherFront> Fronts(ulong worldSeed, CellKey region, string regionId, long span, WeatherRules rules,
        Func<long, Season>? seasonOf = null)
    {
        var weights = WeightsOf(span, rules, seasonOf);
        var lengths = RngChannel.Open(worldSeed, region, Subsystem, $"{regionId}/{span}/fronts");
        var draws = new List<int>();
        int remaining = rules.SpanHours;
        while (remaining > rules.FrontHoursMax)
        {
            int length = lengths.Int((uint)draws.Count, rules.FrontHoursMin, Math.Min(rules.FrontHoursMax, remaining - rules.FrontHoursMin) + 1);
            draws.Add(length);
            remaining -= length;
        }
        draws.Add(remaining);

        var states = States(worldSeed, region, regionId, span);
        var next = First(worldSeed, region, regionId, span + 1, WeightsOf(span + 1, rules, seasonOf));
        var fronts = ImmutableArray.CreateBuilder<WeatherFront>(draws.Count);
        long start = span * rules.SpanHours;
        WeatherState state = First(worldSeed, region, regionId, span, weights);
        for (int i = 0; i < draws.Count; i++)
        {
            if (i > 0)
                state = Draw(states, (uint)i, weights, state, i == draws.Count - 1 ? next : state);
            fronts.Add(new WeatherFront(state, start, start + draws[i]));
            start += draws[i];
        }
        return fronts.MoveToImmutable();
    }

    private static RngChannel States(ulong worldSeed, CellKey region, string regionId, long span) =>
        RngChannel.Open(worldSeed, region, Subsystem, $"{regionId}/{span}/states");

    private static ImmutableSortedDictionary<WeatherState, int> WeightsOf(long span, WeatherRules rules, Func<long, Season>? seasonOf) =>
        rules.WeightsIn(seasonOf?.Invoke(span * rules.SpanHours));

    /// <summary>A span's first state: its own draw, owing nothing to the span before - which is what lets a span be laid out alone.</summary>
    private static WeatherState First(ulong worldSeed, CellKey region, string regionId, long span, ImmutableSortedDictionary<WeatherState, int> weights) =>
        Draw(States(worldSeed, region, regionId, span), 0, weights, null, null);

    /// <summary>A state by the weights, never one of the two excluded.</summary>
    private static WeatherState Draw(RngChannel channel, uint sample, ImmutableSortedDictionary<WeatherState, int> weights, WeatherState? not, WeatherState? norThis)
    {
        var allowed = weights.Where(w => w.Value > 0 && w.Key != not && w.Key != norThis).ToList();
        int pick = channel.Int(sample, 0, allowed.Sum(w => w.Value));
        foreach (var (state, weight) in allowed)
        {
            if (pick < weight)
                return state;
            pick -= weight;
        }
        throw new InvalidOperationException("a weighted draw fell outside its weights");
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.Weather"/> - the seed cursor: the game hour the front last announced began, and nothing else, because the
/// weather itself is derived (<see cref="Weather.At"/>). Each new game hour it reads the front over the region played, and raises
/// <see cref="WeatherChanged"/> when that front is not the one the cursor names. Its abstract advance is closed-form: the front at the end
/// of the absence, whatever the absence's pieces.
/// </summary>
internal sealed class WeatherSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly CellKey _region;
    private long _checkedHour = -1;

    public WeatherSystem(SystemContext context, SliceOwner owner, CellKey region)
    {
        _context = context;
        _owner = owner;
        _region = region;
    }

    private RuntimeState State => _context.State;
    private WorldClock Clock => _context.Setup.Clock;
    private string RegionId => _context.Setup.Layout.Id;

    public WeatherFront FrontAt(long tick) => FrontOfHour(Clock.Hour(tick));

    public WeatherView View(long tick)
    {
        var front = FrontAt(tick);
        return new WeatherView(RegionId, front.State, Clock.TickOfHour(front.StartHour), Clock.TickOfHour(front.EndHour))
        {
            Previous = FrontOfHour(front.StartHour - 1).State,
        };
    }

    public void Tick(long tick)
    {
        long hour = Clock.Hour(tick);
        if (hour == _checkedHour)
            return;
        _checkedHour = hour;
        Announce(FrontOfHour(hour), tick);
    }

    /// <summary>The world going on while the character was away (WP11): the front over the region when they come back, closed-form.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        long hour = Clock.Hour(toTick);
        var front = FrontOfHour(hour);
        _checkedHour = hour;
        log.Add(new AdvanceLine(toTick, AdvanceKinds.WeatherNow, WeatherStates.Key(front.State)));
        Announce(front, toTick);
    }

    private WeatherFront FrontOfHour(long hour) => Weather.At(State.World.WorldSeed, _region, RegionId, hour, _context.Setup.Weather, SeasonOfHour);

    /// <summary>The season of a game hour's day (WLD-33): what the weather of that span is drawn by.</summary>
    private Season SeasonOfHour(long hour) =>
        _context.Setup.Seasons.SeasonOn((int)(Math.Max(0, hour) / Clock.HoursPerDay % _context.Setup.Calendar.DaysPerReckoning));

    private void Announce(WeatherFront front, long tick)
    {
        if (State.WeatherCursor == front.StartHour)
            return;
        WeatherState? from = State.WeatherCursor is { } cursor ? FrontOfHour(cursor).State : null;
        State.SetWeatherCursor(_owner, front.StartHour);
        _context.Events.Publish(new WeatherChanged(RegionId, from, front.State, Clock.TickOfHour(front.StartHour), Clock.TickOfHour(front.EndHour), tick));
    }
}
