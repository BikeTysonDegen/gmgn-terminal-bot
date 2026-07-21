using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Engine;

// session counters shown in the UI stats bar, updated from CopyEvents
public class SessionStats : Observable
{
    private int _leaderTrades;
    private int _buysCopied;
    private int _sellsCopied;
    private int _skipped;
    private int _wins;
    private int _losses;
    private decimal _realizedSol;

    public int LeaderTrades { get => _leaderTrades; private set => SetProperty(ref _leaderTrades, value); }
    public int BuysCopied { get => _buysCopied; private set => SetProperty(ref _buysCopied, value); }
    public int SellsCopied { get => _sellsCopied; private set => SetProperty(ref _sellsCopied, value); }
    public int Skipped { get => _skipped; private set => SetProperty(ref _skipped, value); }
    public int Wins { get => _wins; private set => SetProperty(ref _wins, value); }
    public int Losses { get => _losses; private set => SetProperty(ref _losses, value); }
    public decimal RealizedSol { get => _realizedSol; private set => SetProperty(ref _realizedSol, value); }

    public double WinRatePct => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) * 100.0 : 0.0;

    public void OnLeaderTrade() => LeaderTrades++;

    public void Apply(CopyEvent ev)
    {
        switch (ev.Action)
        {
            case CopyAction.BuyCopied:
                BuysCopied++;
                break;
            case CopyAction.SellCopied:
                SellsCopied++;
                if (ev.OurFill != null)
                {
                    RealizedSol += ev.OurFill.RealizedSol;
                    // a trim that realized profit counts as a win too
                    if (ev.OurFill.RealizedSol > 0m) Wins++;
                    else if (ev.OurFill.RealizedSol < 0m) Losses++;
                }
                break;
            case CopyAction.Skipped:
                Skipped++;
                break;
        }
        OnPropertyChanged(nameof(WinRatePct));
    }

    public void Reset()
    {
        LeaderTrades = 0;
        BuysCopied = 0;
        SellsCopied = 0;
        Skipped = 0;
        Wins = 0;
        Losses = 0;
        RealizedSol = 0m;
        OnPropertyChanged(nameof(WinRatePct));
    }
}
