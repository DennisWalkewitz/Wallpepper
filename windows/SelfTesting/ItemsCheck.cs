using System.Diagnostics;
using System.Threading.Tasks;
using Wallpepper.Library;
using Wallpepper.Rendering;
using Wallpepper.Web;

namespace Wallpepper.SelfTesting;

/// <summary>Sets every library item the way the button sets it and checks that it really shows on every display.</summary>
internal sealed class ItemsCheck
{
    private const int CompileTimeoutSeconds = 120;
    private const int PageTimeoutSeconds = 30;

    /// <summary>A frame with this few colours on the sample grid is blank.</summary>
    private const int BlankColours = 8;

    private readonly App _app;
    private readonly SelfTestReport _report;
    private double _lightestMs = double.MaxValue;

    public ItemsCheck(App app, SelfTestReport report)
    {
        _app = app;
        _report = report;
    }

    /// <summary>The two shaders with the shortest frame times: what the recovery checks run with.</summary>
    public string? Lightest { get; private set; }

    public string? SecondLightest { get; private set; }

    /// <param name="quick">only find a light shader to work with (a few items), instead of checking every one</param>
    public async Task RunAsync(bool quick)
    {
        if (!quick)
        {
            _report.Note("name | result | compiled, frames per display in 1 s, average frame time, distinct colours per display (or what is wrong)");
        }
        foreach (string path in _app.Library.Items)
        {
            if (quick && (WebInfo.IsWeb(path) || SecondLightest != null))
            {
                continue;
            }
            _app.Wallpaper.Apply(path);
            if (WebInfo.IsWeb(path))
            {
                await CheckPage(path);
            }
            else
            {
                await CheckShader(path, report: !quick);
            }
        }
    }

    private async Task CheckPage(string path)
    {
        var waited = Stopwatch.StartNew();
        var state = _app.Web.StateOf(path);
        while (waited.Elapsed.TotalSeconds < PageTimeoutSeconds && state.State != WebPageState.Loaded && state.State != WebPageState.Failed
            && !_app.Status.HasError(path))
        {
            await Task.Delay(250);
            state = _app.Web.StateOf(path);
        }
        string? problem = _app.Status.ErrorOf(path) ?? state.Problem;
        bool ok = state.State == WebPageState.Loaded && problem == null;
        _report.Check(Path.GetFileName(path), ok, "web page " + (problem ?? state.State.ToString().ToLowerInvariant()));
    }

    private async Task CheckShader(string path, bool report)
    {
        string name = Path.GetFileName(path);
        var metadata = _app.Library.Metadata(path);

        // wait for the first frame of this shader (compiling can take seconds), then measure one second
        bool shown = await Probe.ShaderOnScreen(_app, path, CompileTimeoutSeconds);
        if (_app.Status.ErrorOf(path) is string error)
        {
            Check(report, name, false, "does not compile: " + error.Split('\n')[0].Trim());
            return;
        }
        if (!shown)
        {
            Check(report, name, false, $"not on screen after {CompileTimeoutSeconds} s");
            return;
        }

        var clock = Stopwatch.StartNew();
        int[] frames = await Probe.CountFrames(_app, 1000);
        double seconds = clock.Elapsed.TotalSeconds;
        FrameCapture[] captures = await CaptureAll();

        // terminal effects only change terminal text, which a wallpaper does not have; and a shader that spans the
        // displays may leave one of them almost empty, so "blank" means blank everywhere
        bool terminalOnly = metadata.IsTerminalOnly;
        string? windows = Probe.WindowProblem(_app);
        bool blank = !terminalOnly && captures.Max(capture => capture.Colours) <= BlankColours;
        string? problem = frames.Any(count => count == 0) ? "NO FRAMES PRESENTED" : windows ?? (blank ? "blank image" : null);
        double ms = frames.Max() > 0 ? seconds * 1000.0 / frames.Max() : 0;
        Check(report, name, problem == null,
            $"compiled, frames {Probe.Join(frames)}, {Probe.Number(ms)} ms, colours {Probe.Join(captures.Select(capture => capture.Colours))}"
            + (terminalOnly ? " (terminal effect: blank without text is expected)" : "")
            + (problem != null ? " - " + problem : ""));

        if (problem == null && !terminalOnly && !metadata.IsMultiMonitor && ms < _lightestMs)
        {
            SecondLightest = Lightest;
            Lightest = path;
            _lightestMs = ms;
        }
        else if (problem == null && !terminalOnly && !metadata.IsMultiMonitor)
        {
            SecondLightest ??= path;
        }
    }

    private void Check(bool report, string name, bool ok, string detail)
    {
        if (report || !ok)
        {
            _report.Check(name, ok, detail);
        }
    }

    /// <summary>Reads back what each display is about to show.</summary>
    private async Task<FrameCapture[]> CaptureAll()
    {
        var slots = _app.Displays.ToList();
        var captures = new FrameCapture[slots.Count];
        var pending = new List<Task>();
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            slots[i].Target.CaptureNextFrame(capture =>
            {
                captures[index] = capture;
                done.TrySetResult(true);
            });
            pending.Add(done.Task);
        }
        _app.Renderer.Wake();
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(5000));
        return captures;
    }
}
