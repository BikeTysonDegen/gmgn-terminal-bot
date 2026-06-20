namespace GmgnTerminal.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string Line => $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{Level,-5}] {Message}";
}
