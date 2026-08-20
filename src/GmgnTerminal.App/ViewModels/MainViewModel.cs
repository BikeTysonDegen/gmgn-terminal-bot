using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Engine;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.App.ViewModels;

// one row of the activity feed
public class FeedRow
{
    public string Time { get; init; } = "";
    public string Leader { get; init; } = "";
    public string Side { get; init; } = "";       // leader's side
    public string Token { get; init; } = "";
    public string LeaderSol { get; init; } = "";
    public string Action { get; init; } = "";     // what we did
    public string Pnl { get; init; } = "";
    public Brush SideBrush { get; init; } = Brushes.Gray;
    public Brush ActionBrush { get; init; } = Brushes.Gray;
    public Brush PnlBrush { get; init; } = Brushes.Gray;

    public static FeedRow From(CopyEvent ev)
    {
        var buy = ev.LeaderTrade.Side == TradeSide.Buy;
        var sideBrush = Res(buy ? "Buy" : "Sell");
        Brush actionBrush;
        string action;
        var pnl = "";
        var pnlBrush = Res("TextSecondary");

        switch (ev.Action)
        {
            case CopyAction.BuyCopied:
                action = $"copy buy {ev.OurFill!.SolAmount:F4} SOL";
                actionBrush = Res("Accent");
                break;
            case CopyAction.SellCopied:
                action = $"copy sell {ev.OurFill!.SolAmount:F4} SOL";
                actionBrush = Res("Accent");
                pnl = $"{(ev.OurFill.RealizedSol >= 0 ? "+" : "")}{ev.OurFill.RealizedSol:F4}";
                pnlBrush = Res(ev.OurFill.RealizedSol >= 0 ? "Buy" : "Sell");
                break;
            default:
                action = "skip";
                actionBrush = Res("TextSecondary");
                break;
        }

        return new FeedRow
        {
            Time = ev.TimeUtc.ToLocalTime().ToString("HH:mm:ss"),
            Leader = ev.LeaderDisplay,
            Side = buy ? "BUY" : "SELL",
            Token = string.IsNullOrEmpty(ev.LeaderTrade.Symbol) ? GmgnTerminal.Core.Models.Leader.Short(ev.LeaderTrade.Mint) : ev.LeaderTrade.Symbol,
            LeaderSol = $"{ev.LeaderTrade.SolAmount:F2}",
            Action = ev.Action == CopyAction.Skipped ? ev.Detail : action,
            Pnl = pnl,
            SideBrush = sideBrush,
            ActionBrush = actionBrush,
            PnlBrush = pnlBrush
        };
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}

// header chips, leaders, feed, positions, stats — everything on the Main tab
public class MainViewModel : Observable
{
    private const int FeedCap = 400;
    private readonly BotHost _host;

    private bool _isRunning;
    private string _solUsdText = "—";
    private string _balanceText = "—";
    private string _unrealizedText = "0.0000";
    private string _equityText = "—";
    private string _realizedText = "0.0000";
    private string _lastLogText = "ready";
    private string _newLeaderAddress = "";
    private string _newLeaderAlias = "";
    private Leader? _selectedLeader;

    public MainViewModel(BotHost host)
    {
        _host = host;

        Leaders = new ObservableCollection<Leader>(AppServices.Config.Leaders);
        Settings = new SettingsViewModel(host);
        StartStopCommand = new RelayCommand(ToggleEngine);
        AddLeaderCommand = new RelayCommand(AddLeader, () => !string.IsNullOrWhiteSpace(NewLeaderAddress));
        RemoveLeaderCommand = new RelayCommand(RemoveLeader, () => SelectedLeader != null);
        ClearFeedCommand = new RelayCommand(() => Ui(Feed.Clear));

        _host.StatusChanged += running => Ui(() => { IsRunning = running; OnPropertyChanged(nameof(StartStopText)); OnPropertyChanged(nameof(EngineStateText)); });
        _host.Tracker.PricesUpdated += () => Ui(UpdateMarket);
        _host.CopyDecided += OnCopyDecided;
        _host.Rebuilt += () => Ui(() =>
        {
            SyncPositions();
            UpdateMarket();
        });
        LogBus.EntryAdded += entry => Ui(() => LastLogText = $"{entry.Time:HH:mm:ss} {entry.Level.ToString().ToLower()}: {entry.Message}");

        UpdateMarket();
    }

    public ObservableCollection<FeedRow> Feed { get; } = new();
    public ObservableCollection<Position> Positions { get; } = new();
    public ObservableCollection<Leader> Leaders { get; }
    public SessionStats Stats => _host.Stats;
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; } = new();

    public RelayCommand StartStopCommand { get; }
    public RelayCommand AddLeaderCommand { get; }
    public RelayCommand RemoveLeaderCommand { get; }
    public RelayCommand ClearFeedCommand { get; }

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public string EngineStateText => IsRunning ? "RUNNING" : "STOPPED";
    public string StartStopText => IsRunning ? "STOP" : "START";
    public string SolUsdText { get => _solUsdText; private set => SetProperty(ref _solUsdText, value); }
    public string BalanceText { get => _balanceText; private set => SetProperty(ref _balanceText, value); }
    public string UnrealizedText { get => _unrealizedText; private set => SetProperty(ref _unrealizedText, value); }
    public string EquityText { get => _equityText; private set => SetProperty(ref _equityText, value); }
    public string RealizedText { get => _realizedText; private set => SetProperty(ref _realizedText, value); }
    public string LastLogText { get => _lastLogText; private set => SetProperty(ref _lastLogText, value); }

    public string NewLeaderAddress { get => _newLeaderAddress; set { if (SetProperty(ref _newLeaderAddress, value.Trim())) AddLeaderCommand.RaiseCanExecuteChanged(); } }
    public string NewLeaderAlias { get => _newLeaderAlias; set => SetProperty(ref _newLeaderAlias, value); }
    public Leader? SelectedLeader { get => _selectedLeader; set { if (SetProperty(ref _selectedLeader, value)) RemoveLeaderCommand.RaiseCanExecuteChanged(); } }

    private void OnCopyDecided(CopyEvent ev)
    {
        // dispatcher hop made the feed lag on busy polls, trying direct
        Feed.Insert(0, FeedRow.From(ev));
        while (Feed.Count > FeedCap) Feed.RemoveAt(Feed.Count - 1);
        SyncPositions();
        UpdateMarket();
    }

    private void ToggleEngine()
    {
        if (_host.IsRunning)
        {
            _host.Stop();
        }
        else
        {
            if (Leaders.Count == 0)
            {
                LastLogText = "add at least one leader wallet first";
                return;
            }
            _host.Start();
        }
    }

    private void AddLeader()
    {
        var address = NewLeaderAddress.Trim();
        if (address.Length < 32 || address.Length > 44)
        {
            LastLogText = $"that doesn't look like a solana address ({address.Length} chars)";
            return;
        }
        if (Leaders.Any(l => l.Address == address))
        {
            LastLogText = "leader already in the list";
            return;
        }

        var leader = new Leader { Address = address, Alias = NewLeaderAlias.Trim() };
        Leaders.Add(leader);
        AppServices.Config.Leaders.Add(leader);
        AppServices.SaveConfig();
        _host.NotifyLeaderAdded(leader);

        Log.Info($"leader added: {leader.Display} ({address})");
        NewLeaderAddress = "";
        NewLeaderAlias = "";
    }

    private void RemoveLeader()
    {
        var leader = SelectedLeader;
        if (leader == null) return;

        Leaders.Remove(leader);
        AppServices.Config.Leaders.Remove(leader);
        AppServices.SaveConfig();
        SelectedLeader = null;
        Log.Info($"leader removed: {leader.Display}");
    }

    // called from code-behind after a leaders grid cell edit
    public void PersistLeaders() => AppServices.SaveConfig();

    private void SyncPositions()
    {
        var open = _host.Broker.OpenPositions;
        for (var i = Positions.Count - 1; i >= 0; i--)
            if (!open.Contains(Positions[i])) Positions.RemoveAt(i);
        foreach (var p in open)
            if (!Positions.Contains(p)) Positions.Add(p);
    }

    private void UpdateMarket()
    {
        var broker = _host.Broker;
        var sol = _host.Tracker.SolUsd;
        if (sol > 0) SolUsdText = $"${sol:F2}";
        BalanceText = $"{broker.BalanceSol:F4} SOL";

        var unrealized = 0m;
        foreach (var p in broker.OpenPositions) unrealized += p.ValueSol - p.CostSol;
        UnrealizedText = $"{unrealized:F4}";
        RealizedText = $"{Stats.RealizedSol:F4}";
        EquityText = $"{broker.BalanceSol + unrealized:F4} SOL";
    }

    internal static void Ui(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
