using System.Runtime.InteropServices;

namespace Wallpepper.Interop;

internal static class Gdi32
{
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
}
