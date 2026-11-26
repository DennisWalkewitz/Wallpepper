using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Wallpepper.Desktop;
using Wallpepper.Interop;

namespace Wallpepper.SelfTesting;

/// <summary>Measuring helpers shared by the self-test's parts.</summary>
internal static class Probe
{
    public static string Join(IEnumerable<int> values) => string.Join("+", values);

    public static string Number(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);

    public static int[] Frames(IReadOnlyList<DisplaySlot> slots) => slots.Select(slot => slot.Target.Stats.Frames).ToArray();

    /// <summary>Frames presented per display during the next <paramref name="ms"/> milliseconds.</summary>
    public static async Task<int[]> CountFrames(App app, int ms)
    {
        var slots = app.Displays.ToList();
        int[] before = Frames(slots);
        await Task.Delay(ms);
        return slots.Select((slot, i) => slot.Target.Stats.Frames - before[i]).ToArray();
    }

    /// <summary>Frames per display in one second, measured again and again until every display renders (or the time is up).</summary>
    public static async Task<int[]> FramesOncePresenting(App app, int maxSeconds)
    {
        int[] frames = await CountFrames(app, 1000);
        for (int second = 1; second < maxSeconds && !(frames.Length > 0 && frames.All(count => count >= 5)); second++)
        {
            frames = await CountFrames(app, 1000);
        }
        return frames;
    }

    /// <summary>
    /// Waits until every display draws the given shader (it may have to be compiled first) or it turns out not to compile.
    /// </summary>
    /// <returns>false when the time ran out</returns>
    public static async Task<bool> ShaderOnScreen(App app, string path, int maxSeconds)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed.TotalSeconds < maxSeconds)
        {
            if (app.Status.HasError(path))
            {
                return true;
            }
            bool everywhere = app.Displays.Count > 0 && app.Displays.All(slot =>
                string.Equals(slot.Target.Stats.DrawnPath, path, StringComparison.OrdinalIgnoreCase));
            if (everywhere)
            {
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>Is every wallpaper window a visible child of the desktop layer, exactly as large as its display? null = yes.</summary>
    public static string? WindowProblem(App app)
    {
        if (app.Displays.Count == 0)
        {
            return "no wallpaper windows";
        }
        foreach (var slot in app.Displays)
        {
            IntPtr hwnd = slot.Window.Handle;
            var bounds = slot.Screen.Bounds;
            string display = $"display {slot.Index + 1}: ";
            if (!User32.IsWindow(hwnd))
            {
                return display + "window is gone";
            }
            if (User32.GetParent(hwnd) != app.Desktop.Layer)
            {
                return display + "not in the desktop layer";
            }
            if (!User32.IsWindowVisible(hwnd))
            {
                return display + "not visible";
            }
            if (!User32.GetClientRect(hwnd, out var client) || client.Width != bounds.Width || client.Height != bounds.Height)
            {
                return display + $"size {client.Width}x{client.Height} instead of {bounds.Width}x{bounds.Height}";
            }
        }
        return null;
    }

    /// <summary>"private 91 MB, working set 134 MB, managed 4 MB, handles 501, GDI 7, USER 19"</summary>
    public static string Memory(Process process)
    {
        process.Refresh();
        return $"private {process.PrivateMemorySize64 >> 20} MB, working set {process.WorkingSet64 >> 20} MB, "
            + $"managed {GC.GetTotalMemory(false) >> 20} MB, handles {process.HandleCount}, "
            + $"GDI {User32.GetGuiResources(process.Handle, User32.GR_GDIOBJECTS)}, USER {User32.GetGuiResources(process.Handle, User32.GR_USEROBJECTS)}";
    }
}
