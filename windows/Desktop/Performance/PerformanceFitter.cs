namespace Wallpepper.Desktop.Performance;

/// <summary>What was chosen for one display.</summary>
internal readonly record struct Fit(float Scale, int Fps, bool Meets)
{
    /// <summary>Share of the full-resolution cost per second: scale x scale x frames per second.</summary>
    public double Load => Scale * Scale * Fps;
}

/// <summary>
/// Policy only, no measuring and no state: turns the GPU cost of a frame and a budget into a render scale and a
/// frame rate. Resolution goes down first, to half; then the frame rate to its floor; then the resolution to its
/// floor. The plan aims a little below the budget so that small swings do not cross it.
/// (Same numbers as the Linux build: linux/src/fit.cpp.)
/// </summary>
internal static class PerformanceFitter
{
    public const float MinScale = 0.3f;
    public const float ComfortScale = 0.5f;
    public const int MinFps = 12;

    /// <summary>The plan aims at this share of the budget.</summary>
    public const double Headroom = 0.85;

    /// <summary>Quality is only raised again when the measured load is below this share of the budget.</summary>
    public const double RaiseBelow = 0.6;

    /// <summary>Below this resolution a shader's cheaper "@light" settings are the better trade.</summary>
    public const float LightBelowScale = 0.5f;

    public const double Pixels1080 = 1920.0 * 1080.0;

    private static readonly int[] Rates = { 12, 15, 20, 24, 30, 40, 48, 60, 72, 90, 100, 120, 144, 165, 240 };

    /// <param name="frameMs">GPU milliseconds for one frame of this display at full resolution</param>
    /// <param name="budgetMs">GPU milliseconds per second this display may use; 0 = no limit</param>
    /// <param name="maxFps">frame-rate ceiling</param>
    public static Fit Plan(double frameMs, double budgetMs, int maxFps)
    {
        float scale = 1f;
        int fps = Math.Max(1, maxFps);
        if (budgetMs <= 0 || frameMs <= 0)
        {
            return new Fit(scale, fps, true);
        }
        double Used() => frameMs * scale * scale * fps;

        double aim = budgetMs * Headroom;
        scale = ScaleFor(frameMs, aim, fps, ComfortScale);
        if (Used() > aim)
        {
            fps = RateFor(frameMs, aim, scale, MinFps, maxFps);
        }
        if (Used() > aim)
        {
            scale = ScaleFor(frameMs, aim, fps, MinScale);
        }
        return new Fit(scale, fps, Used() <= budgetMs);
    }

    /// <summary>GPU milliseconds per second a single display may use when <paramref name="displays"/> are rendering.</summary>
    public static double BudgetMs(ResourceLevel? level, int displays) =>
        level == null || !level.IsLimited ? 0 : level.Share * 1000.0 / Math.Max(1, displays);

    /// <summary>The largest scale (in steps of 5%) that stays in the budget, but not below <paramref name="floor"/>.</summary>
    private static float ScaleFor(double frameMs, double budgetMs, int fps, float floor) =>
        (float)Math.Clamp(Math.Floor(Math.Sqrt(budgetMs / (frameMs * fps)) * 20) / 20, floor, 1);

    /// <summary>The highest usual frame rate that stays in the budget, but not below <paramref name="floor"/>.</summary>
    private static int RateFor(double frameMs, double budgetMs, float scale, int floor, int max)
    {
        double allowed = budgetMs / (frameMs * scale * scale);
        int best = Math.Min(floor, max);
        foreach (int rate in Rates)
        {
            if (rate >= best && rate <= allowed && rate <= max)
            {
                best = rate;
            }
        }
        return best;
    }
}
