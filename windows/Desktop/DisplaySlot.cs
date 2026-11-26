using System.Windows.Forms;
using Wallpepper.Desktop.Performance;
using Wallpepper.Rendering;
using Wallpepper.Web;

namespace Wallpepper.Desktop;

/// <summary>One display: its wallpaper window, the render target in it, and what it currently shows. UI thread.</summary>
internal sealed class DisplaySlot
{
    public DisplaySlot(Screen screen, int index, WallpaperWindow window, RenderTarget target)
    {
        Screen = screen;
        Index = index;
        Window = window;
        Target = target;
        Id = DesktopHost.DisplayId(screen);
    }

    public Screen Screen { get; }

    /// <summary>Zero-based; the user sees "Display 1" for index 0.</summary>
    public int Index { get; }

    /// <summary>The key per-display settings are stored under.</summary>
    public string Id { get; }

    public WallpaperWindow Window { get; }

    public RenderTarget Target { get; }

    /// <summary>What the display shows: a .hlsl (then also the target's shader) or a .wallpepperweb file.</summary>
    public string? ItemPath { get; set; }

    /// <summary>null = rendering.</summary>
    public PauseReason? PauseReason { get; set; }

    /// <summary>What the performance fitter chose for this display.</summary>
    public FitState Fit { get; } = new FitState();

    public bool ShowsWebPage => WebInfo.IsWeb(ItemPath);

    public bool IsPaused => PauseReason != null;
}
