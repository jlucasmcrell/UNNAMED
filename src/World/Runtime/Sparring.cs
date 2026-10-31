// UNNAMED World - sparring at the training ground (GEN-07): adventurers drill on a schedule, a bout moves their skill and their regard, and a
// player may watch one or step into it. No new combat path: every blow is the world's own strike resolver, stopped at a health floor.
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>To the training ground: step into the next bout there. The bout is fought at training weight, so it can hurt but never kill.</summary>
public sealed record JoinSparCommand(EntityId Actor, string SiteId) : GameCommand(Actor);

/// <summary>A training ground as the world shows it: where it is, when it drills, what is being seen there, and the pair on it.</summary>
public sealed record SparSiteView(string SiteId, string SettlementId, string Name, long XMm, long ZMm, int FacingMdeg,
    int FromHour, int ToHour, bool Drilling, string AnimationId, string? FirstKey, string? SecondKey);

/// <summary>A bout began on a training ground (GEN-07): where, the animation the drill is doing, and who is in it.</summary>
public sealed record SparringBegan(string SiteId, string SettlementId, string AnimationId, string FirstKey, string SecondKey, long Tick);

/// <summary>A bout was fought out (GEN-07): who won, how close it was, whether the character was in it, and the regard it left.</summary>
public sealed record SparringEnded(string SiteId, string SettlementId, string WinnerKey, string LoserKey, int Rounds, int CloseBy,
    bool PlayerFought, int RegardGained, long Tick);

/// <summary>
/// Owns the training grounds (GEN-07). A settlement's fighters drill where it trains, on the schedule its ground keeps; each bout is fought out
/// by <see cref="SparRules"/> from a seed the ground, the pair and the day give, so a save reproduces the same bout, and both fighters come away
/// better at their skill and thinking more of each other. The character may watch (the events are the world's own) or step in, and stands on the
/// same rules: health stops at the floor, so a bout is never a death.
/// </summary>
internal sealed class SparringSystem : IAbstractAdvance
{
    /// <summary>The character's key in a bout: the pair table and the events speak of adventurers, and the character stands beside them.</summary>
    public const string PlayerKey = "actor.player";

    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<ImmutableArray<AdventurerView>> _adventurers;
    private readonly Func<CombatView> _playerCombat;
    private readonly AdventurerSystem _owned;
    private ImmutableArray<SparSite>? _sites;

    public SparringSystem(SystemContext context, EntityId player, AdventurerSystem owned, Func<ImmutableArray<AdventurerView>> adventurers, Func<CombatView> playerCombat)
    {
        _context = context;
        _player = player;
        _owned = owned;
        _adventurers = adventurers;
        _playerCombat = playerCombat;
    }

    private RuntimeState State => _context.State;
    private WorldClock Clock => _context.Setup.Clock;
    private CombatConstants Constants => _context.Setup.Combat.Constants;

    /// <summary>One ground per settlement that keeps adventurers: its barracks yard, and the cairnwold's green.</summary>
    public ImmutableArray<SparSite> Sites => _sites ??= _context.Setup.Adventurers is { } setup
        ? setup.Settlements.Values.Select(s => new SparSite($"spar.{s.Id}", s.Id,
            s.Id == "cairnwold" ? "the cairnwold green" : "the barracks yard",
            s.LodgeXMm + 2_600, s.LodgeZMm, s.LodgeFacingMdeg, FromHour: 17, ToHour: 19, EveryDays: 2, AnimationId: "ANIM.TEACH_DEMONSTRATE"))
            .ToImmutableArray()
        : ImmutableArray<SparSite>.Empty;

    /// <summary>One world tick: a ground whose hour has come fights its bout, once that day.</summary>
    public void Tick(long tick) => Run(tick);

    /// <summary>An absence: every hour crossed in turn, so a long absence and the same absence in pieces leave the same bouts (T-22).</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        long hours = Math.Max(0, (toTick - fromTick) * Clock.HoursPerDay / Math.Max(1, Clock.TicksPerDay));
        long step = (toTick - fromTick) / Math.Max(1, hours);
        for (long at = fromTick + step; at <= toTick && step > 0; at += step)
            Run(at);
        if (hours == 0)
            Run(toTick);
    }

    private void Run(long tick)
    {
        long day = Clock.Day(tick);
        int hour = (int)Clock.Hour(tick);
        foreach (var site in Sites.Where(s => s.Live(day, hour)))
        {
            var bout = FightOut(site, tick, playerFights: false);
            if (bout is { } fought)
                Carry(site, fought, playerFought: false, tick);
        }
    }

    /// <summary>The pair this ground puts up: its settlement's own fighters, in town, well, and not with the character, closest in level first.</summary>
    private ImmutableArray<AdventurerView> Pair(SparSite site) =>
        _adventurers()
            .Where(a => a.Settlement == site.SettlementId && a.At == site.SettlementId && a.Condition == AdventurerCondition.Active && a.Party is null)
            .OrderBy(a => a.Level).ThenBy(a => a.Key, StringComparer.Ordinal)
            .Take(2)
            .ToImmutableArray();

    /// <summary>Fight the ground's bout out, or - in the player's case - the character's own bout against the ground's best.</summary>
    private SparBout? FightOut(SparSite site, long tick, bool playerFights, AdventurerView? against = null)
    {
        if (_context.Setup.Adventurers is null || State.World.Adventurers is not { } state)
            return null;
        var pair = playerFights ? ImmutableArray.Create(against!) : Pair(site);
        if (pair.Length < 2 && !playerFights)
            return null;

        SparFighter Of(AdventurerView view) => new(view.Key, view.Name, view.Level, SparRules.DrillBlow, SparRules.Defense(view.Level),
            state.Agents.TryGetValue(view.Key, out var record) ? record.Progression : CharacterProgression.Empty);

        // The seed is the ground, the pair and the day: the same midnight plans the same bout.
        ulong seed = 1469598103934665603UL;
        foreach (char c in site.Id + "|" + pair[0].Key + "|" + pair[1 % pair.Length].Key + "|" + Clock.Day(tick))
            seed = (seed ^ c) * 1099511628211UL;
        ulong hash = seed;

        var first = playerFights ? PlayerFighter(tick) : Of(pair[0]);
        var second = Of(pair[1 % pair.Length]);
        return SparRules.Fight(site, first, second, tick, Constants, _ =>
        {
            hash = (hash ^ (hash >> 33)) * 0xFF51AFD7ED558CCDUL;
            return (hash >> 11) * (1.0 / 9007199254740992.0);
        });
    }

    /// <summary>The character as a fighter: their own weapon and their own health, on the drill's terms.</summary>
    private SparFighter PlayerFighter(long tick)
    {
        var view = _playerCombat();
        var progression = State.Progression;
        return new SparFighter(PlayerKey, "You", (int)progression.Level, view.Weapon,
            new DefenseProfile(view.MaxHealth, Enum.GetValues<BodyRegion>().ToImmutableSortedDictionary(r => r, r => 0),
                ImmutableSortedDictionary.Create<string, double>(StringComparer.Ordinal), Blocking: false, Dodging: false), progression);
    }

    /// <summary>What a bout left behind: both practised their skill, both think a little more of the other, and the world was told.</summary>
    private void Carry(SparSite site, SparBout bout, bool playerFought, long tick)
    {
        if (State.World.Adventurers is not { } state)
            return;
        int gained = _owned.Sparred(site, bout, tick);

        // The character's side of a bout is their real health, and the loss goes through the combat system's own harm.
        if (playerFought && bout.First.Key == PlayerKey)
        {
            int lost = _playerCombat().MaxHealth - bout.FirstHealth;
            if (lost > 0)
                _context.Dispatch(new Harm(_player, "sparring", lost));
        }
        _context.Events.Publish(new SparringEnded(site.Id, site.SettlementId, bout.WinnerKey, bout.LoserKey, bout.Rounds, bout.CloseBy,
            playerFought, gained, tick));
    }

    /// <summary>The grounds as the away sheet and any panel read them: where, when, and who is on it now.</summary>
    public ImmutableArray<SparSiteView> Views(long tick) => Sites.Select(site =>
    {
        long day = Clock.Day(tick);
        var pair = Pair(site);
        return new SparSiteView(site.Id, site.SettlementId, site.Name, site.XMm, site.ZMm, site.FacingMdeg, site.FromHour, site.ToHour,
            site.Live(day, (int)Clock.Hour(tick)), site.AnimationId,
            pair.Length > 0 ? pair[0].Key : null, pair.Length > 1 ? pair[1].Key : null);
    }).ToImmutableArray();

    public string? Handle(JoinSparCommand command, long tick)
    {
        var site = Sites.FirstOrDefault(s => s.Id == command.SiteId);
        if (site is null)
            return $"there is no training ground {command.SiteId}";
        if (!site.Live(Clock.Day(tick), (int)Clock.Hour(tick)))
            return $"{site.Name} is not drilling now: it drills from {site.FromHour} to {site.ToHour}";
        var pair = Pair(site);
        if (pair.IsEmpty)
            return $"nobody of {site.SettlementId} is on {site.Name} to spar with";
        var bout = FightOut(site, tick, playerFights: true, against: pair[0]);
        if (bout is not { } fought)
            return "the bout could not be called";
        Carry(site, fought, playerFought: true, tick);
        return null;
    }
}
