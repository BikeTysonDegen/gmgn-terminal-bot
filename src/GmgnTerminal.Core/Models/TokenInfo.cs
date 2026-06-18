namespace GmgnTerminal.Core.Models;

public class TokenInfo
{
    public string Mint { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceUsd { get; set; }
    public decimal LiquidityUsd { get; set; }
    public decimal MarketCapUsd { get; set; }
    public decimal Volume24hUsd { get; set; }
    public long UpdatedAt { get; set; } // unix seconds
}
