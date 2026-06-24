using System.Text.Json;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Gmgn;

// pure mapping functions: JsonElement (already unwrapped "data") -> models.
// tested against recorded fixtures in tools/testdata/.
public static class GmgnMapper
{
    // GET /api/v1/token_info/sol/{mint}
    public static TokenInfo? MapTokenInfo(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        return new TokenInfo
        {
            Mint = Json.Str(data, "address"),
            Symbol = Json.Str(data, "symbol"),
            Name = Json.Str(data, "name"),
            LiquidityUsd = Json.Dec(data, "liquidity"),
            UpdatedAt = Json.Lng(data, "open_timestamp")
        };
    }

    // GET /defi/quotation/v1/sol/tokens/realtime_token_price?address={mint}
    // data is either {"price":x} or {"{mint}":{"price":x}}
    public static decimal? MapRealtimePrice(JsonElement data, string mint)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (Json.Has(data, "price")) return Json.Dec(data, "price");
        var byMint = Json.Prop(data, mint);
        if (byMint.ValueKind == JsonValueKind.Object && Json.Has(byMint, "price"))
            return Json.Dec(byMint, "price");
        return null;
    }

    // GET /defi/quotation/v1/rank/sol/swaps/{period}
    public static List<TokenInfo> MapRank(JsonElement data)
    {
        var result = new List<TokenInfo>();
        var rank = Json.Prop(data, "rank");
        if (rank.ValueKind != JsonValueKind.Array) return result;

        foreach (var t in rank.EnumerateArray())
        {
            result.Add(new TokenInfo
            {
                Mint = Json.Str(t, "address"),
                Symbol = Json.Str(t, "symbol"),
                Name = Json.Str(t, "name"),
                PriceUsd = Json.Dec(t, "price"),
                LiquidityUsd = Json.Dec(t, "liquidity"),
                MarketCapUsd = Json.Dec(t, "market_cap"),
                Volume24hUsd = Json.Dec(t, "volume"),
                UpdatedAt = Json.Lng(t, "open_timestamp")
            });
        }
        return result;
    }

    // GET /api/v1/wallet_activity/sol?...&wallet={addr} — data is a flat array.
    // only buy/sell rows become Trades, transfers etc are skipped.
    public static List<Trade> MapWalletActivity(JsonElement data, string wallet, decimal solUsdFallback = 0m)
    {
        var result = new List<Trade>();
        if (data.ValueKind != JsonValueKind.Array) return result;

        foreach (var a in data.EnumerateArray())
        {
            var type = Json.Str(a, "event_type");
            if (string.IsNullOrEmpty(type)) type = Json.Str(a, "type");
            if (type != "buy" && type != "sell") continue;

            var token = Json.Prop(a, "token");
            var usd = Json.Dec(a, "cost_usd");
            var sol = Json.Dec(a, "cost_sol");
            if (sol == 0m && usd > 0m && solUsdFallback > 0m) sol = usd / solUsdFallback;

            result.Add(new Trade
            {
                TxHash = Json.Str(a, "tx_hash"),
                Maker = wallet,
                Mint = Json.Str(token, "address"),
                Symbol = Json.Str(token, "symbol"),
                Side = type == "buy" ? TradeSide.Buy : TradeSide.Sell,
                SolAmount = sol,
                UsdAmount = usd,
                TokenAmount = Json.Dec(a, "token_amount"),
                PriceUsd = Json.Dec(a, "price_usd"),
                Timestamp = Json.Lng(a, "timestamp")
            });
        }
        return result;
    }

    // GET /defi/quotation/v1/trades/sol/{mint} — data.history array
    public static List<Trade> MapTrades(JsonElement data, string mint, decimal solUsdFallback = 0m)
    {
        var result = new List<Trade>();
        var history = Json.Prop(data, "history");
        if (history.ValueKind != JsonValueKind.Array) return result;

        foreach (var t in history.EnumerateArray())
        {
            var side = Json.Str(t, "side");
            if (side != "buy" && side != "sell") continue;

            var usd = Json.Dec(t, "usd_amount");
            var sol = Json.Dec(t, "sol_amount");
            if (sol == 0m && usd > 0m && solUsdFallback > 0m) sol = usd / solUsdFallback;

            result.Add(new Trade
            {
                TxHash = Json.Str(t, "tx_hash"),
                Maker = Json.Str(t, "maker"),
                Mint = mint,
                Symbol = Json.Str(t, "token_symbol"),
                Side = side == "buy" ? TradeSide.Buy : TradeSide.Sell,
                SolAmount = sol,
                UsdAmount = usd,
                TokenAmount = Json.Dec(t, "token_amount"),
                PriceUsd = Json.Dec(t, "price_usd"),
                Timestamp = Json.Lng(t, "time_unix")
            });
        }
        return result;
    }
}
