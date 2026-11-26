using System.Runtime.CompilerServices;
using Wallpepper.Desktop;
using Wallpepper.Settings;

namespace Wallpepper.Web;

/// <summary>
/// The app's handle on web wallpapers. <see cref="WebWallpapers"/> (and with it WebView2) only comes into being when a
/// web wallpaper is actually shown, and goes again with the last one. UI thread only.
/// </summary>
internal sealed class WebWallpaperService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly DesktopHost _host;
    private WebWallpapers? _pages;

    public WebWallpaperService(SettingsStore settings, DesktopHost host)
    {
        _settings = settings;
        _host = host;
    }

    /// <summary>Interaction started or ended, or a page was muted or unmuted.</summary>
    public event Action? Changed;

    /// <summary>A page started (error null) or failed: file path and message.</summary>
    public event Action<string, string?>? PageReported;

    public bool Interacting => _pages != null && _pages.Interacting;

    /// <summary>The web wallpaper file that is live right now (the primary display's first), or null.</summary>
    public string? LiveItem => _host.Slots
        .OrderByDescending(slot => slot.Screen.Primary)
        .Select(slot => slot.ItemPath)
        .FirstOrDefault(WebInfo.IsWeb);

    /// <summary>The page of this file is running and can be reloaded or lifted in front.</summary>
    public bool IsReady(string path) => _pages != null && _pages.IsReady(path);

    /// <summary>Shows exactly these pages; an empty list closes all and lets go of WebView2.</summary>
    public void Show(IReadOnlyList<WebPageRequest> wanted)
    {
        if (wanted.Count == 0)
        {
            if (_pages != null)
            {
                _pages.Dispose();
                _pages = null;
                Changed?.Invoke();
            }
            return;
        }
        _pages ??= Create();
        _pages.Sync(wanted, _host.Layer);
    }

    /// <summary>Applies the pause rules and the mute settings to the open pages.</summary>
    public void Refresh() => _pages?.Refresh();

    /// <summary>Lifts a page in front (path null: the one on the primary display) or sends the lifted one back.</summary>
    public void Interact(string? path, bool on)
    {
        if (on)
        {
            _pages?.BeginInteract(path);
        }
        else
        {
            _pages?.EndInteract();
        }
    }

    public void Reload(string path) => _pages?.Reload(path);

    public bool IsMuted(string path) => _settings.Config.WebMuted.TryGetValue(Path.GetFileName(path), out bool muted) && muted;

    public void SetMuted(string path, bool muted)
    {
        string file = Path.GetFileName(path);
        _settings.Change(SettingsArea.None, config =>
        {
            if (muted)
            {
                config.WebMuted[file] = true;
            }
            else
            {
                config.WebMuted.Remove(file);
            }
        });
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Self-test: how far the page of a file has come and what is wrong with it, if anything.</summary>
    public (WebPageState State, string? Problem) StateOf(string path) => _pages?.StateOf(path) ?? (WebPageState.NotStarted, null);

    public void Dispose()
    {
        _pages?.Dispose();
        _pages = null;
    }

    /// <summary>The one method that touches the type's constructor: kept out of line so nothing loads WebView2 before it runs.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WebWallpapers Create()
    {
        var pages = new WebWallpapers(
            () => _settings.Config.UserPaused, IsMuted, id => _host.Slots.FirstOrDefault(slot => slot.Id == id)?.IsPaused ?? false);
        pages.InteractionChanged += () => Changed?.Invoke();
        pages.PageReported += (path, error) => PageReported?.Invoke(path, error);
        return pages;
    }
}
