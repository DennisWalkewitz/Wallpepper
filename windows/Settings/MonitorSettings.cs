namespace Wallpepper.Settings;

/// <summary>Per-display overrides. 0 = use the global value.</summary>
internal sealed class MonitorSettings
{
    public int Fps { get; set; }

    public float RenderScale { get; set; }

    public bool KeepRunningWhenCovered { get; set; }

    public bool CustomFilters { get; set; }

    public FilterSettings Filters { get; set; } = new();

    /// <summary>Frame rate or resolution is set by hand: the performance fitter leaves this display alone.</summary>
    public bool IsPinned => Fps > 0 || RenderScale > 0;
}
