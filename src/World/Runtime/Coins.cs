// UNNAMED World - the coin ledger's home (GEN-04): every movement of the purse written down with its reason
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Economy;

namespace UNNAMED.World.Runtime;

/// <summary>
/// To the coin ledger (GEN-04): one movement of the purse, dispatched where the purse actually moved, so the record cannot drift from the fact.
/// The balance is the purse's after the movement.
/// </summary>
internal sealed record RecordCoin(long Tick, CoinFlow Flow, string Reason, long Amount, long Balance) : InternalCommand;

/// <summary>
/// Owns the character's coin ledger (GEN-04): it writes down what came in and what went out, why, and what was left. The purse itself stays the
/// authoritative balance; this is the record a player reads and a tool checks against it.
/// </summary>
internal sealed class CoinSystem
{
    private readonly CoinLedger _ledger = new();

    /// <summary>The ledger as presentation and a test read it: the newest entries last.</summary>
    public CoinLedger Ledger => _ledger;

    /// <summary>One movement, in the order it happened.</summary>
    public string? Handle(RecordCoin command)
    {
        _ledger.Record(command.Tick, command.Flow, command.Reason, command.Amount, command.Balance);
        return null;
    }

    /// <summary>Whether the ledger accounts for the purse as it stands: the one check a ledger can make about itself.</summary>
    public bool Agrees(long purse) => _ledger.Balances(purse);
}
