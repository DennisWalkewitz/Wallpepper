using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Wallpepper.Library;

namespace Wallpepper.Rendering;

/// <summary>The GPU benchmark's workload and measurement (docs/BENCHMARK.md). The command line around it is BenchCommand.</summary>
internal static class Benchmark
{
    public const string ShaderFile = "rtx-pathtracer.hlsl";

    private const int TileSize = 240;
    private const int MinFrames = 3;
    private const int MaxFrames = 600;
    private const int FrameTimeoutMs = 180000;
    private const float FramesPerShaderSecond = 60f;

    /// <summary>
    /// The reference workload: the bundled path tracer with BENCHMARK 1 (never the library copy, which may be edited).
    /// With <paramref name="testFile"/> another file is used, to test the machinery.
    /// </summary>
    public static string LoadSource(string? testFile)
    {
        string source = testFile != null ? File.ReadAllText(testFile) : BundledLibrary.ReadText(ShaderFile);
        bool hasSwitch = ShaderCompiler.ParseDefines(source).Any(define => define.Name == ShaderCompiler.BenchmarkDefine);
        if (!hasSwitch && testFile == null)
        {
            throw new InvalidOperationException($"{ShaderFile} has no \"#define {ShaderCompiler.BenchmarkDefine}\" switch.");
        }
        var on = new Dictionary<string, string> { [ShaderCompiler.BenchmarkDefine] = "1" };
        return hasSwitch ? ShaderCompiler.ApplyOverrides(source, on) : source;
    }

    /// <summary>
    /// Renders the workload off screen in 240 x 240 tiles (flush after each) so a slow GPU never spends seconds in one
    /// draw call, and waits for the GPU with an event query after every frame. One warm-up frame, then frames until
    /// <paramref name="minSeconds"/> have passed and 3 frames are done (at most 600).
    /// </summary>
    /// <param name="progress">called with phase ("warmup" or "frame"), frames done and seconds measured</param>
    public static BenchResult Run(
        GraphicsDevice device, string source, int width, int height, double minSeconds, Action<string, int, double>? progress)
    {
        var result = new BenchResult { Width = width, Height = height, Gpu = device.AdapterName, Driver = device.DriverVersion };
        byte[]? code = ShaderCompiler.Compile(source, ShaderFile, "main", device.PixelProfile, out string? error);
        if (code == null)
        {
            result.Error = "The benchmark shader does not compile: " + (error ?? "").Split('\n')[0].Trim();
            return result;
        }

        float[] background = { 0, 0, 0, 1 };
        var context = device.Context;
        using var shader = device.CreatePixelShader(code);
        using var texture = device.Device.CreateTexture2D(new Texture2DDescription(
            Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        using var target = device.Device.CreateRenderTargetView(texture);
        using var finished = device.Device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));

        // frame n is drawn at Time = n / 60 (the warm-up is frame 0); returns milliseconds including the wait for the GPU
        double Frame(int index, double expectedMs)
        {
            long start = Stopwatch.GetTimestamp();
            device.BeginShaderPass(shader, target, ShaderPass.FullTarget(width, height, index / FramesPerShaderSecond, background));
            for (int y = 0; y < height; y += TileSize)
            {
                for (int x = 0; x < width; x += TileSize)
                {
                    device.DrawTile(x, y, Math.Min(TileSize, width - x), Math.Min(TileSize, height - y));
                    context.Flush();
                }
            }
            context.End(finished);
            WaitForGpu(device, finished, start, expectedMs);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        progress?.Invoke("warmup", 0, 0);
        Frame(0, double.MaxValue);

        double min = double.MaxValue;
        double max = 0;
        int frames = 0;
        var total = Stopwatch.StartNew();
        while (frames < MaxFrames && (frames < MinFrames || total.Elapsed.TotalSeconds < minSeconds))
        {
            double ms = Frame(frames + 1, frames == 0 ? 0 : min);
            frames++;
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);
            progress?.Invoke("frame", frames, total.Elapsed.TotalSeconds);
        }
        double seconds = total.Elapsed.TotalSeconds;
        double megapixelsPerSecond = frames * (width * (double)height / 1e6) / seconds;

        result.Frames = frames;
        result.Seconds = Math.Round(seconds, 2);
        result.AvgMs = Math.Round(seconds * 1000.0 / frames, 1);
        result.MinMs = Math.Round(min, 1);
        result.MaxMs = Math.Round(max, 1);
        result.MPixelsPerSecond = Math.Round(megapixelsPerSecond, 2);
        result.Score = (int)Math.Round(BenchResult.ReferenceScore * megapixelsPerSecond / BenchResult.ReferenceMPixelsPerSecond);
        return result;
    }

    /// <summary>
    /// Blocks until the event query reports that the GPU has finished everything submitted before it. Sleeps while the
    /// frame is expected to be far from done (so the CPU does not compete with an integrated GPU for power), then spins.
    /// </summary>
    private static unsafe void WaitForGpu(GraphicsDevice device, ID3D11Query finished, long start, double expectedMs)
    {
        int flag = 0;
        int polls = 0;
        while (true)
        {
            var result = device.Context.GetData(finished, (IntPtr)(&flag), sizeof(int), AsyncGetDataFlags.None);
            if (result.Code == 0)
            {
                return;   // S_OK: finished; S_FALSE: still working
            }
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            bool removed = (++polls & 255) == 0 && device.Device.DeviceRemovedReason.Failure;
            if (result.Failure || removed)
            {
                throw new InvalidOperationException("The graphics driver reset the device during the benchmark.");
            }
            if (ms > FrameTimeoutMs)
            {
                throw new TimeoutException("The GPU did not finish a frame within three minutes.");
            }
            if (ms < expectedMs - 3)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(60);
            }
        }
    }
}
