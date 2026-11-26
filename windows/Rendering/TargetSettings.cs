namespace Wallpepper.Rendering;

/// <summary>
/// What a render target should show and how. Immutable: the UI thread replaces the whole object, the render thread
/// reads it once per frame, so a frame never sees half a change.
/// </summary>
internal sealed record TargetSettings
{
    /// <summary>Full path of the .hlsl; null = nothing to draw (unassigned, or a web wallpaper covers the display).</summary>
    public string? ShaderPath { get; init; }

    public bool Paused { get; init; }

    /// <summary>Frames per second; 0 = the renderer's default.</summary>
    public int FpsCap { get; init; }

    /// <summary>Render resolution 0.25 .. 1 of the window; 0 = the renderer's default.</summary>
    public float Scale { get; init; }

    /// <summary>null or neutral = no filter pass.</summary>
    public FilterState? Filter { get; init; }

    /// <summary>Draw this display's slice of one image that spans the virtual desktop.</summary>
    public bool Span { get; init; }

    /// <summary>The slice's origin inside the virtual desktop, and the size of the virtual desktop (pixels).</summary>
    public int SpanX { get; init; }

    public int SpanY { get; init; }

    public int SpanWidth { get; init; }

    public int SpanHeight { get; init; }

    /// <summary>Index into the renderer's monitor rectangles.</summary>
    public int MonitorIndex { get; init; }

    /// <summary>For pacing by vsync.</summary>
    public int RefreshHz { get; init; } = 60;
}
