// UNNAMED Domain tests - the coin ledger and barter (GEN-04)
using UNNAMED.Domain.Economy;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// GEN-04's ledger and the barter rule: what came in, what went out, why, and whether goods are worth what a trader is asked to take for them.
/// Deterministic: the same movements always read the same way.
/// </summary>
public class CoinLedgerTests
{
    [Fact]
    public void EveryMovementIsWrittenWithItsReasonAndTheBalanceItLeft()
    {
        var ledger = new CoinLedger();
        ledger.Record(100, CoinFlow.In, CoinReasons.Quest, 40, 40);
        ledger.Record(140, CoinFlow.Out, CoinReasons.Teaching, 25, 15);
        ledger.Record(200, CoinFlow.In, CoinReasons.TradeSell, 6, 21);

        var entries = ledger.Entries;
        Assert.Equal(3, entries.Length);
        Assert.Equal((100L, CoinFlow.In, "quest.reward", 40L, 40L), (entries[0].Tick, entries[0].Flow, entries[0].Reason, entries[0].Amount, entries[0].Balance));
        Assert.Equal((46L, 25L), ledger.Totals());
        Assert.Equal(21, entries[^1].Balance);
        Assert.True(ledger.Balances(21));
        Assert.False(ledger.Balances(20));
    }

    [Fact]
    public void AMovementOfNothing_IsNotWritten()
    {
        var ledger = new CoinLedger();
        ledger.Record(1, CoinFlow.In, CoinReasons.Loot, 0, 10);
        Assert.Empty(ledger.Entries);
        ledger.Record(2, CoinFlow.Out, CoinReasons.TradeBuy, 0, 10);
        Assert.Empty(ledger.Entries);
    }

    [Fact]
    public void AReasonNobodyNamed_IsRecordedAsOther_RatherThanAsAnEmptyLine()
    {
        var ledger = new CoinLedger();
        ledger.Record(5, CoinFlow.Out, "", 3, 7);
        Assert.Equal(CoinReasons.Other, ledger.Entries.Single().Reason);
    }

    [Fact]
    public void TheLedgerKeepsTheRecentAndForgetsTheOld()
    {
        var ledger = new CoinLedger();
        for (int i = 0; i < CoinLedger.Kept + 10; i++)
            ledger.Record(i, CoinFlow.In, CoinReasons.Loot, 1, i + 1);

        Assert.Equal(CoinLedger.Kept, ledger.Entries.Length);
        Assert.Equal(CoinLedger.Kept + 9, ledger.Entries[^1].Tick);   // the newest is kept
        Assert.Equal(10, ledger.Entries[0].Tick);                     // the oldest ten are gone
        Assert.Equal(3, ledger.Recent(3).Length);
        Assert.Equal(CoinLedger.Kept + 9, ledger.Recent(3)[^1].Tick);
        Assert.Empty(ledger.Recent(0));
    }

    [Fact]
    public void AnEmptyLedger_DoesNotPretendToKnowThePurse()
    {
        var ledger = new CoinLedger();
        Assert.True(ledger.Balances(0));
        Assert.True(ledger.Balances(999));   // nothing recorded, so nothing to contradict
    }

    [Fact]
    public void GoodsCoverAPrice_WhenTheyAreWorthWhatTheTraderWants()
    {
        // A torch at 5 coin, a trader wanting a fifth over: 6 in goods does it, 5 does not.
        Assert.Equal(6, BarterRules.Required(5, 1_200));
        Assert.True(BarterRules.Judge(5, 1_200, new[] { ("item.hide.cured", 6L) }).Accepts);
        Assert.False(BarterRules.Judge(5, 1_200, new[] { ("item.hide.cured", 5L) }).Accepts);

        // At the fair-trade margin the price itself is enough.
        Assert.Equal(5, BarterRules.Required(5, BarterRules.FairTradePermille));
        Assert.True(BarterRules.Judge(5, BarterRules.FairTradePermille, new[] { ("item.hide.cured", 5L) }).Accepts);
    }

    [Fact]
    public void ARefusalSaysHowShortTheGoodsAre()
    {
        var verdict = BarterRules.Judge(priceCoin: 20, marginPermille: 1_200, goods: new[] { ("item.hide.cured", 9L), ("item.ore.iron", 3L) });
        Assert.False(verdict.Accepts);
        Assert.Equal(24, verdict.Required);
        Assert.Equal(12, verdict.Offered);
        Assert.Equal(12, verdict.Short);
    }

    [Fact]
    public void GoodsWorthMoreThanThePrice_GiveChange_AndANothingPriceAsksNothing()
    {
        Assert.Equal(4, BarterRules.Change(offered: 30, priceCoin: 26));
        Assert.Equal(0, BarterRules.Change(offered: 10, priceCoin: 26));
        Assert.Equal(0, BarterRules.Required(0, 1_200));
        Assert.True(BarterRules.Judge(0, 1_200, Array.Empty<(string, long)>()).Accepts);
    }

    [Fact]
    public void WorthlessGoods_CountForNothing()
    {
        var verdict = BarterRules.Judge(10, 1_000, new[] { ("item.junk", 0L), ("item.junk2", -5L) });
        Assert.False(verdict.Accepts);
        Assert.Equal(0, verdict.Offered);
    }
}
