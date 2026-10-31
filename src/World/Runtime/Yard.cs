// UNNAMED World - the sparring yard (GEN-07): bouts in a settlement's yard that a player can watch, bet on or join
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>
/// To the yard: put coin on one side of a bout. The book's odds come from the level gap, and a win pays the stake times the odds into the ledger as
/// a wager - so betting is part of the economy the coin ledger already records, not a room behind it.
/// </summary>
public sealed record BetOnBoutCommand(EntityId Actor, string SettlementId, string BackedKey, long Stake) : GameCommand(Actor);

/// <summary>To the yard: step into a bout. The player fights by the yard's own rules and can be hurt by them.</summary>
public sealed record JoinBoutCommand(EntityId Actor, string SettlementId, string AgainstKey) : GameCommand(Actor);

/// <summary>A bout as the yard shows it: who is in it, the odds the book offers, and - once it is run - who won and by how much.</summary>
public sealed record YardBout(string SettlementId, string FirstKey, string FirstName, int FirstLevel, string SecondKey, string SecondName, int SecondLevel,
    int OddsPermille, bool Open)
{
    /// <summary>Whether these two are close enough in level for the yard to call the bout.</summary>
    public bool Matched => SparRules.CloseEnough(FirstLevel, SecondLevel);
}

/// <summary>What a bet did: what was staked, at what odds, and what came back.</summary>
public sealed record BoutResult(YardBout Bout, string WinnerKey, string LoserKey, int Margin, bool Hurt, long Payout, bool Won);

/// <summary>
/// Owns the yard (GEN-07). A bout is called from the adventurers standing in a settlement, run by <see cref="SparRules"/> from a seed the
/// settlement and the day give, and then it is three things at once: something to watch, something to bet on, and something to step into.
///
/// The yard holds no state of its own: the fighters are the adventurer views the world already publishes, and the result is made where it is
/// reported, so a save reproduces the same bout from the same day.
/// </summary>
internal sealed class YardSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<ImmutableArray<AdventurerView>> _adventurers;

    public YardSystem(SystemContext context, EntityId player, Func<ImmutableArray<AdventurerView>> adventurers)
    {
        _context = context;
        _player = player;
        _adventurers = adventurers;
    }

    private RuntimeState State => _context.State;

    /// <summary>Every bout the yard has on offer in one settlement: each pair of its adventurers that is close enough in level to be called.</summary>
    public ImmutableArray<YardBout> Offer(string settlementId) =>
        _adventurers().Where(a => a.Settlement == settlementId && a.Condition == AdventurerCondition.Active)
            .SelectMany(a => _adventurers().Where(b => b.Settlement == settlementId && b.Condition == AdventurerCondition.Active
                    && string.CompareOrdinal(a.Key, b.Key) < 0)
                .Select(b => new YardBout(settlementId, a.Key, a.Name, a.Level, b.Key, b.Name, b.Level,
                    SparRules.OddsPermille(a.Level, b.Level), Open: true)))
            .Where(b => b.Matched)
            .OrderBy(b => b.FirstKey, StringComparer.Ordinal).ThenBy(b => b.SecondKey, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>Run a bout: the seed is the settlement, the pair and the day, so the yard's own fight is the same one every time it is asked.</summary>
    public BoutResult Run(YardBout bout, long day, long stake = 0, string? backedKey = null)
    {
        ulong seed = 1469598103934665603UL;
        foreach (char c in bout.SettlementId + "|" + bout.FirstKey + "|" + bout.SecondKey + "|" + day)
            seed = (seed ^ c) * 1099511628211UL;

        var outcome = SparRules.Bout(bout.FirstKey, bout.FirstLevel, bout.SecondKey, bout.SecondLevel, seed);
        int odds = SparRules.OddsPermille(outcome.Winner == bout.FirstKey ? bout.FirstLevel : bout.SecondLevel,
            outcome.Winner == bout.FirstKey ? bout.SecondLevel : bout.FirstLevel);
        bool won = backedKey is not null && backedKey == outcome.Winner;
        long payout = stake > 0 && won ? SparRules.Payout(stake, odds, won: true) : 0;
        return new BoutResult(bout, outcome.Winner, outcome.Loser, outcome.Margin, SparRules.Hurts(outcome), payout, won);
    }

    public string? Handle(BetOnBoutCommand command, long tick)
    {
        var day = _context.Setup.Clock.Day(tick);
        var bout = Offer(command.SettlementId).FirstOrDefault(b => b.FirstKey == command.BackedKey || b.SecondKey == command.BackedKey);
        if (bout is null)
            return $"{command.SettlementId} has no bout to back";
        if (command.Stake <= 0)
            return "a bet is worth something";
        if (command.Stake > State.Currency)
            return $"not enough coin: {command.Stake} asked, {State.Currency} carried";

        // The stake leaves the purse and reaches the ledger as a wager before the bout is run, so a bet is always paid for.
        if (_context.Dispatch(new SpendCurrency((int)command.Stake)) is { } refused)
            return refused;

        var result = Run(bout, day, command.Stake, command.BackedKey);
        if (result.Payout > 0)
            _context.Dispatch(new AddCurrency((int)result.Payout));
        _context.Events.Publish(new BoutSettled(command.SettlementId, result.WinnerKey, result.LoserKey, result.Margin, result.Payout, tick));
        return null;
    }

    public string? Handle(JoinBoutCommand command, long tick)
    {
        var day = _context.Setup.Clock.Day(tick);
        var opponent = _adventurers().FirstOrDefault(a => a.Key == command.AgainstKey && a.Settlement == command.SettlementId);
        if (opponent is null)
            return "there is no such body in that yard";

        int level = _context.Setup.Progression is null ? 1 : (int)State.Progression.Level;
        var bout = new YardBout(command.SettlementId, _player.Value, "You", level, opponent.Key, opponent.Name, opponent.Level,
            SparRules.OddsPermille(level, opponent.Level), Open: true);
        if (!bout.Matched)
            return $"{opponent.Name} is not a match for you: {level} against {opponent.Level}";

        var result = Run(bout, day);
        _context.Events.Publish(new BoutSettled(command.SettlementId, result.WinnerKey, result.LoserKey, result.Margin, 0, tick));
        return null;
    }
}

/// <summary>A bout was settled in a yard (GEN-07): who won, by how much, and what it paid.</summary>
public sealed record BoutSettled(string SettlementId, string WinnerKey, string LoserKey, int Margin, long Payout, long Tick);
