namespace GmgnTerminal.Core.Models;

// open or closed bag of one token in the paper wallet
public class Position : Observable
{
    private decimal _quantity;
    private decimal _costUsd;
    private decimal _costSol;
    private decimal _realizedSol;
    private decimal _lastPriceUsd;
    private decimal _lastSolUsd = 150m;

    public string Mint { get; set; } = "";
    public string Symbol { get; set; } = "";
    public DateTime OpenedAtUtc { get; set; }

    public decimal Quantity { get => _quantity; set { if (SetProperty(ref _quantity, value)) { RaisePnl(); OnPropertyChanged(nameof(IsOpen)); } } }
    public decimal CostUsd { get => _costUsd; set { if (SetProperty(ref _costUsd, value)) { OnPropertyChanged(nameof(EntryPriceUsd)); RaisePnl(); } } }
    public decimal CostSol { get => _costSol; set { if (SetProperty(ref _costSol, value)) OnPropertyChanged(nameof(ValueSol)); } }
    public decimal RealizedSol { get => _realizedSol; set => SetProperty(ref _realizedSol, value); }
    public decimal LastPriceUsd { get => _lastPriceUsd; set { if (SetProperty(ref _lastPriceUsd, value)) RaisePnl(); } }
    public decimal LastSolUsd { get => _lastSolUsd; set { if (SetProperty(ref _lastSolUsd, value)) OnPropertyChanged(nameof(ValueSol)); } }

    public bool IsOpen => Quantity > 0m;
    public decimal EntryPriceUsd => Quantity > 0m ? CostUsd / Quantity : 0m;
    public decimal ValueUsd => Quantity * LastPriceUsd;
    public decimal ValueSol => _lastSolUsd > 0m ? ValueUsd / _lastSolUsd : 0m;
    public decimal UnrealizedPnlUsd => IsOpen ? ValueUsd - CostUsd : 0m;
    public decimal UnrealizedSol => _lastSolUsd > 0m && IsOpen ? UnrealizedPnlUsd / _lastSolUsd : 0m;
    public decimal PnlPercent => IsOpen && CostUsd > 0m ? (ValueUsd / CostUsd - 1m) * 100m : 0m;
    public bool PnlPercentSign => PnlPercent >= 0m;

    private void RaisePnl()
    {
        OnPropertyChanged(nameof(ValueUsd));
        OnPropertyChanged(nameof(ValueSol));
        OnPropertyChanged(nameof(UnrealizedPnlUsd));
        OnPropertyChanged(nameof(UnrealizedSol));
        OnPropertyChanged(nameof(PnlPercent));
        OnPropertyChanged(nameof(PnlPercentSign));
    }
}
