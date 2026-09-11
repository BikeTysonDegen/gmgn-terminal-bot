using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Tests;

public class PaperBrokerTests
{
    private const decimal SolUsd = 150m;

    private static BuyOrder Buy(decimal sol, decimal priceUsd, string mint = "mint1", decimal slip = 0m, decimal fee = 0m) => new()
    {
        Mint = mint,
        Symbol = "TST",
        SolToSpend = sol,
        PriceUsd = priceUsd,
        SolUsd = SolUsd,
        SlippagePercent = slip,
        FeePercent = fee,
        LeaderAddress = "leader1",
        LeaderTxHash = "tx1"
    };

    private static SellOrder Sell(decimal fraction, decimal priceUsd, string mint = "mint1", decimal slip = 0m, decimal fee = 0m) => new()
    {
        Mint = mint,
        Fraction = fraction,
        PriceUsd = priceUsd,
        SolUsd = SolUsd,
        SlippagePercent = slip,
        FeePercent = fee,
        LeaderAddress = "leader1",
        LeaderTxHash = "tx2"
    };

    [Fact]
    public void Buy_UpdatesBalanceAndPosition()
    {
        var b = new PaperBroker(10m);

        // price 0.001 usd, spend 1 SOL, no slip/fee -> 150 usd worth -> 150000 tokens
        var r = b.Buy(Buy(1m, 0.001m));

        Assert.True(r.Ok);
        Assert.Equal(9m, b.BalanceSol);
        var pos = b.GetPosition("mint1")!;
        Assert.Equal(150000m, pos.Quantity);
        Assert.Equal(1m, pos.CostSol);
        Assert.Equal(150m, pos.CostUsd);
        Assert.Equal(0.001m, pos.EntryPriceUsd);
        Assert.Equal(TradeSide.Buy, r.Fill!.Side);
    }

    [Fact]
    public void Buy_WithSlippageAndFee_GetsFewerTokens()
    {
        var b = new PaperBroker(10m);

        // 10% slippage -> exec price 0.0011; 1% fee on 1 SOL -> net 0.99 SOL = 148.5 usd
        var r = b.Buy(Buy(1m, 0.001m, slip: 10m, fee: 1m));

        Assert.True(r.Ok);
        var pos = b.GetPosition("mint1")!;
        var expected = 148.5m / 0.0011m;
        Assert.Equal(Math.Round(expected, 9), Math.Round(pos.Quantity, 9));
        Assert.Equal(0.01m, r.Fill!.FeeSol);
    }

    [Fact]
    public void Buy_InsufficientBalance_Fails()
    {
        var b = new PaperBroker(0.5m);
        var r = b.Buy(Buy(1m, 0.001m));
        Assert.False(r.Ok);
        Assert.Contains("insufficient", r.Error);
        Assert.Equal(0.5m, b.BalanceSol);
        Assert.Null(b.GetPosition("mint1"));
    }

    [Fact]
    public void Buy_InvalidPrice_Fails()
    {
        var b = new PaperBroker(10m);
        Assert.False(b.Buy(Buy(1m, 0m)).Ok);
        Assert.False(b.Buy(Buy(0m, 1m)).Ok);
    }

    [Fact]
    public void Sell_Partial_TakesFractionOfBag_RealizesPnl()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(1m, 0.001m)); // 150000 tokens, cost 1 SOL

        // price doubled, sell 50%
        var r = b.Sell(Sell(0.5m, 0.002m));

        Assert.True(r.Ok);
        var pos = b.GetPosition("mint1")!;
        Assert.Equal(75000m, pos.Quantity);
        Assert.True(pos.IsOpen);

        // sold 75000 * 0.002 = 150 usd = 1 SOL, basis 0.5 SOL -> realized 0.5 SOL
        Assert.Equal(0.5m, Math.Round(pos.RealizedSol, 6));
        Assert.Equal(0.5m, Math.Round(r.Fill!.RealizedSol, 6));
        Assert.Equal(10m, b.BalanceSol); // 9 after buy + 1 from sell
    }

    [Fact]
    public void Sell_FullExit_ClosesPosition()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(2m, 0.001m));

        var r = b.Sell(Sell(1m, 0.001m)); // flat exit

        Assert.True(r.Ok);
        var pos = b.GetPosition("mint1")!;
        Assert.False(pos.IsOpen);
        Assert.Equal(0m, pos.Quantity);
        Assert.Equal(0m, pos.CostSol);
        Assert.Equal(0, b.OpenPositionCount);
        Assert.Equal(10m, b.BalanceSol); // break even
    }

    [Fact]
    public void Sell_FullExit_AtLoss_RealizesNegative()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(1m, 0.001m));

        var r = b.Sell(Sell(1m, 0.0005m)); // -50%

        Assert.True(r.Ok);
        Assert.Equal(-0.5m, Math.Round(r.Fill!.RealizedSol, 6));
        Assert.Equal(9.5m, b.BalanceSol);
    }

    [Fact]
    public void Sell_NoPosition_Fails()
    {
        var b = new PaperBroker(10m);
        var r = b.Sell(Sell(1m, 0.001m, mint: "unknown"));
        Assert.False(r.Ok);
        Assert.Contains("no open position", r.Error);
    }

    [Fact]
    public void Sell_WithSlippageAndFee_ReceivesLess()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(1m, 0.001m)); // 150000 tokens

        // full exit, price 0.001, 10% slip -> exec 0.0009; gross 135 usd = 0.9 SOL; 1% fee -> 0.891 SOL
        var r = b.Sell(Sell(1m, 0.001m, slip: 10m, fee: 1m));

        Assert.True(r.Ok);
        Assert.Equal(0.891m, Math.Round(r.Fill!.SolAmount, 6));
        Assert.Equal(9.891m, b.BalanceSol);
    }

    [Fact]
    public void MarkPrice_UpdatesUnrealized()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(1m, 0.001m)); // cost 150 usd

        b.MarkPrice("mint1", 0.002m, SolUsd);
        var pos = b.GetPosition("mint1")!;

        Assert.Equal(300m, pos.ValueUsd);       // 150000 * 0.002
        Assert.Equal(150m, pos.UnrealizedPnlUsd);
        Assert.Equal(100m, pos.PnlPercent);
        Assert.Equal(2m, pos.ValueSol);         // 300/150
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var b = new PaperBroker(10m);
        b.Buy(Buy(1m, 0.001m));

        b.Reset(5m);

        Assert.Equal(5m, b.BalanceSol);
        Assert.Empty(b.Positions);
        Assert.Empty(b.Fills);
        Assert.Null(b.GetPosition("mint1"));
    }

    [Fact]
    public void Fills_AreNumberedSequentially()
    {
        var b = new PaperBroker(10m);
        var r1 = b.Buy(Buy(1m, 0.001m));
        var r2 = b.Sell(Sell(0.5m, 0.001m));

        Assert.Equal(1, r1.Fill!.Id);
        Assert.Equal(2, r2.Fill!.Id);
        Assert.Equal(2, b.Fills.Count);
    }
}
