// UNNAMED World - looking for company: an NPC setting out asks along, the character answers, and they leave together (WP16)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;

namespace UNNAMED.World.Runtime;

/// <summary>What an NPC sets out to do (<see cref="SettingOut.Purpose"/>): the template family that words their offer.</summary>
public static class LfgPurposes
{
    public const string Hunt = "hunt";
    public const string Bounty = "bounty";
    public const string Errand = "errand";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Hunt, Bounty, Errand);
}

/// <summary>
/// An NPC is setting out and will take company (the WP12 contract; WP16 consumes it). The NPC waits at <see cref="MeetAnchor"/> until
/// <see cref="DepartTick"/> - holding them there is the life simulation's - then leaves, with whoever answered and came. Every field a
/// line words is already words. Deterministic: the life simulation raises the same SettingOut at the same tick of the same world.
/// </summary>
/// <param name="Purpose">One of <see cref="LfgPurposes"/>.</param>
/// <param name="PurposeName">What they are going to do, in words (a bounty's title, "the boar in the birch").</param>
/// <param name="Destination">Where they are going, in words.</param>
/// <param name="MeetAnchor">The anchor they wait at (a location or work-anchor ID); <see cref="AnchorName"/> is its name.</param>
public sealed record SettingOut(
    EntityId Npc,
    string NpcName,
    string Purpose,
    string PurposeName,
    string Destination,
    string MeetAnchor,
    string AnchorName,
    long MeetXMm,
    long MeetZMm,
    long DepartTick,
    long Tick);

/// <summary>
/// Where SettingOut comes from: the NPC life simulation (WP12), read by tick so an absence can be caught up closed-form. A pure read of
/// the world: <see cref="Between"/> gives every SettingOut whose <see cref="SettingOut.Tick"/> lies in (fromTick, toTick], in tick order.
/// The NPC life simulation's trip planner supplies it (WP12b); a fixed schedule (<see cref="SettingOuts.Of"/>) stands in for it in tests.
/// </summary>
public interface ISettingOutSource
{
    IEnumerable<SettingOut> Between(long fromTick, long toTick);
}

public static class SettingOuts
{
    /// <summary>A fixed schedule of setting-outs: the stub the tests run the contract with.</summary>
    public static ISettingOutSource Of(IEnumerable<SettingOut> schedule) => new Fixed(schedule.OrderBy(s => s.Tick).ThenBy(s => s.Npc).ToImmutableArray());

    private sealed class Fixed : ISettingOutSource
    {
        private readonly ImmutableArray<SettingOut> _schedule;

        public Fixed(ImmutableArray<SettingOut> schedule) => _schedule = schedule;

        public IEnumerable<SettingOut> Between(long fromTick, long toTick) => _schedule.Where(s => s.Tick > fromTick && s.Tick <= toTick);
    }
}

/// <summary>Answer an NPC's offer to set out together (the Join action, or <c>/join</c>). Null <paramref name="Npc"/>: the newest open offer.</summary>
public sealed record JoinGroupCommand(EntityId Actor, EntityId? Npc) : GameCommand(Actor);

/// <summary>An open offer to set out together, as presentation shows it (the Join action reads it).</summary>
public sealed record LfgOfferView(
    EntityId Npc,
    string NpcName,
    string Purpose,
    string PurposeName,
    string Destination,
    string AnchorName,
    long MeetXMm,
    long MeetZMm,
    long DepartTick,
    long PostedTick,
    bool Joined);

/// <summary>
/// A group that set out together (WP16). Contract-free, as <see cref="PartyMembership.TemporaryAlly"/>: no wage, no contract, a shared
/// goal. WP13 gives it its life - who fights beside whom, and when the group parts.
/// </summary>
public sealed record AllyGroupView(string GroupKey, EntityId Leader, ImmutableArray<EntityId> Members, string Purpose, string Destination, long FormedTick)
{
    public PartyMembership Membership => PartyMembership.TemporaryAlly;
}

/// <summary>The character answered an NPC's offer (raised by <see cref="LfgSystem"/>).</summary>
public sealed record LfgJoined(EntityId Npc, string NpcName, string AnchorName, long DepartTick, long Tick);

/// <summary>The NPC left for <see cref="Destination"/> without the character, who had answered but was not at the meeting place.</summary>
public sealed record LfgMissed(EntityId Npc, string NpcName, string Destination, long MeetXMm, long MeetZMm, long Tick);

/// <summary>A temporary-ally group formed at its meeting place and set out (raised by <see cref="LfgSystem"/>; WP13 listens).</summary>
public sealed record AllyGroupFormed(AllyGroupView Group, string LeaderName, long Tick);

/// <summary>An open offer as the simulation holds it.</summary>
internal sealed record LfgOffer(SettingOut Out, bool Joined);

/// <summary>The offers open now and the groups that formed. Transient: not saved (WP16 bumps no schema); offers are re-read at load.</summary>
internal sealed record LfgState(ImmutableSortedDictionary<EntityId, LfgOffer> Offers, ImmutableArray<AllyGroupView> Groups)
{
    public static LfgState Empty { get; } = new(ImmutableSortedDictionary<EntityId, LfgOffer>.Empty, ImmutableArray<AllyGroupView>.Empty);
}

/// <summary>
/// Owns: <see cref="StateSlice.Lfg"/> - the open offers to set out together, and the groups that formed. Each tick it reads who set out
/// (<see cref="ISettingOutSource"/>) and opens their offer, raising <see cref="SettingOut"/> (which chat words); it takes the character's
/// <see cref="JoinGroupCommand"/>; and at each offer's depart tick it forms the group, if the character answered and stands at the meeting
/// place, or lets the NPC go alone. Its abstract advance is closed-form: an offer still open at the end of the absence is open, every
/// other one has left.
/// </summary>
internal sealed class LfgSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly ISettingOutSource _source;

    public LfgSystem(SystemContext context, SliceOwner owner, EntityId player, ISettingOutSource source)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _source = source;
    }

    private RuntimeState State => _context.State;
    private ISettingOutSource Source => _source;
    private ChatRules Rules => _context.Setup.Chat.Rules;

    /// <summary>
    /// At load: the offers made before it that are still open, re-read from the source - quietly, since they were announced before.
    /// Who had answered is not kept (nothing of chat is saved).
    /// </summary>
    public void Populate()
    {
        long now = State.WorldTick;
        long lookback = Rules.OfferLookbackHours * _context.Setup.Clock.TicksPerDay / _context.Setup.Clock.HoursPerDay;
        foreach (var s in Source.Between(Math.Max(-1, now - lookback), now))
            Open(s, now, announce: false);
    }

    public void Tick(long tick)
    {
        // Departures first: an NPC whose offer leaves now may post their next one on the same tick.
        foreach (var offer in State.Lfg.Offers.Values.Where(o => o.Out.DepartTick <= tick).ToList())
            Depart(offer, tick, present: Within(State.Body.XMm - offer.Out.MeetXMm, State.Body.ZMm - offer.Out.MeetZMm, Rules.JoinRadiusMm));
        foreach (var s in Source.Between(tick - 1, tick))
            Open(s, tick, announce: true);
    }

    /// <summary>The absence (WP11): who set out while the character was away and is still waiting is waiting; everyone else has gone.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        foreach (var offer in State.Lfg.Offers.Values.Where(o => o.Out.DepartTick <= toTick).ToList())
            Depart(offer, toTick, present: false);
        foreach (var s in Source.Between(fromTick, toTick))
            Open(s, toTick, announce: true);
    }

    public string? Handle(JoinGroupCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var open = State.Lfg.Offers.Values.Where(o => !o.Joined && o.Out.DepartTick > tick);
        var offer = command.Npc is { } npc
            ? State.Lfg.Offers.GetValueOrDefault(npc)
            : open.OrderByDescending(o => o.Out.Tick).ThenBy(o => o.Out.Npc).FirstOrDefault();
        if (offer is null || offer.Out.DepartTick <= tick)
            return command.Npc is null ? "no one is looking for company" : "they are not looking for company";
        if (offer.Joined)
            return $"{offer.Out.NpcName} already expects you";
        Set(State.Lfg with { Offers = State.Lfg.Offers.SetItem(offer.Out.Npc, offer with { Joined = true }) });
        _context.Events.Publish(new LfgJoined(offer.Out.Npc, offer.Out.NpcName, offer.Out.AnchorName, offer.Out.DepartTick, tick));
        return null;
    }

    public ImmutableArray<LfgOfferView> Offers() => State.Lfg.Offers.Values
        .Select(o => new LfgOfferView(o.Out.Npc, o.Out.NpcName, o.Out.Purpose, o.Out.PurposeName, o.Out.Destination, o.Out.AnchorName,
            o.Out.MeetXMm, o.Out.MeetZMm, o.Out.DepartTick, o.Out.Tick, o.Joined))
        .OrderBy(o => o.DepartTick).ThenBy(o => o.Npc).ToImmutableArray();

    public ImmutableArray<AllyGroupView> Groups() => State.Lfg.Groups;

    /// <summary>A group's key: its leader and the tick it set out - the same for the offer it grows from and the group it becomes.</summary>
    public static string GroupKey(EntityId leader, long departTick) => $"{leader}@{departTick}";

    /// <summary>
    /// The groups the character belongs to, or has answered the offer of, by key: whose Party messages they hear. Only the character
    /// answers offers, so an answered offer is theirs.
    /// </summary>
    public ImmutableHashSet<string> GroupsOf(EntityId member) =>
        State.Lfg.Groups.Where(g => g.Members.Contains(member)).Select(g => g.GroupKey)
            .Concat(member == _player ? State.Lfg.Offers.Values.Where(o => o.Joined).Select(o => GroupKey(o.Out.Npc, o.Out.DepartTick)) : Enumerable.Empty<string>())
            .ToImmutableHashSet(StringComparer.Ordinal);

    private void Open(SettingOut s, long tick, bool announce)
    {
        // Someone already waiting keeps their first plan; a plan that leaves before it is heard was never an offer.
        if (s.DepartTick <= tick || State.Lfg.Offers.ContainsKey(s.Npc) || !LfgPurposes.All.Contains(s.Purpose))
            return;
        Set(State.Lfg with { Offers = State.Lfg.Offers.SetItem(s.Npc, new LfgOffer(s, false)) });
        if (announce)
            _context.Events.Publish(s);
    }

    private void Depart(LfgOffer offer, long tick, bool present)
    {
        var lfg = State.Lfg with { Offers = State.Lfg.Offers.Remove(offer.Out.Npc) };
        if (offer.Joined && present)
        {
            var group = new AllyGroupView(GroupKey(offer.Out.Npc, offer.Out.DepartTick), offer.Out.Npc, ImmutableArray.Create(offer.Out.Npc, _player),
                offer.Out.Purpose, offer.Out.Destination, tick);
            Set(lfg with { Groups = lfg.Groups.Add(group) });
            _context.Events.Publish(new AllyGroupFormed(group, offer.Out.NpcName, tick));
            return;
        }
        Set(lfg);
        if (offer.Joined)
            _context.Events.Publish(new LfgMissed(offer.Out.Npc, offer.Out.NpcName, offer.Out.Destination, offer.Out.MeetXMm, offer.Out.MeetZMm, tick));
    }

    private void Set(LfgState lfg) => State.SetLfg(_owner, lfg);

    private static bool Within(long dx, long dz, long radius) => dx * dx + dz * dz <= radius * radius;
}
