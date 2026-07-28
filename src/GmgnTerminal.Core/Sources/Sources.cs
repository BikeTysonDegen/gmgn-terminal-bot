using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Sources;

// where the engine gets leader trades from: live gmgn client or offline feed
public interface ITradeSource
{
    Task<IReadOnlyList<Trade>> GetWalletActivityAsync(string wallet, int limit, decimal solUsdFallback, CancellationToken ct = default);
}

// where the engine gets prices from
public interface IPriceSource
{
    Task<decimal?> GetTokenPriceUsdAsync(string mint, CancellationToken ct = default);
    Task<decimal> GetSolUsdAsync(CancellationToken ct = default);
}
