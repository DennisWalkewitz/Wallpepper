namespace Wallpepper.Rendering;

/// <summary>Self-test: size of a frame that is about to be presented and the number of distinct colours on a sample grid.</summary>
internal readonly record struct FrameCapture(int Width, int Height, int Colours);
