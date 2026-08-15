using System.Collections.ObjectModel;
using GmgnTerminal.Core.Logging;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.App.ViewModels;

public class LogRow
{
    public string Time { get; init; } = "";
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
}

public class LogsViewModel : Observable
{
    private const int Cap = 2000;
    private string _selectedLevel = "Debug";

    public LogsViewModel()
    {
        Reload();
        LogBus.EntryAdded += OnEntry;
        ClearCommand = new RelayCommand(() => Lines.Clear());
    }

    public ObservableCollection<LogRow> Lines { get; } = new();
    public RelayCommand ClearCommand { get; }

    public string[] Levels { get; } = { "Debug", "Info", "Warn", "Error" };

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value)) Reload();
        }
    }

    public bool AutoScroll { get; set; } = true;

    private void Reload()
    {
        var min = ParseLevel(_selectedLevel);
        Lines.Clear();
        foreach (var e in LogBus.Snapshot())
            if (e.Level >= min) Lines.Add(Row(e));
    }

    private void OnEntry(LogEntry entry)
    {
        if (entry.Level < ParseLevel(_selectedLevel)) return;
        MainViewModel.Ui(() =>
        {
            Lines.Add(Row(entry));
            while (Lines.Count > Cap) Lines.RemoveAt(0);
        });
    }

    private static LogRow Row(LogEntry e) => new()
    {
        Time = e.Time.ToString("HH:mm:ss.fff"),
        Level = e.Level.ToString().ToUpperInvariant(),
        Message = e.Message
    };

    private static LogLevel ParseLevel(string s) =>
        Enum.TryParse<LogLevel>(s, true, out var l) ? l : LogLevel.Debug;
}
