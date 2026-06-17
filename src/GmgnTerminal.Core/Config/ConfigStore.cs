using System.Text.Json;
using System.Text.Json.Serialization;
using GmgnTerminal.Core.Logging;

namespace GmgnTerminal.Core.Config;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static string? LastLoadError { get; private set; }

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        LastLoadError = null;
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new AppConfig();
                Log.Info($"config not found at {path}, writing defaults");
                Save(fresh, path);
                return fresh;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            if (cfg == null) throw new InvalidDataException("deserialized to null");
            Log.Info($"config loaded from {path} (v{cfg.Version}, {cfg.Leaders.Count} leaders, offline={cfg.Connection.OfflineFeed})");
            return cfg;
        }
        catch (Exception ex)
        {
            // corrupt config: quarantine and start fresh instead of crashing
            LastLoadError = ex.Message;
            Log.Warn($"config load failed ({path}): {ex.Message}, falling back to defaults");
            try { File.Move(path, path + ".bad", true); } catch { /* best effort */ }
            return new AppConfig();
        }
    }

    public static void Save(AppConfig cfg, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOpts));
        File.Move(tmp, path, true);
        Log.Debug($"config saved to {path}");
    }
}
