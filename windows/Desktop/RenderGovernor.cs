using System.Diagnostics;
using System.Windows.Threading;
using Wallpepper.Desktop.Performance;
using Wallpepper.Interop;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Desktop;

/// <summary>
/// Decides once a second what every display does: a display stops rendering entirely when the desktop on it is
/// covered by a maximised or full-screen window, the session is locked, the display is off, or (optionally) on
/// battery; the resource level is fitted; a display that should render but does not is recovered. UI thread only.
/// </summary>
internal sealed class RenderGovernor : IDisposable
{
    private const double TickSeconds = 1;

    /// <summary>While everything is paused there is little to watch: look less often.</summary>
    private const double PausedTickSeconds = 5;
    private const float MaxIdleDim = 0.95f;

    private readonly SettingsStore _settings;
    private readonly Renderer _renderer;
    private readonly DesktopHost _host;
    private readonly SessionState _session;
    private readonly PerformanceController _fitter;
    private readonly StallWatchdog _watchdog = new StallWatchdog();
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _ui;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly Dictionary<string, string> _coveredBy = new Dictionary<string, string>();

    public RenderGovernor(SettingsStore settings, Renderer renderer, DesktopHost host, SessionState session, PerformanceController fitter)
    {
        _settings = settings;
        _renderer = renderer;
        _host = host;
        _session = session;
        _fitter = fitter;
        _ui = Dispatcher.CurrentDispatcher;
        _session.Changed += OnSessionChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(TickSeconds) };
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
    }

    /// <summary>The status line changed.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised after every evaluation (web wallpapers follow the same pause rules).</summary>
    public event Action? Evaluated;

    /// <summary>A wallpaper window was destroyed from outside (Explorer rebuilt the desktop layer). Raised after the evaluation.</summary>
    public event Action? WindowsLost;

    /// <summary>
    /// No frames for a while although nothing is paused: the windows and the device should be rebuilt. Raised after the evaluation.
    /// </summary>
    public event Action? RecoveryNeeded;

    public string Status { get; private set; } = "";

    /// <summary>Displays that render right now, and all displays.</summary>
    public int Running { get; private set; }

    public int Total { get; private set; }

    public bool OnBattery { get; private set; }

    /// <summary>The reason everything is paused, or null.</summary>
    public PauseReason? GlobalReason { get; private set; }

    public bool BenchmarkRunning { get; private set; }

    /// <summary>A display should be rendering but presents no frames (shown in the status; recovery is under way).</summary>
    public bool Stalled { get; private set; }

    /// <summary>The level in force right now (one lower on battery); null = manual.</summary>
    public ResourceLevel? Level { get; private set; }

    /// <summary>Self-test: the covered, battery and remote rules are ignored, so that every display renders at the chosen level.</summary>
    public bool IgnoreRules { get; set; }

    /// <summary>Someone is looking at the GPU numbers (library window, debug log): keep the timing queries on.</summary>
    public bool MeasureWanted { get; set; }

    public void Evaluate()
    {
        var slots = _host.Slots;
        if (slots.Any(slot => !User32.IsWindow(slot.Target.Hwnd)))
        {
            _ui.InvokeAsync(() => WindowsLost?.Invoke());   // the handler builds new windows and evaluates again
            return;
        }

        var config = _settings.Config;
        BenchmarkRunning = SingleInstance.BenchmarkIsRunning;
        _renderer.Suspended = BenchmarkRunning;

        var power = IgnoreRules ? default : SessionState.Power();
        Level = ResourceLevel.Get(config.Resource, power.OnBattery);
        int manualFps = power.OnBattery ? Math.Min(config.BatteryFps, config.Fps) : config.Fps;
        _renderer.DefaultFps = Level == null ? manualFps : Level.Fps > 0 ? Level.Fps : ResourceLevel.FallbackFps;

        PauseReason? global = PauseRules.Global(new PauseInputs
        {
            BenchmarkRunning = BenchmarkRunning,
            UserPaused = config.UserPaused,
            DisplayOff = _session.DisplayOff,
            Locked = _session.IsLocked(),
            RemoteSession = _session.Remote,
            BatterySaver = power.BatterySaver,
            OnBattery = power.OnBattery,
            PauseWhenLocked = config.PauseWhenLocked,
            PauseOnBatterySaver = config.PauseOnBatterySaver,
            PauseOnBattery = config.PauseOnBattery,
            IgnoreRules = IgnoreRules,
        });

        bool lookForCovers = config.PauseWhenCovered && global == null && !IgnoreRules;
        HashSet<string> covered = lookForCovers ? CoverScan.Scan(slots, _ownProcessId, _coveredBy) : new HashSet<string>();
        if (!lookForCovers)
        {
            _coveredBy.Clear();
        }

        double now = _clock.Elapsed.TotalSeconds;
        bool stalled = false;
        int running = 0;
        var notes = new List<string>();
        foreach (var slot in slots)
        {
            config.Monitors.TryGetValue(slot.Id, out MonitorSettings? monitor);
            PauseReason? reason = PauseRules.ForDisplay(global, covered.Contains(slot.Id), monitor?.KeepRunningWhenCovered ?? false);
            slot.PauseReason = reason;
            if (reason == null)
            {
                running++;
            }
            else
            {
                notes.Add($"display {slot.Index + 1} {PauseReasonText.Of(reason.Value)}");
            }
            ApplyManualSettings(slot, monitor, power.OnBattery);

            var stats = slot.Target.Stats;
            bool expectFrames = reason == null && slot.Target.Settings.ShaderPath != null
                && !_renderer.Busy && !_renderer.Suspended && !stats.AwaitingShader;
            if (_watchdog.Check(slot, now, expectFrames))
            {
                stalled = true;
                running--;
                notes.Add($"display {slot.Index + 1} is not rendering (recovering)");
            }
        }

        _fitter.Update(slots, Level, now);
        _renderer.MeasureGpu = MeasureWanted || _fitter.NeedsSample;

        bool idle = config.DimOnIdle && running > 0 && User32.IdleSeconds() >= config.IdleSeconds;
        _renderer.IdleDimTarget = idle ? Math.Clamp(config.IdleDim, 0f, MaxIdleDim) : 0f;
        _renderer.Wake();

        Running = running;
        Total = slots.Count;
        OnBattery = power.OnBattery;
        GlobalReason = global;
        Stalled = stalled;
        if (_watchdog.ShouldRecover(stalled, now, slots))
        {
            _ui.InvokeAsync(() => RecoveryNeeded?.Invoke());
        }

        string status = StatusText.Format(global, running, slots, Level, manualFps, power.OnBattery, _renderer.SoftwareRendering, notes);
        if (status != Status)
        {
            Status = status;
            StatusChanged?.Invoke();
            if (_coveredBy.Count > 0)
            {
                Log.Write("covered: " + string.Join(", ", _coveredBy.Select(pair => pair.Key + " by " + pair.Value)));
            }
        }
        Evaluated?.Invoke();

        bool nothingToWatch = global != null && global != PauseReason.BenchmarkRunning;
        _timer.Interval = TimeSpan.FromSeconds(nothingToWatch ? PausedTickSeconds : TickSeconds);
    }

    public void Dispose()
    {
        _timer.Stop();
        _session.Changed -= OnSessionChanged;
    }

    private void OnSessionChanged() => _ui.InvokeAsync(Evaluate);

    /// <summary>
    /// Sets pause, and for a display that is not fitted its own frame rate and resolution. A display with its own
    /// values (Settings > Displays), or the manual level, is left alone by the performance fitter.
    /// </summary>
    private void ApplyManualSettings(DisplaySlot slot, MonitorSettings? monitor, bool onBattery)
    {
        bool paused = slot.IsPaused;
        slot.Fit.Manual = Level == null || (monitor?.IsPinned ?? false);
        if (!slot.Fit.Manual)
        {
            slot.Target.Update(settings => settings with { Paused = paused });
            return;
        }
        int fps = monitor != null && monitor.Fps > 0 ? monitor.Fps : 0;
        if (fps > 0 && onBattery)
        {
            fps = Math.Min(fps, _renderer.DefaultFps);
        }
        float scale = monitor != null && monitor.RenderScale > 0 ? monitor.RenderScale : 0f;
        slot.Target.Update(settings => settings with { Paused = paused, FpsCap = fps, Scale = scale });
    }
}
