using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Engine;

// polls prices for open positions and marks them on the broker,
// also keeps a fresh SOL/USD rate for the UI and conversions.
public class PositionTracker : IDisposable
{
    private readonly PaperBroker _broker;
    private readonly IPriceSource _prices;
    private readonly Func<int> _pollSec;
    private CancellationTokenSource? _cts;

    private decimal _solUsd = 150m;

    public PositionTracker(PaperBroker broker, IPriceSource prices, Func<int>? pollSec = null)
    {
        _broker = broker;
        _prices = prices;
        _pollSec = pollSec ?? (() => 5);
    }

    public decimal SolUsd => _solUsd;
    public bool IsRunning { get; private set; }

    public event Action? PricesUpdated;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = Task.Run(() => LoopAsync(_cts.Token));
        Log.Info("position tracker started");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        Log.Info("position tracker stopped");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var sec = Math.Max(2, _pollSec());
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(sec));
                await TickAsync(ct);
                while (await timer.WaitForNextTickAsync(ct))
                {
                    await TickAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            Log.Error("position tracker crashed", ex);
            IsRunning = false;
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var sol = await _prices.GetSolUsdAsync(ct);
            if (sol > 0) _solUsd = sol;

            var marked = 0;
            // tried Parallel.ForEachAsync here, burned the rate limit in
            // seconds on 12 mints, keeping serial
            foreach (var pos in _broker.OpenPositions)
            {
                var price = await _prices.GetTokenPriceUsdAsync(pos.Mint, ct);
                if (price is > 0)
                {
                    _broker.MarkPrice(pos.Mint, price.Value, _solUsd);
                    marked++;
                }
            }
            if (marked > 0)
            {
                Log.Debug($"marked {marked} position(s), sol/usd {_solUsd:F2}");
                PricesUpdated?.Invoke();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // a dead price source must not kill the loop
            Log.Warn($"price tick failed: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
