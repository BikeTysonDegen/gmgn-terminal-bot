namespace GmgnTerminal.Core.Models;

// our simulated fill from the paper broker
public class Fill
{
    public long Id { get; set; }
    public DateTime TimeUtc { get; set; }
    public string Mint { get; set; } = "";
    public string Symbol { get; set; } = "";
    public TradeSide Side { get; set; }
    public decimal SolAmount { get; set; }   // spent (buy) or received (sell), incl fee
    public decimal TokenAmount { get; set; }
    public decimal PriceUsd { get; set; }    // exec price per token, after slippage
    public decimal SolUsd { get; set; }      // SOL/USD rate used
    public decimal FeeSol { get; set; }
    public decimal RealizedSol { get; set; } // sells only
    public string LeaderAddress { get; set; } = "";
    public string LeaderTxHash { get; set; } = "";
}
