// UNNAMED World - public contribution accounting for shared encounters (CMB-45; EPIC 534-535 #9, #10; E4/E5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;

namespace UNNAMED.World.Runtime;

/// <summary>
/// How one encounter ended: which creature, in which region, how it was finished, who did what down to the part, and who it was who put it
/// down. The shares are public - that is the point of the accounting (the MMO rule): a companion, a hired hand and the character are all in
/// the same list with their own per-mille.
/// </summary>
public sealed record EncounterConcluded(
    string EncounterKey,
    string CreatureDefId,
    string RegionKey,
    string Outcome,
    EntityId Finisher,
    ImmutableArray<ContributionShare> Shares,
    long Tick);

/// <summary>
/// Owns the contribution ledgers (CMB-45): it accumulates every landed blow part by part while a creature is alive, and when the creature
/// falls it divides the credit, publishes the outcome qualified by region, and records what a save must keep - the encounter happened here,
/// how it ended, and whether the character qualified for its rewards.
///
/// The counters live in memory only: a ledger is worth nothing after its encounter has ended, and what has to survive a save is the outcome,
/// which is what the flags keep.
/// </summary>
internal sealed class ContributionSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Dictionary<string, ContributionLedger> _ledgers = new(StringComparer.Ordinal);

    public ContributionSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private RuntimeState State => _context.State;

    /// <summary>The ledger of one encounter, as the board would show it while the fight is on: every part, every attacker.</summary>
    public ContributionLedger? Ledger(string encounterKey) => _ledgers.GetValueOrDefault(encounterKey);

    /// <summary>Every live ledger, in encounter-key order, so a presentation or a test reads them the same way.</summary>
    public ImmutableArray<ContributionLedger> Ledgers() => _ledgers.Values.OrderBy(l => l.EncounterKey, StringComparer.Ordinal).ToImmutableArray();

    /// <summary>
    /// A blow landed on a creature: it counts against that creature's encounter, part by part. A critical is the runtime's mark for a blow
    /// that landed where it counts - CMB-38's timed windows do not yet report their own, so the multiplier rides on the critical until they do.
    /// </summary>
    public void Landed(HitResolved hit)
    {
        if (hit.Damage <= 0 || hit.Blocked || hit.Dodged)
            return;
        if (State.Creatures.TryGetValue(hit.Target, out var target) is false)
            return;
        if (!_ledgers.TryGetValue(target.Key, out var ledger))
            _ledgers[target.Key] = ledger = new ContributionLedger(target.Key);
        ledger.Landed(hit.Attacker.Value, hit.Region, hit.Damage, hit.Critical, hit.Tick);
    }

    /// <summary>
    /// The creature fell: divide the credit, publish the outcome qualified by the region it happened in, and keep the two facts a save needs -
    /// that this encounter concluded here, and whether the character's own contribution qualified (E4). A creature nobody was seen to hit
    /// still concludes, with the finisher as its only share: the world does not pretend a kill did not happen because nobody watched.
    /// </summary>
    public void Concluded(EntityId id, string defId, EntityId killer, long tick)
    {
        if (!State.Creatures.TryGetValue(id, out var target))
            return;
        if (!_ledgers.Remove(target.Key, out var ledger))
            ledger = new ContributionLedger(target.Key);
        var shares = ledger.Shares();
        if (shares.IsEmpty)
            shares = ImmutableArray.Create(new ContributionShare(killer.Value, 1, 1000));

        var regionKey = _context.CellOf(target.Body).Region;
        string region = "r" + regionKey.Rx + "_" + regionKey.Rz;
        _context.Events.Publish(new EncounterConcluded(target.Key, defId, region, "killed", killer, shares, tick));

        string encounter = FlagKey(target.Key);
        _context.Dispatch(new SetWorldFlag(_context.CellOf(target.Body), "world.contribution." + encounter + ".outcome", 1));
        _context.Dispatch(new SetWorldFlag(_context.CellOf(target.Body), "world.contribution.region." + FlagKey(region), 1));

        var mine = shares.FirstOrDefault(s => s.ActorId == _player.Value);
        if (mine is { Qualified: true })
            _context.Dispatch(new SetWorldFlag(_context.CellOf(target.Body), "world.contribution.qualified", 1));

        Pay((int)target.Definition.XpValue, shares, target.Key, tick);
    }

    /// <summary>
    /// Pay the people who fought it (the MMO rule). The purse is derived from what the encounter was worth, so it is deterministic, and each
    /// contributor takes their own per-mille of it. The character is paid by the reward systems that own a character's purse; an adventurer
    /// is paid here, through <see cref="AdventurerShare"/>, which is the event the bounty purse split already speaks.
    /// </summary>
    private void Pay(int xpValue, ImmutableArray<ContributionShare> shares, string encounterKey, long tick)
    {
        long purse = ContributionRules.Purse(xpValue);
        if (purse <= 0)
            return;
        foreach (var share in shares)
        {
            if (share.ActorId == _player.Value || !share.Qualified)
                continue;
            if (AdventurerOf(share.ActorId) is not { } key)
                continue;
            _context.Events.Publish(new AdventurerShare(key, encounterKey, purse * share.Permille / 1000, tick));
        }
    }

    /// <summary>The adventurer behind a body's instance id, if the contributor is one: the same bodies the adventurer system embodies.</summary>
    private string? AdventurerOf(string instanceId)
    {
        var id = EntityId.Parse(instanceId);
        var agents = State.World.Adventurers?.Agents;
        if (agents is null)
            return null;
        return agents.Values.FirstOrDefault(a => AdventurerSystem.InstanceIdOf(a.Key) == id)?.Key;
    }

    /// <summary>A spawn key or a region made into a flag id: a world flag is a definition id, so only lower-case letters, digits and dots stay.</summary>
    private static string FlagKey(string key)
    {
        var chars = key.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        return new string(chars).Trim('_');
    }
}
