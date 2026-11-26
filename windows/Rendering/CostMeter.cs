using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Wallpepper.Rendering;

/// <summary>
/// Measures the GPU time of full frames one after the other and waits for each result. For "--check", which has the
/// device to itself; the running app measures without waiting (<see cref="ThumbnailRenderer"/>).
/// </summary>
internal static class CostMeter
{
    private const int ProbeWidth = 64;
    private const int ProbeHeight = 36;
    private const int ReadTimeoutMs = 8000;

    /// <summary>The run stops early once it has cost this much, so a heavy shader never queues seconds of work.</summary>
    private const double EnoughMs = 500;

    /// <summary>Average GPU milliseconds per frame at the given size, -1 when nothing could be measured.</summary>
    public static float Measure(GraphicsDevice device, ID3D11PixelShader shader, int width, int height, int frames)
    {
        var context = device.Context;
        float[] background = { 0, 0, 0, 1 };
        using var texture = device.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        using var target = device.Device.CreateRenderTargetView(texture);
        using var probe = device.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, ProbeWidth, ProbeHeight, 1, 1, BindFlags.RenderTarget));
        using var probeTarget = device.Device.CreateRenderTargetView(probe);
        using var timer = new GpuTimer(device.Device);

        // warm-up (the driver finishes preparing the shader) at a negligible size
        device.BeginShaderPass(shader, probeTarget, ShaderPass.FullTarget(ProbeWidth, ProbeHeight, 1f, background));
        device.Draw();
        context.Flush();

        double total = 0;
        int measured = 0;
        for (int i = 0; i < frames && total <= EnoughMs; i++)
        {
            timer.Begin(context);
            device.BeginShaderPass(shader, target, ShaderPass.FullTarget(width, height, 2f + i * 0.5f, background));
            device.Draw();
            timer.End(context);
            context.Flush();

            var waited = Stopwatch.StartNew();
            double ms;
            while (!timer.TryRead(context, AsyncGetDataFlags.None, out ms))
            {
                if (waited.ElapsedMilliseconds > ReadTimeoutMs)
                {
                    return -1;
                }
                Thread.Sleep(1);
            }
            if (ms >= 0)   // a disjoint sample is not usable
            {
                total += ms;
                measured++;
            }
        }
        return measured == 0 ? -1 : (float)(total / measured);
    }
}
