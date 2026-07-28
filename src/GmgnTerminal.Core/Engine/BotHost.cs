using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Offline;
using GmgnTerminal.Core.Gmgn;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;
using GmgnTerminal.Core.Sources;

namespace GmgnTerminal.Core.Engine;

// top-level wiring: config -> sources (offline or live) -> broker -> engine ->
// tracker -> stats. the app layer talks to this only.
public class BotHost : IDisposable
{
    private readonly Func<AppConfig> _configProvider;
    private readonly Func<IReadOnlyList<TokenInfo>?>? _offlineCatalog;
    private AppConfig _config = new();

    private GmgnApiClient? _liveClient;
    private OfflineFeed? _OfflineFeed;
    private ITradeSource _tradeSource = null!;
    private IPriceSource _priceSource = null!;

    public PaperBroker Broker { get; private set; } = null!;
    public CopyEngine Engine { get; private set; } = null!;
    public PositionTracker Tracker { get; private set; } = null!;
    public SessionStats Stats { get; } = new();

    public bool IsRunning => Engine?.IsRunning ?? false;
    public bool IsOffline => _tradeSource is OfflineFeed;

    public event Action<Trade, string>? LeaderTradeSeen;
    public event Action<CopyEvent>? CopyDecided;
    public event Action<bool>? StatusChanged;
    public event Action? Rebuilt;

    public BotHost(Func<AppConfig> configProvider)
    {
        _configProvider = configProvider;
        _offlineCatalog = offlineCatalog;
        Rebuild(configProvider());
    }

    // call after config changes (saved on Trading/Connection tabs) —
    // recreates sources and engine with the new settings
    public void Rebuild(AppConfig config)
    {
        var wasRunning = IsRunning;
        Teardown();

        _config = config;
        Broker = new PaperBroker(config.Wallet.PaperBalanceSol);

        if (config.Connection.OfflineFeed)
        {
            _OfflineFeed = new OfflineFeed(new OfflineFeedOptions { Seed = Environment.TickCount & 0xFFFF },
                seedTokens: _offlineCatalog?.Invoke());
            _tradeSource = _OfflineFeed;
            _priceSource = _OfflineFeed;
            Log.Info("bot host: offline feed (simulated gmgn data)");
        }
        else
        {
            _liveClient = new GmgnApiClient(GmgnApiOptions.FromConfig(config.Connection));
            _tradeSource = _liveClient;
            _priceSource = _liveClient;
            Log.Info("bot host: live mode, using gmgn api client");
        }

        Engine = new CopyEngine(_tradeSource, _priceSource, Broker,
            () => _configProvider(), () => _configProvider().Leaders);
        Engine.LeaderTradeSeen += OnLeaderTrade;
        Engine.CopyDecided += OnCopyDecided;
        Engine.StatusChanged += OnEngineStatus;

        Tracker = new PositionTracker(Broker, _priceSource, () => _configProvider().Connection.PricePollSec);

        if (wasRunning) Start();
        Rebuilt?.Invoke();
    }

    public void Start()
    {
        Engine.Start();
        Tracker.Start();
    }

    public void Stop()
    {
        Engine.Stop();
        Tracker.Stop();
    }


    private void OnLeaderTrade(Trade trade, string leaderDisplay)
    {
        Stats.OnLeaderTrade();
        LeaderTradeSeen?.Invoke(trade, leaderDisplay);
    }

    private void OnCopyDecided(CopyEvent ev)
    {
        Stats.Apply(ev);
        CopyDecided?.Invoke(ev);
    }

    private void OnEngineStatus(bool running) => StatusChanged?.Invoke(running);

    private void Teardown()
    {
        if (Engine != null)
        {
            Engine.Stop();
            Engine.LeaderTradeSeen -= OnLeaderTrade;
            Engine.CopyDecided -= OnCopyDecided;
            Engine.StatusChanged -= OnEngineStatus;
        }
        Tracker?.Dispose();
        _liveClient?.Dispose();
        _liveClient = null;
        _OfflineFeed = null;
    }

    public void Dispose() => Teardown();
}
