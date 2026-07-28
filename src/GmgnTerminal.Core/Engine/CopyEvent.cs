using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Engine;

public enum CopyAction
{
    BuyCopied,
    SellCopied,
    Skipped
}

// one row in the UI feed: leader trade + what we did about it
public class CopyEvent
{
    public DateTime TimeUtc { get; init; } = DateTime.UtcNow;
    public Trade LeaderTrade { get; init; } = null!;
    public string LeaderDisplay { get; init; } = "";
    public CopyAction Action { get; init; }
    public string Detail { get; init; } = "";   // skip reason or sizing info
    public Fill? OurFill { get; init; }
}
