// UNNAMED World - factions and reputation (M7 design §5.5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Factions;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The factions as the running world uses them (M7 design §5.5): the definitions, the one ladder, and the act log's capacity. Built by
/// <c>FactionContent.Build</c>; relevance - which (kind, subject) pairs some faction reacts to - is fixed here, at boot.
/// </summary>
public sealed record FactionSetup
{
    private readonly ImmutableSortedDictionary<string, ImmutableArray<string>> _reactors;

    public FactionSetup(ImmutableSortedDictionary<string, FactionDefinition> factions, StandingLadder ladder, int logCapacity)
    {
        Factions = factions;
        Ladder = ladder;
        LogCapacity = logCapacity;
        _reactors = factions.Values
            .SelectMany(f => f.Reactions.Select(r => (Key: r.Kind + "|" + r.Subject, f.Id)))
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
    }

    /// <summary>No factions, the shipped ladder, and a log of 256 acts.</summary>
    public static FactionSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, FactionDefinition>(StringComparer.Ordinal),
        StandingLadder.Default, 256);

    public ImmutableSortedDictionary<string, FactionDefinition> Factions { get; }
    public StandingLadder Ladder { get; }
    public int LogCapacity { get; }

    /// <summary>Whether some faction has a reaction row for the pair: an act outside this set is never recorded.</summary>
    public bool IsRelevant(string kind, string subject) => _reactors.ContainsKey(kind + "|" + subject);

    /// <summary>The factions that react to the pair, in ordinal ID order.</summary>
    public ImmutableArray<string> ReactorsTo(string kind, string subject) =>
        _reactors.GetValueOrDefault(kind + "|" + subject, ImmutableArray<string>.Empty);
}

/// <summary>To FactionSystem: the character did something a faction may react to, standing here, now (M7 design §5.2).</summary>
internal sealed record RecordAct(string Kind, string Subject, long XMm, long ZMm) : InternalCommand;

/// <summary>To FactionSystem: the character told <paramref name="SpeakerNpcId"/> of every logged act of this kind and subject (§5.3, K5).</summary>
internal sealed record ReportAct(string Kind, string Subject, string SpeakerNpcId) : InternalCommand;

/// <summary>An act was recorded in the character's log. No faction knows of it yet.</summary>
public sealed record ActRecorded(long Seq, string Kind, string Subject, string CellKey, long Tick);

/// <summary>A faction learned of an act: by which channel and member, and whether it knows who did it.</summary>
public sealed record FactionLearned(string FactionId, long ActSeq, string Source, string? Via, string Identity, bool Upgraded, long Tick);

/// <summary>A faction's regard for the character changed, with the tiers before and after (the only place a tier reaches a view but <see cref="FactionView"/>).</summary>
public sealed record ReputationChanged(string FactionId, int From, int To, string TierFrom, string TierTo, long ActSeq, string Source, string? Via,
    long Tick);

public sealed record RelationView(string FactionId, string Attitude);

/// <summary>A faction as the character stands with it: points, the derived tier and level, its seat, members and relations.</summary>
public sealed record FactionView(string Id, string Name, int Points, string Tier, int Level, string SeatLocationId, ImmutableArray<string> Members,
    ImmutableArray<RelationView> Relations);

public sealed record KnowledgeView(string Knower, string Identity, string Source, string? Via, long Tick, int Delta);

/// <summary>An act in the character's log, with what each faction knows of it.</summary>
public sealed record ActView(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick, ImmutableArray<KnowledgeView> Known);

/// <summary>
/// Owns: <see cref="StateSlice.Factions"/> - the character's act log, what each faction knows of it, and the standing each holds (M7
/// design §5.5). It records an act only when some faction reacts to it, and writes knowledge and standing only when the character tells a
/// member (report-only, K5). It has no tick, reads no body, sight or conversation, and dispatches nothing.
/// </summary>
internal sealed class FactionSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public FactionSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private FactionSetup Setup => _context.Setup.Factions;

    public string? Handle(RecordAct act, long tick)
    {
        if (!Setup.IsRelevant(act.Kind, act.Subject))
            return null;
        var ledger = _context.State.Factions;
        long seq = ledger.NextActSeq;
        string cell = CellKey.OfWorld(act.XMm / 1000.0, act.ZMm / 1000.0).ToString();
        ledger = ledger.WithAct(new ActRecord(seq, act.Kind, act.Subject, cell, act.XMm, act.ZMm, tick));
        _context.Events.Publish(new ActRecorded(seq, act.Kind, act.Subject, cell, tick));
        _context.State.SetFactions(_owner, FactionRules.Compact(ledger, Setup.LogCapacity).Ledger);
        return null;
    }

    public string? Handle(ReportAct report, long tick)
    {
        if (_context.Setup.Social.Npcs.GetValueOrDefault(report.SpeakerNpcId)?.FactionId is not { } factionId
            || !Setup.Factions.TryGetValue(factionId, out var faction))
            return null;
        var ledger = _context.State.Factions;
        var start = ledger;
        foreach (var act in start.Acts.Where(a => a.Kind == report.Kind && a.Subject == report.Subject).OrderBy(a => a.Seq))
        {
            var (next, change) = FactionRules.Learn(ledger, faction, act.Seq, KnowledgeSources.Reported, report.SpeakerNpcId,
                Identities.Identified, tick, Setup.Ladder);
            ledger = next;
            if (change is null)
                continue;
            _context.Events.Publish(new FactionLearned(faction.Id, act.Seq, change.Source, change.Via, change.Identity, change.Upgraded, tick));
            if (change.To != change.From)
                _context.Events.Publish(new ReputationChanged(faction.Id, change.From, change.To, Setup.Ladder.StandingTierOf(change.From).Key,
                    Setup.Ladder.StandingTierOf(change.To).Key, act.Seq, change.Source, change.Via, tick));
        }
        if (!ledger.Equals(start))
            _context.State.SetFactions(_owner, ledger);
        return null;
    }

    /// <summary>Every faction, in ordinal ID order, as the character stands with it.</summary>
    public ImmutableArray<FactionView> Views() => Setup.Factions.Values.Select(f =>
    {
        int points = _context.State.StandingOf(f.Id);
        var tier = Setup.Ladder.StandingTierOf(points);
        var members = _context.Setup.Social.Npcs.Values.Where(n => n.FactionId == f.Id).Select(n => n.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray();
        return new FactionView(f.Id, f.Name, points, tier.Key, tier.Level, f.SeatLocationId, members,
            f.Relations.Select(r => new RelationView(r.FactionId, r.Attitude)).ToImmutableArray());
    }).ToImmutableArray();

    /// <summary>The act log, in ascending sequence, with what each faction knows of each act.</summary>
    public ImmutableArray<ActView> Acts()
    {
        var ledger = _context.State.Factions;
        return ledger.Acts.Select(a => new ActView(a.Seq, a.Kind, a.Subject, a.CellKey, a.XMm, a.ZMm, a.Tick,
            ledger.Knowledge.Where(k => k.Act == a.Seq).Select(k => new KnowledgeView(k.Knower, k.Identity, k.Source, k.Via, k.Tick, k.Delta))
                .ToImmutableArray())).ToImmutableArray();
    }
}
