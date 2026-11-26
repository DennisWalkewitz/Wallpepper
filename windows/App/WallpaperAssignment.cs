using System.Drawing;
using System.Windows.Forms;
using Wallpepper.Desktop;
using Wallpepper.Interop;
using Wallpepper.Library;
using Wallpepper.Rendering;
using Wallpepper.Settings;
using Wallpepper.Web;

namespace Wallpepper;

/// <summary>
/// Which library item is on which display, in which mode (duplicate, per display, span), and the step that pushes
/// that from the settings to the render targets and web pages. UI thread only.
/// </summary>
internal sealed class WallpaperAssignment
{
    private readonly SettingsStore _settings;
    private readonly ShaderLibrary _library;
    private readonly DesktopHost _host;
    private readonly Renderer _renderer;
    private readonly WebWallpaperService _web;
    private readonly CompileStatus _status;

    public WallpaperAssignment(
        SettingsStore settings, ShaderLibrary library, DesktopHost host, Renderer renderer, WebWallpaperService web, CompileStatus status)
    {
        _settings = settings;
        _library = library;
        _host = host;
        _renderer = renderer;
        _web = web;
        _status = status;
    }

    /// <summary>What is live, or the mode, changed.</summary>
    public event Action? Changed;

    /// <summary>The wallpaper windows were created again (displays changed, Explorer restarted).</summary>
    public event Action? DisplaysChanged;

    /// <summary>The targets were updated: the pause rules and the fitter should look again.</summary>
    public event Action? TargetsApplied;

    private Config Config => _settings.Config;

    /// <summary>The mode in force: span needs more than one display.</summary>
    public DisplayMode Mode => Config.Mode == DisplayMode.Span && _host.Slots.Count <= 1 ? DisplayMode.Duplicate : Config.Mode;

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The library item (shader or web wallpaper) assigned to a display; a default shader when there is none.</summary>
    public string? ItemFor(Screen screen)
    {
        string? file = null;
        if (Config.Mode == DisplayMode.PerDisplay)
        {
            Config.WallpaperByDisplay.TryGetValue(DesktopHost.DisplayId(screen), out file);
        }
        file ??= Config.Wallpaper;
        string? path = file == null ? null : _library.PathOf(file);
        return path != null && File.Exists(path) ? path : _library.DefaultShader();
    }

    public bool IsLive(string path) => _host.Slots.Any(slot => SamePath(slot.ItemPath, path));

    /// <summary>Name of what is on the (main) display right now, for the tray.</summary>
    public string CurrentTitle()
    {
        string? item = _host.Slots.OrderByDescending(slot => slot.Screen.Primary).Select(slot => slot.ItemPath).FirstOrDefault(path => path != null);
        return _library.Title(item);
    }

    public void SetMode(DisplayMode mode)
    {
        _settings.Change(SettingsArea.None, config =>
        {
            if (mode == DisplayMode.PerDisplay && config.Mode != DisplayMode.PerDisplay)
            {
                // every display keeps what it shows
                foreach (var slot in _host.Slots.Where(slot => slot.ItemPath != null))
                {
                    config.WallpaperByDisplay[slot.Id] = Path.GetFileName(slot.ItemPath!);
                }
            }
            else if (mode != DisplayMode.PerDisplay && config.Mode == DisplayMode.PerDisplay)
            {
                // all displays take over what the first one shows
                string? first = _host.Slots.FirstOrDefault()?.ItemPath;
                if (first != null)
                {
                    config.Wallpaper = Path.GetFileName(first);
                }
            }
            config.Mode = mode;
            config.SpanChosenAutomatically = false;
        });
        ApplyTargets();
        Changed?.Invoke();
    }

    /// <summary>Sets a wallpaper.</summary>
    /// <param name="displayIds">null or empty = all displays (duplicate or span); otherwise only these (switches to per display)</param>
    public void Apply(string path, IReadOnlyCollection<string>? displayIds = null)
    {
        string file = Path.GetFileName(path);
        bool everywhere = displayIds == null || displayIds.Count == 0 || _host.Slots.Count <= 1;
        _settings.Change(SettingsArea.None, config =>
        {
            if (everywhere)
            {
                ApplyEverywhere(config, path, file);
            }
            else
            {
                if (config.Mode != DisplayMode.PerDisplay)
                {
                    foreach (var slot in _host.Slots)
                    {
                        config.WallpaperByDisplay[slot.Id] = Path.GetFileName(slot.ItemPath ?? path);
                    }
                }
                config.Mode = DisplayMode.PerDisplay;
                config.SpanChosenAutomatically = false;
                foreach (string id in displayIds!)
                {
                    config.WallpaperByDisplay[id] = file;
                }
            }
            config.LastUsed[file] = DateTime.UtcNow.Ticks;
            config.UserPaused = false;   // setting a wallpaper means wanting to see it
        });
        ApplyTargets();
        // the desktop changes at once, even on a display that is paused right now (it draws one frame)
        foreach (var slot in _host.Slots)
        {
            slot.Target.Kick();
        }
        _renderer.Wake();
        Changed?.Invoke();
    }

    /// <summary>Next or previous shader (hotkeys, tray). Web wallpapers and terminal-only effects are skipped.</summary>
    /// <returns>the path that was applied, null when the library has no shader</returns>
    public string? Cycle(int direction)
    {
        var shaders = _library.Items.Where(path => ShaderLibrary.IsShader(path) && !_library.Metadata(path).IsTerminalOnly).ToList();
        if (shaders.Count == 0)
        {
            return null;
        }
        string? current = _host.Slots.FirstOrDefault()?.ItemPath;
        int index = current == null ? -1 : shaders.FindIndex(path => SamePath(path, current));
        index = ((index + direction) % shaders.Count + shaders.Count) % shaders.Count;
        Apply(shaders[index]);
        return shaders[index];
    }

    /// <summary>Pushes the assignment, span, and filters from the settings to the render targets and web pages.</summary>
    public void ApplyTargets()
    {
        bool span = Mode == DisplayMode.Span;
        var globalFilter = FilterState.From(Config.Filters);
        var pages = new List<WebPageRequest>();
        foreach (var slot in _host.Slots)
        {
            Config.Monitors.TryGetValue(slot.Id, out MonitorSettings? monitor);
            string? item = ItemFor(slot.Screen);
            bool isWeb = WebInfo.IsWeb(item);
            var filter = monitor != null && monitor.CustomFilters ? FilterState.From(monitor.Filters) : globalFilter;
            slot.ItemPath = item;
            slot.Target.Update(settings => settings with { ShaderPath = isWeb ? null : item, Span = span, Filter = filter });
            User32.ShowWindow(slot.Window.Handle, isWeb ? User32.SW_HIDE : User32.SW_SHOWNA);   // a page has its own window on top
            if (isWeb)
            {
                AddPage(pages, slot, item!, span);
            }
        }
        _web.Show(pages);
        _renderer.Wake();
        TargetsApplied?.Invoke();
    }

    /// <summary>Creates the wallpaper windows and assigns what they show.</summary>
    public void Attach()
    {
        try
        {
            _host.Attach(_renderer);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("attach to the desktop", e);
        }
        ApplyTargets();
    }

    /// <summary>Builds the wallpaper windows again (displays changed, Explorer restarted, recovery).</summary>
    public void Reattach()
    {
        _host.Detach(_renderer);
        Attach();
        DisplaysChanged?.Invoke();
        Changed?.Invoke();
    }

    private void ApplyEverywhere(Config config, string path, string file)
    {
        config.Wallpaper = file;
        config.WallpaperByDisplay.Clear();
        // shaders written to travel across screens default to span; the next ordinary one goes back to duplicate
        bool multiMonitor = _host.Slots.Count > 1 && _library.Metadata(path).IsMultiMonitor;
        if (config.Mode == DisplayMode.PerDisplay)
        {
            config.Mode = DisplayMode.Duplicate;
        }
        if (multiMonitor && config.Mode != DisplayMode.Span)
        {
            config.Mode = DisplayMode.Span;
            config.SpanChosenAutomatically = true;
        }
        else if (!multiMonitor && config.Mode == DisplayMode.Span && config.SpanChosenAutomatically)
        {
            config.Mode = DisplayMode.Duplicate;
            config.SpanChosenAutomatically = false;
        }
    }

    private void AddPage(List<WebPageRequest> pages, DisplaySlot slot, string item, bool span)
    {
        WebInfo info = _library.Metadata(item).Web ?? WebInfo.Load(item);
        if (info.Error != null)
        {
            _status.Finished(item, info.Error);
            return;
        }
        // A page gets the working area, not the whole display: the taskbar would otherwise cover the bottom of the
        // page, which is where web players keep their controls.
        if (!span)
        {
            pages.Add(new WebPageRequest(slot.Id, item, info, slot.Screen.WorkingArea, new[] { slot.Id }, slot.Screen.Primary));
        }
        else if (pages.Count == 0)
        {
            // span: one page across the whole desktop
            var all = _host.Slots.Select(other => other.Id).ToArray();
            var area = _host.Slots
                .Select(other => other.Screen.WorkingArea)
                .Aggregate(Rectangle.Union);
            pages.Add(new WebPageRequest(WebPageRequest.SpanKey, item, info, area, all, true));
        }
    }
}
