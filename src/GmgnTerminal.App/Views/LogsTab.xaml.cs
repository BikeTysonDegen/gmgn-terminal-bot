using System.Collections.Specialized;
using System.Windows.Controls;

namespace GmgnTerminal.App.Views;

public partial class LogsTab : UserControl
{
    public LogsTab()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)LinesList.Items).CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is ViewModels.LogsViewModel vm && vm.AutoScroll && LinesList.Items.Count > 0)
            LinesList.ScrollIntoView(LinesList.Items[^1]);
    }
}
