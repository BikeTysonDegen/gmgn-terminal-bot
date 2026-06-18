namespace GmgnTerminal.Core.Models;

public enum TradeSide
{
    Buy,
    Sell
}

// a leader's trade decoded from gmgn wallet activity
public class Trade
{
    public string TxHash { get; set; } = "";
    public string Maker { get; set; } = "";
    public string Mint { get; set; } = "";
    public string Symbol { get; set; } = "";
    public TradeSide Side { get; set; }
    public decimal SolAmount { get; set; }
    public decimal UsdAmount { get; set; }
    public decimal TokenAmount { get; set; }
    public decimal PriceUsd { get; set; }
    public long Timestamp { get; set; } // unix seconds

    public string DedupeKey => $"{TxHash}|{Maker}|{Mint}";

    public DateTimeOffset Time => DateTimeOffset.FromUnixTimeSeconds(Timestamp);
}
