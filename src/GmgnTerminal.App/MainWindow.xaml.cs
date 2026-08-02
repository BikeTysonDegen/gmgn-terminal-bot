using System.Windows;
using GmgnTerminal.App.ViewModels;

namespace GmgnTerminal.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(AppServices.Host);
    }
}
