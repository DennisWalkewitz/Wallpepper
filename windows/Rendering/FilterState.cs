using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>Immutable post-process parameters; the UI thread swaps the whole object (no locking, no per-frame allocation).</summary>
internal sealed record FilterState(float Brightness, float Contrast, float Saturation, float Temperature, float Blur, float Vignette, bool IsNeutral)
{
    public static FilterState From(FilterSettings settings) => new FilterState(
        settings.Brightness, settings.Contrast, settings.Saturation, settings.Temperature, settings.Blur, settings.Vignette, settings.IsNeutral());
}
