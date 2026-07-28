using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Engine;

public interface ITradeExecutor
{
    decimal BalanceSol { get; }
    int OpenPositionCount { get; }
    bool HasOpenPosition(string mint);
    ExecResult Buy(BuyOrder order);
    ExecResult Sell(SellOrder order);
}

public class BuyOrder
{
    public string Mint { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal SolToSpend { get; set; }
    public decimal PriceUsd { get; set; }      // market price before slippage
    public decimal SolUsd { get; set; }
    public decimal SlippagePercent { get; set; }
    public decimal FeePercent { get; set; }
    public string LeaderAddress { get; set; } = "";
    public string LeaderTxHash { get; set; } = "";
}

public class SellOrder
{
    public string Mint { get; set; } = "";
    public decimal Fraction { get; set; } = 1m;  // 1 = full exit, 0.3 = trim 30% of bag
    public decimal PriceUsd { get; set; }
    public decimal SolUsd { get; set; }
    public decimal SlippagePercent { get; set; }
    public decimal FeePercent { get; set; }
    public string LeaderAddress { get; set; } = "";
    public string LeaderTxHash { get; set; } = "";
}

public class ExecResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public Fill? Fill { get; init; }
    public decimal BalanceSol { get; init; }

    public static ExecResult Fail(string error, decimal balance) => new() { Ok = false, Error = error, BalanceSol = balance };
}

// simulated wallet + swap execution. prices come from the source, fills happen
// instantly at price +/- slippage minus fee. no chain interaction in v0.1.
// TODO: flat percent fee is a placeholder, real swaps pay curve fee + priority,
// pull real numbers from jup quotes when a live executor lands
public class PaperBroker : ITradeExecutor
{
    public const decimal DustQty = 0.000001m;

    private readonly object _gate = new();
    private readonly Dictionary<string, Position> _positions = new();
    private readonly List<Fill> _fills = new();
    private decimal _balanceSol;
    private decimal _startBalanceSol;
    private long _nextFillId = 1;

    public PaperBroker(decimal startBalanceSol)
    {
        _balanceSol = startBalanceSol;
        _startBalanceSol = startBalanceSol;
    }

    public decimal BalanceSol { get { lock (_gate) return _balanceSol; } }

    public IReadOnlyList<Position> Positions { get { lock (_gate) return _positions.Values.ToList(); } }

    public IReadOnlyList<Position> OpenPositions
    {
        get { lock (_gate) return _positions.Values.Where(p => p.IsOpen).ToList(); }
    }

    public IReadOnlyList<Fill> Fills { get { lock (_gate) return _fills.ToList(); } }

    public int OpenPositionCount { get { lock (_gate) return _positions.Values.Count(p => p.IsOpen); } }

    public bool HasOpenPosition(string mint)
    {
        lock (_gate) return _positions.TryGetValue(mint, out var p) && p.IsOpen;
    }

    public Position? GetPosition(string mint)
    {
        lock (_gate) return _positions.TryGetValue(mint, out var p) ? p : null;
    }

    // wallet tab reset button
    public void Reset(decimal balanceSol)
    {
        lock (_gate)
        {
            _balanceSol = balanceSol;
            _startBalanceSol = balanceSol;
            _positions.Clear();
            _fills.Clear();
            _nextFillId = 1;
        }
        Log.Info($"paper broker reset, balance {balanceSol} SOL");
    }

    public ExecResult Buy(BuyOrder o)
    {
        lock (_gate)
        {
            if (o.PriceUsd <= 0m || o.SolUsd <= 0m)
                return ExecResult.Fail("invalid price", _balanceSol);
            if (o.SolToSpend <= 0m)
                return ExecResult.Fail("zero size", _balanceSol);
            if (o.SolToSpend > _balanceSol)
                return ExecResult.Fail($"insufficient balance ({_balanceSol:F4} SOL)", _balanceSol);

            var execPriceUsd = o.PriceUsd * (1m + o.SlippagePercent / 100m);
            var feeSol = Round(o.SolToSpend * o.FeePercent / 100m, 9);
            var netSol = o.SolToSpend - feeSol;
            var usdSpent = netSol * o.SolUsd;
            var tokens = execPriceUsd > 0 ? usdSpent / execPriceUsd : 0m;
            if (tokens <= DustQty)
                return ExecResult.Fail("fill too small", _balanceSol);

            _balanceSol -= o.SolToSpend;

            if (!_positions.TryGetValue(o.Mint, out var pos))
            {
                pos = new Position { Mint = o.Mint, Symbol = o.Symbol, OpenedAtUtc = DateTime.UtcNow };
                _positions[o.Mint] = pos;
            }
            pos.Quantity += tokens;
            pos.CostSol += o.SolToSpend;
            pos.CostUsd += usdSpent;
            pos.LastPriceUsd = execPriceUsd;
            pos.LastSolUsd = o.SolUsd;

            var fill = NewFill(o.Mint, o.Symbol, TradeSide.Buy, o.SolToSpend, tokens, execPriceUsd, o.SolUsd, feeSol, 0m, o.LeaderAddress, o.LeaderTxHash);
            Log.Info($"paper BUY {pos.Symbol} {tokens:F2} tok for {o.SolToSpend:F4} SOL @ {execPriceUsd:G8} (fee {feeSol:F6})");
            return new ExecResult { Ok = true, Fill = fill, BalanceSol = _balanceSol };
        }
    }

    public ExecResult Sell(SellOrder o)
    {
        lock (_gate)
        {
            if (o.PriceUsd <= 0m || o.SolUsd <= 0m)
                return ExecResult.Fail("invalid price", _balanceSol);
            if (!_positions.TryGetValue(o.Mint, out var pos) || !pos.IsOpen)
                return ExecResult.Fail("no open position", _balanceSol);

            var fraction = Math.Clamp(o.Fraction, 0m, 1m);
            var fullExit = fraction >= 1m - DustQty;
            var qtyToSell = fullExit ? pos.Quantity : Round(pos.Quantity * fraction, 9);
            if (qtyToSell <= DustQty)
                return ExecResult.Fail("nothing to sell (dust)", _balanceSol);

            var execPriceUsd = o.PriceUsd * (1m - o.SlippagePercent / 100m);
            var usdGross = qtyToSell * execPriceUsd;
            var solGross = usdGross / o.SolUsd;
            var feeSol = Round(solGross * o.FeePercent / 100m, 9);
            var solReceived = solGross - feeSol;
            if (solReceived <= 0m)
                return ExecResult.Fail("fee eats the fill", _balanceSol);

            // proportional cost basis for the sold slice
            var ratio = qtyToSell / pos.Quantity;
            var basisUsd = fullExit ? pos.CostUsd : Round(pos.CostUsd * ratio, 10);
            var basisSol = fullExit ? pos.CostSol : Round(pos.CostSol * ratio, 10);
            var realizedUsd = usdGross - basisUsd;
            var realizedSol = realizedUsd / o.SolUsd;

            _balanceSol += solReceived;
            pos.Quantity -= qtyToSell;
            pos.CostUsd -= basisUsd;
            pos.CostSol -= basisSol;
            pos.RealizedSol += realizedSol;
            pos.LastPriceUsd = execPriceUsd;
            pos.LastSolUsd = o.SolUsd;
            if (pos.Quantity <= DustQty)
            {
                pos.Quantity = 0m;
                pos.CostUsd = 0m;
                pos.CostSol = 0m;
            }

            var fill = NewFill(o.Mint, pos.Symbol, TradeSide.Sell, solReceived, qtyToSell, execPriceUsd, o.SolUsd, feeSol, realizedSol, o.LeaderAddress, o.LeaderTxHash);
            Log.Info($"paper SELL {pos.Symbol} {qtyToSell:F2} tok for {solReceived:F4} SOL, realized {realizedSol:F4} SOL, {(pos.IsOpen ? "bag left" : "position closed")}");
            return new ExecResult { Ok = true, Fill = fill, BalanceSol = _balanceSol };
        }
    }

    // called by the position tracker on every price update
    public void MarkPrice(string mint, decimal priceUsd, decimal solUsd)
    {
        lock (_gate)
        {
            if (_positions.TryGetValue(mint, out var pos) && pos.IsOpen)
            {
                pos.LastPriceUsd = priceUsd;
                pos.LastSolUsd = solUsd;
            }
        }
    }

    private Fill NewFill(string mint, string symbol, TradeSide side, decimal sol, decimal tokens,
        decimal priceUsd, decimal solUsd, decimal feeSol, decimal realizedSol, string leader, string leaderTx)
    {
        var fill = new Fill
        {
            Id = _nextFillId++,
            TimeUtc = DateTime.UtcNow,
            Mint = mint,
            Symbol = symbol,
            Side = side,
            SolAmount = Round(sol, 9),
            TokenAmount = Round(tokens, 9),
            PriceUsd = priceUsd,
            SolUsd = solUsd,
            FeeSol = feeSol,
            RealizedSol = Round(realizedSol, 9),
            LeaderAddress = leader,
            LeaderTxHash = leaderTx
        };
        _fills.Add(fill);
        return fill;
    }

    private static decimal Round(decimal d, int digits) => Math.Round(d, digits, MidpointRounding.AwayFromZero);
}
