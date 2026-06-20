namespace GmgnTerminal.Core.Logging;

// static facade: writes to LogBus always, to FileLog when attached.
public static class Log
{
    private static FileLog? _file;
    private static readonly object Gate = new();

    public static LogLevel MinLevel { get; set; } = LogLevel.Debug;

    public static void AttachFile(FileLog file)
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = file;
        }
    }

    public static void DetachFile()
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write(LogLevel.Error, ex == null ? msg : $"{msg} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel) return;
        var entry = new LogEntry(DateTime.Now, level, msg);
        LogBus.Publish(entry);
        FileLog? file;
        lock (Gate) file = _file;
        try { file?.Write(entry); } catch { /* disk full etc, bus still has it */ }
    }
}
