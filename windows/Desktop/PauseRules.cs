namespace Wallpepper.Desktop;

/// <summary>Everything the pause decision depends on.</summary>
internal readonly record struct PauseInputs
{
    public bool BenchmarkRunning { get; init; }
    public bool UserPaused { get; init; }
    public bool DisplayOff { get; init; }
    public bool Locked { get; init; }
    public bool RemoteSession { get; init; }
    public bool BatterySaver { get; init; }
    public bool OnBattery { get; init; }

    // the user's settings
    public bool PauseWhenLocked { get; init; }
    public bool PauseOnBatterySaver { get; init; }
    public bool PauseOnBattery { get; init; }

    /// <summary>Self-test: only what cannot be ignored pauses (benchmark, the user, display off, lock).</summary>
    public bool IgnoreRules { get; init; }
}

/// <summary>The pause rules: pure decisions, no state.</summary>
internal static class PauseRules
{
    /// <summary>The reason that stops every display, or null.</summary>
    public static PauseReason? Global(in PauseInputs inputs)
    {
        if (inputs.BenchmarkRunning)
        {
            return PauseReason.BenchmarkRunning;
        }
        if (inputs.UserPaused)
        {
            return PauseReason.PausedByUser;
        }
        if (inputs.DisplayOff)
        {
            return PauseReason.DisplayOff;
        }
        if (inputs.PauseWhenLocked && inputs.Locked)
        {
            return PauseReason.ScreenLocked;
        }
        if (inputs.IgnoreRules)
        {
            return null;
        }
        if (inputs.RemoteSession)
        {
            return PauseReason.RemoteSession;
        }
        if (inputs.PauseOnBatterySaver && inputs.BatterySaver)
        {
            return PauseReason.BatterySaver;
        }
        return inputs.PauseOnBattery && inputs.OnBattery ? PauseReason.OnBattery : null;
    }

    /// <summary>The reason one display is paused, or null.</summary>
    public static PauseReason? ForDisplay(PauseReason? global, bool covered, bool keepRunningWhenCovered)
    {
        if (global != null)
        {
            return global;
        }
        return covered && !keepRunningWhenCovered ? PauseReason.Covered : null;
    }
}
