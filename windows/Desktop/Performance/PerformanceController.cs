using Wallpepper.Rendering;

namespace Wallpepper.Desktop.Performance;

/// <summary>
/// Measurement and bookkeeping for the fitter. Per display it keeps a cost estimate, plans from it and applies the
/// plan with hysteresis (at once when it gets tighter, slowly when there is room).
///
/// The estimate is the shader's stored cost, measured in a burst at full GPU clock. Live frame timings are not used
/// to derive a cost: under the light load of a fitted wallpaper an integrated GPU clocks itself down, and whatever
/// else uses the GPU lengthens our frames as well, so each frame can take several times longer than the shader needs.
/// A fitter that believed those timings would lower the quality, which slows the GPU further. Live timings only
/// answer one question: is the GPU time spent per second far above the budget, again and again? Only then the
/// estimate is raised, and the raise is taken back if it did not lower the load.
/// </summary>
internal sealed class PerformanceController
{
    /// <summary>A never measured shader starts as if it were heavy (ms per 1080p frame).</summary>
    private const double UnknownCostMs = 40;

    /// <summary>Light settings before they have been measured: assumed share of the normal cost.</summary>
    public const double LightGuess = 0.3;

    private const double WindowSeconds = 3;
    private const double SettleSeconds = 1.5;
    private const int MinTimedFrames = 3;

    /// <summary>
    /// "Clearly over": the measured share of GPU time is at least this many times the budget. Live timings on a PC that
    /// is in use swing widely (other programs' work on the GPU lengthens our frames too), so less than double proves
    /// nothing about the shader.
    /// </summary>
    private const double OverBudget = 2.0;

    /// <summary>... in this many windows in a row (also the number of calm windows before quality goes up again).</summary>
    private const int Patience = 3;
    private const double MaxRaiseStep = 3;
    private const double TightenAfterSeconds = 2;
    private const double LoosenAfterSeconds = 10;
    private const double FirstLoosenWaitSeconds = 30;
    private const double MaxLoosenWaitSeconds = 600;
    private const double SampleAfterChangeSeconds = 8;
    private const double SampleEverySeconds = 30;

    private readonly IShaderCosts _costs;
    private readonly Action<string> _requestMeasurement;

    /// <param name="requestMeasurement">
    /// asks for the cost of a shader to be measured and stored (it then shows up in <see cref="IShaderCosts.KnownCost"/>)
    /// </param>
    public PerformanceController(IShaderCosts costs, Action<string> requestMeasurement)
    {
        _costs = costs;
        _requestMeasurement = requestMeasurement;
    }

    /// <summary>The GPU timing queries are needed right now (after a change, and for a moment every half minute).</summary>
    public bool NeedsSample { get; private set; }

    public void Update(IReadOnlyList<DisplaySlot> slots, ResourceLevel? level, double now)
    {
        int running = slots.Count(slot => !slot.IsPaused && slot.Target.Settings.ShaderPath != null);
        bool needsSample = false;
        foreach (var slot in slots)
        {
            needsSample |= UpdateDisplay(slot, level, running, now);
        }
        NeedsSample = needsSample;
    }

    /// <returns>true when this display wants GPU timings right now.</returns>
    private bool UpdateDisplay(DisplaySlot slot, ResourceLevel? level, int running, double now)
    {
        var target = slot.Target;
        var state = slot.Fit;
        var settings = target.Settings;
        string? path = settings.ShaderPath;
        if (level == null || state.Manual || slot.IsPaused || path == null)
        {
            state.Clear();
            return false;
        }

        bool light = _costs.IsLight(path);
        if (state.Path != path || state.Light != light)
        {
            state.Begin(path, light, FirstLoosenWaitSeconds);
        }
        double known = _costs.KnownCost(path, light);
        if (known != state.BaseCost)
        {
            // the first look at this shader, or its cost has just been measured: the stored figure is the truth again
            state.BaseCost = known;
            state.Correction = 1;
            state.RaisePending = false;
            state.LiveIgnored = false;
        }
        if (known <= 0 && !state.MeasurementRequested)
        {
            state.MeasurementRequested = true;
            _requestMeasurement(path);
        }

        var bounds = slot.Screen.Bounds;
        double pixels = Math.Max(1, bounds.Width * (double)bounds.Height) / PerformanceFitter.Pixels1080;
        state.Budget = level.Share / Math.Max(1, running);
        MeasureLoad(target.Stats, state, now);
        state.FrameMs = BaseCost(path, light, known) * pixels * state.Correction;

        var plan = PerformanceFitter.Plan(state.FrameMs, PerformanceFitter.BudgetMs(level, running), level.CeilingFor(settings.RefreshHz));

        // a shader that would need less than half resolution (or cannot fit at all) switches to its light settings
        if ((!plan.Meets || plan.Scale < PerformanceFitter.LightBelowScale) && !light && _costs.CanUseLight(path))
        {
            _costs.UseLight(path);
            return true;
        }
        state.TooHeavy = !plan.Meets;
        Apply(target, state, plan, now);
        return now - state.ChangedAt < SampleAfterChangeSeconds || now - state.SampledAt > SampleEverySeconds;
    }

    private double BaseCost(string path, bool light, double known)
    {
        if (known > 0)
        {
            return known;
        }
        double normal = light ? _costs.KnownCost(path, false) : 0;
        return normal > 0 ? normal * LightGuess : UnknownCostMs;
    }

    /// <summary>The plan in force follows the new plan: at once the first time, soon when tighter, later when looser.</summary>
    private static void Apply(RenderTarget target, FitState state, Fit plan, double now)
    {
        var current = state.Plan;
        bool first = state.ChangedAt == double.MinValue;
        bool tighter = plan.Scale < current.Scale - 0.01f || plan.Fps < current.Fps;
        bool looser = !tighter && (plan.Scale >= current.Scale + 0.1f || plan.Fps > current.Fps);
        bool change = first || (tighter && now - state.ChangedAt > TightenAfterSeconds) || (looser && now - state.ChangedAt > LoosenAfterSeconds);
        if (change)
        {
            state.Plan = plan;
            state.ChangedAt = now;
            if (!first)
            {
                state.PlanChanges++;
            }
            // timings taken at the old plan say nothing about the new one: measure afresh once it has settled
            target.RestartGpuTiming();
            state.WindowOpen = false;
            state.WindowStart = now + SettleSeconds;
        }
        var inForce = state.Plan;
        target.Update(settings => settings with { Scale = inForce.Scale, FpsCap = inForce.Fps });
    }

    /// <summary>
    /// Every few seconds: the share of GPU time the display really used (average measured frame time times frames
    /// presented per second), and what follows from it.
    /// </summary>
    private static void MeasureLoad(TargetStats stats, FitState state, double now)
    {
        if (!state.WindowOpen)
        {
            if (now >= state.WindowStart)
            {
                state.WindowOpen = true;
                state.WindowStart = now;
                state.WindowTimed = stats.GpuSamples;
                state.WindowFrames = stats.Frames;
                state.WindowMicros = stats.GpuMicrosTotal;
            }
            return;
        }
        double seconds = now - state.WindowStart;
        if (seconds < WindowSeconds)
        {
            return;
        }
        int timed = stats.GpuSamples - state.WindowTimed;
        int frames = stats.Frames - state.WindowFrames;
        long micros = stats.GpuMicrosTotal - state.WindowMicros;
        state.WindowOpen = false;
        state.WindowStart = now;
        if (timed < MinTimedFrames || frames <= 0)
        {
            return;   // timing was off for (most of) the window
        }
        double frameMs = micros / 1000.0 / timed;
        state.Share = frameMs * (frames / seconds) / 1000.0;
        state.SampledAt = now;
        Judge(state, now);
    }

    private static void Judge(FitState state, double now)
    {
        double share = state.Share;
        double budget = state.Budget;
        if (budget <= 0 || state.LiveIgnored)
        {
            return;
        }

        if (state.RaisePending && state.Plan.Load < state.LoadBeforeRaise)
        {
            state.RaisePending = false;
            double expected = state.ShareBeforeRaise * state.Plan.Load / state.LoadBeforeRaise;
            if (share > (state.ShareBeforeRaise + expected) / 2)
            {
                // Lowering the quality did not lower the load: the GPU clocks down under this light work, and its
                // timings say nothing about what the shader costs. Back to the stored cost, for as long as it stands.
                state.Correction = state.CorrectionBeforeRaise;
                state.LiveIgnored = true;
                return;
            }
        }

        if (share > budget * OverBudget)
        {
            state.CalmWindows = 0;
            state.OverWindows++;
            if (state.OverWindows >= Patience)
            {
                state.OverWindows = 0;
                state.CorrectionBeforeRaise = state.Correction;
                state.ShareBeforeRaise = share;
                state.LoadBeforeRaise = state.Plan.Load;
                state.Correction *= Math.Clamp(share / (budget * PerformanceFitter.Headroom), 1.1, MaxRaiseStep);
                state.RaisePending = true;
                state.RaisedAt = now;
            }
        }
        else if (share < budget * PerformanceFitter.RaiseBelow && state.Correction > 1)
        {
            state.OverWindows = 0;
            state.CalmWindows++;
            if (state.CalmWindows >= Patience && now - state.RaisedAt >= state.LoosenWaitSeconds)
            {
                // far below the budget for a while: give some quality back; each time this is needed the wait doubles,
                // so a shader on the edge does not swing back and forth
                state.CalmWindows = 0;
                state.Correction = Math.Max(1, state.Correction * Math.Max(0.5, share / (budget * PerformanceFitter.Headroom)));
                state.RaisedAt = now;
                state.LoosenWaitSeconds = Math.Min(MaxLoosenWaitSeconds, state.LoosenWaitSeconds * 2);
            }
        }
        else
        {
            state.OverWindows = 0;
            state.CalmWindows = 0;
        }
    }
}
