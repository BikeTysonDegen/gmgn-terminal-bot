using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Tests;

public class SessionStatsTests
{
    private static CopyEvent Ev(CopyAction action, decimal realized = 0m) => new()
    {
        LeaderTrade = new Trade(),
        Action = action,
        OurFill = action == CopyAction.Skipped ? null : new Fill { RealizedSol = realized }
    };

    [Fact]
    public void Apply_CountsAllKinds()
    {
        var s = new SessionStats();

        s.OnLeaderTrade();
        s.Apply(Ev(CopyAction.BuyCopied));
        s.Apply(Ev(CopyAction.SellCopied, realized: 0.5m));
        s.Apply(Ev(CopyAction.SellCopied, realized: -0.2m));
        s.Apply(Ev(CopyAction.Skipped));

        Assert.Equal(1, s.LeaderTrades);
        Assert.Equal(1, s.BuysCopied);
        Assert.Equal(2, s.SellsCopied);
        Assert.Equal(1, s.Skipped);
        Assert.Equal(0.3m, s.RealizedSol);
        Assert.Equal(1, s.Wins);
        Assert.Equal(1, s.Losses);
        Assert.Equal(50.0, s.WinRatePct);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var s = new SessionStats();
        s.OnLeaderTrade();
        s.Apply(Ev(CopyAction.BuyCopied));

        s.Reset();

        Assert.Equal(0, s.LeaderTrades);
        Assert.Equal(0, s.BuysCopied);
        Assert.Equal(0m, s.RealizedSol);
    }

    [Fact]
    public void PropertyChanged_FiresForUi()
    {
        var s = new SessionStats();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.Apply(Ev(CopyAction.BuyCopied));

        Assert.Contains(nameof(SessionStats.BuysCopied), raised);
        Assert.Contains(nameof(SessionStats.WinRatePct), raised);
    }
}

public class PositionTrackerTests
{
    [Fact]
    public async Task Tick_MarksOpenPositions_AndUpdatesSolUsd()
    {
        var broker = new PaperBroker(10m);
        broker.Buy(new BuyOrder { Mint = "mint1", Symbol = "T1", SolToSpend = 1m, PriceUsd = 0.001m, SolUsd = 150m });
        var prices = new StubPriceSource { TokenPriceUsd = 0.002m, SolUsd = 160m };
        var updates = 0;
        using var tracker = new PositionTracker(broker, prices);
        tracker.PricesUpdated += () => updates++;

        await tracker.TickAsync(CancellationToken.None);

        var pos = broker.GetPosition("mint1")!;
        Assert.Equal(0.002m, pos.LastPriceUsd);
        Assert.Equal(160m, pos.LastSolUsd);
        Assert.Equal(160m, tracker.SolUsd);
        Assert.Equal(1, updates);
        Assert.Equal(300m, pos.ValueUsd); // 150000 * 0.002
    }

    [Fact]
    public async Task Tick_SurvivesPriceSourceFailure()
    {
        var broker = new PaperBroker(10m);
        broker.Buy(new BuyOrder { Mint = "mint1", Symbol = "T1", SolToSpend = 1m, PriceUsd = 0.001m, SolUsd = 150m });
        var prices = new ThrowingPriceSource();
        using var tracker = new PositionTracker(broker, prices);

        await tracker.TickAsync(CancellationToken.None); // must not throw

        Assert.Equal(150m, tracker.SolUsd); // keeps previous rate
    }

    private class ThrowingPriceSource : Sources.IPriceSource
    {
        public Task<decimal?> GetTokenPriceUsdAsync(string mint, CancellationToken ct = default) =>
            throw new HttpRequestException("cloudflare");
        public Task<decimal> GetSolUsdAsync(CancellationToken ct = default) =>
            throw new HttpRequestException("cloudflare");
    }
}

public class BotHostTests
{
    [Fact]
    public void OfflineFeed_WiresOfflineFeed_AndStartStopWorks()
    {
        var cfg = new AppConfig();
        cfg.Connection.OfflineFeed = true;
        cfg.Connection.ActivityPollSec = 2;
        cfg.Connection.PricePollSec = 2;
        cfg.Leaders.Add(new Leader { Address = "TestLeader1111111111111111111111111111111", Alias = "test whale" });

        using var host = new BotHost(() => cfg);

        Assert.True(host.IsOffline);
        Assert.Equal(10m, host.Broker.BalanceSol);

        host.Start();
        Assert.True(host.IsRunning);
        host.Stop();
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Rebuild_KeepsRunningState()
    {
        var cfg = new AppConfig();
        cfg.Connection.OfflineFeed = true;
        using var host = new BotHost(() => cfg);
        host.Start();

        cfg.Wallet.PaperBalanceSol = 5m;
        host.Rebuild(cfg);

        Assert.True(host.IsRunning);
        Assert.Equal(5m, host.Broker.BalanceSol);
        host.Stop();
    }

    [Fact]
    public async Task EndToEnd_OfflineFeed_CopiesTrades()
    {
        var cfg = new AppConfig();
        cfg.Connection.OfflineFeed = true;
        cfg.Connection.ActivityPollSec = 2;
        cfg.Trading.DelayMinMs = 0;
        cfg.Trading.DelayMaxMs = 0;
        cfg.Trading.MinLeaderSol = 0.01m;
        cfg.Trading.FixedSizeSol = 0.1m;
        var leader = new Leader { Address = "E2ELeader11111111111111111111111111111111", Alias = "e2e" };
        cfg.Leaders.Add(leader);

        using var host = new BotHost(() => cfg);
        var events = new List<CopyEvent>();
        host.CopyDecided += e => { lock (events) events.Add(e); };

        host.Start();
        // offline feed generates trades every poll; give it a few cycles
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            lock (events) if (events.Count > 0) break;
            await Task.Delay(250);
        }
        host.Stop();

        lock (events)
        {
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.Action == CopyAction.BuyCopied || e.Action == CopyAction.Skipped);
        }
        Assert.True(host.Stats.LeaderTrades > 0);
    }
}
