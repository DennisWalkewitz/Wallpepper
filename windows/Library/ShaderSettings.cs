using Wallpepper.Desktop.Performance;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Library;

/// <summary>
/// The values shaders are compiled with: the user's own settings ("overrides") and the light settings the performance
/// fitter switches on. The UI thread changes them; the compile thread reads them through <see cref="LoadSource"/>,
/// which only ever sees a complete, immutable snapshot.
/// </summary>
internal sealed class ShaderSettings : IShaderCosts
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    /// <summary>Path -> values. Dictionaries inside a snapshot are never changed after it is published.</summary>
    private sealed record Snapshot(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Overrides,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Light);

    private readonly SettingsStore _settings;
    private readonly ShaderLibrary _library;
    private volatile Snapshot _snapshot;

    public ShaderSettings(SettingsStore settings, ShaderLibrary library)
    {
        _settings = settings;
        _library = library;
        _snapshot = new Snapshot(SnapshotOfOverrides(), NewMap());
    }

    /// <summary>The source a shader is compiled from changed (path, or null for all): it has to be compiled again.</summary>
    public event Action<string?>? SourceChanged;

    /// <summary>A shader was switched to or from its light settings, or the full-quality choice changed.</summary>
    public event Action? QualityChanged;

    // ---------------------------------------------------------------- the user's own values
    /// <summary>The user's values for a shader's settings (define name -> literal); empty when there are none.</summary>
    public IReadOnlyDictionary<string, string> OverridesFor(string path) =>
        _snapshot.Overrides.TryGetValue(Path.GetFileName(path), out var values) ? values : NoValues;

    /// <summary>Sets several values at once (a colour is three); a null value puts the shader's default back.</summary>
    public void SetOverrides(string path, IReadOnlyDictionary<string, string?> changes)
    {
        string file = Path.GetFileName(path);
        var values = new Dictionary<string, string>(OverridesFor(path), StringComparer.Ordinal);
        foreach (var (name, value) in changes)
        {
            if (name == ShaderCompiler.BenchmarkDefine)
            {
                continue;   // the benchmark switch belongs to the benchmark alone
            }
            if (value == null)
            {
                values.Remove(name);
            }
            else if (ShaderCompiler.IsNumber(value))
            {
                values[name] = value;
            }
        }
        _settings.Change(SettingsArea.None, config =>
        {
            if (values.Count == 0)
            {
                config.Overrides.Remove(file);
            }
            else
            {
                config.Overrides[file] = new Dictionary<string, string>(values);
            }
        });
        PublishOverrides(path);
    }

    public void ResetOverrides(string path)
    {
        _settings.Change(SettingsArea.None, config => config.Overrides.Remove(Path.GetFileName(path)));
        PublishOverrides(path);
    }

    /// <summary>The library folder changed: paths differ, file names (which the settings are stored by) do not.</summary>
    public void Reload()
    {
        _snapshot = new Snapshot(SnapshotOfOverrides(), NewMap());
        SourceChanged?.Invoke(null);
    }

    // ---------------------------------------------------------------- light settings
    public bool IsLight(string path) => _snapshot.Light.ContainsKey(path);

    /// <summary>The cheaper values a shader declares with "// @light: NAME=value, ..."; empty when it declares none.</summary>
    public IReadOnlyDictionary<string, string> LightSettings(string path) => _library.Metadata(path).LightSettings;

    public bool WantsFullQuality(string path) => Config.ContainsName(_settings.Config.FullQuality, Path.GetFileName(path));

    public bool CanUseLight(string path) => LightSettings(path).Count > 0 && !WantsFullQuality(path);

    public void UseLight(string path)
    {
        if (IsLight(path))
        {
            return;
        }
        var light = NewMap(_snapshot.Light);
        light[path] = new Dictionary<string, string>(LightSettings(path));
        _snapshot = _snapshot with { Light = light };
        Log.Write($"light settings for {Path.GetFileName(path)} (too heavy for the resource level at its normal settings)");
        SourceChanged?.Invoke(path);
        QualityChanged?.Invoke();
    }

    /// <summary>
    /// "Use full quality anyway" and back: the user decides against (or for) fitting this shader with its light settings.
    /// </summary>
    public void SetFullQuality(string path, bool fullQuality)
    {
        string file = Path.GetFileName(path);
        _settings.Change(SettingsArea.Governor, config =>
        {
            Config.RemoveName(config.FullQuality, file);
            if (fullQuality)
            {
                config.FullQuality.Add(file);
            }
        });
        if (IsLight(path))
        {
            var light = NewMap(_snapshot.Light);
            light.Remove(path);
            _snapshot = _snapshot with { Light = light };
            SourceChanged?.Invoke(path);
        }
        QualityChanged?.Invoke();
    }

    /// <summary>Every shader goes back to its normal settings (the resource level changed: all are fitted from scratch).</summary>
    public void ClearLight()
    {
        var paths = _snapshot.Light.Keys.ToList();
        if (paths.Count == 0)
        {
            return;
        }
        _snapshot = _snapshot with { Light = NewMap() };
        foreach (string path in paths)
        {
            SourceChanged?.Invoke(path);
        }
        QualityChanged?.Invoke();
    }

    // ---------------------------------------------------------------- costs
    public double KnownCost(string path, bool light)
    {
        var costs = light ? _settings.Config.LightCosts : _settings.Config.Costs;
        return costs.TryGetValue(Path.GetFileName(path), out float ms) ? ms : 0;
    }

    /// <summary>Remembers a measured cost (GPU ms per 1080p frame) for the settings the shader runs with right now.</summary>
    public void StoreCost(string path, float ms)
    {
        if (ms <= 0)
        {
            return;
        }
        bool light = IsLight(path);
        string file = Path.GetFileName(path);
        _settings.Change(SettingsArea.None, config => (light ? config.LightCosts : config.Costs)[file] = ms);
    }

    // ---------------------------------------------------------------- source
    /// <summary>
    /// Shader source as it is compiled for display: the light settings when the fitter switched them on, the user's own
    /// settings on top (they always win), and the benchmark switch of a shader tagged "benchmark" forced off (only
    /// "--bench" runs the heavy reference workload). Safe to call from any thread.
    /// </summary>
    public string LoadSource(string path)
    {
        var snapshot = _snapshot;
        string source = File.ReadAllText(path);
        if (snapshot.Light.TryGetValue(path, out var light))
        {
            source = ShaderCompiler.ApplyOverrides(source, light);
        }
        if (snapshot.Overrides.TryGetValue(Path.GetFileName(path), out var overrides))
        {
            source = ShaderCompiler.ApplyOverrides(source, overrides);
        }
        return ShaderCompiler.WithoutBenchmarkMode(source);
    }

    /// <summary>A stable short text that changes with the user's values of a shader (thumbnails are cached per value set).</summary>
    public string OverridesKey(string path) =>
        string.Join(";", OverridesFor(path).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));

    private void PublishOverrides(string path)
    {
        _snapshot = _snapshot with { Overrides = SnapshotOfOverrides() };
        SourceChanged?.Invoke(path);
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SnapshotOfOverrides()
    {
        var copy = NewMap();
        foreach (var (file, values) in _settings.Config.Overrides)
        {
            copy[file] = new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        return copy;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> NewMap(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? from = null)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (from != null)
        {
            foreach (var (key, value) in from)
            {
                map[key] = value;
            }
        }
        return map;
    }
}
