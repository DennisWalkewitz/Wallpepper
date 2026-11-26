using System.Diagnostics;
using System.Threading.Tasks;
using Wallpepper.Desktop;
using Wallpepper.Desktop.Performance;
using Wallpepper.Library;

namespace Wallpepper.SelfTesting;

/// <summary>
/// The resource budget at the default level: a light shader must run at (or near) full resolution and the level's
/// frame-rate ceiling, a medium one must be fitted to about the budget, the heaviest must end on its light settings or
/// be flagged as too heavy; and in every case the plan must come to rest.
/// </summary>
internal sealed class FitCheck
{
    private const string HeaviestShader = "rtx-pathtracer.hlsl";
    private const double MediumCostMs = 9;
    private const double MinLightCostMs = 1;
    private const int SettleSeconds = 14;
    private const int MaxSettleSeconds = 90;
    private const int WatchSeconds = 20;

    /// <summary>
    /// The plan is what is checked. The measured share is reported next to it; it only fails the check when it is far
    /// off, because on a PC in use live timings include other programs' GPU work and a GPU at its idle clock.
    /// </summary>
    private const double Tolerance = 2.5;

    private readonly App _app;
    private readonly SelfTestReport _report;

    public FitCheck(App app, SelfTestReport report)
    {
        _app = app;
        _report = report;
    }

    private IReadOnlyList<DisplaySlot> Slots => _app.Displays;

    public async Task RunAsync()
    {
        _app.SetResourceLevel(ResourceLevel.Default);
        _app.Governor.MeasureWanted = true;
        ResourceLevel level = ResourceLevel.Get(ResourceLevel.Default, false)!;

        var measured = _app.Library.Items
            .Where(path => ShaderLibrary.IsShader(path) && ShaderLibrary.IsBundled(path) && _app.Shaders.KnownCost(path, false) > 0)
            .Where(path => !_app.Library.Metadata(path).IsTerminalOnly && !_app.Library.Metadata(path).IsMultiMonitor)
            .OrderBy(path => _app.Shaders.KnownCost(path, false))
            .ToList();
        if (measured.Count == 0)
        {
            _report.Check("resource budget", false, "no shader has a measured cost yet (open the library once so that they are measured)");
            return;
        }
        string light = measured.FirstOrDefault(path => _app.Shaders.KnownCost(path, false) >= MinLightCostMs) ?? measured[0];
        string medium = measured.OrderBy(path => Math.Abs(_app.Shaders.KnownCost(path, false) - MediumCostMs)).First();
        string heavy = measured.FirstOrDefault(path => Path.GetFileName(path).Equals(HeaviestShader, StringComparison.OrdinalIgnoreCase))
            ?? measured[^1];

        // a light shader keeps the frame-rate ceiling and close to full resolution (the plan aims a little under the
        // budget, so a shader that only just fits at full size ends slightly below it)
        await Check(light, level, "light", plan => plan.All(fit => fit.Scale >= 0.8f && fit.Fps >= level.Fps * 0.9));
        // "fitted to about the budget": below full quality, and not further down than half the budget would allow
        await Check(medium, level, "medium", plan => plan.Any(fit => fit.Load < level.Fps) && PlannedShare() >= level.Share * 0.5);
        await Check(heavy, level, "heavy", _ => _app.Shaders.IsLight(heavy) || Slots.Any(slot => slot.Fit.TooHeavy));
    }

    private double PlannedShare() => Slots.Sum(slot => slot.Fit.FrameMs * slot.Fit.Plan.Load) / 1000.0;

    private async Task Check(string path, ResourceLevel level, string kind, Func<IReadOnlyList<Fit>, bool> planIsRight)
    {
        _app.Wallpaper.Apply(path);
        await Probe.ShaderOnScreen(_app, path, 120);
        await WaitUntilSettled();

        // watch: frames, GPU time actually spent, and whether the plan still moves
        int changesBefore = Slots.Sum(slot => slot.Fit.PlanChanges);
        var before = Slots.Select(slot => (slot.Target.Stats.Frames, slot.Target.Stats.GpuSamples, slot.Target.Stats.GpuMicrosTotal)).ToList();
        var watch = Stopwatch.StartNew();
        await Task.Delay(WatchSeconds * 1000);
        double seconds = watch.Elapsed.TotalSeconds;
        int changes = Slots.Sum(slot => slot.Fit.PlanChanges) - changesBefore;

        double share = 0;
        var frames = new List<int>();
        for (int i = 0; i < Slots.Count && i < before.Count; i++)
        {
            var stats = Slots[i].Target.Stats;
            int presented = stats.Frames - before[i].Frames;
            int timed = stats.GpuSamples - before[i].GpuSamples;
            double frameMs = timed > 0 ? (stats.GpuMicrosTotal - before[i].GpuMicrosTotal) / 1000.0 / timed : 0;
            share += frameMs * (presented / seconds) / 1000.0;
            frames.Add(presented);
        }

        var plans = Slots.Select(slot => slot.Fit.Plan).ToList();
        bool tooHeavy = Slots.Any(slot => slot.Fit.TooHeavy);
        bool isLight = _app.Shaders.IsLight(path);
        double planned = PlannedShare();
        bool flows = frames.All(count => count >= WatchSeconds * PerformanceFitter.MinFps / 2);
        bool planWithinBudget = planned <= level.Share || tooHeavy;
        bool measuredPlausible = share <= level.Share * Tolerance || tooHeavy;
        bool ok = flows && planWithinBudget && measuredPlausible && changes == 0 && planIsRight(plans);

        _report.Check($"budget {level.Name}, {kind}: {Path.GetFileName(path)}", ok,
            string.Join(", ", plans.Select(fit => $"{fit.Scale * 100:0}% at {fit.Fps} fps"))
            + $", stored cost {Probe.Number(_app.Shaders.KnownCost(path, isLight), "0.00")} ms per 1080p frame"
            + $", GPU measured {Probe.Number(share * 100)}% (planned {Probe.Number(planned * 100)}%) of {level.Share * 100:0}% allowed"
            + $", plan changes in {WatchSeconds} s: {changes}, frames {Probe.Join(frames)}"
            + (isLight ? ", light settings" : "") + (tooHeavy ? ", flagged too heavy for this PC" : ""));
    }

    /// <summary>Waits until no display's plan has changed for a while (compile, cost measurement, light settings, hysteresis).</summary>
    private async Task WaitUntilSettled()
    {
        var total = Stopwatch.StartNew();
        var calm = Stopwatch.StartNew();
        string last = "";
        while (calm.Elapsed.TotalSeconds < SettleSeconds && total.Elapsed.TotalSeconds < MaxSettleSeconds)
        {
            await Task.Delay(500);
            string now = string.Join("|", Slots.Select(slot => $"{slot.Fit.Plan} {slot.Fit.Light} {slot.Fit.BaseCost:0.00} {slot.Target.Stats.DrawnPath}"));
            if (now != last)
            {
                last = now;
                calm.Restart();
            }
        }
    }
}
