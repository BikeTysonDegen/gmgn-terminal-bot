using System.Security.Cryptography;
using System.Text;
using GmgnTerminal.Core.Models;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Offline;

public class OfflineFeedOptions
{
    public int Seed { get; set; } = 42;
    public int TokenCount { get; set; } = 8;
    public decimal StartSolUsd { get; set; } = 150m;
    public int MaxTradesPerPoll { get; set; } = 3;
    public double TradeChancePerPoll { get; set; } = 0.8; // 0..1, per poll per wallet
}

// offline generator that mimics the gmgn data surface: a catalog of fake
// pump-style tokens with random-walking prices and synthetic leader activity.
// deterministic per (seed, wallet) so tests can pin behavior.
public class OfflineFeed : ITradeSource, IPriceSource
{
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    // fallback catalog: real solana blue-chip mints, used when the live
    // trending fetch at startup didn't work out
    private static readonly (string Symbol, string Name, string Mint, decimal Price)[] RealTokens =
    {
        ("BONK", "Bonk", "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263", 0.000022m),
        ("WIF", "dogwifhat", "EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm", 0.85m),
        ("POPCAT", "Popcat", "7GCihgDB8fe6KNjn2MYtkzZcRjQy3t9GHdC8uHYmW2hr", 0.32m),
        ("JUP", "Jupiter", "JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN", 0.42m),
        ("PYTH", "Pyth Network", "HZ1JovNiWvGrGNiiYvEozEVgZ58xaU3RKwX8eACQBCt3", 0.28m),
        ("RAY", "Raydium", "4k3Dyjzvzp8eMZWUXbBCjEvwSkkk59S5iCNLY3Qrk", 2.40m)
    };

    private readonly OfflineFeedOptions _opts;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, Random> _walletRng = new();
    private readonly Dictionary<string, FeedToken> _tokens = new();
    private readonly object _gate = new();
    private readonly Random _globalRng;
    private decimal _solUsd;
    private long _txCounter;

    private class FeedToken
    {
        public string Mint = "";
        public string Symbol = "";
        public string Name = "";
        public decimal PriceUsd;
        public double Momentum; // >0 buys more likely, <0 sells
    }

    public OfflineFeed(OfflineFeedOptions? opts = null, Func<DateTimeOffset>? clock = null,
        IReadOnlyList<TokenInfo>? seedTokens = null)
    {
        _opts = opts ?? new OfflineFeedOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _globalRng = new Random(_opts.Seed);
        _solUsd = _opts.StartSolUsd;

        if (seedTokens != null && seedTokens.Count > 0)
        {
            foreach (var t in seedTokens.Take(12))
            {
                var price = t.PriceUsd > 0 ? t.PriceUsd : 0.0001m;
                _tokens[t.Mint] = new FeedToken
                {
                    Mint = t.Mint,
                    Symbol = t.Symbol,
                    Name = t.Name,
                    PriceUsd = price,
                    Momentum = (_globalRng.NextDouble() - 0.45) * 0.4
                };
            }
        }
        else
        {
            foreach (var (symbol, name, mint, price) in RealTokens)
            {
                _tokens[mint] = new FeedToken
                {
                    Mint = mint,
                    Symbol = symbol,
                    Name = name,
                    PriceUsd = price,
                    Momentum = (_globalRng.NextDouble() - 0.45) * 0.4
                };
            }
        }
    }

    public IReadOnlyList<TokenInfo> TokenCatalog
    {
        get
        {
            lock (_gate)
            {
                return _tokens.Values.Select(t => new TokenInfo
                {
                    Mint = t.Mint,
                    Symbol = t.Symbol,
                    Name = t.Name,
                    PriceUsd = t.PriceUsd,
                    LiquidityUsd = 50000m,
                    MarketCapUsd = t.PriceUsd * 1_000_000_000m
                }).ToList();
            }
        }
    }

    public Task<IReadOnlyList<Trade>> GetWalletActivityAsync(string wallet, int limit, decimal solUsdFallback, CancellationToken ct = default)
    {
        List<Trade> trades;
        lock (_gate)
        {
            trades = new List<Trade>();
            var rng = WalletRng(wallet);

            // sol price random walk
            _solUsd = Math.Max(20m, _solUsd * (1m + (decimal)(rng.NextDouble() - 0.5) * 0.002m));

            if (rng.NextDouble() > _opts.TradeChancePerPoll)
                return Task.FromResult<IReadOnlyList<Trade>>(trades);

            var count = rng.Next(1, _opts.MaxTradesPerPoll + 1);
            var now = _clock().ToUnixTimeSeconds();
            for (var i = 0; i < count && trades.Count < limit; i++)
            {
                var token = _tokens.Values.ElementAt(rng.Next(_tokens.Count));
                WalkPrice(token, rng);

                // side: momentum-biased coin flip
                var buyProb = 0.5 + token.Momentum;
                var side = rng.NextDouble() < buyProb ? TradeSide.Buy : TradeSide.Sell;
                token.Momentum = Math.Clamp(token.Momentum + (side == TradeSide.Buy ? 0.05 : -0.07), -0.45, 0.45);

                // log-uniform sol size 0.02 .. 8
                var sol = Round4((decimal)Math.Exp(rng.NextDouble() * Math.Log(400) + Math.Log(0.02)));
                var usd = Round2(sol * _solUsd);
                var price = token.PriceUsd;
                var tokenAmount = price > 0 ? Round2(usd / price) : 0m;

                trades.Add(new Trade
                {
                    TxHash = FakeTxHash(rng),
                    Maker = wallet,
                    Mint = token.Mint,
                    Symbol = token.Symbol,
                    Side = side,
                    SolAmount = sol,
                    UsdAmount = usd,
                    TokenAmount = tokenAmount,
                    PriceUsd = price,
                    Timestamp = now - rng.Next(0, 5)
                });
                _txCounter++;
            }
        }
        return Task.FromResult<IReadOnlyList<Trade>>(trades);
    }

    public Task<decimal?> GetTokenPriceUsdAsync(string mint, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(mint, out var token))
                return Task.FromResult<decimal?>(null);
            WalkPrice(token, WalletRng("price-walker"));
            return Task.FromResult<decimal?>(token.PriceUsd);
        }
    }

    public Task<decimal> GetSolUsdAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_solUsd);
    }

    public long GeneratedTxCount { get { lock (_gate) return _txCounter; } }

    private void WalkPrice(FeedToken token, Random rng)
    {
        var drift = (double)token.Momentum * 0.01;
        var shock = (rng.NextDouble() - 0.5) * 0.06;
        var mult = 1m + (decimal)(drift + shock);
        if (mult < 0.2m) mult = 0.2m;
        token.PriceUsd = RoundPrice(token.PriceUsd * mult);
    }

    private Random WalletRng(string wallet)
    {
        if (!_walletRng.TryGetValue(wallet, out var rng))
        {
            // stable per-wallet seed (string.GetHashCode is process-randomized)
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{_opts.Seed}|{wallet}"));
            var seed = BitConverter.ToInt32(hash, 0);
            rng = new Random(seed);
            _walletRng[wallet] = rng;
        }
        return rng;
    }

    private static string FakeTxHash(Random rng)
    {
        var sb = new StringBuilder(64);
        for (var i = 0; i < 64; i++) sb.Append(Base58Alphabet[rng.Next(Base58Alphabet.Length)]);
        return sb.ToString();
    }

    private static decimal Round2(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal d) => Math.Round(d, 4, MidpointRounding.AwayFromZero);
    private static decimal RoundPrice(decimal d) => Math.Round(d, 10, MidpointRounding.AwayFromZero);
}
