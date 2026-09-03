using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Gmgn;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.App;

// poor man's service locator — one config, one bot host for the whole app
public static class AppServices
{
    public static AppConfig Config { get; private set; } = new();
    public static BotHost Host { get; private set; } = null!;
    public static string ConfigPath => ConfigStore.DefaultPath;

    // real trending tokens fetched once at startup; feeds the offline
    // simulator so the feed shows mints that actually trade right now
    public static IReadOnlyList<TokenInfo>? LiveCatalog { get; private set; }

    public static void Init()
    {
        Config = ConfigStore.Load();
        if (ConfigStore.LastLoadError != null)
            Log.Warn($"previous config was corrupt and quarantined: {ConfigStore.LastLoadError}");

        FetchLiveCatalog();
        SeedLeadersIfNeeded();
        Host = new BotHost(() => Config, () => LiveCatalog);
    }

    private static void FetchLiveCatalog()
    {
        try
        {
            var opts = GmgnApiOptions.FromConfig(Config.Connection);
            opts.TimeoutSec = 6;
            using var client = new GmgnApiClient(opts);
            var tokens = client.GetTrendingAsync("1h", 12).GetAwaiter().GetResult();
            if (tokens.Count > 0)
            {
                LiveCatalog = tokens;
                Log.Info($"fetched {tokens.Count} trending tokens from gmgn for the offline catalog");
                return;
            }
            Log.Warn("gmgn trending returned nothing, using built-in token list");
        }
        catch (Exception ex)
        {
            Log.Warn($"gmgn trending fetch failed ({ex.Message}), using built-in token list");
        }
    }

    // first run: two wallets from gmgn 7d profit rank so START does something
    // right away. offline feed generates activity for any address.
    private static void SeedLeadersIfNeeded()
    {
        if (Config.Leaders.Count > 0) return;

        Config.Leaders.Add(new Leader
        {
            Address = "498g1rVnFcnjBjpfw1xyqA1WvgQXUU8RWuELjxkjAayQ",
            Multiplier = 0.5m
        });
        Config.Leaders.Add(new Leader
        {
            Address = "BtMBMPkoNbnLF9Xn552guQq528KKXcsNBNNBre3oaQtr",
            Multiplier = 0.3m,
            MinSol = 0.1m
        });
        SaveConfig();
        Log.Info("first run: seeded 2 wallets from gmgn 7d profit rank");
    }

    public static void SaveConfig()
    {
        ConfigStore.Save(Config);
    }

    // call after structural config changes (data source, balance reset, leaders)
    public static void ApplyConfigRebuild()
    {
        SaveConfig();
        Host.Rebuild(Config);
    }

    public static void Shutdown()
    {
        try { Host?.Dispose(); } catch (Exception ex) { Log.Error("shutdown error", ex); }
    }
}
