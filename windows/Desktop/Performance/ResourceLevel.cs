namespace Wallpepper.Desktop.Performance;

internal enum ResourceLevelId
{
    Minimal,
    Low,
    Balanced,
    High,
    Unlimited,

    /// <summary>The user sets frame rate and resolution by hand; nothing is fitted.</summary>
    Manual,
}

/// <summary>
/// "Resource use": how much of the GPU's time the wallpaper may take, summed over all displays, and the highest
/// frame rate it aims for. The default is Low; on battery the next lower level applies.
/// (Same numbers as the Linux build: linux/src/fit.cpp.)
/// </summary>
internal sealed class ResourceLevel
{
    public const ResourceLevelId Default = ResourceLevelId.Low;

    /// <summary>Frame-rate ceiling of a level that names none, when the display's refresh rate is unknown.</summary>
    public const int FallbackFps = 60;

    public static readonly IReadOnlyList<ResourceLevel> All = new[]
    {
        new ResourceLevel(ResourceLevelId.Minimal, "Minimal", 0.08, 24),
        new ResourceLevel(ResourceLevelId.Low, "Low", 0.15, 30),
        new ResourceLevel(ResourceLevelId.Balanced, "Balanced", 0.30, 60),
        new ResourceLevel(ResourceLevelId.High, "High", 0.60, 60),
        new ResourceLevel(ResourceLevelId.Unlimited, "Max", 0, 0),
    };

    private ResourceLevel(ResourceLevelId id, string name, double share, int fps)
    {
        Id = id;
        Name = name;
        Share = share;
        Fps = fps;
    }

    public ResourceLevelId Id { get; }

    public string Name { get; }

    /// <summary>Fraction of GPU time, 0 = no limit.</summary>
    public double Share { get; }

    /// <summary>Frame-rate ceiling, 0 = the display's refresh rate.</summary>
    public int Fps { get; }

    public bool IsLimited => Share > 0;

    /// <summary>The level for an id (null for manual); one step lower on battery.</summary>
    public static ResourceLevel? Get(ResourceLevelId id, bool onBattery)
    {
        if (id == ResourceLevelId.Manual)
        {
            return null;
        }
        int index = Math.Max(0, All.ToList().FindIndex(level => level.Id == id));
        return All[onBattery ? Math.Max(0, index - 1) : index];
    }

    /// <summary>The frame-rate ceiling on a display with the given refresh rate.</summary>
    public int CeilingFor(int refreshHz) => Fps > 0 ? Fps : Math.Max(24, refreshHz);
}
