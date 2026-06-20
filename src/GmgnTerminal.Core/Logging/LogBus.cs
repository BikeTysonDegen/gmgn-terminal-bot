namespace GmgnTerminal.Core.Logging;

// in-memory stream for the UI log tab. ring buffer, thread-safe.
public static class LogBus
{
    private const int Capacity = 2000;
    private static readonly object Gate = new();
    private static readonly LinkedList<LogEntry> Buffer = new();

    public static event Action<LogEntry>? EntryAdded;

    public static void Publish(LogEntry entry)
    {
        lock (Gate)
        {
            Buffer.AddLast(entry);
            while (Buffer.Count > Capacity) Buffer.RemoveFirst();
        }
        EntryAdded?.Invoke(entry);
    }

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Gate) return Buffer.ToArray();
    }

    public static void Clear()
    {
        lock (Gate) Buffer.Clear();
    }
}
