using Wallpepper.Desktop.Performance;

namespace Wallpepper.Desktop;

/// <summary>The one-line status shown in the library window ("Rendering on 2 displays · resource use Low").</summary>
internal static class StatusText
{
    public static string Format(
        PauseReason? global, int running, IReadOnlyList<DisplaySlot> slots, ResourceLevel? level, int manualFps,
        bool onBattery, bool softwareRendering, IReadOnlyList<string> notes)
    {
        if (global != null)
        {
            return "Paused · " + PauseReasonText.Of(global.Value);
        }
        var parts = new List<string>();
        parts.Add(running == slots.Count
            ? $"Rendering on {running} display{(running == 1 ? "" : "s")}"
            : $"Rendering on {running} of {slots.Count} displays");
        parts.Add((level == null ? $"{manualFps} fps" : "resource use " + level.Name) + (onBattery ? " (battery)" : ""));
        if (slots.Any(slot => slot.Fit.IsFitted && slot.Fit.Light))
        {
            parts.Add("light settings");
        }
        if (slots.Any(slot => slot.Fit.IsFitted && slot.Fit.TooHeavy))
        {
            parts.Add("too heavy for this level");
        }
        if (softwareRendering)
        {
            parts.Add("no GPU: drawn on the CPU");
        }
        if (notes.Count > 0)
        {
            parts.Add(string.Join(", ", notes));
        }
        return string.Join(" · ", parts);
    }
}
