using System.Windows.Forms;
using Wallpepper.Library;

namespace Wallpepper.Desktop.Performance;

/// <summary>Answers, from stored costs alone, whether a shader can be fitted into the resource level at all.</summary>
internal static class HeavyCheck
{
    /// <summary>
    /// Even at the lowest resolution and frame rate (and with light settings, if it has any) this shader would be above
    /// the budget on this PC. Shown as a badge; setting such a shader asks once.
    /// </summary>
    public static bool IsTooHeavy(App app, string path)
    {
        ResourceLevel? level = ResourceLevel.Get(app.Settings.Config.Resource, false);
        double cost = app.Shaders.KnownCost(path, false);
        if (level == null || !level.IsLimited || !ShaderLibrary.IsShader(path) || cost <= 0)
        {
            return false;
        }
        var screen = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        double pixels = screen.Width * (double)screen.Height / PerformanceFitter.Pixels1080;
        double budgetMs = PerformanceFitter.BudgetMs(level, Math.Max(1, app.Displays.Count));
        bool Fits(double ms) => PerformanceFitter.Plan(ms * pixels, budgetMs, level.CeilingFor(ResourceLevel.FallbackFps)).Meets;

        if (Fits(cost))
        {
            return false;
        }
        if (!app.Shaders.CanUseLight(path))
        {
            return true;
        }
        double lightCost = app.Shaders.KnownCost(path, true);
        return !Fits(lightCost > 0 ? lightCost : cost * PerformanceController.LightGuess);
    }
}
