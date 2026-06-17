using GmgnTerminal.Core.Config;
using GmgnTerminal.Core.Models;

namespace GmgnTerminal.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GmgnTerminal-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string PathFor(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void Load_MissingFile_CreatesDefaults()
    {
        var path = PathFor("config.json");

        var cfg = ConfigStore.Load(path);

        Assert.True(File.Exists(path));
        Assert.True(cfg.Connection.OfflineFeed);
        Assert.True(cfg.Trading.PaperMode);
        Assert.Equal(10m, cfg.Wallet.PaperBalanceSol);
    }

    [Fact]
    public void SaveLoad_Roundtrip_KeepsValues()
    {
        var path = PathFor("config.json");
        var cfg = new AppConfig();
        cfg.Connection.Proxy = "http://127.0.0.1:8080";
        cfg.Connection.OfflineFeed = false;
        cfg.Trading.SizeMode = SizeMode.Proportional;
        cfg.Trading.FixedSizeSol = 0.25m;
        cfg.Trading.SkipMints.Add("EPvS3oRfL11zXtuV91P1Yf2g9PeF8k8QYbbNj7fHpump");
        cfg.Leaders.Add(new Leader
        {
            Address = "9aTL1vU7gRLP1EZ48zQrGzr6dLOH3u9BxmN8e1X4cKdF",
            Alias = "whale1",
            Multiplier = 0.5m,
            MinSol = 1.5m,
            Enabled = false
        });

        ConfigStore.Save(cfg, path);
        var loaded = ConfigStore.Load(path);

        Assert.Equal("http://127.0.0.1:8080", loaded.Connection.Proxy);
        Assert.False(loaded.Connection.OfflineFeed);
        Assert.Equal(SizeMode.Proportional, loaded.Trading.SizeMode);
        Assert.Equal(0.25m, loaded.Trading.FixedSizeSol);
        Assert.Single(loaded.Trading.SkipMints);
        var leader = Assert.Single(loaded.Leaders);
        Assert.Equal("whale1", leader.Alias);
        Assert.Equal(0.5m, leader.Multiplier);
        Assert.False(leader.Enabled);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults_AndQuarantines()
    {
        var path = PathFor("config.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ this is not json ]]");

        var cfg = ConfigStore.Load(path);

        Assert.NotNull(ConfigStore.LastLoadError);
        Assert.True(File.Exists(path + ".bad"));
        Assert.True(cfg.Trading.PaperMode);
    }

    [Fact]
    public void Save_IsAtomic_NoTmpLeftBehind()
    {
        var path = PathFor("config.json");

        ConfigStore.Save(new AppConfig(), path);
        ConfigStore.Save(new AppConfig { Version = 2 }, path);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(2, ConfigStore.Load(path).Version);
    }
}
