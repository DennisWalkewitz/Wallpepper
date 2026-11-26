namespace Wallpepper.Settings;

/// <summary>Where the library window was when it closed (device pixels).</summary>
internal sealed record WindowPlacement(int Left, int Top, int Right, int Bottom, bool Maximized);
