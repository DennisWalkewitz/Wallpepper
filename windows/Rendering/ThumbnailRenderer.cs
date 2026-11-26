using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Wallpepper.Desktop.Performance;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>A gallery picture: BGRA pixels, or the reason there is none.</summary>
internal sealed record ThumbnailPicture(string Path, byte[]? Pixels, int Width, int Height, string? Error);

/// <summary>What a call to <see cref="ThumbnailRenderer.Step"/> did.</summary>
internal enum SlowWork
{
    /// <summary>Nothing queued.</summary>
    None,

    /// <summary>A job is waiting for its shader or for the GPU.</summary>
    Waiting,

    /// <summary>A slice was drawn.</summary>
    Worked,
}

/// <summary>
/// Gallery pictures and cost measurements. Neither may hold up a wallpaper frame, so a job is a little state machine
/// that the render loop advances by one small step between frames: the shader is compiled on the compile thread, the
/// picture is drawn in bands sized to a few milliseconds of GPU time, and results are read back without waiting.
/// Requests come from any thread; results are delivered on the render thread.
/// </summary>
internal sealed class ThumbnailRenderer
{
    /// <summary>GPU time one step may take, as far as the probe can tell.</summary>
    private const double SliceBudgetMs = 4;

    /// <summary>GPU time the one-off cost measurement may take: long enough for the GPU to leave its idle clock.</summary>
    private const double BurstBudgetMs = 40;
    private const int ProbeWidth = 64;
    private const int ProbeHeight = 36;
    private const int BurstWidth = 960;
    private const int BurstHeight = 540;
    private const int MinBurstRows = 135;
    private const int MaxBurstDraws = 16;

    /// <summary>
    /// The fastest of a few bursts counts: other work on the GPU and a clock that has not come up yet can only make a
    /// burst slower, never faster.
    /// </summary>
    private const int BurstCount = 3;
    private const int MaxBurstAttempts = 6;

    /// <summary>A shader whose probe gave no usable time is treated as this heavy (ms per 1080p frame).</summary>
    private const double UnknownCostMs = 40;

    private enum Phase
    {
        Shader,
        Probe,
        ProbeWait,
        Bands,
        ReadbackWait,
        Burst,
        BurstWait,
    }

    private sealed class Job
    {
        public required string Path { get; init; }
        public Action<ThumbnailPicture>? PictureDone { get; init; }
        public Action<string, float>? CostDone { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public float Time { get; init; }

        public Phase Phase { get; set; }
        public ID3D11PixelShader? Shader { get; set; }
        public double MsPerPixel { get; set; }
        public int NextRow { get; set; }
        public int RowsPerStep { get; set; }
        public int BurstPixels { get; set; }
        public int Attempts { get; set; }
        public int Bursts { get; set; }
        public double BestMsPerPixel { get; set; } = double.MaxValue;

        public ID3D11Texture2D? Texture { get; set; }
        public ID3D11RenderTargetView? Target { get; set; }
        public ID3D11Texture2D? Staging { get; set; }
        public ID3D11Query? Finished { get; set; }
        public GpuTimer? Timer { get; set; }

        public bool IsPicture => PictureDone != null;

        public void ReleaseDeviceObjects()
        {
            Timer?.Dispose();
            Finished?.Dispose();
            Staging?.Dispose();
            Target?.Dispose();
            Texture?.Dispose();
            Timer = null;
            Finished = null;
            Staging = null;
            Target = null;
            Texture = null;
        }
    }

    private readonly object _gate = new object();
    private readonly Queue<Job> _pictures = new Queue<Job>();
    private readonly Queue<Job> _costs = new Queue<Job>();
    private readonly ShaderCache _cache;
    private readonly Func<string, bool> _inUseElsewhere;
    private readonly Action _wakeRenderThread;
    private readonly Func<float[]> _background;
    private volatile bool _picturesEnabled;
    private volatile Job? _current;

    public ThumbnailRenderer(ShaderCache cache, Func<string, bool> inUse, Action wakeRenderThread, Func<float[]> background)
    {
        _cache = cache;
        _inUseElsewhere = inUse;
        _wakeRenderThread = wakeRenderThread;
        _background = background;
    }

    /// <summary>Pictures are only drawn while someone looks at them (library window open). Measurements always run.</summary>
    public bool PicturesEnabled
    {
        get => _picturesEnabled;
        set => _picturesEnabled = value;
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Draws a shader once off screen at the given shader time.</summary>
    public void RequestPicture(string path, int width, int height, float time, Action<ThumbnailPicture> done)
    {
        lock (_gate)
        {
            _pictures.Enqueue(new Job { Path = path, Width = width, Height = height, Time = time, PictureDone = done });
        }
        _wakeRenderThread();
    }

    /// <summary>
    /// Measures what a shader costs: GPU milliseconds per 1920x1080 frame under sustained load, -1 when it could not
    /// be measured. This is the figure the performance fitter plans with.
    /// </summary>
    public void RequestCost(string path, Action<string, float> done)
    {
        lock (_gate)
        {
            if (_costs.Any(job => SamePath(job.Path, path)))
            {
                return;
            }
            _costs.Enqueue(new Job { Path = path, Width = BurstWidth, Height = BurstHeight, CostDone = done });
        }
        _wakeRenderThread();
    }

    /// <summary>Drops queued pictures (library window closed). A picture that is half drawn is abandoned too.</summary>
    public void CancelPictures()
    {
        lock (_gate)
        {
            _pictures.Clear();
        }
        _wakeRenderThread();
    }

    /// <summary>Whether a queued or running job needs the shader of a path. Any thread.</summary>
    public bool Wants(string path)
    {
        if (SamePath(_current?.Path, path))
        {
            return true;
        }
        lock (_gate)
        {
            return _pictures.Any(job => SamePath(job.Path, path)) || _costs.Any(job => SamePath(job.Path, path));
        }
    }

    /// <summary>The device is going away (render thread): the running job starts again on the next one.</summary>
    internal void ReleaseDeviceObjects()
    {
        var job = _current;
        if (job != null)
        {
            job.ReleaseDeviceObjects();
            job.Shader = null;
            job.Phase = Phase.Shader;
        }
    }

    /// <summary>Advances the current job by one small step (render thread).</summary>
    internal SlowWork Step(GraphicsDevice device)
    {
        var job = _current;
        if (job != null && job.IsPicture && !_picturesEnabled)
        {
            Finish(job);
            job = null;
        }
        job ??= _current = Next();
        if (job == null)
        {
            return SlowWork.None;
        }
        try
        {
            return Advance(job, device);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warn((job.IsPicture ? "thumbnail " : "cost measurement ") + Path.GetFileName(job.Path), e);
            Deliver(job, null, e.Message, -1);
            return SlowWork.Worked;
        }
    }

    private Job? Next()
    {
        lock (_gate)
        {
            if (!_picturesEnabled)
            {
                _pictures.Clear();
            }
            if (_pictures.Count > 0)
            {
                return _pictures.Dequeue();
            }
            return _costs.Count > 0 ? _costs.Dequeue() : null;
        }
    }

    private SlowWork Advance(Job job, GraphicsDevice device)
    {
        var context = device.Context;
        switch (job.Phase)
        {
            case Phase.Shader:
                var lookup = _cache.Resolve(job.Path, urgent: false);
                if (lookup.Pending)
                {
                    return SlowWork.Waiting;
                }
                if (lookup.Shader == null)
                {
                    Deliver(job, null, lookup.Error ?? "compile error", -1);
                    return SlowWork.Worked;
                }
                job.Shader = lookup.Shader;
                job.Phase = Phase.Probe;
                return SlowWork.Worked;

            case Phase.Probe:
                // a tiny draw tells roughly how heavy the shader is, and lets the driver finish preparing it
                CreateTarget(job, device, ProbeWidth, ProbeHeight);
                job.Timer = new GpuTimer(device.Device);
                job.Timer.Begin(context);
                device.BeginShaderPass(job.Shader!, job.Target!, ShaderPass.FullTarget(ProbeWidth, ProbeHeight, 1f, _background()));
                device.Draw();
                job.Timer.End(context);
                context.Flush();
                job.Phase = Phase.ProbeWait;
                return SlowWork.Worked;

            case Phase.ProbeWait:
                if (!job.Timer!.TryRead(context, AsyncGetDataFlags.DoNotFlush, out double probeMs))
                {
                    return SlowWork.Waiting;
                }
                job.MsPerPixel = probeMs > 0 ? probeMs / (ProbeWidth * ProbeHeight) : UnknownCostMs / PerformanceFitter.Pixels1080;
                job.ReleaseDeviceObjects();
                CreateTarget(job, device, job.Width, job.Height);
                if (job.IsPicture)
                {
                    job.RowsPerStep = (int)Math.Clamp(SliceBudgetMs / (job.MsPerPixel * job.Width), 2, job.Height);
                    job.NextRow = 0;
                    job.Phase = Phase.Bands;
                }
                else
                {
                    job.Phase = Phase.Burst;
                }
                return SlowWork.Worked;

            case Phase.Bands:
                DrawBand(job, device);
                return SlowWork.Worked;

            case Phase.ReadbackWait:
                int done = 0;
                unsafe
                {
                    if (context.GetData(job.Finished!, (IntPtr)(&done), sizeof(int), AsyncGetDataFlags.DoNotFlush).Code != 0)
                    {
                        return SlowWork.Waiting;
                    }
                }
                Deliver(job, ReadPixels(job, context), null, -1);
                return SlowWork.Worked;

            case Phase.Burst:
                DrawBurst(job, device);
                return SlowWork.Worked;

            default:
                if (!job.Timer!.TryRead(context, AsyncGetDataFlags.DoNotFlush, out double burstMs))
                {
                    return SlowWork.Waiting;
                }
                job.Attempts++;
                if (burstMs >= 0)   // a disjoint sample is not usable
                {
                    job.Bursts++;
                    job.BestMsPerPixel = Math.Min(job.BestMsPerPixel, burstMs / job.BurstPixels);
                }
                if (job.Bursts < BurstCount && job.Attempts < MaxBurstAttempts)
                {
                    job.Phase = Phase.Burst;
                    return SlowWork.Worked;
                }
                float cost = job.Bursts == 0 ? -1 : (float)(job.BestMsPerPixel * PerformanceFitter.Pixels1080);
                Deliver(job, null, null, cost);
                return SlowWork.Worked;
        }
    }

    /// <summary>One horizontal band of the picture; after the last one the copy for the read-back is queued.</summary>
    private void DrawBand(Job job, GraphicsDevice device)
    {
        var context = device.Context;
        int rows = Math.Min(job.RowsPerStep, job.Height - job.NextRow);
        device.BeginShaderPass(job.Shader!, job.Target!, ShaderPass.FullTarget(job.Width, job.Height, job.Time, _background()));
        device.DrawTile(0, job.NextRow, job.Width, rows);
        job.NextRow += rows;
        if (job.NextRow >= job.Height)
        {
            job.Staging = device.Device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, (uint)job.Width, (uint)job.Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            job.Finished = device.Device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            context.CopyResource(job.Staging, job.Texture!);
            context.End(job.Finished);
            job.Phase = Phase.ReadbackWait;
        }
        context.Flush();
    }

    /// <summary>
    /// The cost measurement: draws of a known size back to back inside one timer, sized by the probe to about
    /// <see cref="BurstBudgetMs"/> of GPU time, so the GPU runs at its working clock rather than its idle one.
    /// </summary>
    private void DrawBurst(Job job, GraphicsDevice device)
    {
        var context = device.Context;
        double msPerRow = job.MsPerPixel * BurstWidth;
        int rows = (int)Math.Clamp(BurstBudgetMs / msPerRow, MinBurstRows, BurstHeight);
        int draws = (int)Math.Clamp(BurstBudgetMs / (msPerRow * rows), 1, MaxBurstDraws);
        job.BurstPixels = BurstWidth * rows * draws;
        job.Timer ??= new GpuTimer(device.Device);
        job.Timer.Begin(context);
        for (int i = 0; i < draws; i++)
        {
            device.BeginShaderPass(job.Shader!, job.Target!, ShaderPass.FullTarget(BurstWidth, BurstHeight, 2f + i * 0.5f, _background()));
            device.DrawTile(0, 0, BurstWidth, rows);
        }
        job.Timer.End(context);
        context.Flush();
        job.Phase = Phase.BurstWait;
    }

    private static void CreateTarget(Job job, GraphicsDevice device, int width, int height)
    {
        job.Texture = device.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        job.Target = device.Device.CreateRenderTargetView(job.Texture);
    }

    private static byte[] ReadPixels(Job job, ID3D11DeviceContext context)
    {
        var mapped = context.Map(job.Staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var pixels = new byte[job.Width * job.Height * 4];
        for (int y = 0; y < job.Height; y++)
        {
            Marshal.Copy(mapped.DataPointer + (int)(y * mapped.RowPitch), pixels, y * job.Width * 4, job.Width * 4);
        }
        context.Unmap(job.Staging!, 0);
        return pixels;
    }

    private void Deliver(Job job, byte[]? pixels, string? error, float cost)
    {
        Finish(job);
        try
        {
            if (job.IsPicture)
            {
                job.PictureDone!(new ThumbnailPicture(job.Path, pixels, job.Width, job.Height, error));
            }
            else
            {
                job.CostDone!(job.Path, cost);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("thumbnail result", e);
        }
    }

    /// <summary>The job is over: its device objects go, and its shader too unless a display or the preview shows it.</summary>
    private void Finish(Job job)
    {
        job.ReleaseDeviceObjects();
        _current = null;
        _cache.Release(job.Path, path => _inUseElsewhere(path));
    }
}
