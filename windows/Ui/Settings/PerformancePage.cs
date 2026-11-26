using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Wallpepper.Desktop;
using Wallpepper.Desktop.Performance;
using Wallpepper.Settings;

namespace Wallpepper.Ui.Settings;

/// <summary>
/// Settings > Performance: the resource level, the live readout per display, manual rendering values, the pause rules, the benchmark.
/// </summary>
internal sealed class PerformancePage
{
    private static readonly int[] FpsChoices = { 15, 24, 30, 60 };
    private static readonly int[] BatteryFpsChoices = { 10, 15, 24, 30 };
    private static readonly int[] ScaleChoices = { 50, 67, 75, 100 };

    private readonly App _app;
    private readonly StackPanel _host;
    private readonly Dictionary<string, PerfGraph> _graphs = new Dictionary<string, PerfGraph>();
    private readonly Process _process = Process.GetCurrentProcess();
    private TextBlock? _processReadout;
    private TimeSpan _lastCpu;
    private long _lastCpuAt;

    public PerformancePage(App app, StackPanel host, Action<string, bool> toast)
    {
        _app = app;
        _host = host;
        Benchmark = new BenchmarkPanel(app, toast);
    }

    public BenchmarkPanel Benchmark { get; }

    private Config Config => _app.Settings.Config;

    public void Build()
    {
        _host.Children.Clear();
        _graphs.Clear();
        AddResourceLevel();
        AddLiveReadout();
        AddRendering();
        AddPauseRules();
        _host.Children.Add(SettingsRows.Caption("Benchmark"));
        _host.Children.Add(Benchmark.Build());
    }

    /// <summary>Once a second while the window is open. Returns the short text for the status line.</summary>
    /// <param name="pageVisible">the page itself is on screen: the graphs are redrawn</param>
    public string Tick(bool pageVisible)
    {
        _process.Refresh();
        long now = Stopwatch.GetTimestamp();
        TimeSpan cpuTime = _process.TotalProcessorTime;
        double elapsed = Stopwatch.GetElapsedTime(_lastCpuAt, now).TotalSeconds;
        double cpu = _lastCpuAt == 0 ? 0 : (cpuTime - _lastCpu).TotalSeconds / Math.Max(0.001, elapsed) / Environment.ProcessorCount * 100;
        _lastCpu = cpuTime;
        _lastCpuAt = now;
        long megabytes = _process.WorkingSet64 >> 20;

        double load = 0;
        int active = 0;
        foreach (var display in _app.Displays)
        {
            var stats = display.Target.Stats;
            bool running = !display.IsPaused && display.Target.Settings.ShaderPath != null;
            float gpuMs = running ? stats.GpuMs : 0;
            double share = gpuMs * stats.Fps / 1000.0;
            if (running)
            {
                load += share;
                active++;
            }
            if (_graphs.TryGetValue(display.Id, out PerfGraph? graph))
            {
                graph.Add(gpuMs);
                if (pageVisible)
                {
                    graph.Show(Readout(display, running, share));
                }
            }
        }
        string process = $"CPU {cpu.ToString("0.0", CultureInfo.InvariantCulture)}% · {megabytes} MB";
        if (pageVisible && _processReadout != null)
        {
            _processReadout.Text = process;
        }
        if (active == 0)
        {
            return process.Replace(" · ", "  ·  ");
        }
        ResourceLevel? level = _app.Governor.Level;
        string budget = level != null && level.IsLimited ? $" of {level.Share * 100:0}%" : "";
        return $"GPU {load * 100:0}%{budget}  ·  " + process.Replace(" · ", "  ·  ");
    }

    private static string Readout(DisplaySlot display, bool running, double share)
    {
        string pause = display.PauseReason is PauseReason reason ? PauseReasonText.Of(reason) : "";
        if (display.ShowsWebPage)
        {
            return display.IsPaused ? "web page · hidden · " + pause : "web page · not measured";
        }
        if (!running)
        {
            return display.IsPaused ? "paused · " + pause : "paused";
        }
        var stats = display.Target.Stats;
        var fit = display.Fit;
        string budget = fit.Budget > 0 && !fit.Manual ? $" of {fit.Budget * 100:0}%" : "";
        string note = fit.Manual ? " · manual" : fit.TooHeavy ? " · too heavy" : fit.Light ? " · light settings" : "";
        return $"{stats.EffectiveScale * 100:0}% · {stats.Fps:0} fps · GPU {share * 100:0}%{budget}{note}";
    }

    /// <summary>Resource use: a share of the GPU; resolution and frame rate are fitted to it per shader and display.</summary>
    private void AddResourceLevel()
    {
        var ids = ResourceLevel.All.Select(level => level.Id).Append(ResourceLevelId.Manual).ToList();
        var names = ResourceLevel.All.Select(level => level.Name).Append("Manual").ToList();
        _host.Children.Add(SettingsRows.Caption("Resource use"));
        _host.Children.Add(SettingsRows.Segments(names, Math.Max(0, ids.IndexOf(Config.Resource)), index =>
        {
            _app.SetResourceLevel(ids[index]);
            Build();
        }));

        ResourceLevel? chosen = ResourceLevel.Get(Config.Resource, false);
        string text;
        if (chosen == null)
        {
            text = "You set frame rate and resolution yourself below. Nothing is adjusted automatically.";
        }
        else if (!chosen.IsLimited)
        {
            text = "No limit: full resolution at the display's refresh rate.";
        }
        else
        {
            text = $"The wallpaper may use about {chosen.Share * 100:0}% of the GPU, at up to {chosen.Fps} frames per second. "
                + "Resolution and frame rate are fitted to each shader and display; heavy shaders switch to their light settings. "
                + "On battery the next lower level applies.";
        }
        _host.Children.Add(SettingsRows.Hint(text, new Thickness(2, 8, 0, 20)));
    }

    private void AddLiveReadout()
    {
        _host.Children.Add(SettingsRows.Caption("Live"));
        var rows = new List<UIElement?>();
        foreach (var display in _app.Displays)
        {
            var graph = new PerfGraph("Display " + (display.Index + 1));
            _graphs[display.Id] = graph;
            rows.Add(graph.Row);
        }
        _processReadout = SettingsRows.Value();
        _processReadout.FontSize = 11.5;
        rows.Add(SettingsRows.Row("Wallpepper itself", null, _processReadout));
        _host.Children.Add(SettingsRows.Group(rows.ToArray()));
    }

    private void AddRendering()
    {
        _host.Children.Add(SettingsRows.Caption("Rendering"));
        var speed = SettingsRows.SliderRow(
            "Animation speed", 0.1, 3, Config.Speed, 1,
            value => value.ToString("0.00", CultureInfo.InvariantCulture) + "×",
            value => _app.Settings.Change(SettingsArea.Rendering, config => config.Speed = (float)Math.Round(value, 2)));
        if (Config.Resource != ResourceLevelId.Manual)
        {
            _host.Children.Add(SettingsRows.Group(speed));
            return;
        }
        _host.Children.Add(SettingsRows.Group(
            SettingsRows.SegmentRow(
                "Frame rate", FpsChoices.Select(fps => fps + " fps").ToList(), SettingsRows.Nearest(FpsChoices, Config.Fps),
                index => _app.Settings.Change(SettingsArea.Governor, config => config.Fps = FpsChoices[index])),
            SettingsRows.SegmentRow(
                "Frame rate on battery", BatteryFpsChoices.Select(fps => fps + " fps").ToList(), SettingsRows.Nearest(BatteryFpsChoices, Config.BatteryFps),
                index => _app.Settings.Change(SettingsArea.Governor, config => config.BatteryFps = BatteryFpsChoices[index])),
            SettingsRows.SegmentRow(
                "Render resolution", ScaleChoices.Select(scale => scale + "%").ToList(), SettingsRows.Nearest(ScaleChoices, Config.RenderScale * 100),
                index => _app.Settings.Change(SettingsArea.Rendering, config => config.RenderScale = ScaleChoices[index] / 100f)),
            speed));
    }

    private void AddPauseRules()
    {
        void Change(Action<Config> change) => _app.Settings.Change(SettingsArea.Governor, change);
        _host.Children.Add(SettingsRows.Caption("Pause automatically"));
        _host.Children.Add(SettingsRows.Group(
            SettingsRows.SwitchRow(
                "When a window covers the display", "Maximised and full-screen apps hide the desktop anyway.", Config.PauseWhenCovered,
                on => Change(config => config.PauseWhenCovered = on)),
            SettingsRows.SwitchRow("When the screen is locked", null, Config.PauseWhenLocked, on => Change(config => config.PauseWhenLocked = on)),
            SettingsRows.SwitchRow("When battery saver is on", null, Config.PauseOnBatterySaver, on => Change(config => config.PauseOnBatterySaver = on)),
            SettingsRows.SwitchRow("Whenever on battery", null, Config.PauseOnBattery, on => Change(config => config.PauseOnBattery = on))));
    }
}
