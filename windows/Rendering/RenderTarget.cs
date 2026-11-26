using System.Threading;

namespace Wallpepper.Rendering;

/// <summary>
/// One window the renderer draws into: a display's wallpaper window or the library's preview.
/// <see cref="Settings"/> belong to the UI thread, <see cref="Stats"/> and <see cref="Resources"/> to the render thread.
/// </summary>
internal sealed class RenderTarget
{
    private volatile TargetSettings _settings;
    private Action<FrameCapture>? _capture;
    private int _kicks;
    private int _timingEpoch;
    private int _gapEpoch;

    public RenderTarget(IntPtr hwnd, bool isPreview, TargetSettings settings)
    {
        Hwnd = hwnd;
        IsPreview = isPreview;
        _settings = settings;
    }

    public IntPtr Hwnd { get; }

    /// <summary>The preview is drawn without filters and idle dim.</summary>
    public bool IsPreview { get; }

    public TargetSettings Settings => _settings;

    public TargetStats Stats { get; } = new TargetStats();

    /// <summary>Swap chain, resolved shader, clocks: touched by the render thread only.</summary>
    internal TargetResources Resources { get; } = new TargetResources();

    internal int Kicks => Volatile.Read(ref _kicks);

    internal int TimingEpoch => Volatile.Read(ref _timingEpoch);

    internal int GapEpoch => Volatile.Read(ref _gapEpoch);

    /// <summary>Replaces the settings (UI thread only).</summary>
    public void Update(Func<TargetSettings, TargetSettings> change)
    {
        var current = _settings;
        var next = change(current);
        if (!ReferenceEquals(next, current) && !next.Equals(current))
        {
            _settings = next;
        }
    }

    /// <summary>Draw one frame of the wanted shader even though the target is paused (a newly applied wallpaper shows at once).</summary>
    public void Kick() => Interlocked.Increment(ref _kicks);

    /// <summary>The smoothed GPU time starts fresh (the render scale or frame rate changed).</summary>
    public void RestartGpuTiming() => Interlocked.Increment(ref _timingEpoch);

    /// <summary>The worst-gap and late-frame counters start at zero again.</summary>
    public void RestartGapStats() => Interlocked.Increment(ref _gapEpoch);

    /// <summary>Self-test: the next presented frame is reported once to this callback (on the render thread).</summary>
    public void CaptureNextFrame(Action<FrameCapture> done) => Volatile.Write(ref _capture, done);

    internal bool WantsCapture => Volatile.Read(ref _capture) != null;

    internal Action<FrameCapture>? TakeCapture() => Interlocked.Exchange(ref _capture, null);
}
