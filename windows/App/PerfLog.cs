using System.Diagnostics;
using System.Windows.Threading;
using Wallpepper.Desktop;
using Wallpepper.Settings;

namespace Wallpepper;

/// <summary>
/// Development aid (only with "debuglog.on" next to the exe): every few seconds one line about the process and one
/// about every display (frame rate, GPU time, the longest gap between two frames, what the fitter chose).
/// </summary>
internal sealed class PerfLog : IDisposable
{
    private const int IntervalSeconds = 5;

    private readonly DesktopHost _host;
    private readonly DispatcherTimer _timer;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;

    public PerfLog(DesktopHost host)
    {
        _host = host;
        _lastCpu = _process.TotalProcessorTime;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(IntervalSeconds) };
        _timer.Tick += (_, _) => Write();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _process.Dispose();
    }

    private void Write()
    {
        _process.Refresh();
        TimeSpan cpu = _process.TotalProcessorTime;
        double cpuPercent = (cpu - _lastCpu).TotalSeconds / IntervalSeconds / Environment.ProcessorCount * 100;
        _lastCpu = cpu;
        bool webView = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetName().Name?.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal) == true);
        Log.Write(
            $"proc: cpu {cpuPercent:0.00}% of {Environment.ProcessorCount} cores, ws {_process.WorkingSet64 >> 20} MB, "
            + $"private {_process.PrivateMemorySize64 >> 20} MB, managed {GC.GetTotalMemory(false) >> 20} MB, "
            + $"handles {_process.HandleCount}, webview2 {(webView ? "loaded" : "not loaded")}");
        Log.Write("perf: " + string.Join("  |  ", _host.Slots.Select(Describe)));
    }

    private static string Describe(DisplaySlot slot)
    {
        string what = $"{slot.Index + 1} {Path.GetFileNameWithoutExtension(slot.ItemPath ?? "-")} ";
        string pause = slot.PauseReason is PauseReason reason ? PauseReasonText.Of(reason) : "";
        if (slot.ShowsWebPage)
        {
            return what + (slot.IsPaused ? $"web hidden({pause})" : "web");
        }
        if (slot.IsPaused)
        {
            return what + $"paused({pause})";
        }
        var stats = slot.Target.Stats;
        var settings = slot.Target.Settings;
        var fit = slot.Fit;
        var bounds = slot.Screen.Bounds;
        string line = what
            + $"{stats.Fps:0.0} fps {stats.GpuMs:0.00} ms gap {stats.WorstGapMs:0.0} ms late {stats.LateFrames} "
            + $"scale {stats.EffectiveScale:0.00} cap {settings.FpsCap} gpu {fit.Share * 100:0.0}% of {fit.Budget * 100:0.0}% "
            + $"est {fit.FrameMs:0.0} ms x{fit.Correction:0.00}"
            + (fit.Light ? " light" : "") + (fit.TooHeavy ? " too-heavy" : "")
            + $" {bounds.Width}x{bounds.Height}@{settings.RefreshHz}";
        slot.Target.RestartGapStats();
        return line;
    }
}
