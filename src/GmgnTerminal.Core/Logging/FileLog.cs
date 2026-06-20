namespace GmgnTerminal.Core.Logging;

// rolling file logger: logs/GmgnTerminal-yyyyMMdd.log, rolls daily and at 10 MB.
// thread-safe, autoflush — we want the tail even if the process dies.
public class FileLog : IDisposable
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private readonly string _dir;
    private readonly string _prefix;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string _currentFile = "";
    private DateTime _currentDay;

    public FileLog(string dir, string prefix = "GmgnTerminal", Func<DateTime>? clock = null)
    {
        _dir = dir;
        _prefix = prefix;
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(dir);
    }

    public string CurrentFile { get { lock (_gate) return _currentFile; } }

    public void Write(LogEntry entry)
    {
        lock (_gate)
        {
            var now = _clock();
            if (_writer == null || now.Date != _currentDay || OverSize())
            {
                OpenWriter(now);
            }
            _writer!.WriteLine(entry.Line);
            _writer.Flush();
        }
    }

    private bool OverSize()
    {
        if (string.IsNullOrEmpty(_currentFile) || !File.Exists(_currentFile)) return false;
        return new FileInfo(_currentFile).Length >= MaxFileBytes;
    }

    private void OpenWriter(DateTime now)
    {
        _writer?.Dispose();
        _currentDay = now.Date;

        var path = Path.Combine(_dir, $"{_prefix}-{now:yyyyMMdd}.log");
        if (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes)
        {
            // same-day size roll: append -N until a non-full slot is found
            var n = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(_dir, $"{_prefix}-{now:yyyyMMdd}-{n}.log");
                n++;
            }
            while (File.Exists(candidate) && new FileInfo(candidate).Length >= MaxFileBytes);
            path = candidate;
        }

        _currentFile = path;
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = false };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
