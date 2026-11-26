using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wallpepper.Desktop.Performance;
using Wallpepper.Library;
using Wallpepper.Ui;

namespace Wallpepper.SelfTesting;

/// <summary>
/// Browsing the library while a wallpaper runs: selects cards one after the other and watches that the window stays
/// responsive, that the preview follows quickly, that the wallpaper keeps its frame rate, and that memory and handles
/// do not grow.
/// </summary>
internal sealed class BrowseCheck
{
    private const string WallpaperShader = "drifting-clouds.hlsl";
    private const int Selections = 100;
    private const int DwellMs = 300;
    private const int PreviewTimeoutSeconds = 60;
    private const int ThumbnailTimeoutSeconds = 600;

    // what "stays fixed" means
    private const double MaxUiLagMs = 100;
    private const double MaxUiLagOver50Share = 0.005;
    private const double MaxCachedPreviewMs = 300;
    private const double MinWallpaperFpsShare = 0.8;
    private const int MaxPrivateGrowthMb = 16;
    private const int MaxHandleGrowth = 40;

    private readonly App _app;
    private readonly SelfTestReport _report;
    private readonly Process _process = Process.GetCurrentProcess();

    public BrowseCheck(App app, SelfTestReport report)
    {
        _app = app;
        _report = report;
    }

    public async Task RunAsync()
    {
        _app.SetResourceLevel(ResourceLevel.Default);
        string wallpaper = _app.Library.Items.FirstOrDefault(path => Path.GetFileName(path) == WallpaperShader)
            ?? _app.Library.Items.First(ShaderLibrary.IsShader);
        _app.Wallpaper.Apply(wallpaper);
        await Probe.ShaderOnScreen(_app, wallpaper, 120);
        await Task.Delay(8000);
        _report.Note("memory, tray only: " + Probe.Memory(_process));

        _app.ShowLibrary();
        LibraryWindow window = _app.LibraryWindow!;
        var opened = Stopwatch.StartNew();
        await Task.Delay(2000);
        while (window.PendingThumbnails > 0 && opened.Elapsed.TotalSeconds < ThumbnailTimeoutSeconds)
        {
            await Task.Delay(250);
        }
        await Task.Delay(1000);
        _report.Note($"memory, library open (pictures ready after {Probe.Number(opened.Elapsed.TotalSeconds)} s): " + Probe.Memory(_process));

        var paths = window.VisiblePaths.Where(ShaderLibrary.IsShader).ToList();
        if (paths.Count == 0)
        {
            _report.Check("browse", false, "the library shows no shaders");
            return;
        }

        var heartbeat = new UiHeartbeat(Dispatcher.CurrentDispatcher);
        foreach (var slot in _app.Displays)
        {
            slot.Target.RestartGapStats();
        }
        await Task.Delay(1200);   // the reset takes effect with the next frame
        int[] framesBefore = Probe.Frames(_app.Displays);
        var total = Stopwatch.StartNew();
        var cached = new List<double>();
        var compiled = new List<double>();
        long privateAtHalf = 0;
        int handlesAtHalf = 0;

        for (int n = 0; n < Selections; n++)
        {
            string path = paths[n % paths.Count];
            var selected = Stopwatch.StartNew();
            heartbeat.Doing = $"selection {n + 1} ({Path.GetFileName(path)})";
            window.SelectForTest(path);
            bool neededCompile = false;
            while (!window.Preview.Shows(path) && !_app.Status.HasError(path) && selected.Elapsed.TotalSeconds < PreviewTimeoutSeconds)
            {
                neededCompile |= _app.Status.IsCompiling(path);
                await Task.Delay(5);
            }
            (neededCompile ? compiled : cached).Add(selected.Elapsed.TotalMilliseconds);
            await Task.Delay(DwellMs);
            if (n == Selections / 2 - 1)
            {
                _process.Refresh();
                privateAtHalf = _process.PrivateMemorySize64;
                handlesAtHalf = _process.HandleCount;
                _report.Note($"memory, after {n + 1} selections: " + Probe.Memory(_process));
            }
        }

        double seconds = total.Elapsed.TotalSeconds;
        var lag = heartbeat.Stop();
        _report.Note($"memory, after {Selections} selections: " + Probe.Memory(_process));
        _process.Refresh();
        int privateGrowthMb = (int)((_process.PrivateMemorySize64 - privateAtHalf) >> 20);
        int handleGrowth = _process.HandleCount - handlesAtHalf;

        double[] fps = _app.Displays.Select((slot, i) => (slot.Target.Stats.Frames - framesBefore[i]) / seconds).ToArray();
        int[] caps = _app.Displays.Select(slot => slot.Target.Settings.FpsCap > 0 ? slot.Target.Settings.FpsCap : _app.Renderer.DefaultFps).ToArray();
        float worstGap = _app.Displays.Max(slot => slot.Target.Stats.WorstGapMs);

        cached.Sort();
        compiled.Sort();
        double cachedMedian = Percentile(cached, 0.5);
        _report.Check($"browse: window stays responsive ({Selections} selections, {Path.GetFileName(wallpaper)} live)",
            lag.Max <= MaxUiLagMs && lag.Over50 <= lag.Count * MaxUiLagOver50Share,
            $"an input waits for the UI thread: median {Probe.Number(lag.Median)} ms, 95% {Probe.Number(lag.P95)} ms, max {Probe.Number(lag.Max)} ms, "
            + $"over 50 ms {lag.Over50} of {lag.Count}; longest during {lag.WorstDuring}");
        _report.Check("browse: preview follows the selection", cached.Count > 0 && cachedMedian <= MaxCachedPreviewMs,
            $"selection to first preview frame, compiled shader: median {Probe.Number(cachedMedian, "0")} ms, 95% {Probe.Number(Percentile(cached, 0.95), "0")} ms, "
            + $"max {Probe.Number(Percentile(cached, 1), "0")} ms ({cached.Count} selections)"
            + (compiled.Count > 0
                ? $"; shader that had to be compiled first (progress shown meanwhile): median {Probe.Number(Percentile(compiled, 0.5), "0")} ms, "
                  + $"max {Probe.Number(Percentile(compiled, 1), "0")} ms ({compiled.Count} selections)"
                : ""));
        _report.Check("browse: wallpaper keeps its frame rate",
            fps.Select((value, i) => value >= caps[i] * MinWallpaperFpsShare).All(ok => ok),
            $"{string.Join("+", fps.Select(value => Probe.Number(value)))} fps of {Probe.Join(caps)} wanted, longest gap between two frames {worstGap:0} ms");
        _report.Check("browse: memory and handles stay flat", privateGrowthMb <= MaxPrivateGrowthMb && handleGrowth <= MaxHandleGrowth,
            $"from selection {Selections / 2} to {Selections}: private bytes {privateGrowthMb:+0;-0;0} MB, handles {handleGrowth:+0;-0;0}");

        window.Close();
        await Task.Delay(3000);
        _report.Note("memory, library closed again: " + Probe.Memory(_process));
    }

    private static double Percentile(List<double> sorted, double share) =>
        sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, sorted.Count * share)];

    /// <summary>
    /// Measures what a user would feel: a background thread hands the UI thread an item of input priority every few
    /// milliseconds and notes how long it waits for its turn.
    /// </summary>
    private sealed class UiHeartbeat
    {
        private const int PauseMs = 5;

        private readonly List<double> _waits = new List<double>();
        private readonly Dispatcher _ui;
        private readonly Thread _thread;
        private volatile bool _run = true;
        private double _worstMs;
        private string _worstDuring = "";

        public UiHeartbeat(Dispatcher ui)
        {
            _ui = ui;
            _thread = new Thread(Run) { IsBackground = true, Name = "Wallpepper self-test heartbeat" };
            _thread.Start();
        }

        /// <summary>What the test is doing right now; noted with the longest wait.</summary>
        public string Doing { get; set; } = "";

        public (double Median, double P95, double Max, int Over50, int Count, string WorstDuring) Stop()
        {
            _run = false;
            _thread.Join(2000);
            lock (_waits)
            {
                var sorted = _waits.OrderBy(wait => wait).ToList();
                int over50 = sorted.Count(wait => wait > 50);
                return (Percentile(sorted, 0.5), Percentile(sorted, 0.95), Percentile(sorted, 1), over50, sorted.Count, _worstDuring);
            }
        }

        private void Run()
        {
            while (_run)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    _ui.Invoke(() => { }, DispatcherPriority.Input);
                }
                catch (TaskCanceledException)
                {
                    return;   // the dispatcher is shutting down
                }
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                lock (_waits)
                {
                    if (_waits.Count == 0 || ms > _worstMs)
                    {
                        _worstMs = ms;
                        _worstDuring = Doing;
                    }
                    _waits.Add(ms);
                }
                Thread.Sleep(PauseMs);
            }
        }
    }
}
