using System.Windows.Controls;

namespace GmgnTerminal.App.Views;

public partial class MainTab : UserControl
{
    public MainTab()
    {
        InitializeComponent();
        // persist leader cell edits (alias/multiplier/minSol) as they land
        LeadersGrid.CellEditEnding += (_, _) =>
        {
            var vm = DataContext as ViewModels.MainViewModel;
            // CellEditEnding fires before the value is committed; defer
            Dispatcher.BeginInvoke(new System.Action(() => vm?.PersistLeaders()));
        };
    }
}
