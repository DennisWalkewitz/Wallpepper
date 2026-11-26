using System.Windows;
using Wallpepper.Desktop;

namespace Wallpepper.Ui;

/// <summary>One display as the <see cref="MonitorPicker"/> draws it.</summary>
/// <param name="Bounds">physical pixels, virtual desktop coordinates</param>
/// <param name="Caption">small second line (what the display shows)</param>
internal sealed record PickerMonitor(string Id, int Index, Rect Bounds, bool Primary, string Caption)
{
    public static IReadOnlyList<PickerMonitor> From(IEnumerable<DisplaySlot> slots, Func<string?, string> titleOf) =>
        slots.Select(slot =>
        {
            var bounds = slot.Screen.Bounds;
            return new PickerMonitor(
                slot.Id, slot.Index, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), slot.Screen.Primary, titleOf(slot.ItemPath));
        }).ToList();
}
