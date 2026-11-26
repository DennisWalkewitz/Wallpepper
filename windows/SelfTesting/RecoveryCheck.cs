using System.Threading.Tasks;
using Wallpepper.Interop;

namespace Wallpepper.SelfTesting;

/// <summary>The situations that must not stop rendering: a lock, a lost graphics device, destroyed wallpaper windows.</summary>
internal sealed class RecoveryCheck
{
    private readonly App _app;
    private readonly SelfTestReport _report;

    public RecoveryCheck(App app, SelfTestReport report)
    {
        _app = app;
        _report = report;
    }

    /// <param name="shader">a light shader to run the checks with</param>
    /// <param name="other">another one, to set while locked</param>
    public async Task RunAsync(string? shader, string? other)
    {
        if (shader == null)
        {
            _report.Check("recovery checks", false, "no working shader to test with");
            return;
        }
        other ??= shader;
        _app.Wallpaper.Apply(shader);
        await Probe.ShaderOnScreen(_app, shader, 60);
        await Task.Delay(1000);

        await LockAndUnlock(other);
        await LostDevice();
        await DestroyedWindows();
        await Task.Delay(1500);
    }

    /// <summary>Lock: rendering stops; setting a wallpaper meanwhile still draws it once; unlock: rendering is back.</summary>
    private async Task LockAndUnlock(string other)
    {
        SetLocked(true);
        await Task.Delay(700);
        int[] locked = await Probe.CountFrames(_app, 1000);

        int[] before = Probe.Frames(_app.Displays);
        _app.Wallpaper.Apply(other);
        // it may have to be compiled first
        for (int i = 0; i < 600 && _app.Displays.Where((slot, n) => n < before.Length && slot.Target.Stats.Frames == before[n]).Any(); i++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(500);
        int[] kicked = _app.Displays.Select((slot, n) => n < before.Length ? slot.Target.Stats.Frames - before[n] : 0).ToArray();
        int[] still = await Probe.CountFrames(_app, 1000);

        SetLocked(false);
        int[] unlocked = await Probe.FramesOncePresenting(_app, 15);
        bool ok = locked.All(count => count == 0) && kicked.All(count => count >= 1) && still.All(count => count == 0)
            && unlocked.All(count => count >= 5);
        _report.Check("lock, set a wallpaper while locked, unlock", ok,
            $"frames while locked {Probe.Join(locked)}, drawn for the wallpaper set while locked {Probe.Join(kicked)}, "
            + $"then paused again {Probe.Join(still)}, in 1 s after unlock {Probe.Join(unlocked)}");
    }

    /// <summary>A lost graphics device: device and swap chains are rebuilt.</summary>
    private async Task LostDevice()
    {
        _app.Renderer.ResetDevice();
        await Task.Delay(500);
        int[] rebuilt = await Probe.FramesOncePresenting(_app, 30);
        _report.Check("graphics device lost", rebuilt.All(count => count >= 5), $"frames in 1 s after the rebuild {Probe.Join(rebuilt)}");
    }

    /// <summary>Explorer rebuilding the desktop destroys the wallpaper windows: they must come back by themselves.</summary>
    private async Task DestroyedWindows()
    {
        foreach (var slot in _app.Displays.ToList())
        {
            User32.DestroyWindow(slot.Window.Handle);
        }
        await Task.Delay(1500);
        int[] reattached = await Probe.FramesOncePresenting(_app, 30);
        string? windows = Probe.WindowProblem(_app);
        bool ok = windows == null && reattached.Length > 0 && reattached.All(count => count >= 5);
        _report.Check("wallpaper windows destroyed from outside", ok,
            $"frames in 1 s after re-attaching {Probe.Join(reattached)}" + (windows != null ? " - " + windows : ""));
    }

    /// <summary>The same state a real lock or unlock produces.</summary>
    private void SetLocked(bool locked)
    {
        _app.Session.SimulateLock(locked);
        _app.Governor.Evaluate();
    }
}
