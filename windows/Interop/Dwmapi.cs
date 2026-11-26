using System.Runtime.InteropServices;

namespace Wallpepper.Interop;

internal static class Dwmapi
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;
    /// <summary>Acrylic.</summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;
    /// <summary>Mica Alt.</summary>
    public const int DWMSBT_TABBEDWINDOW = 4;

    public const int S_OK = 0;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out User32.RECT value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
