using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Config;

public enum SizeMode
{
    Fixed,
    Proportional
}

public class ConnectionConfig
{
    public string BaseUrl { get; set; } = "https://gmgn.ai";
    public string Proxy { get; set; } = "";
    public int ActivityPollSec { get; set; } = 5;
    public int PricePollSec { get; set; } = 5;
    public int RequestTimeoutSec { get; set; } = 10;
    public bool OfflineFeed { get; set; } = true;
}

public class TradingConfig
{
    public bool PaperMode { get; set; } = true; // v0.1: always paper
    public SizeMode SizeMode { get; set; } = SizeMode.Fixed;
    public decimal FixedSizeSol { get; set; } = 0.05m;
    public decimal SlippagePercent { get; set; } = 10m;
    public decimal FeePercent { get; set; } = 1m;
    public decimal MinLeaderSol { get; set; } = 0.5m;
    public decimal MaxOurSolPerTrade { get; set; } = 0.5m;
    public int MaxOpenPositions { get; set; } = 10;
    public int DelayMinMs { get; set; } = 300;
    public int DelayMaxMs { get; set; } = 2500;
    public List<string> SkipMints { get; set; } = new();
}

public class WalletConfig
{
    public decimal PaperBalanceSol { get; set; } = 10m;
    public string PrivateKey { get; set; } = ""; // stub for a future live executor
}

public class AppConfig
{
    public int Version { get; set; } = 1;
    public ConnectionConfig Connection { get; set; } = new();
    public TradingConfig Trading { get; set; } = new();
    public WalletConfig Wallet { get; set; } = new();
    public List<Leader> Leaders { get; set; } = new();
}
