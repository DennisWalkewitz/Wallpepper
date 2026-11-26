using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Wallpepper.Interop;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>Renderer-wide values of one pass of the render loop.</summary>
internal readonly record struct FrameContext(
    double Now, float Dim, float DefaultScale, float Speed, float[] Background, float[] MonitorRects, bool Measure);

/// <summary>
/// Draws one frame for one target: swap chain, shader pass, optional filter pass, GPU timing. Render thread only.
/// </summary>
internal sealed class TargetRenderer
{
    private const float MinScale = 0.25f;
    private const double SizeRefreshSeconds = 0.5;
    private const int GpuTimersPerTarget = 4;

    /// <summary>A frame advances the shader clock by at most this much, so a pause is not seen as a jump.</summary>
    private const double MaxClockStepSeconds = 0.25;

    /// <summary>
    /// After a pause this long (lock, display off, covered for minutes) the clock starts at zero again. Nobody saw
    /// the last frame, and a small clock keeps the float "Time" precise however long the PC stays on.
    /// </summary>
    private const double ClockRebaseAfterSeconds = 300;

    private readonly GraphicsDevice _device;
    private readonly ShaderCache _cache;

    // span: every display draws its slice of one picture, so all slices share one clock
    private string? _spanPath;
    private double _spanElapsed;
    private double _spanLastDrawAt;

    public TargetRenderer(GraphicsDevice device, ShaderCache cache)
    {
        _device = device;
        _cache = cache;
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Draws the target's next frame.</summary>
    /// <param name="kicks">the target's kick counter as the loop read it; remembered once the wanted shader was drawn</param>
    /// <returns>true when a frame was drawn and still has to be presented.</returns>
    public bool Render(RenderTarget target, TargetSettings settings, in FrameContext frame, int kicks)
    {
        var res = target.Resources;
        string path = settings.ShaderPath!;
        RefreshWindowSize(target, frame.Now);
        float wantedScale = settings.Scale > 0 ? settings.Scale : target.IsPreview ? 1f : frame.DefaultScale;
        float scale = Math.Clamp(wantedScale, MinScale, 1f);
        target.Stats.SetEffectiveScale(scale);
        int clientWidth = res.ClientWidth;                                // swap chain: native size
        int clientHeight = res.ClientHeight;
        int width = Math.Max(1, (int)(clientWidth * scale));              // shader: scaled
        int height = Math.Max(1, (int)(clientHeight * scale));
        EnsureSwapChain(target, clientWidth, clientHeight);

        ResolveShader(target, path);
        bool drawsWanted = res.Shader != null && SamePath(res.DrawnPath, path);
        target.Stats.SetAwaitingShader(res.Awaiting && !drawsWanted);
        if (res.Shader == null)
        {
            // a compile error, or the very first shader is still compiling: the background colour, once
            if (res.Failed || !res.ClearedWhileAwaiting)
            {
                PresentCleared(target, frame.Background);
                res.ClearedWhileAwaiting = true;
            }
            if (res.Failed)
            {
                res.SeenKicks = kicks;
            }
            return false;
        }
        if (settings.Paused && !drawsWanted)
        {
            return false;   // a paused display draws one frame of a newly applied shader, not of the old one
        }

        // span: every monitor draws with the virtual desktop as viewport and resolution, shifted so its own slice
        // lands on its target
        bool span = settings.Span && settings.SpanWidth > 0 && !target.IsPreview;
        double time = span ? AdvanceSpanClock(res.DrawnPath, frame) : AdvanceClock(res, frame);
        float scaleX = (float)width / clientWidth;
        float scaleY = (float)height / clientHeight;

        // second pass only when a filter is active (also when rendering below native size: the pass upscales with a
        // linear filter)
        FilterState? filter = target.IsPreview ? null : settings.Filter;
        float dim = target.IsPreview ? 0f : frame.Dim;
        bool filtered = (filter != null && !filter.IsNeutral) || dim > 0.001f || width != clientWidth || height != clientHeight;
        bool twoPass = _device.FilterShader != null && filtered;
        if (twoPass)
        {
            EnsureOffscreen(res, width, height);
        }
        else
        {
            width = clientWidth;
            height = clientHeight;
            scaleX = 1;
            scaleY = 1;
            if (res.Offscreen != null)
            {
                res.ReleaseOffscreen();
            }
        }

        var pass = new ShaderPass
        {
            ViewX = span ? -settings.SpanX * scaleX : 0,
            ViewY = span ? -settings.SpanY * scaleY : 0,
            ViewWidth = span ? settings.SpanWidth * scaleX : width,
            ViewHeight = span ? settings.SpanHeight * scaleY : height,
            Time = (float)time,
            DpiScale = res.DpiScale,
            Background = frame.Background,
            MonitorRects = span ? frame.MonitorRects : null,
            MonitorIndex = settings.MonitorIndex,
        };

        GpuTimer? timer = frame.Measure ? BeginTimer(target) : null;
        _device.BeginShaderPass(res.Shader, twoPass ? res.OffscreenTarget! : res.BackBufferView!, pass);
        _device.Draw();
        if (twoPass)
        {
            _device.DrawFilterPass(res.BackBufferView!, res.OffscreenView!, clientWidth, clientHeight, FilterConstantsFor(filter, dim, width, height));
        }
        timer?.End(_device.Context);
        target.Stats.SetDrawnPath(res.DrawnPath);

        if (drawsWanted)
        {
            res.SeenKicks = kicks;
        }
        return true;
    }

    /// <summary>Presents a drawn frame. <paramref name="syncInterval"/> 0 = at once, n = wait for the n-th vertical blank.</summary>
    /// <returns>false when the device was lost.</returns>
    public bool Present(RenderTarget target, int syncInterval)
    {
        var swapChain = target.Resources.SwapChain;
        if (swapChain == null)
        {
            return true;
        }
        var result = swapChain.Present((uint)syncInterval, PresentFlags.None);
        if (result.Success)
        {
            target.Stats.CountFrame();
            return true;
        }
        return !GraphicsDevice.IsLostCode(result.Code);
    }

    /// <summary>Self-test: reads the frame that is about to be presented and reports its size and its colours.</summary>
    public void Capture(RenderTarget target)
    {
        var done = target.TakeCapture();
        var res = target.Resources;
        if (done == null || res.SwapChain == null)
        {
            return;
        }
        int colours = 0;
        try
        {
            colours = CountColours(res);
        }
        catch (SharpGen.Runtime.SharpGenException e)
        {
            Log.Warn("capture frame", e);
        }
        done(new FrameCapture(res.BufferWidth, res.BufferHeight, colours));
    }

    private unsafe int CountColours(TargetResources res)
    {
        const int Columns = 64;
        const int Rows = 40;
        const int Margin = 8;
        using var backBuffer = res.SwapChain!.GetBuffer<ID3D11Texture2D>(0);
        using var staging = _device.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)res.BufferWidth, (uint)res.BufferHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        _device.Context.CopyResource(staging, backBuffer);
        var mapped = _device.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var seen = new HashSet<int>();
        for (int y = Margin; y < res.BufferHeight; y += Math.Max(1, res.BufferHeight / Rows))
        {
            int* row = (int*)((byte*)mapped.DataPointer + (long)y * mapped.RowPitch);
            for (int x = Margin; x < res.BufferWidth; x += Math.Max(1, res.BufferWidth / Columns))
            {
                seen.Add(row[x] & 0xFFFFFF);
            }
        }
        _device.Context.Unmap(staging, 0);
        return seen.Count;
    }

    /// <summary>Client size and DPI change rarely: they are read twice a second, not for every frame.</summary>
    private static void RefreshWindowSize(RenderTarget target, double now)
    {
        var res = target.Resources;
        if (now - res.SizeReadAt < SizeRefreshSeconds && res.ClientWidth > 0)
        {
            return;
        }
        User32.GetClientRect(target.Hwnd, out var client);
        res.ClientWidth = Math.Max(1, client.Width);
        res.ClientHeight = Math.Max(1, client.Height);
        uint dpi = User32.GetDpiForWindow(target.Hwnd);
        res.DpiScale = dpi > 0 ? dpi / 96f : 1f;
        res.SizeReadAt = now;
    }

    private void EnsureSwapChain(RenderTarget target, int width, int height)
    {
        var res = target.Resources;
        if (res.SwapChain == null)
        {
            var description = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
            };
            res.SwapChain = _device.Factory.CreateSwapChainForHwnd(_device.Device, target.Hwnd, description);
            _device.Factory.MakeWindowAssociation(target.Hwnd, WindowAssociationFlags.IgnoreAll);
        }
        else if (width != res.BufferWidth || height != res.BufferHeight)
        {
            res.BackBufferView?.Dispose();
            res.BackBufferView = null;
            res.SwapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        }
        else
        {
            return;
        }
        using var backBuffer = res.SwapChain.GetBuffer<ID3D11Texture2D>(0);
        res.BackBufferView = _device.Device.CreateRenderTargetView(backBuffer);
        res.BufferWidth = width;
        res.BufferHeight = height;
    }

    /// <summary>
    /// Finds the shader to draw. The wanted one when it is compiled; while it compiles, the one drawn before stays
    /// (the animation goes on instead of freezing). Looked up only after the cache or the wanted path changed, so a
    /// frame does no I/O, hashing or allocation for this.
    /// </summary>
    private void ResolveShader(RenderTarget target, string path)
    {
        var res = target.Resources;
        if (res.ResolvedGeneration == _cache.Generation && SamePath(res.ResolvedPath, path))
        {
            return;
        }
        res.ResolvedPath = path;
        var lookup = _cache.Resolve(path, urgent: true);
        res.ResolvedGeneration = _cache.Generation;
        res.Awaiting = lookup.Pending;
        if (lookup.Shader != null)
        {
            if (!SamePath(res.DrawnPath, path))
            {
                res.DrawnPath = path;
                res.Elapsed = 0;
                res.LastDrawAt = 0;
            }
            res.Shader = lookup.Shader;
            res.Failed = false;
        }
        else if (lookup.Pending)
        {
            res.Shader = SamePath(res.DrawnPath, path) ? null : _cache.Peek(res.DrawnPath);
            res.Failed = false;
        }
        else
        {
            res.Shader = null;
            res.DrawnPath = path;
            res.Failed = true;
        }
        if (res.Shader != null)
        {
            res.ClearedWhileAwaiting = false;
        }
    }

    private void PresentCleared(RenderTarget target, float[] background)
    {
        var res = target.Resources;
        _device.Context.ClearRenderTargetView(res.BackBufferView!, new Color4(background[0], background[1], background[2], 1f));
        if (res.SwapChain!.Present(0, PresentFlags.None).Success)
        {
            target.Stats.CountFrame();   // a cleared frame is a frame too
        }
    }

    private static double AdvanceClock(TargetResources res, in FrameContext frame)
    {
        res.Elapsed = Advance(res.Elapsed, res.LastDrawAt, frame);
        res.LastDrawAt = frame.Now;
        return res.Elapsed;
    }

    private double AdvanceSpanClock(string? path, in FrameContext frame)
    {
        if (!SamePath(_spanPath, path))
        {
            _spanPath = path;
            _spanElapsed = 0;
            _spanLastDrawAt = 0;
        }
        if (_spanLastDrawAt != frame.Now)   // once per pass of the loop, whichever slice comes first
        {
            _spanElapsed = Advance(_spanElapsed, _spanLastDrawAt, frame);
            _spanLastDrawAt = frame.Now;
        }
        return _spanElapsed;
    }

    private static double Advance(double elapsed, double lastDrawAt, in FrameContext frame)
    {
        if (lastDrawAt <= 0)
        {
            return elapsed;
        }
        double step = frame.Now - lastDrawAt;
        if (step > ClockRebaseAfterSeconds)
        {
            return 0;
        }
        return elapsed + Math.Min(step, MaxClockStepSeconds) * frame.Speed;
    }

    private void EnsureOffscreen(TargetResources res, int width, int height)
    {
        if (res.Offscreen != null && res.OffscreenWidth == width && res.OffscreenHeight == height)
        {
            return;
        }
        res.ReleaseOffscreen();
        res.Offscreen = _device.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        res.OffscreenTarget = _device.Device.CreateRenderTargetView(res.Offscreen);
        res.OffscreenView = _device.Device.CreateShaderResourceView(res.Offscreen);
        res.OffscreenWidth = width;
        res.OffscreenHeight = height;
    }

    private static FilterConstants FilterConstantsFor(FilterState? filter, float dim, int width, int height)
    {
        var constants = new FilterConstants
        {
            TexelX = 1f / width,
            TexelY = 1f / height,
            Brightness = 1,
            Contrast = 1,
            Saturation = 1,
            Dim = dim,
        };
        if (filter != null)
        {
            constants.Brightness = filter.Brightness;
            constants.Contrast = filter.Contrast;
            constants.Saturation = filter.Saturation;
            constants.Temperature = filter.Temperature;
            constants.Blur = filter.Blur;
            constants.Vignette = filter.Vignette;
        }
        return constants;
    }

    /// <summary>Collects finished measurements and starts a new one if a query set is free. Never waits for the GPU.</summary>
    private GpuTimer? BeginTimer(RenderTarget target)
    {
        var res = target.Resources;
        if (res.SeenTimingEpoch != target.TimingEpoch)
        {
            res.SeenTimingEpoch = target.TimingEpoch;
            target.Stats.ResetGpuTime();
        }
        res.Timers ??= Enumerable.Range(0, GpuTimersPerTarget).Select(_ => new GpuTimer(_device.Device)).ToArray();
        foreach (var timer in res.Timers)
        {
            if (timer.Pending && timer.TryRead(_device.Context, AsyncGetDataFlags.DoNotFlush, out double ms) && ms >= 0)
            {
                target.Stats.AddGpuSample(ms);
            }
        }
        for (int i = 0; i < res.Timers.Length; i++)
        {
            var timer = res.Timers[(res.NextTimer + i) % res.Timers.Length];
            if (timer.Pending)
            {
                continue;
            }
            res.NextTimer = (res.NextTimer + i + 1) % res.Timers.Length;
            timer.Begin(_device.Context);
            return timer;
        }
        return null;   // all still in flight: skip this frame
    }
}
