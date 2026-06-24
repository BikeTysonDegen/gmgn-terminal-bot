using GmgnTerminal.Core.Config;

namespace GmgnTerminal.Core.Gmgn;

public class GmgnApiOptions
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    public string BaseUrl { get; set; } = "https://gmgn.ai";
    public string Proxy { get; set; } = "";
    public int TimeoutSec { get; set; } = 10;
    public string UserAgent { get; set; } = DefaultUserAgent;

    public static GmgnApiOptions FromConfig(ConnectionConfig c) => new()
    {
        BaseUrl = string.IsNullOrWhiteSpace(c.BaseUrl) ? "https://gmgn.ai" : c.BaseUrl,
        Proxy = c.Proxy ?? "",
        TimeoutSec = c.RequestTimeoutSec > 0 ? c.RequestTimeoutSec : 10
    };
}
