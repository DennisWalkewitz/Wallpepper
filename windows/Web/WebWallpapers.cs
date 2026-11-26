using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Wallpepper.Interop;
using Wallpepper.Settings;
using Wallpepper.Ui;

namespace Wallpepper.Web;

/// <summary>
/// Hosts web wallpapers in WebView2. Created when the first web wallpaper is applied and disposed with the last one,
/// so nothing of WebView2 is loaded while only shaders are in use. UI thread only.
/// </summary>
internal sealed class WebWallpapers : IDisposable
{
    private sealed class Page
    {
        public required WebPageRequest Request { get; set; }
        public required WebHostWindow Window { get; init; }
        public CoreWebView2Controller? Controller { get; set; }
        public bool Hidden { get; set; }
        public bool Muted { get; set; }
        public bool Interacting { get; set; }
        public bool Closed { get; set; }
        public WebPageState State { get; set; } = WebPageState.Starting;
        public string? Problem { get; set; }
    }

    private readonly List<Page> _pages = new List<Page>();
    private readonly Func<bool> _userPaused;
    private readonly Func<string, bool> _isMuted;
    private readonly Func<string, bool> _isDisplayPaused;
    private Task<CoreWebView2Environment>? _environment;
    private InteractPill? _pill;
    private IntPtr _layer;

    /// <param name="userPaused">the user paused the wallpaper (silences every page)</param>
    /// <param name="isMuted">whether the user muted the page of a file</param>
    /// <param name="isDisplayPaused">whether a display (by id) is paused right now</param>
    public WebWallpapers(Func<bool> userPaused, Func<string, bool> isMuted, Func<string, bool> isDisplayPaused)
    {
        _userPaused = userPaused;
        _isMuted = isMuted;
        _isDisplayPaused = isDisplayPaused;
    }

    /// <summary>Interaction started or ended.</summary>
    public event Action? InteractionChanged;

    /// <summary>A page started (error null) or could not be started or loaded: file path and message.</summary>
    public event Action<string, string?>? PageReported;

    public int Count => _pages.Count;

    public bool Interacting => _pages.Any(page => page.Interacting);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page of this file is running and can be reloaded or lifted in front.</summary>
    public bool IsReady(string path) => _pages.Any(page => SamePath(page.Request.Path, path) && page.Controller != null);

    /// <summary>Makes the open pages match what the app wants: closes the others, moves or creates the rest.</summary>
    public void Sync(IReadOnlyList<WebPageRequest> wanted, IntPtr layer)
    {
        _layer = layer;
        foreach (var page in _pages.ToList())
        {
            bool stillWanted = wanted.Any(request =>
                request.Key == page.Request.Key && SamePath(request.Path, page.Request.Path) && request.Info.Url == page.Request.Info.Url);
            if (!stillWanted || !User32.IsWindow(page.Window.Handle))
            {
                Close(page);
            }
        }
        foreach (var request in wanted)
        {
            var page = _pages.FirstOrDefault(existing => existing.Request.Key == request.Key);
            if (page != null)
            {
                bool resized = page.Request.Bounds.Size != request.Bounds.Size;
                page.Request = request;
                if (!page.Interacting)
                {
                    Place(page);
                }
                if (resized && page.Controller != null)
                {
                    page.Controller.Bounds = new Rectangle(0, 0, request.Bounds.Width, request.Bounds.Height);
                }
                continue;
            }
            page = new Page { Request = request, Window = new WebHostWindow(layer, request.Bounds) };
            page.Window.CloseRequested += EndInteract;
            _pages.Add(page);
            Place(page);
            _ = StartAsync(page);
        }
        Refresh();
    }

    /// <summary>
    /// Pause rules and the one-voice rule. A paused display hides its page instead of suspending it, so audio keeps
    /// playing behind a full-screen window; only the user's own pause silences it. With several pages, one plays sound.
    /// </summary>
    public void Refresh()
    {
        bool userPaused = _userPaused();
        Page? voice = _pages
            .OrderByDescending(page => page.Request.Primary)
            .FirstOrDefault(page => page.Request.Info.Audio && !userPaused && !_isMuted(page.Request.Path));
        foreach (var page in _pages)
        {
            bool hide = !page.Interacting && page.Request.DisplayIds.All(_isDisplayPaused);
            Apply(page, hide, mute: page != voice);
        }
    }

    /// <summary>Self-test: how far the page of this file has come, and whether its window really sits in the desktop layer.</summary>
    public (WebPageState State, string? Problem) StateOf(string path)
    {
        var page = _pages.FirstOrDefault(existing => SamePath(existing.Request.Path, path));
        if (page == null)
        {
            return (WebPageState.NotStarted, null);
        }
        IntPtr hwnd = page.Window.Handle;
        var bounds = page.Request.Bounds;
        bool placed = User32.IsWindow(hwnd) && User32.GetParent(hwnd) == _layer && User32.IsWindowVisible(hwnd)
            && User32.GetClientRect(hwnd, out var client) && client.Width == bounds.Width && client.Height == bounds.Height;
        return (page.State, placed ? page.Problem : "window not in the desktop layer");
    }

    public void Reload(string? path)
    {
        foreach (var page in _pages.Where(page => page.Controller != null && (path == null || SamePath(page.Request.Path, path))))
        {
            Try.Run("reload web page", () => page.Controller!.CoreWebView2.Reload());
        }
    }

    /// <summary>Lifts the page in front so it can be clicked; a small "Done" pill (or Esc) sends it back.</summary>
    /// <param name="path">the page's file; null = the page on the primary display</param>
    public void BeginInteract(string? path)
    {
        var page = _pages
            .OrderByDescending(candidate => candidate.Request.Primary)
            .FirstOrDefault(candidate => candidate.Controller != null && (path == null || SamePath(candidate.Request.Path, path)));
        if (page == null || page.Interacting)
        {
            return;
        }
        EndInteract();
        page.Interacting = true;
        Apply(page, hide: false, mute: page.Muted);
        page.Window.Lift(page.Request.Bounds);
        Try.Run("focus web page", () =>
        {
            page.Controller!.NotifyParentWindowPositionChanged();
            page.Controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        });

        // the pill goes to the display the mouse is on (a spanning page covers several)
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!page.Request.Bounds.Contains(screen))
        {
            screen = page.Request.Bounds;
        }
        _pill = new InteractPill(page.Window.Handle, screen, EndInteract);
        _pill.Show();
        InteractionChanged?.Invoke();
    }

    public void EndInteract()
    {
        ClosePill();
        var page = _pages.FirstOrDefault(candidate => candidate.Interacting);
        if (page == null)
        {
            return;
        }
        page.Interacting = false;
        if (User32.IsWindow(page.Window.Handle))
        {
            Place(page);
        }
        Refresh();
        InteractionChanged?.Invoke();
    }

    public void Dispose()
    {
        foreach (var page in _pages.ToList())
        {
            Close(page);
        }
        _environment = null;
    }

    private void Place(Page page)
    {
        page.Window.Sink(_layer, page.Request.Bounds);
        if (page.Controller != null)
        {
            Try.Run("place web page", page.Controller.NotifyParentWindowPositionChanged);
        }
    }

    private async Task StartAsync(Page page)
    {
        string path = page.Request.Path;
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(
                null, AppPaths.WebViewDir,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" });
            var environment = await _environment;
            if (page.Closed)
            {
                return;
            }
            var controller = await environment.CreateCoreWebView2ControllerAsync(page.Window.Handle);
            if (page.Closed)
            {
                controller.Close();
                return;
            }

            page.Controller = controller;
            controller.DefaultBackgroundColor = Color.Black;
            controller.Bounds = new Rectangle(0, 0, page.Request.Bounds.Width, page.Request.Bounds.Height);
            controller.AcceleratorKeyPressed += (_, e) =>
            {
                if (page.Interacting && e.VirtualKey == User32.VK_ESCAPE && e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
                {
                    e.Handled = true;
                    EndInteract();
                }
            };

            var web = controller.CoreWebView2;
            WebViewPolicy.Apply(web, page.Request.Info, () => page.Interacting);
            web.ProcessFailed += (_, e) => OnProcessFailed(page, e);
            web.NavigationCompleted += (_, e) => OnNavigationCompleted(page, e);

            Apply(page, page.Hidden, page.Muted);
            page.State = WebPageState.Loading;
            web.Navigate(page.Request.Info.Url);
            PageReported?.Invoke(path, null);
            Refresh();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_environment != null && _environment.IsFaulted)
            {
                _environment = null;   // try again next time
            }
            Log.Error("start web page", e);
            page.State = WebPageState.Failed;
            page.Problem = e is WebView2RuntimeNotFoundException
                ? "The Microsoft Edge WebView2 Runtime is not installed. It ships with Windows 11; on Windows 10 install it from Microsoft."
                : "The page could not be started: " + e.Message;
            PageReported?.Invoke(path, page.Problem);
        }
    }

    private void OnNavigationCompleted(Page page, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            page.State = WebPageState.Loaded;
            page.Problem = null;
            PageReported?.Invoke(page.Request.Path, null);
            return;
        }
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;   // a navigation the policy blocked, or one replaced by the next
        }
        page.State = WebPageState.Failed;
        page.Problem = "The page could not be loaded (" + e.WebErrorStatus + "). It needs an internet connection.";
        PageReported?.Invoke(page.Request.Path, page.Problem);
    }

    /// <summary>The browser process behind a page died: the page is loaded again.</summary>
    private void OnProcessFailed(Page page, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Write("web: process failed (" + e.ProcessFailedKind + "), reloading " + Path.GetFileName(page.Request.Path));
        if (page.Closed || page.Controller == null || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            page.State = WebPageState.Failed;
            page.Problem = "The page's browser process ended (" + e.ProcessFailedKind + ").";
            PageReported?.Invoke(page.Request.Path, page.Problem);
            return;
        }
        page.State = WebPageState.Loading;
        Try.Run("reload web page", () => page.Controller.CoreWebView2.Reload());
    }

    private static void Apply(Page page, bool hide, bool mute)
    {
        page.Hidden = hide;
        page.Muted = mute;
        var controller = page.Controller;
        if (controller == null)
        {
            return;
        }
        Try.Run("show or mute web page", () =>
        {
            if (controller.IsVisible == hide)
            {
                controller.IsVisible = !hide;
            }
            if (controller.CoreWebView2.IsMuted != mute)
            {
                controller.CoreWebView2.IsMuted = mute;
            }
        });
    }

    private void ClosePill()
    {
        var pill = _pill;
        _pill = null;
        if (pill != null)
        {
            Try.Run("close the interaction pill", pill.Close);
        }
    }

    private void Close(Page page)
    {
        if (page.Interacting)
        {
            ClosePill();
            page.Interacting = false;
        }
        page.Closed = true;
        _pages.Remove(page);
        if (page.Controller != null)
        {
            Try.Run("close web page", page.Controller.Close);
            page.Controller = null;
        }
        Try.Run("destroy web window", page.Window.DestroyHandle);
    }
}
