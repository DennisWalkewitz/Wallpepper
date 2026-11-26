using Wallpepper.Desktop.Performance;
using Wallpepper.Rendering;

namespace Wallpepper.Settings;

/// <summary>
/// Everything that is saved (config.json). Plain data: <see cref="SettingsStore"/> loads, changes and saves it.
/// Library items are named by file name; displays by their device name.
/// </summary>
internal sealed class Config
{
    public const int CurrentVersion = 1;

    public int ConfigVersion { get; set; } = CurrentVersion;

    // ---- library
    public string LibraryPath { get; set; } = "";

    public List<string> Favourites { get; set; } = new();

    public LibrarySort Sort { get; set; } = LibrarySort.Name;

    /// <summary>When each item was last set as wallpaper (UTC ticks).</summary>
    public Dictionary<string, long> LastUsed { get; set; } = NewMap<long>();

    /// <summary>
    /// Bundled items as they were last copied into the library (file name -> hash): an item that still has that hash is
    /// updated with the app, an edited one is left alone, a deleted one is not brought back.
    /// </summary>
    public Dictionary<string, string> Seeded { get; set; } = NewMap<string>();

    /// <summary>The build that last synchronised the bundled items; the same build does not look again.</summary>
    public string? SeededByBuild { get; set; }

    // ---- what is on which display
    public DisplayMode Mode { get; set; } = DisplayMode.Duplicate;

    /// <summary>Span was switched on by a shader made for several displays (and goes off with the next ordinary one).</summary>
    public bool SpanChosenAutomatically { get; set; }

    /// <summary>The wallpaper of all displays (duplicate and span).</summary>
    public string? Wallpaper { get; set; }

    /// <summary>Display -> wallpaper, for <see cref="DisplayMode.PerDisplay"/>.</summary>
    public Dictionary<string, string> WallpaperByDisplay { get; set; } = new();

    public Dictionary<string, MonitorSettings> Monitors { get; set; } = new();

    // ---- shader settings
    /// <summary>The user's own values for a shader's "#define" settings.</summary>
    public Dictionary<string, Dictionary<string, string>> Overrides { get; set; } = NewMap<Dictionary<string, string>>();

    public string Background { get; set; } = "#000000";

    public float Speed { get; set; } = 1f;

    /// <summary>Web wallpapers muted by the user.</summary>
    public Dictionary<string, bool> WebMuted { get; set; } = NewMap<bool>();

    // ---- performance: the resource level; Fps, BatteryFps and RenderScale apply to "manual" only
    public ResourceLevelId Resource { get; set; } = ResourceLevel.Default;

    public int Fps { get; set; } = 60;

    public int BatteryFps { get; set; } = 30;

    public float RenderScale { get; set; } = 0.75f;

    /// <summary>GPU milliseconds per 1080p frame, measured on this PC.</summary>
    public Dictionary<string, float> Costs { get; set; } = NewMap<float>();

    /// <summary>The same with the shader's light settings.</summary>
    public Dictionary<string, float> LightCosts { get; set; } = NewMap<float>();

    /// <summary>Shaders the user wants at full quality although they are heavy.</summary>
    public List<string> FullQuality { get; set; } = new();

    /// <summary>"Too heavy for this PC" was confirmed for these.</summary>
    public List<string> HeavyConfirmed { get; set; } = new();

    public BenchResult? LastBenchmark { get; set; }

    // ---- pause rules
    public bool UserPaused { get; set; }

    public bool PauseWhenCovered { get; set; } = true;

    public bool PauseWhenLocked { get; set; } = true;

    public bool PauseOnBatterySaver { get; set; } = true;

    public bool PauseOnBattery { get; set; }

    // ---- picture
    public FilterSettings Filters { get; set; } = new();

    public bool DimOnIdle { get; set; }

    public int IdleSeconds { get; set; } = 120;

    public float IdleDim { get; set; } = 0.6f;

    // ---- updates (GitHub releases)
    public bool CheckUpdates { get; set; } = true;

    public bool AutoInstallUpdates { get; set; }

    /// <summary>UTC ticks of the last successful check.</summary>
    public long LastUpdateCheck { get; set; }

    public WindowPlacement? Window { get; set; }

    public MonitorSettings GetOrAddMonitor(string displayId)
    {
        if (!Monitors.TryGetValue(displayId, out var monitor))
        {
            monitor = new MonitorSettings();
            Monitors[displayId] = monitor;
        }
        return monitor;
    }

    /// <summary>Removes every trace of a library item (it was deleted).</summary>
    public void Forget(string fileName)
    {
        Overrides.Remove(fileName);
        Costs.Remove(fileName);
        LightCosts.Remove(fileName);
        LastUsed.Remove(fileName);
        WebMuted.Remove(fileName);
        RemoveName(Favourites, fileName);
        RemoveName(FullQuality, fileName);
        RemoveName(HeavyConfirmed, fileName);
        if (SameName(Wallpaper, fileName))
        {
            Wallpaper = null;
        }
        foreach (string display in WallpaperByDisplay.Where(pair => SameName(pair.Value, fileName)).Select(pair => pair.Key).ToList())
        {
            WallpaperByDisplay.Remove(display);
        }
    }

    public static bool SameName(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool ContainsName(List<string> names, string fileName) => names.Exists(name => SameName(name, fileName));

    public static void RemoveName(List<string> names, string fileName) => names.RemoveAll(name => SameName(name, fileName));

    /// <summary>File names compare without case, also after loading (the serialiser creates ordinary dictionaries).</summary>
    public void Normalise()
    {
        Favourites ??= new();
        FullQuality ??= new();
        HeavyConfirmed ??= new();
        WallpaperByDisplay ??= new();
        Monitors ??= new();
        Filters ??= new();
        Background ??= "#000000";
        LibraryPath ??= "";
        LastUsed = Rekey(LastUsed);
        Seeded = Rekey(Seeded);
        Overrides = Rekey(Overrides);
        WebMuted = Rekey(WebMuted);
        Costs = Rekey(Costs);
        LightCosts = Rekey(LightCosts);
        foreach (var monitor in Monitors.Values)
        {
            monitor.Filters ??= new();
        }
    }

    private static Dictionary<string, T> NewMap<T>() => new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, T> Rekey<T>(Dictionary<string, T>? map) =>
        map == null ? NewMap<T>() : new Dictionary<string, T>(map, StringComparer.OrdinalIgnoreCase);
}
