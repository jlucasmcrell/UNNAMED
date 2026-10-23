// UNNAMED World - how the party regards the character, and what it says on the road: value reactions to deeds, loyalty from days together
// and wages, going along or not, leaving, and party banter (SYSTEMS_COMPLETENESS_PLAN §2.15, WP15b; EXT/COMPANIONS :38-58, :181-191)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>
/// A party member reacted to something the character did (WP15b): the value it touched (<see cref="LineId"/>, <c>reaction.&lt;npc&gt;.&lt;n&gt;</c>)
/// and, when they said something and the party was not keeping quiet, the remark - said aloud and subtitled.
/// </summary>
public sealed record PartyMemberReacted(string NpcId, string LineId, DeedKind Deed, string SubjectId, string? Remark, long Tick);

/// <summary>A party member went along with what they were asked, after a moment's doubt (<see cref="Disposition.Hesitate"/>).</summary>
public sealed record PartyMemberHesitated(string NpcId, string Asked, long Tick);

/// <summary>A party member said a line on the road (WP15b): a banter line (<c>banter.&lt;npc&gt;.&lt;n&gt;</c>), subtitled.</summary>
public sealed record BanterSpoken(string NpcId, string LineId, string Text, long Tick);

// ── the life beyond the party ───────────────────────────────────────────────

/// <summary>Someone who left the party alive, as the life simulation takes them up (WP15b): who, why, and their sheet - level, gear and belongings.</summary>
public sealed record PartyLifeEntry(string NpcId, PartyMembership Membership, string Reason, PartySheet? Sheet, long Tick);

/// <summary>
/// The life a party member goes back to (§2.15 "Persistent world"; canon :181-183): dismissed companions and hirelings become residents or
/// adventurers in the NPC life simulation (WP12, WP13), keep their level, gear and relationship, and go on living, including while the
/// character is away; and can be asked along again where they now live. WP12b supplies it: joining suspends a life (the <c>hired</c> life
/// state), leaving resumes it and they walk home. The sheet and what they think of the character are kept by the simulation either way.
/// </summary>
public interface IPartyLife
{
    /// <summary>They left the party alive - dismissed, served out, lapsed, left of their own accord, or gravely wounded.</summary>
    void TakeUp(PartyLifeEntry entry);

    /// <summary>They joined the party again: the life simulation lets them go.</summary>
    void Release(string npcId, long tick);

    /// <summary>
    /// Whether someone leaving the party walks back to their life on their own feet (WP12b: they have a routine), rather than being put
    /// at home as WP15a built it. A recorder that only listens says no.
    /// </summary>
    bool Walks(string npcId) => false;
}

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="CompanionSystem"/>: a party member leaves of their own accord (WP15b: past the leave bound).</summary>
internal sealed record LeaveParty(string NpcId, string Reason) : InternalCommand;

/// <summary>What a party member does when asked, read from what they think of the character now (WP15b).</summary>
internal static class PartyDisposition
{
    public static Disposition Of(RuntimeState state, PartyRules? rules, string npcId) =>
        Approval.Of(dimension => state.RelationshipOf(npcId, dimension), rules?.Approval ?? ApprovalRules.None);
}

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// The party's regard and voice (WP15b). It owns no state of its own: what an NPC thinks of the character is the relationship system's, and
/// this system moves it only by command. A deed (E-7) the character does within sight of a party member that touches one of their values
/// moves what they think on the value's dimensions and may draw a remark. Loyalty grows a little for every midnight a companion or an ally
/// spends on their feet in the party, and for every wage a hireling is paid, and falls for every wage owed. A member whose regard falls past
/// a leave bound (any dimension, however it moved) leaves; past a refuse or hesitate bound, the companion system refuses or hesitates.
/// Banter: at each boundary of <c>banter.every_s</c>, a member following on their feet, out of a fight and out of conversation, with a line
/// whose conditions hold, says one - by chance, and never while the party keeps quiet after a remark.
/// <para>
/// The absence (§2.12): only loyalty goes on - one step per midnight, the same online and away, so any split of an absence visits the same
/// midnights (T-22). Members who could fall while away are those already down, who are not counted at a midnight either way. Nothing is said.
/// </para>
/// </summary>
internal sealed class ApprovalSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly Func<string, ImmutableArray<SpokenLine>> _banter;

    /// <summary>The tick before which the party says nothing more (a remark was just made). Transient: a load starts talkative, and nothing saved depends on it.</summary>
    private long _quietUntil;

    /// <summary>When each banter line was last said (owner finding 52: no line twice within <see cref="RepeatTicks"/>). Transient, like the quiet.</summary>
    private readonly Dictionary<string, long> _said = new(StringComparer.Ordinal);

    /// <summary>Ten real minutes at the fixed 20 Hz tick.</summary>
    private const long RepeatTicks = 10 * 60 * 20;

    public ApprovalSystem(SystemContext context, Func<string, ImmutableArray<SpokenLine>> banter)
    {
        _context = context;
        _banter = banter;
    }

    private RuntimeState State => _context.State;
    private PartyRules? Rules => _context.Setup.Social.Party;

    public void Listen(SimulationEvents events)
    {
        events.Listen<DeedDone>(React);
        events.Listen<HirelingWage>(Wage);
        events.Listen<RelationshipChanged>(Reconsider);
    }

    public void Tick(long tick)
    {
        if (_context.Setup.Clock.DaysCrossed(tick - 1, tick) > 0)
            Daily(tick);
        if (Rules?.Banter is { } banter && tick % banter.EveryTicks == 0)
            Banter(tick, banter);
    }

    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (toTick <= fromTick)
            return;
        var clock = _context.Setup.Clock;
        for (long day = clock.Day(fromTick) + 1; day <= clock.Day(toTick); day++)
            Daily(day * clock.TicksPerDay - clock.StartTicks);
    }

    /// <summary>A midnight: every companion and ally on their feet in the party is a day more loyal.</summary>
    private void Daily(long tick)
    {
        int per = Rules?.Approval.LoyaltyPerDay ?? 0;
        if (per == 0)
            return;
        foreach (var member in State.Companions.Values.Where(c => c.Membership != PartyMembership.Hireling && c.Condition == CompanionCondition.Up).ToList())
            _context.Dispatch(new ChangeRelationship(member.NpcId, "loyalty", per, "days_together"));
    }

    /// <summary>A hireling's wage: paid, they are a little more loyal; owed, a little less.</summary>
    private void Wage(HirelingWage wage)
    {
        if (Rules is not { } rules || !State.Companions.ContainsKey(wage.NpcId))
            return;
        int delta = wage.Paid > 0 ? rules.Approval.LoyaltyWagePaid : wage.UnpaidDays > 0 ? rules.Approval.LoyaltyWageUnpaid : 0;
        if (delta != 0)
            _context.Dispatch(new ChangeRelationship(wage.NpcId, "loyalty", delta, wage.Paid > 0 ? "wage_paid" : "wage_owed"));
    }

    /// <summary>
    /// A deed the character did: each party member on their feet within sight of them (the catch-up distance) whose values it touches moves on
    /// each of the value's dimensions, and says the value's remark unless the party is keeping quiet.
    /// </summary>
    private void React(DeedDone deed)
    {
        long sight = _context.Setup.Social.Companions?.CatchUpBeyondMm ?? 0;
        foreach (var member in State.Companions.Values.Where(c => c.Condition == CompanionCondition.Up).ToList())
        {
            if (!State.Npcs.TryGetValue(member.NpcId, out var npc) || Distance(npc.Body, State.Body) > sight)
                continue;
            var values = npc.Definition.Values;
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (!value.Matches(deed.Kind, deed.SubjectId, deed.NpcId))
                    continue;
                string line = LineIds.Reaction(member.NpcId, i + 1);
                foreach (var (dimension, delta) in value.Changes)
                    _context.Dispatch(new ChangeRelationship(member.NpcId, dimension, delta, line));
                string? remark = value.Remark is not null && deed.Tick >= _quietUntil ? value.Remark : null;
                if (remark is not null)
                    _quietUntil = deed.Tick + (Rules?.Banter?.QuietTicks ?? 0);
                _context.Events.Publish(new PartyMemberReacted(member.NpcId, line, deed.Kind, deed.SubjectId, remark, deed.Tick));
            }
        }
    }

    /// <summary>What a party member thinks moved: past a leave bound, they leave (a member who is down stays where they lie).</summary>
    private void Reconsider(RelationshipChanged changed)
    {
        if (State.Companions.TryGetValue(changed.NpcId, out var member) && member.Condition == CompanionCondition.Up
            && PartyDisposition.Of(State, Rules, changed.NpcId) == Disposition.Leave)
            _context.Dispatch(new LeaveParty(changed.NpcId, "left"));
    }

    /// <summary>
    /// A banter boundary: of the members following on their feet, out of a fight and out of conversation, near the character, those with a
    /// line to say now; one of their lines is said, by chance - rolled from the world, the boundary and nothing else, so a replay agrees.
    /// </summary>
    private void Banter(long tick, BanterRules rules)
    {
        if (tick < _quietUntil || State.PlayerCombat.Defeated)
            return;
        long near = _context.Setup.Social.Companions?.CatchUpBeyondMm ?? 0;
        var lines = State.Companions.Values
            .Where(c => c.Condition == CompanionCondition.Up && c.Order == CompanionOrder.Follow && c.TargetKey is null && State.Conversation?.NpcId != c.NpcId
                        && State.Npcs.TryGetValue(c.NpcId, out var npc) && Distance(npc.Body, State.Body) <= near)
            .SelectMany(c => _banter(c.NpcId).Select(line => (c.NpcId, Line: line)))
            .Where(l => !_said.TryGetValue(l.Line.LineId, out long at) || tick - at >= RepeatTicks)
            .ToList();
        if (lines.Count == 0 || State.Conversation is not null)
            return;
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(0, 0), "banter", $"line@{tick}");
        if (channel.Int(0, 0, 100) >= rules.ChancePercent)
            return;
        var (npcId, spoken) = lines[channel.Int(1, 0, lines.Count)];
        _quietUntil = tick + rules.QuietTicks;
        _said[spoken.LineId] = tick;
        _context.Events.Publish(new BanterSpoken(npcId, spoken.LineId, spoken.Text, tick));
    }

    /// <summary>How a party member regards the character, in words and as what they would do if asked now.</summary>
    public (string Regard, Disposition Disposition) Standing(string npcId, string name) =>
        (Approval.Regard(name, dimension => State.RelationshipOf(npcId, dimension)), PartyDisposition.Of(State, Rules, npcId));

    private static double Distance(Body a, Body b)
    {
        double dx = a.XMm - b.XMm, dz = a.ZMm - b.ZMm;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
