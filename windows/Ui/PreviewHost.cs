using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wallpepper.Interop;

namespace Wallpepper.Ui;

/// <summary>Child HWND the renderer presents the live preview into; clipped to rounded corners with a window region.</summary>
internal sealed class PreviewHost : HwndHost
{
    private const double CornerRadius = 10;

    private IntPtr _hwnd;

    /// <summary>The window exists and can be drawn into.</summary>
    public event Action<IntPtr>? Ready;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _hwnd = User32.CreateWindowEx(
            0, "static", "", User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS,
            0, 0, 16, 9, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Dispatcher.InvokeAsync(() => Ready?.Invoke(_hwnd));
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        User32.DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int diameter = (int)Math.Round(CornerRadius * dpi * 2);
        int width = (int)Math.Round(bounds.Width);
        int height = (int)Math.Round(bounds.Height);
        // the system owns the region from here on
        User32.SetWindowRgn(_hwnd, Gdi32.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter), true);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_ERASEBKGND)
        {
            handled = true;
            return (IntPtr)1;
        }
        if (msg == User32.WM_NCHITTEST)
        {
            handled = true;
            return (IntPtr)User32.HTTRANSPARENT;   // mouse and drops go to the WPF window
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
}
