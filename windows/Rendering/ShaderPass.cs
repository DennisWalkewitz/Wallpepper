namespace Wallpepper.Rendering;

/// <summary>Everything a shader draw needs besides the shader and its target.</summary>
internal readonly record struct ShaderPass
{
    /// <summary>Viewport in target pixels; its size is also the "Resolution" the shader sees.</summary>
    public float ViewX { get; init; }

    public float ViewY { get; init; }

    public float ViewWidth { get; init; }

    public float ViewHeight { get; init; }

    /// <summary>Shader time in seconds.</summary>
    public float Time { get; init; }

    /// <summary>The "Scale" a Terminal shader sees: the display's DPI scale.</summary>
    public float DpiScale { get; init; }

    /// <summary>Red, green, blue, alpha.</summary>
    public float[] Background { get; init; }

    /// <summary>Monitor rectangles in span UV (four floats each); null = one monitor that is the whole picture.</summary>
    public float[]? MonitorRects { get; init; }

    public int MonitorIndex { get; init; }

    /// <summary>A pass that fills a whole target of the given size, as thumbnails, measurements and the benchmark draw it.</summary>
    public static ShaderPass FullTarget(int width, int height, float time, float[] background) => new ShaderPass
    {
        ViewWidth = width,
        ViewHeight = height,
        Time = time,
        DpiScale = 1f,
        Background = background,
    };
}
