namespace GmgnTerminal.Core.Models;

// leader wallet we copy. persisted in config.json, bound to the UI grid.
public class Leader
{
    public string Address { get; set; } = "";
    public string Alias { get; set; } = "";
    public decimal Multiplier { get; set; } = 1.0m;
    public decimal MinSol { get; set; } // 0 = use global trading filter
    public bool Enabled { get; set; } = true;

    public string Display => string.IsNullOrEmpty(Alias) ? Short(Address) : Alias;

    public static string Short(string address) =>
        address.Length > 10 ? address[..4] + ".." + address[^4..] : address;
}
