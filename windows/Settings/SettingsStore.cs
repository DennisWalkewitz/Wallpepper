using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Wallpepper.Settings;

/// <summary>
/// Loads the settings, is the one place they are changed after start-up, and saves them a moment after the last change
/// (temp file plus rename). UI thread only.
/// </summary>
internal sealed class SettingsStore
{
    private const int SaveDelayMs = 400;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Upgrades of the saved format, oldest first: each step brings a config from the version before it to its own.</summary>
    private static readonly (int ToVersion, Action<Config> Upgrade)[] Migrations = Array.Empty<(int, Action<Config>)>();

    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    private SettingsStore(Config config)
    {
        Config = config;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(SaveDelayMs) };
        _saveTimer.Tick += (_, _) => Flush();
    }

    /// <summary>The current settings. Read freely; change only through <see cref="Change"/>.</summary>
    public Config Config { get; }

    /// <summary>Self-test: nothing it does is written to disk.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Raised after every change with the areas it touched.</summary>
    public event Action<SettingsArea>? Changed;

    public static SettingsStore Load()
    {
        Config config = Read() ?? new Config();
        config.Normalise();
        foreach (var (toVersion, upgrade) in Migrations)
        {
            if (config.ConfigVersion < toVersion)
            {
                upgrade(config);
                config.ConfigVersion = toVersion;
            }
        }
        config.ConfigVersion = Config.CurrentVersion;
        return new SettingsStore(config);
    }

    /// <summary>Applies a change, schedules the save and tells the app which areas to re-apply.</summary>
    public void Change(SettingsArea area, Action<Config> change)
    {
        change(Config);
        RequestSave();
        Changed?.Invoke(area);
    }

    public void RequestSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now (exit, window closed).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty || ReadOnly)
        {
            return;
        }
        _dirty = false;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            string temp = AppPaths.ConfigFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Config, Json));
            File.Move(temp, AppPaths.ConfigFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("save settings", e);
        }
    }

    /// <summary>null when there is no file yet. An unreadable file is kept as config.json.bad and defaults are used.</summary>
    private static Config? Read()
    {
        if (!File.Exists(AppPaths.ConfigFile))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(AppPaths.ConfigFile), Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("read settings (the file is kept as config.json.bad, defaults are used)", e);
            Try.Run("keep the unreadable settings file", () => File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".bad", true));
            return null;
        }
    }
}
