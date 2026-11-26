using System.Drawing;
using System.Runtime.InteropServices;
using Wallpepper.Interop;

namespace Wallpepper.Desktop;

/// <summary>Finds the displays whose desktop is hidden by a window (one pass over the top-level windows).</summary>
internal static class CoverScan
{
    /// <summary>A window covers a display when it hides this share of the display's work area.</summary>
    public const double CoverShare = 0.95;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "NotifyIconOverflowWindow",
    };

    /// <summary>
    /// The pure test. A maximised window covers the work area, not the taskbar, so the work area is what counts: full
    /// screen and maximised cover, a window snapped to half the screen does not.
    /// </summary>
    public static bool Covers(Rectangle window, Rectangle workArea)
    {
        var overlap = Rectangle.Intersect(window, workArea);
        if (overlap.IsEmpty)
        {
            return false;
        }
        return (long)overlap.Width * overlap.Height >= (long)workArea.Width * workArea.Height * CoverShare;
    }

    /// <summary>The ids of the covered displays.</summary>
    /// <param name="coveredBy">filled with display id -> class of the covering window (diagnostics)</param>
    public static HashSet<string> Scan(IReadOnlyList<DisplaySlot> slots, uint ownProcessId, Dictionary<string, string> coveredBy)
    {
        var covered = new HashSet<string>();
        coveredBy.Clear();
        if (slots.Count == 0)
        {
            return covered;
        }
        User32.EnumWindows(
            (hwnd, _) =>
            {
                if (!CanCover(hwnd, ownProcessId, out string className, out Rectangle bounds))
                {
                    return true;
                }
                foreach (var slot in slots)
                {
                    if (Covers(bounds, slot.Screen.WorkingArea) && covered.Add(slot.Id))
                    {
                        coveredBy[slot.Id] = $"{className} (0x{hwnd.ToInt64():X})";
                    }
                }
                return covered.Count < slots.Count;   // stop early when everything is covered
            },
            IntPtr.Zero);
        return covered;
    }

    /// <summary>A visible, ordinary application window; overlays, tool windows and the shell's own windows do not count.</summary>
    private static bool CanCover(IntPtr hwnd, uint ownProcessId, out string className, out Rectangle bounds)
    {
        className = "";
        bounds = Rectangle.Empty;
        if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd))
        {
            return false;
        }
        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == ownProcessId)
        {
            return false;
        }
        uint exStyle = User32.ExStyleOf(hwnd);
        bool toolWindow = (exStyle & User32.WS_EX_TOOLWINDOW) != 0;
        bool clickThrough = (exStyle & User32.WS_EX_TRANSPARENT) != 0;
        bool headUpDisplay = (exStyle & User32.WS_EX_LAYERED) != 0 && (exStyle & User32.WS_EX_TOPMOST) != 0;
        bool neverFocused = (exStyle & User32.WS_EX_NOACTIVATE) != 0;
        if (toolWindow || clickThrough || headUpDisplay || neverFocused)
        {
            return false;
        }
        bool cloaked = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, out int cloak, sizeof(int)) == Dwmapi.S_OK && cloak != 0;
        if (cloaked)
        {
            return false;
        }
        className = User32.ClassOf(hwnd);
        if (ShellClasses.Contains(className))
        {
            return false;
        }
        bool hasFrame = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, out User32.RECT rect, Marshal.SizeOf<User32.RECT>()) == Dwmapi.S_OK;
        if (!hasFrame && !User32.GetWindowRect(hwnd, out rect))
        {
            return false;
        }
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }
}
