namespace Wallpepper.Desktop.Performance;

/// <summary>What the performance controller knows about one display: its cost estimate, the plan and the measured load.</summary>
internal sealed class FitState
{
    /// <summary>The shader the estimate belongs to; null while the display is not fitted (paused, manual, web).</summary>
    public string? Path { get; private set; }

    /// <summary>The estimate is for the shader's light settings.</summary>
    public bool Light { get; private set; }

    /// <summary>GPU ms per frame of this display at full resolution, as the plan assumes it.</summary>
    public double FrameMs { get; set; }

    /// <summary>The stored burst cost the estimate started from (ms per 1080p frame); 0 = a guess.</summary>
    public double BaseCost { get; set; }

    /// <summary>Factor (1 or more) by which live measurements raised the estimate above the stored cost.</summary>
    public double Correction { get; set; } = 1;

    public Fit Plan { get; set; }

    /// <summary>Measured share of GPU time (0..1) over the last window.</summary>
    public double Share { get; set; }

    /// <summary>Allowed share for this display (0 = no limit).</summary>
    public double Budget { get; set; }

    /// <summary>Over the budget even at the floors (and with light settings, if the shader has any).</summary>
    public bool TooHeavy { get; set; }

    /// <summary>Scale and frame rate are set by hand (set by the render governor).</summary>
    public bool Manual { get; set; }

    /// <summary>Times the plan changed since the shader was set (the self-test checks that this comes to rest).</summary>
    public int PlanChanges { get; set; }

    // ---- bookkeeping of the controller
    internal double ChangedAt { get; set; } = double.MinValue;
    internal double SampledAt { get; set; } = double.MinValue;
    internal bool WindowOpen { get; set; }
    internal double WindowStart { get; set; }
    internal long WindowMicros { get; set; }
    internal int WindowTimed { get; set; }
    internal int WindowFrames { get; set; }
    internal int OverWindows { get; set; }
    internal int CalmWindows { get; set; }
    internal bool MeasurementRequested { get; set; }

    /// <summary>The correction before the last raise and the share that caused it: undone when the raise changed nothing.</summary>
    internal double CorrectionBeforeRaise { get; set; } = 1;
    internal double ShareBeforeRaise { get; set; }
    internal double LoadBeforeRaise { get; set; }
    internal bool RaisePending { get; set; }

    /// <summary>Live timings stopped being trusted: lowering the quality did not lower the measured load (a throttled GPU).</summary>
    internal bool LiveIgnored { get; set; }
    internal double RaisedAt { get; set; } = double.MinValue;
    internal double LoosenWaitSeconds { get; set; }

    public bool IsFitted => Path != null;

    internal void Begin(string path, bool light, double loosenWaitSeconds)
    {
        Path = path;
        Light = light;
        Correction = 1;
        CorrectionBeforeRaise = 1;
        RaisePending = false;
        LiveIgnored = false;
        TooHeavy = false;
        Share = 0;
        PlanChanges = 0;
        OverWindows = 0;
        CalmWindows = 0;
        MeasurementRequested = false;
        ChangedAt = double.MinValue;
        SampledAt = double.MinValue;
        WindowOpen = false;
        WindowStart = 0;
        BaseCost = -1;
        RaisedAt = double.MinValue;
        LoosenWaitSeconds = loosenWaitSeconds;
    }

    internal void Clear()
    {
        Path = null;
        Share = 0;
        TooHeavy = false;
    }
}
