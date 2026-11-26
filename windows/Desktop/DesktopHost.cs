using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Wallpepper.Interop;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Desktop;

/// <summary>
/// Finds the layer behind the desktop icons (works on Windows 10 and Windows 11 including 24H2, where the WorkerW moved
/// under Progman) and creates one wallpaper window per display.
/// </summary>
internal sealed class DesktopHost
{
    private const int WallpaperPathLength = 520;
    private const uint MessageTimeoutMs = 1000;

    private readonly List<DisplaySlot> _slots = new List<DisplaySlot>();
    private string? _savedWallpaper;

    public IReadOnlyList<DisplaySlot> Slots => _slots;

    /// <summary>Bounding box of all displays (virtual desktop), in pixels.</summary>
    public Rectangle VirtualBounds { get; private set; }

    /// <summary>The window behind the desktop icons that the wallpaper windows are children of.</summary>
    public IntPtr Layer { get; private set; }

    public static string DisplayId(Screen screen) => screen.DeviceName;

    /// <summary>Creates the wallpaper windows and their render targets; the caller assigns what each one shows.</summary>
    public void Attach(Renderer renderer)
    {
        Detach(renderer);
        var current = new StringBuilder(WallpaperPathLength);
        if (User32.SystemParametersInfoGet(User32.SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0))
        {
            _savedWallpaper = current.ToString();
        }

        Layer = FindWallpaperLayer();
        var screens = Screen.AllScreens;
        var all = Rectangle.Empty;
        foreach (var screen in screens)
        {
            all = all.IsEmpty ? screen.Bounds : Rectangle.Union(all, screen.Bounds);
        }
        VirtualBounds = all;

        var rects = new float[Math.Min(MonitorConstants.MaxMonitors, screens.Length) * 4];
        for (int i = 0; i < screens.Length; i++)
        {
            var bounds = screens[i].Bounds;
            var origin = new User32.POINT { X = bounds.X, Y = bounds.Y };
            User32.MapWindowPoints(IntPtr.Zero, Layer, ref origin, 1);   // screen -> layer client coordinates
            var window = new WallpaperWindow(Layer, new Rectangle(origin.X, origin.Y, bounds.Width, bounds.Height));
            var target = new RenderTarget(window.Handle, isPreview: false, new TargetSettings
            {
                SpanX = bounds.X - all.X,
                SpanY = bounds.Y - all.Y,
                SpanWidth = all.Width,
                SpanHeight = all.Height,
                MonitorIndex = i,
                RefreshHz = User32.RefreshRate(screens[i].DeviceName),
            });
            if (i < MonitorConstants.MaxMonitors && all.Width > 0 && all.Height > 0)
            {
                rects[i * 4] = (float)(bounds.X - all.X) / all.Width;
                rects[i * 4 + 1] = (float)(bounds.Y - all.Y) / all.Height;
                rects[i * 4 + 2] = (float)bounds.Width / all.Width;
                rects[i * 4 + 3] = (float)bounds.Height / all.Height;
            }
            _slots.Add(new DisplaySlot(screens[i], i, window, target));
            renderer.AddTarget(target);
        }
        renderer.MonitorRects = rects;
    }

    public void Detach(Renderer renderer)
    {
        foreach (var slot in _slots)
        {
            renderer.RemoveTarget(slot.Target);
            Try.Run("destroy a wallpaper window", slot.Window.DestroyHandle);
        }
        _slots.Clear();
    }

    /// <summary>Repaints the normal wallpaper after we leave.</summary>
    public void RestoreWallpaper()
    {
        if (_savedWallpaper != null)
        {
            User32.SystemParametersInfoSet(User32.SPI_SETDESKWALLPAPER, 0, _savedWallpaper, 0);
        }
    }

    /// <summary>Makes Explorer draw the current wallpaper again (after a wallpaper window disappeared without cleaning up).</summary>
    public static void RepaintWallpaper()
    {
        var current = new StringBuilder(WallpaperPathLength);
        if (User32.SystemParametersInfoGet(User32.SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0))
        {
            User32.SystemParametersInfoSet(User32.SPI_SETDESKWALLPAPER, 0, current.ToString(), 0);
        }
    }

    private static IntPtr FindWallpaperLayer()
    {
        IntPtr progman = User32.FindWindow("Progman", null);
        // ask Explorer to create the WorkerW behind the icons
        User32.SendMessageTimeout(progman, User32.WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), User32.SMTO_NORMAL, MessageTimeoutMs, out _);
        User32.SendMessageTimeout(progman, User32.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, User32.SMTO_NORMAL, MessageTimeoutMs, out _);

        IntPtr workerW = IntPtr.Zero;
        // classic (Windows 10, Windows 11 before 24H2): the top-level WorkerW that follows the one hosting SHELLDLL_DefView
        User32.EnumWindows(
            (top, _) =>
            {
                if (User32.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    workerW = User32.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                }
                return true;
            },
            IntPtr.Zero);

        // Windows 11 24H2 and later: WorkerW is a child of Progman, below SHELLDLL_DefView
        if (workerW == IntPtr.Zero)
        {
            workerW = User32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        }
        return workerW != IntPtr.Zero ? workerW : progman;
    }
}
