using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Wallpepper.Rendering;

/// <summary>Per-target state of the render thread: device objects, the resolved shader, clocks and pacing.</summary>
internal sealed class TargetResources
{
    // ---- device objects (released with the device)
    public IDXGISwapChain1? SwapChain { get; set; }
    public ID3D11RenderTargetView? BackBufferView { get; set; }
    public ID3D11Texture2D? Offscreen { get; set; }
    public ID3D11RenderTargetView? OffscreenTarget { get; set; }
    public ID3D11ShaderResourceView? OffscreenView { get; set; }
    public int OffscreenWidth { get; set; }
    public int OffscreenHeight { get; set; }
    public GpuTimer[]? Timers { get; set; }
    public int NextTimer { get; set; }

    // ---- window (re-read a few times a second, not per frame)
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public float DpiScale { get; set; } = 1f;
    public double SizeReadAt { get; set; } = double.MinValue;
    public int BufferWidth { get; set; }
    public int BufferHeight { get; set; }

    // ---- shader: resolved once, again only after the cache changed or the wanted path did
    public ID3D11PixelShader? Shader { get; set; }

    /// <summary>The path whose shader is being drawn (may lag behind the wanted one while that compiles).</summary>
    public string? DrawnPath { get; set; }
    public string? ResolvedPath { get; set; }
    public int ResolvedGeneration { get; set; } = -1;
    public bool Awaiting { get; set; }
    public bool Failed { get; set; }
    public bool ClearedWhileAwaiting { get; set; }

    // ---- clocks
    /// <summary>Shader time in seconds: advances only with drawn frames, by frame time x speed.</summary>
    public double Elapsed { get; set; }
    public double LastDrawAt { get; set; }

    // ---- pacing and statistics
    public double NextDue { get; set; }
    public double LastFrameAt { get; set; }
    public double FpsWindowStart { get; set; }
    public int FpsWindowFrames { get; set; }
    public int SeenKicks { get; set; }
    public int SeenTimingEpoch { get; set; }
    public int SeenGapEpoch { get; set; }

    // ---- a drawing error that is not a lost device: the target is skipped until its input changes
    public string? FaultPath { get; set; }
    public int FaultGeneration { get; set; }
    public int FaultWidth { get; set; }

    public void ReleaseOffscreen()
    {
        OffscreenView?.Dispose();
        OffscreenTarget?.Dispose();
        Offscreen?.Dispose();
        OffscreenView = null;
        OffscreenTarget = null;
        Offscreen = null;
        OffscreenWidth = 0;
        OffscreenHeight = 0;
    }

    /// <summary>Everything that belongs to the device goes; settings, clocks and pacing stay.</summary>
    public void ReleaseDeviceObjects()
    {
        BackBufferView?.Dispose();
        BackBufferView = null;
        SwapChain?.Dispose();
        SwapChain = null;
        BufferWidth = 0;
        BufferHeight = 0;
        Shader = null;
        ResolvedGeneration = -1;
        ReleaseOffscreen();
        if (Timers != null)
        {
            foreach (var timer in Timers)
            {
                timer.Dispose();
            }
            Timers = null;
        }
    }
}
