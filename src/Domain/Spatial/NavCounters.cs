// UNNAMED Domain - navigation's work counts (M7 design §3.17)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// How much navigation has done (M7 design §3.17): deterministic work counts for the debug view and the budget tests. Never saved,
/// never in a digest, and never read by a decision.
/// </summary>
public sealed record NavCounters(
    long FullBuilds,
    long RectRebuilds,
    long TilesRestamped,
    long NodesRestamped,
    long Plans,
    ImmutableSortedDictionary<string, long> PlansByOutcome,
    long Expansions,
    int MaxExpansionsOneQuery,
    long EditChecks,
    ImmutableSortedDictionary<string, long> EditRefusalsByRule,
    long FloodNodes);

/// <summary>Where one owner's navigation work is counted. One per owner, never shared: two worlds never count into one sink.</summary>
public sealed class NavCounterSink
{
    private readonly SortedDictionary<string, long> _plansByOutcome = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _refusalsByRule = new(StringComparer.Ordinal);
    private long _fullBuilds, _rectRebuilds, _tilesRestamped, _nodesRestamped, _plans, _expansions, _editChecks, _floodNodes;
    private int _maxExpansions;

    public void CountFullBuild(int tiles, long nodes)
    {
        _fullBuilds++;
        _tilesRestamped += tiles;
        _nodesRestamped += nodes;
    }

    public void CountRectRebuild(int tiles, long nodes)
    {
        _rectRebuilds++;
        _tilesRestamped += tiles;
        _nodesRestamped += nodes;
    }

    public void CountPlan(string outcomeKey, int expansions)
    {
        _plans++;
        _plansByOutcome[outcomeKey] = _plansByOutcome.GetValueOrDefault(outcomeKey) + 1;
        _expansions += expansions;
        _maxExpansions = Math.Max(_maxExpansions, expansions);
    }

    public void CountEditCheck(string? refusedRule, long floodNodes)
    {
        _editChecks++;
        _floodNodes += floodNodes;
        if (refusedRule is not null)
            _refusalsByRule[refusedRule] = _refusalsByRule.GetValueOrDefault(refusedRule) + 1;
    }

    /// <summary>The counts so far, for a view.</summary>
    public NavCounters Snapshot() => new(_fullBuilds, _rectRebuilds, _tilesRestamped, _nodesRestamped, _plans,
        _plansByOutcome.ToImmutableSortedDictionary(StringComparer.Ordinal), _expansions, _maxExpansions, _editChecks,
        _refusalsByRule.ToImmutableSortedDictionary(StringComparer.Ordinal), _floodNodes);
}
