using System.Threading;

namespace Wallpepper.Rendering;

/// <summary>Live figures of one render target. Written only by the render thread, read by anyone.</summary>
internal sealed class TargetStats
{
    private const float Smoothing = 0.15f;

    private volatile float _gpuMs;
    private volatile int _gpuSamples;
    private volatile float _fps;
    private volatile float _effectiveScale = 1f;
    private volatile float _worstGapMs;
    private volatile int _lateFrames;
    private volatile int _frames;
    private volatile bool _awaitingShader;
    private volatile string? _drawnPath;
    private long _gpuMicrosTotal;

    /// <summary>Smoothed GPU time per frame (0 until measured).</summary>
    public float GpuMs => _gpuMs;

    /// <summary>Number of frames whose GPU time was measured.</summary>
    public int GpuSamples => _gpuSamples;

    /// <summary>Sum of all measured frame times in microseconds (with <see cref="GpuSamples"/>: the average over any window).</summary>
    public long GpuMicrosTotal => Interlocked.Read(ref _gpuMicrosTotal);

    public float Fps => _fps;

    public float EffectiveScale => _effectiveScale;

    /// <summary>Longest time between two frames since the last reset (smoothness).</summary>
    public float WorstGapMs => _worstGapMs;

    /// <summary>Frames that came more than half a frame interval late since the last reset.</summary>
    public int LateFrames => _lateFrames;

    /// <summary>Frames presented so far (the stall watchdog notices when this stops moving).</summary>
    public int Frames => _frames;

    /// <summary>Nothing new can be shown because the shader is still compiling (not a stall).</summary>
    public bool AwaitingShader => _awaitingShader;

    /// <summary>The shader the last drawn frame showed (while a newly set one compiles, still the previous one).</summary>
    public string? DrawnPath => _drawnPath;

    internal void SetDrawnPath(string? path)
    {
        if (!ReferenceEquals(_drawnPath, path))
        {
            _drawnPath = path;
        }
    }

    internal void AddGpuSample(double ms)
    {
        _gpuMs = _gpuMs <= 0 ? (float)ms : _gpuMs * (1 - Smoothing) + (float)ms * Smoothing;
        Interlocked.Add(ref _gpuMicrosTotal, (long)(ms * 1000));
        _gpuSamples = _gpuSamples + 1;
    }

    internal void ResetGpuTime() => _gpuMs = 0;

    internal void SetFps(float fps) => _fps = fps;

    internal void SetEffectiveScale(float scale) => _effectiveScale = scale;

    internal void SetAwaitingShader(bool awaiting) => _awaitingShader = awaiting;

    internal void CountFrame() => _frames = _frames + 1;

    internal void NoteGap(double gapMs, bool late)
    {
        if (gapMs > _worstGapMs)
        {
            _worstGapMs = (float)gapMs;
        }
        if (late)
        {
            _lateFrames = _lateFrames + 1;
        }
    }

    internal void ResetGaps()
    {
        _worstGapMs = 0;
        _lateFrames = 0;
    }
}
