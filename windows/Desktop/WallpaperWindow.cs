using System.Drawing;
using System.Windows.Forms;
using Wallpepper.Interop;

namespace Wallpepper.Desktop;

/// <summary>Borderless child window placed inside the desktop's wallpaper layer (behind the icons).</summary>
internal sealed class WallpaperWindow : NativeWindow
{
    /// <param name="parent">the wallpaper layer</param>
    /// <param name="bounds">position and size in the layer's client coordinates</param>
    public WallpaperWindow(IntPtr parent, Rectangle bounds)
    {
        CreateHandle(new CreateParams
        {
            Caption = "Wallpepper",
            Parent = parent,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Style = unchecked((int)(User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS | User32.WS_CLIPCHILDREN)),
            ExStyle = unchecked((int)User32.WS_EX_NOACTIVATE),
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == User32.WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;   // the renderer paints every pixel
            return;
        }
        base.WndProc(ref m);
    }
}
