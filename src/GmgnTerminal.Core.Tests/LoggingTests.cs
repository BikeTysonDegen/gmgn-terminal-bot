using GmgnTerminal.Core.Logging;

namespace GmgnTerminal.Core.Tests;

public class LoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gmdegen-log-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 9, 14, 12, 0, 0);

    public void Dispose()
    {
        Log.DetachFile();
        LogBus.Clear();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // the writer holds the file open; read it the same way a tail viewer would
    private static string ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    [Fact]
    public void FileLog_WritesLines_AndBusGetsThem()
    {
        using var file = new FileLog(_dir, clock: () => _now);
        Log.AttachFile(file);
        LogBus.Clear();

        Log.Info("engine started");
        Log.Debug("poll ok");
        Log.Error("rpc failed", new InvalidOperationException("403"));

        var text = ReadAll(file.CurrentFile);
        Assert.Contains("[Info ] engine started", text);
        Assert.Contains("[Debug] poll ok", text);
        Assert.Contains("InvalidOperationException: 403", text);

        // LogBus is static and other test classes log in parallel — match by content
        var snap = LogBus.Snapshot();
        Assert.Contains(snap, e => e.Level == LogLevel.Info && e.Message == "engine started");
        Assert.Contains(snap, e => e.Level == LogLevel.Debug && e.Message == "poll ok");
        Assert.Contains(snap, e => e.Level == LogLevel.Error && e.Message.Contains("403"));
    }

    [Fact]
    public void FileLog_RollsOnDateChange()
    {
        using var file = new FileLog(_dir, clock: () => _now);
        file.Write(new LogEntry(_now, LogLevel.Info, "day one"));

        _now = _now.AddDays(1);
        file.Write(new LogEntry(_now, LogLevel.Info, "day two"));

        Assert.True(File.Exists(Path.Combine(_dir, "GmgnTerminal-20260914.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "GmgnTerminal-20260915.log")));
        Assert.Contains("day two", ReadAll(Path.Combine(_dir, "GmgnTerminal-20260915.log")));
    }

    [Fact]
    public void FileLog_MinLevelFilters()
    {
        using var file = new FileLog(_dir, clock: () => _now);
        Log.AttachFile(file);
        Log.MinLevel = LogLevel.Warn;
        try
        {
            Log.Debug("hidden");
            Log.Info("hidden too");
            Log.Warn("visible");

            var text = ReadAll(file.CurrentFile);
            Assert.DoesNotContain("hidden", text);
            Assert.Contains("visible", text);
        }
        finally
        {
            Log.MinLevel = LogLevel.Debug;
        }
    }

    [Fact]
    public void LogBus_CapsAt2000()
    {
        LogBus.Clear();
        for (var i = 0; i < 2500; i++)
            LogBus.Publish(new LogEntry(_now, LogLevel.Debug, $"m{i}"));

        var snap = LogBus.Snapshot();
        Assert.Equal(2000, snap.Count);
        Assert.Equal("m500", snap[0].Message);
        Assert.Equal("m2499", snap[^1].Message);
    }
}
