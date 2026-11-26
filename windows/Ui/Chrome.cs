using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Wallpepper.Interop;
using Wallpepper.Settings;

namespace Wallpepper.Ui;

/// <summary>Windows 11 window dressing: acrylic backdrop, dark frame, rounded corners; plus placement persistence.</summary>
internal static class Chrome
{
    private const int FirstBuildWithBackdrops = 22621;
    private const int MinRestoredSize = 200;

    private static bool _logged;

    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= FirstBuildWithBackdrops;

    /// <summary>
    /// Windows Settings > Personalisation > Colours > Transparency effects. When off, DWM replaces every backdrop with a
    /// flat colour.
    /// </summary>
    public static bool TransparencyEnabled => Try.Get(
        "read the transparency setting",
        () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        },
        true);

    /// <summary>Returns true when a system backdrop is active (the window content should then stay translucent).</summary>
    public static bool Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        // the app draws its own caption buttons; without WS_SYSMENU the system ones are not drawn underneath the glass
        User32.SetStyle(hwnd, User32.StyleOf(hwnd) & ~User32.WS_SYSMENU);
        User32.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOMOVE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);

        SetAttribute(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        SetAttribute(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, Dwmapi.DWMWCP_ROUND);
        // no accent-coloured band behind the custom title bar (a COLORREF is 0x00BBGGRR)
        Color caption = Theme.Color("WindowColor");
        SetAttribute(hwnd, Dwmapi.DWMWA_CAPTION_COLOR, caption.R | (caption.G << 8) | (caption.B << 16));

        bool transparency = TransparencyEnabled;
        if (!_logged)
        {
            _logged = true;
            Log.Write($"chrome: build {Environment.OSVersion.Version.Build}, transparency effects {(transparency ? "on" : "off")}");
        }
        if (!BackdropSupported || !SystemParameters.IsGlassEnabled || !transparency)
        {
            return false;
        }
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget == null)
        {
            return false;
        }
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        return SetAttribute(hwnd, Dwmapi.DWMWA_SYSTEMBACKDROP_TYPE, Dwmapi.DWMSBT_TRANSIENTWINDOW);
    }

    public static WindowPlacement? SavePlacement(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        var placement = new User32.WINDOWPLACEMENT { length = Marshal.SizeOf<User32.WINDOWPLACEMENT>() };
        if (hwnd == IntPtr.Zero || !User32.GetWindowPlacement(hwnd, ref placement))
        {
            return null;
        }
        var rect = placement.rcNormalPosition;
        return new WindowPlacement(rect.Left, rect.Top, rect.Right, rect.Bottom, placement.showCmd == User32.SW_SHOWMAXIMIZED);
    }

    public static void RestorePlacement(Window window, WindowPlacement? saved)
    {
        if (saved == null || saved.Right - saved.Left < MinRestoredSize || saved.Bottom - saved.Top < MinRestoredSize)
        {
            return;
        }
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        var placement = new User32.WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<User32.WINDOWPLACEMENT>(),
            showCmd = saved.Maximized ? User32.SW_SHOWMAXIMIZED : User32.SW_SHOWNORMAL,
            ptMinPosition = new User32.POINT { X = -1, Y = -1 },
            ptMaxPosition = new User32.POINT { X = -1, Y = -1 },
            rcNormalPosition = new User32.RECT { Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom },
        };
        User32.SetWindowPlacement(hwnd, ref placement);   // Windows keeps it on a visible monitor
    }

    private static bool SetAttribute(IntPtr hwnd, int attribute, int value) =>
        Dwmapi.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == Dwmapi.S_OK;
}
