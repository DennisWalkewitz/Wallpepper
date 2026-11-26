namespace Wallpepper.Desktop.Performance;

/// <summary>
/// The library's live preview gets a small GPU budget of its own: the headroom the wallpaper's plan leaves below the
/// resource level, so wallpaper and preview together stay within it. A heavy shader is previewed at half resolution
/// and a few frames per second; the window itself needs the GPU too.
/// </summary>
internal static class PreviewPlanner
{
    private const double ShareWithoutLimit = 0.10;
    private const double UnknownCostMs = 40;
    private const float MinScale = 0.5f;
    private const int MinFps = 4;
    private const int MaxFps = 30;

    /// <param name="costMs">GPU ms per 1080p frame of the shader, 0 when unknown</param>
    /// <param name="previewPixels">size of the preview in device pixels</param>
    /// <param name="level">the resource level in force, null for manual</param>
    public static Fit Plan(double costMs, double previewPixels, ResourceLevel? level)
    {
        double share = level != null && level.IsLimited ? level.Share * (1 - PerformanceFitter.Headroom) : ShareWithoutLimit;
        double budgetMs = share * 1000.0;
        double frameMs = (costMs > 0 ? costMs : UnknownCostMs) * Math.Max(1, previewPixels) / PerformanceFitter.Pixels1080;

        float scale = 1f;
        int fps = MaxFps;
        if (frameMs * fps > budgetMs)
        {
            scale = (float)Math.Clamp(Math.Floor(Math.Sqrt(budgetMs / (frameMs * fps)) * 20) / 20, MinScale, 1);
        }
        if (frameMs * scale * scale * fps > budgetMs)
        {
            fps = (int)Math.Clamp(Math.Floor(budgetMs / (frameMs * scale * scale)), MinFps, MaxFps);
        }
        return new Fit(scale, fps, frameMs * scale * scale * fps <= budgetMs);
    }
}
