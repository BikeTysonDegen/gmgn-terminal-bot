using System.Windows;
using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Gmgn;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.App.ViewModels;

// bindings for Trading / Connection / Wallet tabs. properties read/write
// AppServices.Config directly so edits are hot; Save persists to disk.
public class SettingsViewModel : Observable
{
    private readonly BotHost _host;
    private string _settingsStatus = "";
    private string _testResult = "";
    private bool _isTesting;
    private bool _OfflineFeedAtLoad;
    private string _skipMintsText;

    public SettingsViewModel(BotHost host)
    {
        _host = host;
        _OfflineFeedAtLoad = Cfg.Connection.OfflineFeed;
        _skipMintsText = string.Join("\n", Cfg.Trading.SkipMints);

        SaveTradingCommand = new RelayCommand(SaveTrading);
        SaveConnectionCommand = new RelayCommand(SaveConnection);
        TestConnectionCommand = new RelayCommand(TestConnection, () => !IsTesting);
        ResetSessionCommand = new RelayCommand(ResetSession);
        ApplyBalanceCommand = new RelayCommand(ApplyBalance);

        _host.Rebuilt += () => MainViewModel.Ui(() =>
        {
            _OfflineFeedAtLoad = Cfg.Connection.OfflineFeed;
            OnPropertyChanged(nameof(PaperBalanceSol));
        });
    }

    private static AppConfig Cfg => AppServices.Config;

    public RelayCommand SaveTradingCommand { get; }
    public RelayCommand SaveConnectionCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand ResetSessionCommand { get; }
    public RelayCommand ApplyBalanceCommand { get; }

    public string[] SizeModes { get; } = { "Fixed", "Proportional" };

    public string SettingsStatus { get => _settingsStatus; private set => SetProperty(ref _settingsStatus, value); }
    public string TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }
    public bool IsTesting { get => _isTesting; private set => SetProperty(ref _isTesting, value); }

    // ---- trading ----
    public SizeMode SizeMode { get => Cfg.Trading.SizeMode; set => SetPropertyRef(Cfg.Trading.SizeMode, value, v => Cfg.Trading.SizeMode = v); }
    public decimal FixedSizeSol { get => Cfg.Trading.FixedSizeSol; set => SetPropertyRef(Cfg.Trading.FixedSizeSol, value, v => Cfg.Trading.FixedSizeSol = v); }
    public decimal SlippagePercent { get => Cfg.Trading.SlippagePercent; set => SetPropertyRef(Cfg.Trading.SlippagePercent, value, v => Cfg.Trading.SlippagePercent = v); }
    public decimal FeePercent { get => Cfg.Trading.FeePercent; set => SetPropertyRef(Cfg.Trading.FeePercent, value, v => Cfg.Trading.FeePercent = v); }
    public decimal MinLeaderSol { get => Cfg.Trading.MinLeaderSol; set => SetPropertyRef(Cfg.Trading.MinLeaderSol, value, v => Cfg.Trading.MinLeaderSol = v); }
    public decimal MaxOurSolPerTrade { get => Cfg.Trading.MaxOurSolPerTrade; set => SetPropertyRef(Cfg.Trading.MaxOurSolPerTrade, value, v => Cfg.Trading.MaxOurSolPerTrade = v); }
    public int MaxOpenPositions { get => Cfg.Trading.MaxOpenPositions; set => SetPropertyRef(Cfg.Trading.MaxOpenPositions, value, v => Cfg.Trading.MaxOpenPositions = v); }
    public int DelayMinMs { get => Cfg.Trading.DelayMinMs; set => SetPropertyRef(Cfg.Trading.DelayMinMs, value, v => Cfg.Trading.DelayMinMs = v); }
    public int DelayMaxMs { get => Cfg.Trading.DelayMaxMs; set => SetPropertyRef(Cfg.Trading.DelayMaxMs, value, v => Cfg.Trading.DelayMaxMs = v); }

    public string SkipMintsText
    {
        get => _skipMintsText;
        set => SetProperty(ref _skipMintsText, value);
    }

    // ---- connection ----
    public string BaseUrl { get => Cfg.Connection.BaseUrl; set => SetPropertyRef(Cfg.Connection.BaseUrl, value, v => Cfg.Connection.BaseUrl = v); }
    public string Proxy { get => Cfg.Connection.Proxy; set => SetPropertyRef(Cfg.Connection.Proxy, value, v => Cfg.Connection.Proxy = v); }
    public int ActivityPollSec { get => Cfg.Connection.ActivityPollSec; set => SetPropertyRef(Cfg.Connection.ActivityPollSec, value, v => Cfg.Connection.ActivityPollSec = v); }
    public int PricePollSec { get => Cfg.Connection.PricePollSec; set => SetPropertyRef(Cfg.Connection.PricePollSec, value, v => Cfg.Connection.PricePollSec = v); }
    public int RequestTimeoutSec { get => Cfg.Connection.RequestTimeoutSec; set => SetPropertyRef(Cfg.Connection.RequestTimeoutSec, value, v => Cfg.Connection.RequestTimeoutSec = v); }

    public bool OfflineFeed
    {
        get => Cfg.Connection.OfflineFeed;
        set
        {
            if (Cfg.Connection.OfflineFeed == value) return;
            Cfg.Connection.OfflineFeed = value;
            OnPropertyChanged();
        }
    }

    // ---- wallet ----
    public decimal PaperBalanceSol { get => Cfg.Wallet.PaperBalanceSol; set => SetPropertyRef(Cfg.Wallet.PaperBalanceSol, value, v => Cfg.Wallet.PaperBalanceSol = v); }
    public string PrivateKey { get => Cfg.Wallet.PrivateKey; set => SetPropertyRef(Cfg.Wallet.PrivateKey, value, v => Cfg.Wallet.PrivateKey = v); }

    private void SaveTrading()
    {
        if (DelayMinMs < 0) DelayMinMs = 0;
        if (DelayMaxMs < DelayMinMs) DelayMaxMs = DelayMinMs;

        Cfg.Trading.SkipMints = SkipMintsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        AppServices.SaveConfig();
        SettingsStatus = $"trading settings saved ({DateTime.Now:HH:mm:ss})";
        Log.Info($"trading settings saved: mode={Cfg.Trading.SizeMode}, fixed={Cfg.Trading.FixedSizeSol}, slip={Cfg.Trading.SlippagePercent}%, skipMints={Cfg.Trading.SkipMints.Count}");
    }

    private void SaveConnection()
    {
        AppServices.SaveConfig();
        SettingsStatus = $"connection saved ({DateTime.Now:HH:mm:ss});"
    }

    private async void TestConnection()
    {
        IsTesting = true;
        TestResult = "testing…";
        try
        {
            using var client = new GmgnApiClient(GmgnApiOptions.FromConfig(Cfg.Connection));
            var result = await client.TestConnectionAsync();
            TestResult = result;
            Log.Info($"connection test: {result}");
        }
        catch (Exception ex)
        {
            TestResult = $"error: {ex.Message}";
            Log.Error("connection test crashed", ex);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void ResetSession()
    {
        _host.Broker.Reset(Cfg.Wallet.PaperBalanceSol);
        _host.Stats.Reset();
        SettingsStatus = $"session reset, balance {Cfg.Wallet.PaperBalanceSol} SOL";
        Log.Info("session reset from UI");
    }

    private void ApplyBalance()
    {
        if (PaperBalanceSol <= 0m)
        {
            SettingsStatus = "balance must be positive";
            return;
        }
        AppServices.SaveConfig();
        _host.Broker.Reset(PaperBalanceSol);
        _host.Stats.Reset();
        SettingsStatus = $"paper balance set to {PaperBalanceSol} SOL (session reset)";
        Log.Info($"paper balance changed to {PaperBalanceSol} SOL");
    }

    // helper: write-through property setter
    private void SetPropertyRef<T>(T current, T value, Action<T> setter, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        setter(value);
        OnPropertyChanged(name);
    }
}
