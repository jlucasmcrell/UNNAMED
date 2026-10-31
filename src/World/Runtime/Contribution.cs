// UNNAMED World - public contribution accounting for shared encounters (CMB-45; EPIC 534-535 #9, #10; E4/E5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;

namespace UNNAMED.World.Runtime;

/// <summary>
/// How one encounter ended: which creature, in which region, how it was finished, who did what down to the part, and who put it down. The
/// shares are public - that is the point of the accounting (the MMO rule): a companion, a hired hand and the character all appear with their
/// own per-mille.
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
/// Owns the contribution ledgers (CMB-45). It hears about the fight the way the carcass system hears about a death: the system that resolves
/// a blow dispatches <see cref="RecordContribution"/>, and the system that owns the death dispatches <see cref="ConcludeEncounter"/>. Commands
/// are the channel a live system is guaranteed to be reached on; events are for views.
///
/// The counters live in memory only - a ledger is worth nothing once its encounter has ended - and what has to survive a save is the outcome,
/// which the flags keep.
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
    /// A blow landed: it counts against that creature's encounter, part by part. A critical stands in for the weak point until CMB-38's timed
    /// windows report their own.
    /// </summary>
    public void Handle(RecordContribution command)
    {
        if (command.Damage <= 0)
            return;
        if (!_ledgers.TryGetValue(command.EncounterKey, out var ledger))
            _ledgers[command.EncounterKey] = ledger = new ContributionLedger(command.EncounterKey);
        ledger.Landed(command.ActorId, command.Region, command.Damage, command.WeakPoint, command.Tick);
    }

    /// <summary>
    /// The encounter ended: divide the credit, publish the outcome qualified by region, pay the adventurers who qualified, and keep the two
    /// facts a save needs - that this encounter concluded here, and whether the character's own contribution qualified (E4). A creature
    /// nobody was seen to hit still concludes, with the finisher as its only share.
    /// </summary>
    public void Handle(ConcludeEncounter command)
    {
        if (!_ledgers.Remove(command.EncounterKey, out var ledger))
            ledger = new ContributionLedger(command.EncounterKey);
        var shares = ledger.Shares();
        if (shares.IsEmpty)
            shares = ImmutableArray.Create(new ContributionShare(command.KillerId, 1, 1000));

        var killer = EntityId.Parse(command.KillerId);
        CellKey cell = State.Creatures.Values.FirstOrDefault(c => c.Key == command.EncounterKey) is { } body
            ? _context.CellOf(body.Body)
            : _context.CellOf(State.Body);
        string region = "r" + cell.Region.Rx + "_" + cell.Region.Rz;

        _context.Events.Publish(new EncounterConcluded(command.EncounterKey, command.CreatureDefId, region, "killed", killer, shares, command.Tick));

        string encounter = FlagKey(command.EncounterKey);
        _context.Dispatch(new SetWorldFlag(cell, "world.contribution." + encounter + ".outcome", 1));
        _context.Dispatch(new SetWorldFlag(cell, "world.contribution.region." + FlagKey(region), 1));

        var mine = shares.FirstOrDefault(s => s.ActorId == _player.Value);
        if (mine is { Qualified: true })
            _context.Dispatch(new SetWorldFlag(cell, "world.contribution.qualified", 1));

        Pay(command.CreatureDefId, shares, command.EncounterKey, command.Tick);
    }

    /// <summary>
    /// Pay the people who fought it (the MMO rule). The purse comes from what the encounter was worth, so it is deterministic, and each
    /// contributor takes their own per-mille. The character's own share stays with the reward systems that own a character's purse; an
    /// adventurer is paid here through <see cref="AdventurerShare"/>, the event the bounty purse split already speaks.
    /// </summary>
    private void Pay(string creatureDefId, ImmutableArray<ContributionShare> shares, string encounterKey, long tick)
    {
        if (!_context.Setup.Combat.Creatures.TryGetValue(creatureDefId, out var definition))
            return;
        long purse = ContributionRules.Purse((int)definition.XpValue);
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
        var agents = State.World.Adventurers?.Agents;
        if (agents is null)
            return null;
        var id = EntityId.Parse(instanceId);
        return agents.Values.FirstOrDefault(a => AdventurerSystem.InstanceIdOf(a.Key) == id)?.Key;
    }

    /// <summary>A spawn key or a region made into a flag id: a world flag is a definition id, so only lower-case letters, digits and dots stay.</summary>
    private static string FlagKey(string key)
    {
        var chars = key.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        return new string(chars).Trim('_');
    }
}
