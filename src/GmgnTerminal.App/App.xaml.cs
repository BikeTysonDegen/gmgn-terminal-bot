using System.IO;
using System.Windows;
using GmgnTerminal.Core.Logging;

namespace GmgnTerminal.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        var fileLog = new FileLog(logDir);
        Log.AttachFile(fileLog);
        Log.Info("==============================================");
        Log.Info("gmgn-terminal starting");

        try
        {
            AppServices.Init();
        }
        catch (Exception ex)
        {
            Log.Error("startup failed", ex);
            MessageBox.Show($"startup failed: {ex.Message}\nsee logs/ for details",
                "gmgn-terminal", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        Log.Info("main window shown");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppServices.Shutdown();
            Log.Info("gmgn-terminal exited");
        }
        finally
        {
            Log.DetachFile();
        }
        base.OnExit(e);
    }
}
