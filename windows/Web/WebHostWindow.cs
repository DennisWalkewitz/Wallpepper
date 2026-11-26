using System.Drawing;
using System.Windows.Forms;
using Wallpepper.Interop;

namespace Wallpepper.Web;

/// <summary>
/// The window a web page lives in. Normally a child of the desktop layer (behind the icons, no input); "Interact"
/// lifts the same window to a borderless top-level window, so the page keeps running and its audio keeps playing.
/// </summary>
internal sealed class WebHostWindow : NativeWindow
{
    public WebHostWindow(IntPtr layer, Rectangle screen)
    {
        var origin = new User32.POINT { X = screen.X, Y = screen.Y };
        User32.MapWindowPoints(IntPtr.Zero, layer, ref origin, 1);
        CreateHandle(new CreateParams
        {
            Caption = "Wallpepper web wallpaper",
            Parent = layer,
            X = origin.X,
            Y = origin.Y,
            Width = screen.Width,
            Height = screen.Height,
            Style = unchecked((int)(User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS | User32.WS_CLIPCHILDREN)),
            ExStyle = unchecked((int)User32.WS_EX_NOACTIVATE),
        });
    }

    /// <summary>Esc or Alt+F4 while lifted.</summary>
    public event Action? CloseRequested;

    /// <summary>Behind the desktop icons, on top of the shader window of that display.</summary>
    public void Sink(IntPtr layer, Rectangle screen)
    {
        if (User32.GetParent(Handle) != layer)
        {
            // child style first, then the new parent
            User32.SetStyle(Handle, (User32.StyleOf(Handle) & ~User32.WS_POPUP) | User32.WS_CHILD);
            User32.SetExStyle(Handle, User32.ExStyleOf(Handle) | User32.WS_EX_NOACTIVATE);
            User32.SetParent(Handle, layer);
        }
        var origin = new User32.POINT { X = screen.X, Y = screen.Y };
        User32.MapWindowPoints(IntPtr.Zero, layer, ref origin, 1);
        User32.SetWindowPos(
            Handle, IntPtr.Zero, origin.X, origin.Y, screen.Width, screen.Height,
            User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED | User32.SWP_SHOWWINDOW);
    }

    /// <summary>A normal borderless window in front, same place and size, that takes mouse and keyboard.</summary>
    public void Lift(Rectangle screen)
    {
        // new parent first, then the popup style
        User32.SetParent(Handle, IntPtr.Zero);
        User32.SetStyle(Handle, (User32.StyleOf(Handle) & ~User32.WS_CHILD) | User32.WS_POPUP);
        User32.SetExStyle(Handle, User32.ExStyleOf(Handle) & ~User32.WS_EX_NOACTIVATE);
        User32.SetWindowPos(
            Handle, IntPtr.Zero, screen.X, screen.Y, screen.Width, screen.Height, User32.SWP_FRAMECHANGED | User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case User32.WM_ERASEBKGND:
                using (var graphics = Graphics.FromHdc(m.WParam))
                {
                    graphics.Clear(Color.Black);
                }
                m.Result = (IntPtr)1;
                return;
            case User32.WM_KEYDOWN when (int)m.WParam == User32.VK_ESCAPE:
            case User32.WM_CLOSE:
                CloseRequested?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }
}
