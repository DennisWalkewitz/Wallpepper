namespace Wallpepper.Desktop;

/// <summary>Why a display is not rendering.</summary>
internal enum PauseReason
{
    BenchmarkRunning,
    PausedByUser,
    DisplayOff,
    ScreenLocked,
    RemoteSession,
    BatterySaver,
    OnBattery,

    /// <summary>A maximised or full-screen window hides the desktop on this display.</summary>
    Covered,
}

internal static class PauseReasonText
{
    public static string Of(PauseReason reason) => reason switch
    {
        PauseReason.BenchmarkRunning => "benchmark running",
        PauseReason.PausedByUser => "paused by you",
        PauseReason.DisplayOff => "display off",
        PauseReason.ScreenLocked => "screen locked",
        PauseReason.RemoteSession => "remote desktop session",
        PauseReason.BatterySaver => "battery saver",
        PauseReason.OnBattery => "on battery",
        _ => "covered",
    };
}
