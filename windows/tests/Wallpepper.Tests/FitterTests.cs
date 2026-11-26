using Wallpepper.Desktop.Performance;
using Xunit;

namespace Wallpepper.Tests;

public class FitterTests
{
    private static readonly ResourceLevel Low = ResourceLevel.Get(ResourceLevelId.Low, false)!;

    [Fact]
    public void NoBudgetMeansFullQuality()
    {
        Fit fit = PerformanceFitter.Plan(frameMs: 50, budgetMs: 0, maxFps: 60);
        Assert.Equal(new Fit(1f, 60, true), fit);
    }

    [Fact]
    public void LightShaderKeepsFullResolutionAndTheCeiling()
    {
        // 1.5 ms per frame at 30 fps is 45 ms per second, below the 75 ms one of two displays gets at Low
        Fit fit = PerformanceFitter.Plan(1.5, PerformanceFitter.BudgetMs(Low, 2), Low.Fps);
        Assert.Equal(new Fit(1f, 30, true), fit);
    }

    [Fact]
    public void MediumShaderLosesResolutionFirstButNotBelowHalf()
    {
        Fit fit = PerformanceFitter.Plan(9.25, PerformanceFitter.BudgetMs(Low, 2), Low.Fps);
        Assert.Equal(PerformanceFitter.ComfortScale, fit.Scale);
        Assert.InRange(fit.Fps, PerformanceFitter.MinFps, 29);
        Assert.True(fit.Meets);
        Assert.True(9.25 * fit.Load <= 75 * PerformanceFitter.Headroom);
    }

    [Fact]
    public void HeavyShaderEndsAtTheFloorsAndIsFlagged()
    {
        Fit fit = PerformanceFitter.Plan(190, PerformanceFitter.BudgetMs(Low, 2), Low.Fps);
        Assert.Equal(new Fit(PerformanceFitter.MinScale, PerformanceFitter.MinFps, false), fit);
    }

    [Fact]
    public void BudgetIsSharedBetweenDisplays()
    {
        Assert.Equal(150, PerformanceFitter.BudgetMs(Low, 1), 6);
        Assert.Equal(75, PerformanceFitter.BudgetMs(Low, 2), 6);
        Assert.Equal(0, PerformanceFitter.BudgetMs(ResourceLevel.Get(ResourceLevelId.Unlimited, false), 2));
        Assert.Equal(0, PerformanceFitter.BudgetMs(null, 2));
    }

    [Fact]
    public void BatteryMeansOneLevelLowerAndManualMeansNoLevel()
    {
        Assert.Equal(ResourceLevelId.Minimal, ResourceLevel.Get(ResourceLevelId.Low, onBattery: true)!.Id);
        Assert.Equal(ResourceLevelId.Minimal, ResourceLevel.Get(ResourceLevelId.Minimal, onBattery: true)!.Id);
        Assert.Null(ResourceLevel.Get(ResourceLevelId.Manual, onBattery: false));
    }

    [Fact]
    public void PreviewOfAHeavyShaderIsThrottledHard()
    {
        Fit light = PreviewPlanner.Plan(costMs: 2, previewPixels: 450 * 250, Low);
        Fit heavy = PreviewPlanner.Plan(costMs: 190, previewPixels: 450 * 250, Low);
        Assert.Equal(new Fit(1f, 30, true), light);
        Assert.Equal(0.5f, heavy.Scale);
        Assert.InRange(heavy.Fps, 4, 12);
    }
}
