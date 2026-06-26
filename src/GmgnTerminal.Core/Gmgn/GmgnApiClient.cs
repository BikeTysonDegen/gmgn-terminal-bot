using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Gmgn;

// REST client for gmgn.ai public endpoints (Solana).
//
// Reality check: most gmgn endpoints sit behind Cloudflare with a browser TLS
// fingerprint check. Plain HttpClient gets through on some paths (rank,
// token_info, token_stat) and receives a challenge page on others
// (wallet_activity, quotation/*). The client never throws on that — it logs,
// sets LastError and returns null/empty, and the engine falls back to the offline feed
// data. Every request and response is logged (bodies truncated).
public class GmgnApiClient : IDisposable, ITradeSource, IPriceSource
{
    public const string SolMint = "So11111111111111111111111111111111111111112";
    public const decimal SolUsdFallback = 150m;

    private readonly HttpClient _http;
    private readonly GmgnApiOptions _opts;
    private decimal _lastKnownSolUsd;

    public GmgnApiClient(GmgnApiOptions opts, HttpMessageHandler? handler = null)
    {
        _opts = opts;
        _http = new HttpClient(handler ?? CreateHandler(opts), disposeHandler: handler == null)
        {
            Timeout = TimeSpan.FromSeconds(opts.TimeoutSec)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://gmgn.ai/?chain=sol");
    }

    // last failure reason, shown in the UI (Connection tab test button)
    public string? LastError { get; private set; }

    private static HttpMessageHandler CreateHandler(GmgnApiOptions opts)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        if (!string.IsNullOrWhiteSpace(opts.Proxy))
        {
            // http(s) proxies only; socks5 is not supported by WebProxy
            handler.Proxy = new WebProxy(opts.Proxy);
            handler.UseProxy = true;
            Log.Info($"gmgn client using proxy {StripCreds(opts.Proxy)}");
        }
        return handler;
    }

    internal static string StripCreds(string proxyUrl)
    {
        try
        {
            var u = new Uri(proxyUrl);
            return string.IsNullOrEmpty(u.UserInfo) ? proxyUrl : $"{u.Scheme}://***@{u.Host}:{u.Port}";
        }
        catch { return "<invalid proxy url>"; }
    }

    public Task<TokenInfo?> GetTokenInfoAsync(string mint, CancellationToken ct = default) =>
        QueryAsync($"/api/v1/token_info/sol/{mint}", GmgnMapper.MapTokenInfo, ct);

    public async Task<decimal?> GetTokenPriceUsdAsync(string mint, CancellationToken ct = default)
    {
        // QueryAsync needs a reference type, box the decimal
        var box = await QueryAsync($"/defi/quotation/v1/sol/tokens/realtime_token_price?address={mint}",
            data =>
            {
                var p = GmgnMapper.MapRealtimePrice(data, mint);
                return p.HasValue ? new PriceBox { Price = p.Value } : null;
            }, ct);
        return box?.Price;
    }

    private class PriceBox
    {
        public decimal Price { get; set; }
    }

    // SOL/USD via the same realtime endpoint; the quotation API is often
    // cloudflare-blocked, so keep the last good value and fall back to a
    // constant. positions are tracked in SOL, we need this for conversions.
    public async Task<decimal> GetSolUsdAsync(CancellationToken ct = default)
    {
        var p = await GetTokenPriceUsdAsync(SolMint, ct);
        if (p is > 0) _lastKnownSolUsd = p.Value;
        return _lastKnownSolUsd > 0 ? _lastKnownSolUsd : SolUsdFallback;
    }

    public async Task<IReadOnlyList<TokenInfo>> GetTrendingAsync(string period, int limit, CancellationToken ct = default) =>
        await QueryAsync($"/defi/quotation/v1/rank/sol/swaps/{period}?orderby=swaps&direction=desc&limit={limit}",
            GmgnMapper.MapRank, ct) ?? new List<TokenInfo>();

    public async Task<IReadOnlyList<Trade>> GetWalletActivityAsync(string wallet, int limit, decimal solUsdFallback, CancellationToken ct = default) =>
        await QueryAsync($"/api/v1/wallet_activity/sol?type=sell&type=buy&wallet={wallet}&limit={limit}&cost=10",
            data => GmgnMapper.MapWalletActivity(data, wallet, solUsdFallback), ct) ?? new List<Trade>();

    public async Task<IReadOnlyList<Trade>> GetTokenTradesAsync(string mint, int limit, decimal solUsdFallback, CancellationToken ct = default) =>
        await QueryAsync($"/defi/quotation/v1/trades/sol/{mint}?limit={limit}",
            data => GmgnMapper.MapTrades(data, mint, solUsdFallback), ct) ?? new List<Trade>();

    // for the Connection tab button: hits a lightweight known-good endpoint
    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        var tokens = await GetTrendingAsync("1h", 1, ct);
        if (tokens.Count > 0)
        {
            LastError = null;
            return $"ok — rank returned {tokens.Count} token(s), top: {tokens[0].Symbol}";
        }
        return $"failed — {LastError ?? "no data"}";
    }

    private async Task<T?> QueryAsync<T>(string pathAndQuery, Func<JsonElement, T?> map, CancellationToken ct)
        where T : class
    {
        var url = _opts.BaseUrl.TrimEnd('/') + pathAndQuery;
        Log.Debug($"gmgn GET {url}");
        LastError = null;
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Log.Debug($"gmgn <- {(int)resp.StatusCode} {resp.StatusCode} {url} ({body.Length}b) {Trunc(body)}");

            // CF challenges usually come as 403/503 + html — check html first, it is more informative
            if (body.TrimStart().StartsWith('<'))
            {
                LastError = "cloudflare challenge (html instead of json)";
                Log.Warn($"gmgn blocked by cloudflare challenge: {url} (http {(int)resp.StatusCode})");
                return null;
            }

            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"HTTP {(int)resp.StatusCode}";
                Log.Warn($"gmgn request failed: {url} -> {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!GmgnEnvelope.TryGetData(doc.RootElement, out var data, out var error))
            {
                LastError = error;
                Log.Warn($"gmgn api error for {url}: {error} | body: {Trunc(body, 800)}");
                return null;
            }

            return map(data);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LastError = $"timeout after {_opts.TimeoutSec}s";
            Log.Warn($"gmgn request timed out: {url}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = ex.Message;
            Log.Error($"gmgn request error: {url}", ex);
            return null;
        }
        catch (JsonException ex)
        {
            LastError = "invalid json response";
            Log.Error($"gmgn returned unparsable json: {url}", ex);
            return null;
        }
    }

    private static string Trunc(string s, int max = 400)
    {
        var oneLine = s.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "...";
    }

    public void Dispose() => _http.Dispose();
}
