using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Engine;

// polls leader activity from ITradeSource, filters, sizes and mirrors trades
// through ITradeExecutor. emits events for the UI feed.
public class CopyEngine
{
    private const int ActivityLimit = 25;
    private const int InitialWatermarkWindowSec = 30;
    private const int DedupeCap = 100_000;

    private readonly ITradeSource _source;
    private readonly IPriceSource _prices;
    private readonly ITradeExecutor _executor;
    private readonly Func<AppConfig> _config;
    private readonly Func<IReadOnlyList<Leader>> _leaders;

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _watermarks = new();     // leader -> last processed ts
    private readonly HashSet<string> _seen = new();                    // dedupe keys
    private readonly Queue<string> _seenOrder = new();

    private CancellationTokenSource? _cts;
    private volatile bool _running;
    private decimal _cachedSolUsd;
    private DateTime _solUsdFetchedAt = DateTime.MinValue;

    public CopyEngine(ITradeSource source, IPriceSource prices, ITradeExecutor executor,
        Func<AppConfig> config, Func<IReadOnlyList<Leader>> leaders)
    {
        _source = source;
        _prices = prices;
        _executor = executor;
        _config = config;
        _leaders = leaders;
    }

    public bool IsRunning => _running;

    // raw leader trade appeared (before any filtering) — UI feed
    public event Action<Trade, string>? LeaderTradeSeen;   // (trade, leaderDisplay)
    // our reaction: copied or skipped with reason
    public event Action<CopyEvent>? CopyDecided;
    public event Action<bool>? StatusChanged;

    public void Start()
    {
        if (_running) return;
        _cts = new CancellationTokenSource();
        _running = true;

        var leaders = _leaders().Where(l => l.Enabled).ToList();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_gate)
        {
            foreach (var l in leaders)
                _watermarks[l.Address] = nowUnix - InitialWatermarkWindowSec;
        }

        var i = 0;
        foreach (var leader in leaders)
        {
            var staggerMs = 400 * i++;
            _ = Task.Run(() => PollLoopAsync(leader.Address, staggerMs, _cts.Token));
        }

        Log.Info($"copy engine started: {leaders.Count} leader(s), source={_source.GetType().Name}, executor={_executor.GetType().Name}");
        StatusChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();
        Log.Info("copy engine stopped");
        StatusChanged?.Invoke(false);
    }

    private async Task PollLoopAsync(string leaderAddress, int staggerMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(staggerMs, ct);
            var pollSec = Math.Max(2, _config().Connection.ActivityPollSec);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSec));
            do
            {
                await PollOnceAsync(leaderAddress, ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            Log.Error($"poll loop crashed for {Leader.Short(leaderAddress)}", ex);
        }
    }

    internal async Task PollOnceAsync(string leaderAddress, CancellationToken ct)
    {
        var leader = _leaders().FirstOrDefault(l => l.Address == leaderAddress);
        if (leader == null || !leader.Enabled) return;

        lock (_gate)
        {
            // lazy init so leaders added mid-run don't replay old history
            if (!_watermarks.ContainsKey(leaderAddress))
                _watermarks[leaderAddress] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - InitialWatermarkWindowSec;
        }

        var solUsd = await GetSolUsdCachedAsync(ct);
        IReadOnlyList<Trade> activity;
        try
        {
            activity = await _source.GetWalletActivityAsync(leaderAddress, ActivityLimit, solUsd, ct);
        }
        catch (Exception ex)
        {
            Log.Error($"activity poll failed for {leader.Display}", ex);
            return;
        }

        // chronological order, then watermark + dedupe
        var fresh = new List<Trade>();
        lock (_gate)
        {
            var watermark = _watermarks.TryGetValue(leaderAddress, out var w) ? w : 0;
            foreach (var t in activity.OrderBy(t => t.Timestamp))
            {
                if (t.Timestamp < watermark) continue;
                if (!_seen.Add(t.DedupeKey)) continue;
                _seenOrder.Enqueue(t.DedupeKey);
                fresh.Add(t);
            }
            if (fresh.Count > 0)
                _watermarks[leaderAddress] = fresh[^1].Timestamp;
            PruneSeen();
        }

        foreach (var trade in fresh)
        {
            LeaderTradeSeen?.Invoke(trade, leader.Display);
            var ev = await ProcessLeaderTradeAsync(leader, trade, ct);
            CopyDecided?.Invoke(ev);
        }
    }

    // full copy decision pipeline for one leader trade; public-ish for tests
    internal async Task<CopyEvent> ProcessLeaderTradeAsync(Leader leader, Trade trade, CancellationToken ct)
    {
        var cfg = _config();
        var t = cfg.Trading;

        // --- filters ---
        // min leader sol applies to buys only: a leader trimming in small
        // chunks must still trigger our proportional exit
        if (trade.Side == TradeSide.Buy)
        {
            var minSol = leader.MinSol > 0 ? leader.MinSol : t.MinLeaderSol;
            if (trade.SolAmount < minSol)
                return Skip(leader, trade, $"leader size {trade.SolAmount:F3} < min {minSol:F3} SOL");
        }

        if (t.SkipMints.Contains(trade.Mint))
            return Skip(leader, trade, "mint is in skip list");

        if (trade.Side == TradeSide.Buy)
            return await CopyBuyAsync(leader, trade, t, ct);
        return await CopySellAsync(leader, trade, t, ct);
    }

    private async Task<CopyEvent> CopyBuyAsync(Leader leader, Trade trade, TradingConfig t, CancellationToken ct)
    {
        var broker = _executor;
        if (!broker.HasOpenPosition(trade.Mint) && broker.OpenPositionCount >= t.MaxOpenPositions)
            return Skip(leader, trade, $"max open positions ({t.MaxOpenPositions})");

        var size = t.SizeMode == SizeMode.Fixed
            ? t.FixedSizeSol
            : trade.SolAmount * leader.Multiplier;
        if (size > t.MaxOurSolPerTrade) size = t.MaxOurSolPerTrade;
        if (size > broker.BalanceSol) size = broker.BalanceSol;
        if (size < 0.0005m)
            return Skip(leader, trade, $"size too small ({size:F4} SOL, balance {broker.BalanceSol:F4})");

        await RandomDelayAsync(t, ct);

        var priceUsd = await FreshPriceAsync(trade, ct);
        var solUsd = await GetSolUsdCachedAsync(ct);
        var result = broker.Buy(new BuyOrder
        {
            Mint = trade.Mint,
            Symbol = trade.Symbol,
            SolToSpend = size,
            PriceUsd = priceUsd,
            SolUsd = solUsd,
            SlippagePercent = t.SlippagePercent,
            FeePercent = t.FeePercent,
            LeaderAddress = leader.Address,
            LeaderTxHash = trade.TxHash
        });

        if (!result.Ok)
            return Skip(leader, trade, $"buy failed: {result.Error}");

        var mode = t.SizeMode == SizeMode.Fixed ? "fixed" : $"x{leader.Multiplier:0.##} prop";
        Log.Info($"copied BUY {leader.Display} -> {trade.Symbol} {size:F4} SOL ({mode})");
        return new CopyEvent
        {
            LeaderTrade = trade,
            LeaderDisplay = leader.Display,
            Action = CopyAction.BuyCopied,
            Detail = $"{size:F4} SOL ({mode})",
            OurFill = result.Fill
        };
    }

    private async Task<CopyEvent> CopySellAsync(Leader leader, Trade trade, TradingConfig t, CancellationToken ct)
    {
        if (!_executor.HasOpenPosition(trade.Mint))
            return Skip(leader, trade, "sell ignored, no open position");

        await RandomDelayAsync(t, ct);

        var priceUsd = await FreshPriceAsync(trade, ct);
        var solUsd = await GetSolUsdCachedAsync(ct);
        var result = _executor.Sell(new SellOrder
        {
            Mint = trade.Mint,
            Fraction = 1m,
            PriceUsd = priceUsd,
            SolUsd = solUsd,
            SlippagePercent = t.SlippagePercent,
            FeePercent = t.FeePercent,
            LeaderAddress = leader.Address,
            LeaderTxHash = trade.TxHash
        });

        if (!result.Ok)
            return Skip(leader, trade, $"sell failed: {result.Error}");

        Log.Info($"copied SELL {leader.Display} -> {trade.Symbol} full exit, realized {result.Fill!.RealizedSol:F4} SOL");
        return new CopyEvent
        {
            LeaderTrade = trade,
            LeaderDisplay = leader.Display,
            Action = CopyAction.SellCopied,
            Detail = `$"full exit, realized {result.Fill.RealizedSol:F4} SOL",
            OurFill = result.Fill
        };
    }

    private CopyEvent Skip(Leader leader, Trade trade, string reason)
    {
        Log.Debug($"skip {trade.Side} {trade.Symbol} by {leader.Display}: {reason}");
        return new CopyEvent
        {
            LeaderTrade = trade,
            LeaderDisplay = leader.Display,
            Action = CopyAction.Skipped,
            Detail = reason
        };
    }

    private async Task<decimal> FreshPriceAsync(Trade trade, CancellationToken ct)
    {
        try
        {
            var p = await _prices.GetTokenPriceUsdAsync(trade.Mint, ct);
            if (p is > 0) return p.Value;
        }
        catch (Exception ex)
        {
            Log.Warn($"price fetch failed for {trade.Symbol}, using trade price: {ex.Message}");
        }
        return trade.PriceUsd > 0 ? trade.PriceUsd : 0.000001m;
    }

    private async Task<decimal> GetSolUsdCachedAsync(CancellationToken ct)
    {
        if (_cachedSolUsd > 0 && DateTime.UtcNow - _solUsdFetchedAt < TimeSpan.FromSeconds(30))
            return _cachedSolUsd;
        try
        {
            var s = await _prices.GetSolUsdAsync(ct);
            if (s > 0)
            {
                _cachedSolUsd = s;
                _solUsdFetchedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sol/usd fetch failed: {ex.Message}");
        }
        return _cachedSolUsd > 0 ? _cachedSolUsd : 150m;
    }

    private static async Task RandomDelayAsync(TradingConfig t, CancellationToken ct)
    {
        var min = Math.Max(0, t.DelayMinMs);
        var max = Math.Max(min, t.DelayMaxMs);
        if (max <= 0) return;
        await Task.Delay(Random.Shared.Next(min, max + 1), ct);
    }

    private void PruneSeen()
    {
        while (_seen.Count > DedupeCap && _seenOrder.Count > 0)
            _seen.Remove(_seenOrder.Dequeue());
    }

    // tests need a way to clear state between runs
    internal void ResetState()
    {
        lock (_gate)
        {
            _watermarks.Clear();
            _seen.Clear();
            _seenOrder.Clear();
            _cachedSolUsd = 0;
        }
    }
}
