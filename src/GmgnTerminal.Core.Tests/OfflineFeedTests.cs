using GmgnTerminal.Core.Offline;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Tests;

public class OfflineFeedTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);

    private static OfflineFeed NewFeed(int seed = 42, int maxTrades = 3) =>
        new(new OfflineFeedOptions { Seed = seed, MaxTradesPerPoll = maxTrades, TradeChancePerPoll = 1.0 },
            () => FixedNow);

    [Fact]
    public async Task SameSeed_SameWallet_Deterministic()
    {
        var a = NewFeed();
        var b = NewFeed();
        const string wallet = "LeaderWallet111111111111111111111111111111";

        var ta = await a.GetWalletActivityAsync(wallet, 10, 0m);
        var tb = await b.GetWalletActivityAsync(wallet, 10, 0m);

        Assert.NotEmpty(ta);
        Assert.Equal(ta.Count, tb.Count);
        for (var i = 0; i < ta.Count; i++)
        {
            Assert.Equal(ta[i].TxHash, tb[i].TxHash);
            Assert.Equal(ta[i].Mint, tb[i].Mint);
            Assert.Equal(ta[i].Side, tb[i].Side);
            Assert.Equal(ta[i].SolAmount, tb[i].SolAmount);
            Assert.Equal(ta[i].PriceUsd, tb[i].PriceUsd);
        }
    }

    [Fact]
    public async Task DifferentWallets_DifferentActivity()
    {
        var feed = NewFeed();

        var t1 = await feed.GetWalletActivityAsync("WalletAAA111111111111111111111111111111111", 10, 0m);
        var t2 = await feed.GetWalletActivityAsync("WalletBBB222222222222222222222222222222222", 10, 0m);

        Assert.NotEmpty(t1);
        Assert.NotEmpty(t2);
        Assert.NotEqual(t1[0].TxHash, t2[0].TxHash);
    }

    [Fact]
    public async Task Trades_AreWellFormed()
    {
        var feed = NewFeed(maxTrades: 5);
        var trades = await feed.GetWalletActivityAsync("WalletCCC333333333333333333333333333333333", 100, 0m);

        Assert.InRange(trades.Count, 1, 5);
        foreach (var t in trades)
        {
            Assert.True(t.Side is TradeSide.Buy or TradeSide.Sell);
            Assert.True(t.SolAmount > 0m);
            Assert.True(t.UsdAmount > 0m);
            Assert.True(t.TokenAmount > 0m);
            Assert.True(t.PriceUsd > 0m);
            Assert.Equal(64, t.TxHash.Length);
            Assert.InRange(t.Timestamp, FixedNow.ToUnixTimeSeconds() - 4, FixedNow.ToUnixTimeSeconds());
            Assert.True(t.Mint.Length >= 32);
            Assert.False(string.IsNullOrEmpty(t.Symbol));
            Assert.Equal("WalletCCC333333333333333333333333333333333", t.Maker);
        }
    }

    [Fact]
    public async Task LimitIsRespected()
    {
        var feed = NewFeed(maxTrades: 5);
        var trades = await feed.GetWalletActivityAsync("WalletDDD444444444444444444444444444444444", 2, 0m);
        Assert.True(trades.Count <= 2);
    }

    [Fact]
    public async Task Prices_WalkAndStayPositive_UnknownMintIsNull()
    {
        var feed = NewFeed();
        var catalog = feed.TokenCatalog;
        Assert.Equal(6, catalog.Count);
        Assert.Contains(catalog, t => t.Symbol == "BONK");

        var mint = catalog[0].Mint;
        var p1 = await feed.GetTokenPriceUsdAsync(mint);
        var p2 = await feed.GetTokenPriceUsdAsync(mint);
        var p3 = await feed.GetTokenPriceUsdAsync(mint);

        Assert.NotNull(p1);
        Assert.True(p1 > 0m && p2 > 0m && p3 > 0m);
        Assert.False(p1 == p2 && p2 == p3); // random walk actually moves

        Assert.Null(await feed.GetTokenPriceUsdAsync("UnknownMint11111111111111111111111111111111"));
    }

    [Fact]
    public async Task SolUsd_StaysSane()
    {
        var feed = NewFeed();
        await feed.GetWalletActivityAsync("WalletEEE555555555555555555555555555555555", 10, 0m);
        var sol = await feed.GetSolUsdAsync();
        Assert.InRange(sol, 100m, 200m);
    }

    [Fact]
    public async Task TradeChance_ZeroMeansNoActivity()
    {
        var feed = new OfflineFeed(new OfflineFeedOptions { Seed = 7, TradeChancePerPoll = 0.0 }, () => FixedNow);
        var trades = await feed.GetWalletActivityAsync("WalletFFF666666666666666666666666666666666", 10, 0m);
        Assert.Empty(trades);
    }
}
