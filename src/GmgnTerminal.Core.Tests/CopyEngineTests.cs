using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Models;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Tests;

internal class StubTradeSource : ITradeSource
{
    public List<Trade> Trades { get; } = new();
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<Trade>> GetWalletActivityAsync(string wallet, int limit, decimal solUsdFallback, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult<IReadOnlyList<Trade>>(Trades.Where(t => t.Maker == wallet).Take(limit).ToList());
    }
}

internal class StubPriceSource : IPriceSource
{
    public decimal TokenPriceUsd { get; set; } = 0.001m;
    public decimal SolUsd { get; set; } = 150m;

    public Task<decimal?> GetTokenPriceUsdAsync(string mint, CancellationToken ct = default) =>
        Task.FromResult<decimal?>(TokenPriceUsd);

    public Task<decimal> GetSolUsdAsync(CancellationToken ct = default) => Task.FromResult(SolUsd);
}

public class CopyEngineTests
{
    private const string LeaderAddr = "LeaderWallet111111111111111111111111111111";
    private const string Mint = "TokenMint111111111111111111111111111pump";

    private readonly AppConfig _cfg = new();
    private readonly List<Leader> _leaders = new();
    private readonly StubTradeSource _source = new();
    private readonly StubPriceSource _prices = new();
    private readonly PaperBroker _broker = new(10m);

    public CopyEngineTests()
    {
        _cfg.Trading.DelayMinMs = 0;
        _cfg.Trading.DelayMaxMs = 0;
        _leaders.Add(new Leader { Address = LeaderAddr, Alias = "whale" });
    }

    private CopyEngine NewEngine() =>
        new(_source, _prices, _broker, () => _cfg, () => _leaders);

    private static Trade Buy(decimal sol, decimal tokens = 1000m, string mint = Mint, long ts = 1789363100) => new()
    {
        TxHash = Guid.NewGuid().ToString("N"),
        Maker = LeaderAddr,
        Mint = mint,
        Symbol = "TST",
        Side = TradeSide.Buy,
        SolAmount = sol,
        UsdAmount = sol * 150m,
        TokenAmount = tokens,
        PriceUsd = 0.001m,
        Timestamp = ts
    };

    private static Trade Sell(decimal sol, decimal tokens = 1000m, string mint = Mint, long ts = 1789363200) => new()
    {
        TxHash = Guid.NewGuid().ToString("N"),
        Maker = LeaderAddr,
        Mint = mint,
        Symbol = "TST",
        Side = TradeSide.Sell,
        SolAmount = sol,
        UsdAmount = sol * 150m,
        TokenAmount = tokens,
        PriceUsd = 0.001m,
        Timestamp = ts
    };

    [Fact]
    public async Task Buy_BelowMinLeaderSol_Skipped()
    {
        var engine = NewEngine();
        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(0.1m), CancellationToken.None);

        Assert.Equal(CopyAction.Skipped, ev.Action);
        Assert.Contains("min", ev.Detail);
        Assert.Equal(10m, _broker.BalanceSol);
    }

    [Fact]
    public async Task Buy_LeaderMinSolOverridesGlobal()
    {
        _leaders[0].MinSol = 2m;
        var engine = NewEngine();

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m), CancellationToken.None);

        Assert.Equal(CopyAction.Skipped, ev.Action);
    }

    [Fact]
    public async Task Buy_FixedSize_Copied()
    {
        _cfg.Trading.FixedSizeSol = 0.2m;
        var engine = NewEngine();

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(3m), CancellationToken.None);

        Assert.Equal(CopyAction.BuyCopied, ev.Action);
        Assert.Equal(0.2m, ev.OurFill!.SolAmount);
        Assert.Equal(9.8m, _broker.BalanceSol);
        Assert.True(_broker.HasOpenPosition(Mint));
    }

    [Fact]
    public async Task Buy_Proportional_UsesMultiplier_AndMaxCap()
    {
        _cfg.Trading.SizeMode = SizeMode.Proportional;
        _cfg.Trading.MaxOurSolPerTrade = 1.5m;
        _leaders[0].Multiplier = 0.5m;
        var engine = NewEngine();

        // leader buys 4 SOL -> 4 * 0.5 = 2 -> capped at 1.5
        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(4m), CancellationToken.None);

        Assert.Equal(CopyAction.BuyCopied, ev.Action);
        Assert.Equal(1.5m, ev.OurFill!.SolAmount);
    }

    [Fact]
    public async Task Buy_ClampedToBalance()
    {
        _cfg.Trading.FixedSizeSol = 50m; // way more than the 10 SOL wallet
        _cfg.Trading.MaxOurSolPerTrade = 50m;
        var engine = NewEngine();

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m), CancellationToken.None);

        Assert.Equal(CopyAction.BuyCopied, ev.Action);
        Assert.Equal(10m, ev.OurFill!.SolAmount);
        Assert.Equal(0m, _broker.BalanceSol);
    }

    [Fact]
    public async Task Buy_MaxOpenPositions_Respected()
    {
        _cfg.Trading.MaxOpenPositions = 1;
        var engine = NewEngine();

        var first = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m, mint: "mintAAA1111111111111111111111111111pump"), CancellationToken.None);
        var second = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m, mint: "mintBBB2222222222222222222222222222pump"), CancellationToken.None);

        Assert.Equal(CopyAction.BuyCopied, first.Action);
        Assert.Equal(CopyAction.Skipped, second.Action);
        Assert.Contains("max open positions", second.Detail);
    }

    [Fact]
    public async Task Buy_AveragingUp_AllowedAtMaxPositions()
    {
        _cfg.Trading.MaxOpenPositions = 1;
        var engine = NewEngine();

        await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m), CancellationToken.None);
        var second = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m), CancellationToken.None);

        Assert.Equal(CopyAction.BuyCopied, second.Action); // same mint = already open
    }

    [Fact]
    public async Task Buy_SkipListedMint_Skipped()
    {
        _cfg.Trading.SkipMints.Add(Mint);
        var engine = NewEngine();

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m), CancellationToken.None);

        Assert.Equal(CopyAction.Skipped, ev.Action);
        Assert.Contains("skip list", ev.Detail);
    }

    [Fact]
    public async Task Sell_NoPosition_Skipped()
    {
        var engine = NewEngine();

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Sell(1m), CancellationToken.None);

        Assert.Equal(CopyAction.Skipped, ev.Action);
        Assert.Contains("no open position", ev.Detail);
    }

    [Fact]
    public async Task Sell_FullTrackedBag_ClosesPosition()
    {
        var engine = NewEngine();
        await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m, tokens: 1000m), CancellationToken.None);

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Sell(1m, tokens: 1000m), CancellationToken.None);

        Assert.Equal(CopyAction.SellCopied, ev.Action);
        Assert.Contains("100%", ev.Detail);
        Assert.False(_broker.HasOpenPosition(Mint));
    }

    [Fact]
    public async Task Sell_PartialTrackedBag_TrimsProportionally()
    {
        var engine = NewEngine();
        await engine.ProcessLeaderTradeAsync(_leaders[0], Buy(1m, tokens: 1000m), CancellationToken.None);
        var pos = _broker.GetPosition(Mint)!;
        var qtyBefore = pos.Quantity;

        // leader sells 300 of 1000 tracked -> we sell 30%
        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Sell(0.4m, tokens: 300m), CancellationToken.None);

        Assert.Equal(CopyAction.SellCopied, ev.Action);
        Assert.Equal(qtyBefore * 0.7m, Math.Round(pos.Quantity, 6));
        Assert.True(pos.IsOpen);
    }

    [Fact]
    public async Task Sell_UnknownBag_FullExit()
    {
        // position exists but leader buys predate the engine -> bag tracker empty
        var engine = NewEngine();
        _broker.Buy(new BuyOrder
        {
            Mint = Mint, Symbol = "TST", SolToSpend = 1m,
            PriceUsd = 0.001m, SolUsd = 150m
        });

        var ev = await engine.ProcessLeaderTradeAsync(_leaders[0], Sell(1m, tokens: 500m), CancellationToken.None);

        Assert.Equal(CopyAction.SellCopied, ev.Action);
        Assert.False(_broker.HasOpenPosition(Mint));
    }

    [Fact]
    public async Task PollOnce_DedupesAndWatermarks()
    {
        var engine = NewEngine();
        var trade = Buy(1m, ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _source.Trades.Add(trade);
        _source.Trades.Add(Buy(1m, ts: trade.Timestamp - 1000)); // older than watermark

        var events = new List<CopyEvent>();
        engine.CopyDecided += events.Add;

        await engine.PollOnceAsync(LeaderAddr, CancellationToken.None);
        await engine.PollOnceAsync(LeaderAddr, CancellationToken.None);

        Assert.Single(events);                   // old trade filtered, fresh copied once
        Assert.Equal(2, _source.CallCount);
        Assert.Equal(CopyAction.BuyCopied, events[0].Action);
    }

    [Fact]
    public async Task PollOnce_DisabledLeader_NotPolled()
    {
        _leaders[0].Enabled = false;
        var engine = NewEngine();
        _source.Trades.Add(Buy(1m, ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        await engine.PollOnceAsync(LeaderAddr, CancellationToken.None);

        Assert.Equal(0, _source.CallCount);
    }

    [Fact]
    public async Task PollOnce_EmitsLeaderTradeSeen()
    {
        var engine = NewEngine();
        _source.Trades.Add(Buy(1m, ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Trade? seen = null;
        engine.LeaderTradeSeen += (t, _) => seen = t;

        await engine.PollOnceAsync(LeaderAddr, CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal(Mint, seen!.Mint);
    }

    [Fact]
    public void StartStop_FlagsAndStatus()
    {
        var engine = NewEngine();
        var statuses = new List<bool>();
        engine.StatusChanged += statuses.Add;

        engine.Start();
        Assert.True(engine.IsRunning);
        engine.Stop();
        Assert.False(engine.IsRunning);
        Assert.Equal(new[] { true, false }, statuses);
    }
}
