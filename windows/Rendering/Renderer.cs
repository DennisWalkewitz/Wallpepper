using System.Diagnostics;
using System.Threading;
using Wallpepper.Interop;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>
/// One Direct3D 11 device and one render thread for all targets. Does no work at all when every target is paused.
/// Shaders are compiled on a separate thread and thumbnails are drawn in small slices between frames, so nothing ever
/// holds up a wallpaper frame. Public members are called from the UI thread unless they say otherwise.
/// </summary>
internal sealed class Renderer : IDisposable
{
    private const int MaxLoggedFailures = 5;
    private const int MaxRetryDelayMs = 10000;
    private const int RetryStepMs = 500;
    private const int IdleWaitMs = 1000;
    private const int SuspendedWaitMs = 250;

    /// <summary>With no frames to draw, thumbnail slices are spaced out so that the GPU stays mostly idle.</summary>
    private const int SliceGapMs = 8;
    private const int SliceWaitMs = 15;
    private const double IdleDimPerSecond = 1.5;

    private readonly object _targetsGate = new object();
    private readonly Queue<Action> _jobs = new Queue<Action>();
    private readonly AutoResetEvent _wake = new AutoResetEvent(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IntPtr[] _waitHandles = new IntPtr[2];
    private readonly List<RenderTarget> _toPresent = new List<RenderTarget>();
    private readonly ShaderCompileQueue _compileQueue;
    private readonly ShaderCache _cache;

    private volatile RenderTarget[] _targets = Array.Empty<RenderTarget>();
    private volatile float[] _background = { 0, 0, 0, 1 };
    private volatile float[] _monitorRects = { 0, 0, 1, 1 };
    private volatile bool _run;
    private volatile bool _deviceLost;
    private volatile bool _busy;
    private volatile bool _suspended;
    private volatile bool _measureGpu;
    private volatile int _defaultFps = 60;
    private volatile float _defaultScale = 0.75f;
    private volatile float _speed = 1f;
    private volatile float _idleDimTarget;
    private volatile string? _adapterName;
    private volatile string? _driverVersion;
    private volatile bool _softwareRendering;

    // render thread only
    private Thread? _thread;
    private GraphicsDevice? _device;
    private TargetRenderer? _targetRenderer;
    private IntPtr _timer;
    private bool _timerPeriodHeld;
    private bool _highResolutionTimer;
    private float _dim;
    private double _lastLoopAt;
    private int _failures;
    private bool _presentedSinceFailure;

    /// <param name="loadSource">final HLSL source for a library path (the user's settings applied); called on the compile thread</param>
    public Renderer(Func<string, string> loadSource)
    {
        _compileQueue = new ShaderCompileQueue(loadSource, IsInUse);
        _cache = new ShaderCache(_compileQueue, Post);
        _cache.CompileStarted += path => CompileStarted?.Invoke(path);
        _cache.Compiled += (path, error) => Compiled?.Invoke(path, error);
        Thumbnails = new ThumbnailRenderer(_cache, IsInUse, Wake, () => _background);
    }

    /// <summary>A shader started compiling (raised on the render thread).</summary>
    public event Action<string>? CompileStarted;

    /// <summary>A shader finished compiling, or a target could not be drawn: path and error or null (render thread).</summary>
    public event Action<string, string?>? Compiled;

    /// <summary>Gallery pictures and cost measurements, drawn in slices between frames.</summary>
    public ThumbnailRenderer Thumbnails { get; }

    /// <summary>Frames per second of targets that name none.</summary>
    public int DefaultFps
    {
        get => _defaultFps;
        set => _defaultFps = value;
    }

    /// <summary>Render resolution of targets that name none.</summary>
    public float DefaultScale
    {
        get => _defaultScale;
        set => _defaultScale = value;
    }

    /// <summary>Animation speed: shader seconds per second.</summary>
    public float Speed
    {
        get => _speed;
        set => _speed = value;
    }

    /// <summary>
    /// GPU timestamp queries on or off (someone looks at the numbers, or the fitter wants a sample). Off = no queries at all.
    /// </summary>
    public bool MeasureGpu
    {
        get => _measureGpu;
        set => _measureGpu = value;
    }

    /// <summary>0..1 extra darkening the renderer eases towards (dim on idle).</summary>
    public float IdleDimTarget
    {
        get => _idleDimTarget;
        set => _idleDimTarget = value;
    }

    /// <summary>Stops all drawing (a GPU benchmark is running and must have the GPU to itself).</summary>
    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    /// <summary>Monitor rectangles in span UV (x, y, width, height each; up to 8). Replaced as a whole.</summary>
    public float[] MonitorRects
    {
        get => _monitorRects;
        set => _monitorRects = value;
    }

    /// <summary>The graphics device is being built or rebuilt (or cannot be built right now): no frames are to be expected.</summary>
    public bool Busy => _busy;

    /// <summary>Name of the graphics adapter in use, once the device exists.</summary>
    public string? AdapterName => _adapterName;

    public string? DriverVersion => _driverVersion;

    /// <summary>No usable GPU was found: Windows' software rasteriser draws the shaders on the CPU.</summary>
    public bool SoftwareRendering => _softwareRendering;

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void Start()
    {
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Wallpepper render", Priority = ThreadPriority.Normal };
        _thread.Start();
    }

    public void AddTarget(RenderTarget target)
    {
        lock (_targetsGate)
        {
            _targets = _targets.Append(target).ToArray();
        }
        _wake.Set();
    }

    public void RemoveTarget(RenderTarget target)
    {
        lock (_targetsGate)
        {
            _targets = _targets.Where(other => !ReferenceEquals(other, target)).ToArray();
        }
        Post(() => target.Resources.ReleaseDeviceObjects());
    }

    /// <summary>The source of a shader (or of all, with null) changed: it is compiled again, the old picture stays until then.</summary>
    public void Invalidate(string? path = null) => Post(() => _cache.Invalidate(path));

    /// <summary>Drops compiled shaders nothing is showing (after the library window closes).</summary>
    public void TrimShaders() => Post(() => _cache.Trim(IsInUse));

    /// <summary>The colour shaders receive as the terminal's background (also shown while a shader is missing).</summary>
    public void SetBackground(float red, float green, float blue)
    {
        _background = new[] { red, green, blue, 1f };
        Wake();
    }

    /// <summary>Ends the render thread's wait: a setting changed or there is work.</summary>
    public void Wake() => _wake.Set();

    /// <summary>Throws the device and every swap chain away and builds them again (also what happens after a real device loss).</summary>
    public void ResetDevice() => Post(() => _deviceLost = true);

    public void Dispose()
    {
        _run = false;
        _wake.Set();
        bool ended = _thread == null || _thread.Join(2000);
        _compileQueue.Dispose();
        if (ended)
        {
            _wake.Dispose();   // a render thread that is still busy keeps its event
        }
    }

    /// <summary>Whether a display, the preview or a queued thumbnail shows or wants the shader of a path. Any thread.</summary>
    private bool IsInUse(string path)
    {
        foreach (var target in _targets)
        {
            if (SamePath(target.Settings.ShaderPath, path) || SamePath(target.Resources.DrawnPath, path))
            {
                return true;
            }
        }
        return Thumbnails.Wants(path);
    }

    private void Post(Action job)
    {
        lock (_jobs)
        {
            _jobs.Enqueue(job);
        }
        _wake.Set();
    }

    // ---------------------------------------------------------------- render thread
    private void Loop()
    {
        CreateTimer();
        _deviceLost = true;   // the first device is built by the same code that rebuilds a lost one
        CompiledShaderStore.Prune();

        while (_run)
        {
            // nothing may end this thread: whatever goes wrong is logged, the device is rebuilt and the loop goes on
            try
            {
                RunJobs();
                if (_suspended)
                {
                    _wake.WaitOne(SuspendedWaitMs);
                    continue;
                }
                if (_deviceLost && !RebuildDevice())
                {
                    continue;
                }
                RunFrame();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                OnLoopFailure(e);
            }
        }

        RunJobs();   // targets removed during shutdown still release their swap chains
        DestroyDevice();
        ReleaseTimer();
    }

    /// <summary>One pass: draw the targets that are due, present them together, do one slice of slow work, wait.</summary>
    private void RunFrame()
    {
        var device = _device!;
        double frameStart = _clock.Elapsed.TotalSeconds;
        EaseIdleDim(frameStart);

        var targets = _targets;
        int active = CountActive(targets, out RenderTarget? only);
        HoldTimerPeriod(active > 0);
        if (active == 0)
        {
            // idle: zero GPU, close to zero CPU; thumbnails are drawn slice by slice with pauses in between
            SlowWork work = Thumbnails.Step(device);
            _wake.WaitOne(work == SlowWork.Worked ? SliceGapMs : work == SlowWork.Waiting ? SliceWaitMs : IdleWaitMs);
            return;
        }

        int sync = active == 1 ? PickVsyncDivisor(only!) : 0;
        var frame = new FrameContext(frameStart, _dim, _defaultScale, _speed, _background, _monitorRects, _measureGpu);
        double nextDue = RenderDueTargets(targets, frame, sync, out bool rendered);
        PresentAll(sync);

        if (rendered)
        {
            Thumbnails.Step(device);   // at most one slice of thumbnail or measurement work per frame gap
        }

        double now = _clock.Elapsed.TotalSeconds;
        if (sync > 0)
        {
            // Present(sync) already waited for the vertical blank; if it did not (occluded, DWM off), fall back to the timer
            double period = 1.0 / TargetFps(only!.Settings);
            if (now - frameStart >= period * 0.5)
            {
                return;
            }
            nextDue = frameStart + period;
        }
        WaitUntil(nextDue - now);
    }

    private int TargetFps(TargetSettings settings) => Math.Max(1, settings.FpsCap > 0 ? settings.FpsCap : _defaultFps);

    /// <summary>
    /// A target takes part in a pass when it has a shader, a window and is not paused; a paused one takes part while a
    /// kick is pending (a newly applied wallpaper draws one frame). A target that failed to draw waits for new input.
    /// </summary>
    private bool IsActive(RenderTarget target, TargetSettings settings)
    {
        var res = target.Resources;
        bool kicked = target.Kicks != res.SeenKicks;
        if ((settings.Paused && !kicked) || settings.ShaderPath == null || !User32.IsWindow(target.Hwnd))
        {
            return false;
        }
        if (res.FaultPath == null)
        {
            return true;
        }
        bool sameInput = SamePath(res.FaultPath, settings.ShaderPath) && res.FaultGeneration == _cache.Generation
            && res.FaultWidth == res.ClientWidth;
        if (sameInput)
        {
            return false;
        }
        res.FaultPath = null;
        return true;
    }

    private int CountActive(RenderTarget[] targets, out RenderTarget? only)
    {
        int active = 0;
        only = null;
        foreach (var target in targets)
        {
            if (IsActive(target, target.Settings))
            {
                active++;
                only = target;
            }
            else
            {
                target.Stats.SetFps(0);
                target.Resources.LastFrameAt = 0;
            }
        }
        return active;
    }

    /// <summary>Eases the idle dim towards its target (only costs anything while it is changing or active).</summary>
    private void EaseIdleDim(double now)
    {
        float target = _idleDimTarget;
        if (_dim != target)
        {
            float step = (float)Math.Clamp((now - _lastLoopAt) * IdleDimPerSecond, 0.0, 0.25);
            _dim = Math.Abs(target - _dim) <= step ? target : _dim + Math.Sign(target - _dim) * step;
        }
        _lastLoopAt = now;
    }

    /// <summary>
    /// One active target whose rate divides the refresh rate: let vsync pace it (smoothest). Returns the divisor, or 0
    /// for the fixed-schedule timer.
    /// </summary>
    private int PickVsyncDivisor(RenderTarget only)
    {
        var settings = only.Settings;
        int fps = TargetFps(settings);
        int hz = Math.Max(1, settings.RefreshHz);
        int divisor = (int)Math.Round((double)hz / fps);
        bool divides = divisor >= 1 && divisor <= 4 && Math.Abs(hz - fps * divisor) <= Math.Max(1, hz / 20);
        return divides ? divisor : 0;
    }

    /// <summary>Draws every active target whose frame is due and collects them for presenting.</summary>
    /// <returns>when the next frame of any target is due.</returns>
    private double RenderDueTargets(RenderTarget[] targets, in FrameContext frame, int sync, out bool rendered)
    {
        double frameStart = frame.Now;
        double nextDue = frameStart + 1.0;
        rendered = false;
        foreach (var target in targets)
        {
            var settings = target.Settings;   // read once: the UI thread may replace it at any moment
            if (!IsActive(target, settings))
            {
                continue;
            }
            var res = target.Resources;
            double period = 1.0 / TargetFps(settings);
            if (sync > 0 || frameStart + 0.0005 >= res.NextDue)
            {
                if (!TryRender(target, settings, frame))
                {
                    break;
                }
                rendered = true;
                NoteFrame(target, frameStart, period);
                // fixed schedule: next = previous deadline + period; resync when more than a period late
                res.NextDue = res.NextDue + period < frameStart ? frameStart + period : res.NextDue + period;
            }
            if (res.NextDue < nextDue)
            {
                nextDue = res.NextDue;
            }
        }
        return nextDue;
    }

    /// <returns>false when the device was lost and the pass has to end.</returns>
    private bool TryRender(RenderTarget target, TargetSettings settings, in FrameContext frame)
    {
        try
        {
            if (_targetRenderer!.Render(target, settings, frame, target.Kicks))
            {
                _toPresent.Add(target);
                if (target.WantsCapture)
                {
                    _targetRenderer.Capture(target);
                }
            }
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException && !_device!.IsLost(e))
        {
            // not the device: this one target cannot be drawn. It is skipped until its shader, size or the cache changes.
            var res = target.Resources;
            res.FaultPath = settings.ShaderPath;
            res.FaultGeneration = _cache.Generation;
            res.FaultWidth = res.ClientWidth;
            Log.Error("render " + Path.GetFileName(settings.ShaderPath), e);
            Compiled?.Invoke(settings.ShaderPath!, "This shader could not be drawn: " + e.Message);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warn("render (graphics device lost)", e);
            _deviceLost = true;
            return false;
        }
    }

    private static void NoteFrame(RenderTarget target, double frameStart, double period)
    {
        var res = target.Resources;
        if (res.SeenGapEpoch != target.GapEpoch)
        {
            res.SeenGapEpoch = target.GapEpoch;
            target.Stats.ResetGaps();
        }
        if (res.LastFrameAt > 0)
        {
            double gap = frameStart - res.LastFrameAt;
            target.Stats.NoteGap(gap * 1000.0, gap > period * 1.5);
        }
        res.LastFrameAt = frameStart;
        res.FpsWindowFrames++;
        if (frameStart - res.FpsWindowStart >= 1.0)
        {
            target.Stats.SetFps((float)(res.FpsWindowFrames / (frameStart - res.FpsWindowStart)));
            res.FpsWindowFrames = 0;
            res.FpsWindowStart = frameStart;
        }
    }

    /// <summary>
    /// Everything is drawn first, then presented together: the displays flip in the same refresh and the compositor
    /// does not get scheduled between two of our frames.
    /// </summary>
    private void PresentAll(int sync)
    {
        foreach (var target in _toPresent)
        {
            if (!_targetRenderer!.Present(target, sync))
            {
                _deviceLost = true;
                break;
            }
            _presentedSinceFailure = true;
        }
        _toPresent.Clear();
        if (_presentedSinceFailure)
        {
            _failures = 0;
        }
    }

    /// <summary>Sleeps on the high-resolution timer; a Wake() (job, settings change) ends the wait early.</summary>
    private void WaitUntil(double seconds)
    {
        if (seconds <= 0.0003)
        {
            return;
        }
        if (_timer == IntPtr.Zero)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(seconds));
            return;
        }
        long due = -(long)(seconds * 10_000_000.0);
        Kernel32.SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        Kernel32.WaitForMultipleObjects(2, _waitHandles, false, 2000);
    }

    private void RunJobs()
    {
        while (true)
        {
            Action job;
            lock (_jobs)
            {
                if (_jobs.Count == 0)
                {
                    return;
                }
                job = _jobs.Dequeue();
            }
            try
            {
                job();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Error("render job", e);
            }
        }
    }

    // ---------------------------------------------------------------- device
    /// <summary>Builds the device (the first time, and again after a loss). Backs off and keeps trying when that fails.</summary>
    /// <returns>true when there is a device to draw with.</returns>
    private bool RebuildDevice()
    {
        if (_device != null && _failures < MaxLoggedFailures)
        {
            Log.Write("graphics device lost or reset - rebuilding it");
        }
        _busy = true;
        DestroyDevice();
        try
        {
            _device = GraphicsDevice.Create();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_failures < MaxLoggedFailures)
            {
                Log.Warn("create the graphics device", e);
            }
            _failures++;
            _wake.WaitOne(Math.Min(MaxRetryDelayMs, RetryStepMs * _failures));
            return false;   // still busy: without a device no frames are to be expected
        }
        _adapterName = _device.AdapterName;
        _driverVersion = _device.DriverVersion;
        _softwareRendering = _device.IsSoftware;
        _targetRenderer = new TargetRenderer(_device, _cache);
        _cache.AttachDevice(_device);
        _deviceLost = false;
        _presentedSinceFailure = false;
        _busy = false;
        return true;
    }

    private void DestroyDevice()
    {
        foreach (var target in _targets)
        {
            target.Resources.ReleaseDeviceObjects();
        }
        Thumbnails.ReleaseDeviceObjects();
        _cache.DetachDevice();
        _targetRenderer = null;
        _device?.Dispose();
        _device = null;
    }

    /// <summary>
    /// An exception outside the drawing of a single target. A loop that keeps failing must not spin or flood the log:
    /// the device is rebuilt with a growing delay, and the count only starts again once a frame was presented.
    /// </summary>
    private void OnLoopFailure(Exception error)
    {
        if (_failures < MaxLoggedFailures)
        {
            Log.Error("render loop", error);
        }
        _failures++;
        _presentedSinceFailure = false;
        _deviceLost = true;
        _wake.WaitOne(Math.Min(MaxRetryDelayMs, RetryStepMs * _failures));
    }

    // ---------------------------------------------------------------- timer
    private void CreateTimer()
    {
        _timer = Kernel32.CreateWaitableTimerEx(
            IntPtr.Zero, null, Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Kernel32.TIMER_ALL_ACCESS);
        _highResolutionTimer = _timer != IntPtr.Zero;
        if (!_highResolutionTimer)
        {
            _timer = Kernel32.CreateWaitableTimerEx(IntPtr.Zero, null, 0, Kernel32.TIMER_ALL_ACCESS);
        }
        _waitHandles[0] = _wake.SafeWaitHandle.DangerousGetHandle();
        _waitHandles[1] = _timer;
    }

    /// <summary>
    /// Before Windows 10 1803 there is no high-resolution timer; only then, and only while frames are drawn, the
    /// system timer is set to 1 ms (it costs power system-wide).
    /// </summary>
    private void HoldTimerPeriod(bool hold)
    {
        if (_highResolutionTimer || hold == _timerPeriodHeld)
        {
            return;
        }
        _timerPeriodHeld = hold;
        if (hold)
        {
            Winmm.timeBeginPeriod(1);
        }
        else
        {
            Winmm.timeEndPeriod(1);
        }
    }

    private void ReleaseTimer()
    {
        HoldTimerPeriod(false);
        if (_timer != IntPtr.Zero)
        {
            Kernel32.CloseHandle(_timer);
            _timer = IntPtr.Zero;
        }
    }
}
