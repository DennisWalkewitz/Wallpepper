namespace Wallpepper.Settings;

/// <summary>Post-process pass settings. Neutral values skip the pass entirely.</summary>
internal sealed class FilterSettings
{
    private const float Epsilon = 0.004f;

    /// <summary>0.2 .. 1.5</summary>
    public float Brightness { get; set; } = 1f;

    /// <summary>0.5 .. 1.5</summary>
    public float Contrast { get; set; } = 1f;

    /// <summary>0 .. 2</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>-1 (cool) .. +1 (warm)</summary>
    public float Temperature { get; set; }

    /// <summary>0 .. 1</summary>
    public float Blur { get; set; }

    /// <summary>0 .. 1</summary>
    public float Vignette { get; set; }

    public bool IsNeutral() =>
        Math.Abs(Brightness - 1) < Epsilon && Math.Abs(Contrast - 1) < Epsilon && Math.Abs(Saturation - 1) < Epsilon
        && Math.Abs(Temperature) < Epsilon && Blur < Epsilon && Vignette < Epsilon;

    public void Reset()
    {
        Brightness = Contrast = Saturation = 1;
        Temperature = Blur = Vignette = 0;
    }
}
