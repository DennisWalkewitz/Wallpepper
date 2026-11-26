using System.Drawing;
using Wallpepper.Desktop;
using Xunit;

namespace Wallpepper.Tests;

public class RulesTests
{
    private static readonly Rectangle Screen = new Rectangle(0, 0, 1920, 1080);
    private static readonly Rectangle WorkArea = new Rectangle(0, 0, 1920, 1032);   // taskbar of 48 pixels at the bottom

    [Fact]
    public void FullScreenAndMaximisedWindowsCoverADisplay()
    {
        Assert.True(CoverScan.Covers(Screen, WorkArea));
        Assert.True(CoverScan.Covers(new Rectangle(-8, -8, 1936, 1048), WorkArea));   // maximised: the frame hangs over the edges
    }

    [Fact]
    public void SnappedAndSmallWindowsDoNot()
    {
        Assert.False(CoverScan.Covers(new Rectangle(0, 0, 960, 1032), WorkArea));
        Assert.False(CoverScan.Covers(new Rectangle(200, 100, 1200, 800), WorkArea));
        Assert.False(CoverScan.Covers(new Rectangle(1920, 0, 1920, 1080), WorkArea));   // on the display next door
    }

    [Fact]
    public void TheMostImportantReasonWins()
    {
        var everything = new PauseInputs
        {
            BenchmarkRunning = true, UserPaused = true, DisplayOff = true, Locked = true, PauseWhenLocked = true,
        };
        Assert.Equal(PauseReason.BenchmarkRunning, PauseRules.Global(everything));
        Assert.Equal(PauseReason.PausedByUser, PauseRules.Global(everything with { BenchmarkRunning = false }));
        Assert.Equal(PauseReason.DisplayOff, PauseRules.Global(everything with { BenchmarkRunning = false, UserPaused = false }));
    }

    [Fact]
    public void TheUsersSettingsDecideAboutLockAndBattery()
    {
        Assert.Null(PauseRules.Global(new PauseInputs { Locked = true, PauseWhenLocked = false }));
        Assert.Equal(PauseReason.ScreenLocked, PauseRules.Global(new PauseInputs { Locked = true, PauseWhenLocked = true }));
        Assert.Null(PauseRules.Global(new PauseInputs { OnBattery = true }));
        Assert.Equal(PauseReason.OnBattery, PauseRules.Global(new PauseInputs { OnBattery = true, PauseOnBattery = true }));
        Assert.Equal(PauseReason.BatterySaver, PauseRules.Global(new PauseInputs { BatterySaver = true, PauseOnBatterySaver = true }));
        Assert.Equal(PauseReason.RemoteSession, PauseRules.Global(new PauseInputs { RemoteSession = true }));
    }

    [Fact]
    public void ACoveredDisplayPausesUnlessItIsToldToKeepRunning()
    {
        Assert.Equal(PauseReason.Covered, PauseRules.ForDisplay(null, covered: true, keepRunningWhenCovered: false));
        Assert.Null(PauseRules.ForDisplay(null, covered: true, keepRunningWhenCovered: true));
        Assert.Equal(PauseReason.ScreenLocked, PauseRules.ForDisplay(PauseReason.ScreenLocked, covered: false, keepRunningWhenCovered: true));
    }
}
