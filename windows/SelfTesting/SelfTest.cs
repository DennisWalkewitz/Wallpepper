using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wallpepper.Settings;
using Wallpepper.Setup;

namespace Wallpepper.SelfTesting;

/// <summary>
/// "Wallpepper.exe --selftest [--out FILE] [--only items|recovery|fit|browse]": proves on the real desktop that the app
/// does its job. Every library item is set the way the "Set as wallpaper" button sets it, rendered on every display and
/// read back; the situations that used to stop rendering are forced through the normal code paths (lock and unlock, a
/// lost graphics device, wallpaper windows destroyed from outside); the resource budget is checked with a light, a
/// medium and a heavy shader; and the library is browsed with a wallpaper running while the window's responsiveness,
/// the wallpaper's frame rate and the memory are watched. One line per check, a summary at the end, exit code 0 only
/// when everything passed. Nothing is saved: the previous wallpaper is back afterwards.
/// </summary>
internal sealed class SelfTest
{
    private const int MemoryCheckSeconds = 3;
    private const long MaxPrivateMegabytes = 3000;
    private const int FirstFramesSeconds = 30;

    private readonly App _app;
    private readonly SelfTestReport _report;
    private readonly string? _only;
    private readonly string? _resumeExe;
    private readonly Process _process = Process.GetCurrentProcess();
    private bool _finished;

    private SelfTest(App app, string? outFile, string? only, string? resumeExe)
    {
        _app = app;
        _report = new SelfTestReport(outFile);
        _only = only;
        _resumeExe = resumeExe;
    }

    /// <param name="only">run just this part, null for all</param>
    /// <param name="resumeExe">exe of the instance that was stopped for the test; started again afterwards</param>
    public static void Start(App app, string? outFile, string? only, string? resumeExe) =>
        _ = new SelfTest(app, outFile, only, resumeExe).RunAsync();

    private bool Wants(string part) => _only == null || string.Equals(_only, part, StringComparison.OrdinalIgnoreCase);

    private async Task RunAsync()
    {
        _app.Settings.ReadOnly = true;
        _app.Governor.IgnoreRules = true;   // a maximised window or battery power must not change what is measured
        _app.SetPaused(false);
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MemoryCheckSeconds) };
        guard.Tick += (_, _) => StopIfMemoryRunsAway();
        guard.Start();
        try
        {
            await RunPartsAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _report.Check("self-test", false, "crashed: " + e.Message);
            Log.Error("selftest", e);
        }
        guard.Stop();
        Finish();
    }

    /// <summary>A test must never endanger the PC it runs on: far more memory than the app can need ends the run.</summary>
    private void StopIfMemoryRunsAway()
    {
        _process.Refresh();
        long megabytes = _process.PrivateMemorySize64 >> 20;
        if (megabytes > MaxPrivateMegabytes)
        {
            _report.Check("memory guard", false, $"the self-test was stopped: the process holds {megabytes} MB");
            Finish();
        }
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        string verdict = _report.Failed == 0 ? " - everything works" : "";
        _report.Finish($"SUMMARY: {_report.Passed} passed, {_report.Failed} failed{verdict}");
        Log.Write($"selftest: {_report.Passed} passed, {_report.Failed} failed");
        Environment.ExitCode = _report.Failed == 0 ? 0 : 1;
        _app.Exit();
        if (_resumeExe != null)
        {
            var start = new ProcessStartInfo(_resumeExe, "--tray") { UseShellExecute = false };
            Try.Run("start the previous instance again", () => Process.Start(start)?.Dispose());
        }
    }

    private async Task RunPartsAsync()
    {
        await Task.Delay(1500);
        var displays = _app.Displays;
        _report.InsertHeader(
            $"Wallpepper {Installer.VersionText} self-test, {DateTime.Now:yyyy-MM-dd HH:mm}, {_app.Renderer.AdapterName}, {displays.Count} display(s): "
            + string.Join(", ", displays.Select(slot => $"{slot.Screen.Bounds.Width}x{slot.Screen.Bounds.Height}@{slot.Target.Settings.RefreshHz}")));
        string? windows = Probe.WindowProblem(_app);
        _report.Check("wallpaper windows", windows == null, windows ?? $"{displays.Count} window(s) in the desktop layer, visible, display-sized");

        // without frames nothing else can be judged (and every further step would only wait for its time-out)
        int[] first = await Probe.FramesOncePresenting(_app, FirstFramesSeconds);
        bool rendering = first.Length > 0 && first.All(count => count > 0);
        _report.Check("rendering", rendering, $"frames per display in 1 s: {Probe.Join(first)}");
        if (!rendering)
        {
            return;
        }

        string? light = null;
        string? second = null;
        if (Wants("items") || Wants("recovery"))
        {
            var items = new ItemsCheck(_app, _report);
            await items.RunAsync(quick: !Wants("items"));
            light = items.Lightest;
            second = items.SecondLightest;
        }
        if (Wants("recovery"))
        {
            await new RecoveryCheck(_app, _report).RunAsync(light, second);
        }
        if (Wants("fit"))
        {
            await new FitCheck(_app, _report).RunAsync();
        }
        if (Wants("browse"))
        {
            await new BrowseCheck(_app, _report).RunAsync();
        }
        if (_only == null)
        {
            var governor = _app.Governor;
            _report.Check("status", !governor.Stalled && governor.Running == _app.Displays.Count, governor.Status);
        }
    }
}
