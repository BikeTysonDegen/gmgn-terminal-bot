using System.Net;
using System.Text;
using System.Text.Json;
using GmgnTerminal.Core.Gmgn;

namespace GmgnTerminal.Core.Tests;

internal static class Fixtures
{
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", name));

    public static JsonElement Data(string name)
    {
        using var doc = JsonDocument.Parse(Load(name));
        if (!GmgnEnvelope.TryGetData(doc.RootElement, out var data, out var error))
            throw new InvalidDataException($"{name}: {error}");
        return data.Clone();
    }
}

internal class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<string> RequestedUrls { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public static StubHandler Returning(string body, HttpStatusCode code = HttpStatusCode.OK, string contentType = "application/json") =>
        new(_ => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, contentType) });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        return Task.FromResult(_responder(request));
    }
}

public class GmgnMapperTests
{
    [Fact]
    public void MapTokenInfo_LiveFixture()
    {
        var info = GmgnMapper.MapTokenInfo(Fixtures.Data("token_info.json"));

        Assert.NotNull(info);
        Assert.Equal("KNOB", info!.Symbol);
        Assert.Equal("Knob", info.Name);
        Assert.Equal("9F3CB3jZ3EFQdGDgpQWzGec2ZeWk9KLfVjw7F7hdSTNK", info.Mint);
        Assert.True(info.LiquidityUsd > 1000m);
    }

    [Fact]
    public void MapRank_LiveFixture()
    {
        var tokens = GmgnMapper.MapRank(Fixtures.Data("rank_swaps.json"));

        Assert.Equal(3, tokens.Count);
        Assert.Equal("KNOB", tokens[0].Symbol);
        Assert.True(tokens[0].PriceUsd > 0m);
        Assert.True(tokens[0].Volume24hUsd > 0m);
        Assert.All(tokens, t => Assert.False(string.IsNullOrEmpty(t.Mint)));
    }

    [Fact]
    public void MapWalletActivity_BuySellOnly_SkipsTransfers()
    {
        var wallet = "498g1rVnFcnjBjpfw1xyqA1WvgQXUU8RWuELjxkjAayQ";
        var trades = GmgnMapper.MapWalletActivity(Fixtures.Data("wallet_activity.sample.json"), wallet);

        Assert.Equal(2, trades.Count);
        var buy = trades[0];
        Assert.Equal(Models.TradeSide.Buy, buy.Side);
        Assert.Equal(wallet, buy.Maker);
        Assert.Equal("CeSFzoAqSMXMgodeLzrTMe3V5MdV5Nhinehedxohpump", buy.Mint);
        Assert.Equal("Humanity", buy.Symbol);
        Assert.Equal(0.85m, buy.SolAmount);
        Assert.Equal(127.5m, buy.UsdAmount);
        Assert.Equal(1523000.5m, buy.TokenAmount);
        Assert.Equal(1789363100, buy.Timestamp);
        Assert.Equal(Models.TradeSide.Sell, trades[1].Side);
    }

    [Fact]
    public void MapWalletActivity_UsesSolUsdFallback_WhenCostSolMissing()
    {
        var json = """
        [{"event_type":"buy","tx_hash":"tx1","token":{"address":"mint1","symbol":"T1"},
          "cost_usd":"300","token_amount":"1000","price_usd":"0.3","timestamp":1789363100}]
        """;
        using var doc = JsonDocument.Parse(json);

        var trades = GmgnMapper.MapWalletActivity(doc.RootElement, "walletX", solUsdFallback: 150m);

        Assert.Single(trades);
        Assert.Equal(2m, trades[0].SolAmount); // 300 usd / 150
    }

    [Fact]
    public void MapTrades_SampleFixture()
    {
        const string mint = "CeSFzoAqSMXMgodeLzrTMe3V5MdV5Nhinehedxohpump";
        var trades = GmgnMapper.MapTrades(Fixtures.Data("trades.sample.json"), mint);

        Assert.Equal(2, trades.Count);
        Assert.Equal(Models.TradeSide.Buy, trades[0].Side);
        Assert.Equal(mint, trades[0].Mint);
        Assert.Equal(1.25m, trades[0].SolAmount);
        Assert.Equal("7yZaBcDeFgHiJkLmNoPqRsTuVwXyZaBcDeFgHiJkLmNo", trades[0].Maker);
        Assert.Equal(Models.TradeSide.Sell, trades[1].Side);
    }

    [Fact]
    public void MapRealtimePrice_BothShapes()
    {
        using var direct = JsonDocument.Parse("""{"price":0.0000842}""");
        Assert.Equal(0.0000842m, GmgnMapper.MapRealtimePrice(direct.RootElement, "mintX"));

        using var keyed = JsonDocument.Parse("""{"mintX":{"price":"0.0001"}}""");
        Assert.Equal(0.0001m, GmgnMapper.MapRealtimePrice(keyed.RootElement, "mintX"));

        using var empty = JsonDocument.Parse("""{}""");
        Assert.Null(GmgnMapper.MapRealtimePrice(empty.RootElement, "mintX"));
    }

    [Fact]
    public void Envelope_NonZeroCode_IsError()
    {
        using var doc = JsonDocument.Parse("""{"code":40000300,"msg":"invalid argument","data":{}}""");
        Assert.False(GmgnEnvelope.TryGetData(doc.RootElement, out _, out var error));
        Assert.Contains("invalid argument", error);
    }
}

public class GmgnApiClientTests
{
    private static GmgnApiClient NewClient(StubHandler handler) =>
        new(new GmgnApiOptions { BaseUrl = "https://gmgn.ai", TimeoutSec = 5 }, handler);

    [Fact]
    public async Task GetTokenInfo_Success_MapsModel()
    {
        var handler = StubHandler.Returning(Fixtures.Load("token_info.json"));
        using var client = NewClient(handler);

        var info = await client.GetTokenInfoAsync("9F3CB3jZ3EFQdGDgpQWzGec2ZeWk9KLfVjw7F7hdSTNK");

        Assert.NotNull(info);
        Assert.Equal("KNOB", info!.Symbol);
        Assert.Contains("/api/v1/token_info/sol/", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task CloudflareChallenge_ReturnsNull_SetsLastError()
    {
        var handler = StubHandler.Returning("<!DOCTYPE html><html>Just a moment...</html>",
            HttpStatusCode.Forbidden, "text/html");
        using var client = NewClient(handler);

        var info = await client.GetTokenInfoAsync("somemint");

        Assert.Null(info);
        Assert.Contains("cloudflare", client.LastError);
    }

    [Fact]
    public async Task ApiErrorCode_ReturnsNull_LogsReason()
    {
        var handler = StubHandler.Returning("""{"code":40000300,"msg":"invalid argument","data":{}}""");
        using var client = NewClient(handler);

        var info = await client.GetTokenInfoAsync("badmint");

        Assert.Null(info);
        Assert.Contains("invalid argument", client.LastError);
    }

    [Fact]
    public async Task Http500_ReturnsEmptyList_ForCollectionMethods()
    {
        var handler = StubHandler.Returning("oops", HttpStatusCode.InternalServerError, "text/plain");
        using var client = NewClient(handler);

        var trending = await client.GetTrendingAsync("1h", 5);
        var activity = await client.GetWalletActivityAsync("wallet1", 10, 150m);

        Assert.Empty(trending);
        Assert.Empty(activity);
        Assert.Equal("HTTP 500", client.LastError);
    }

    [Fact]
    public async Task WalletActivity_RequestUrl_HasTypesAndLimit()
    {
        var handler = StubHandler.Returning(Fixtures.Load("wallet_activity.sample.json"));
        using var client = NewClient(handler);

        var trades = await client.GetWalletActivityAsync("498g1rVnFcnjBjpfw1xyqA1WvgQXUU8RWuELjxkjAayQ", 20, 150m);

        Assert.Equal(2, trades.Count);
        var url = handler.RequestedUrls[0];
        Assert.Contains("type=sell", url);
        Assert.Contains("type=buy", url);
        Assert.Contains("limit=20", url);
    }

    [Fact]
    public async Task TestConnection_Ok_And_Failure()
    {
        using var okClient = NewClient(StubHandler.Returning(Fixtures.Load("rank_swaps.json")));
        var ok = await okClient.TestConnectionAsync();
        Assert.StartsWith("ok", ok);

        using var badClient = NewClient(StubHandler.Returning("<html>challenge</html>", HttpStatusCode.Forbidden, "text/html"));
        var bad = await badClient.TestConnectionAsync();
        Assert.StartsWith("failed", bad);
    }

    [Fact]
    public void StripCreds_HidesProxyPassword()
    {
        Assert.Equal("http://***@rp.example.cc:1000",
            GmgnApiClient.StripCreds("http://user:pass@rp.example.cc:1000"));
        Assert.Equal("http://rp.example.cc:1000",
            GmgnApiClient.StripCreds("http://rp.example.cc:1000"));
    }
}
