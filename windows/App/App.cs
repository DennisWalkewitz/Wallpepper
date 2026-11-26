using System.Windows.Threading;
using Microsoft.Win32;
using Wallpepper.Desktop;
using Wallpepper.Desktop.Performance;
using Wallpepper.Library;
using Wallpepper.Rendering;
using Wallpepper.Settings;
using Wallpepper.Setup;
using Wallpepper.Ui;
using Wallpepper.Web;

namespace Wallpepper;

/// <summary>
/// The tray application: builds its parts, connects them, and takes them down again. The parts do the work; other
/// code reaches them through the properties here. UI thread only.
/// </summary>
internal sealed class App
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly SingleInstance _instance;
    private readonly Renderer _renderer;
    private readonly DesktopHost _desktop = new DesktopHost();
    private readonly SessionState _session = new SessionState();
    private readonly HotkeyWindow _hotkeys = new HotkeyWindow();
    private readonly TrayIcon _tray;
    private readonly PerfLog? _perfLog;
    private LibraryWindow? _library;
    private bool _exiting;

    public App(SingleInstance instance, bool showLibrary, bool justUpdated)
    {
        _instance = instance;
        Settings = SettingsStore.Load();
        Library = new ShaderLibrary(Settings, _ui);
        Library.SyncBundled();
        Shaders = new ShaderSettings(Settings, Library);

        _renderer = new Renderer(Shaders.LoadSource);
        _renderer.CompileStarted += path => OnUi(() => Status.Started(path));
        _renderer.Compiled += (path, error) => OnUi(() => OnCompiled(path, error));
        Shaders.SourceChanged += path => _renderer.Invalidate(path);
        ApplyRendererSettings();
        _renderer.Start();

        Web = new WebWallpaperService(Settings, _desktop);
        Web.PageReported += Status.Finished;
        Wallpaper = new WallpaperAssignment(Settings, Library, _desktop, _renderer, Web, Status);
        Governor = new RenderGovernor(Settings, _renderer, _desktop, _session, new PerformanceController(Shaders, RequestCost));
        Governor.Evaluated += Web.Refresh;
        Governor.WindowsLost += OnWindowsLost;
        Governor.RecoveryNeeded += OnRecoveryNeeded;
        Wallpaper.TargetsApplied += Governor.Evaluate;
        Wallpaper.Attach();

        Settings.Changed += OnSettingsChanged;
        Library.Changed += OnLibraryChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _hotkeys.ExplorerRestarted += Wallpaper.Reattach;
        _hotkeys.DisplayPowerChanged += OnDisplayPowerChanged;
        _hotkeys.HotkeyPressed += OnHotkey;

        Updates = new UpdateService(Settings, Notify);
        _tray = new TrayIcon(this);
        if (Log.Mirror != null)
        {
            _perfLog = new PerfLog(_desktop);
        }
        RefreshMeasuring();

        _instance.ShowRequested += () => OnUi(ShowLibrary);
        _instance.ExitRequested += () => OnUi(Exit);
        _instance.Listen();
        SingleInstance.SignalStarted();   // an updater that started this exe now knows that it works

        if (showLibrary)
        {
            ShowLibrary();
        }
        if (justUpdated)
        {
            _tray.ShowBalloon("Wallpepper was updated to version " + Installer.VersionText);
        }
    }

    /// <summary>The cost of a shader was measured and stored (path).</summary>
    public event Action<string>? CostMeasured;

    public SettingsStore Settings { get; }

    public ShaderLibrary Library { get; }

    public ShaderSettings Shaders { get; }

    public CompileStatus Status { get; } = new CompileStatus();

    public WallpaperAssignment Wallpaper { get; }

    public RenderGovernor Governor { get; }

    public WebWallpaperService Web { get; }

    public UpdateService Updates { get; }

    public IReadOnlyList<DisplaySlot> Displays => _desktop.Slots;

    public ThumbnailRenderer Thumbnails => _renderer.Thumbnails;

    /// <summary>Self-test access to the parts it puts under stress.</summary>
    internal Renderer Renderer => _renderer;

    internal DesktopHost Desktop => _desktop;

    internal SessionState Session => _session;

    internal LibraryWindow? LibraryWindow => _library;

    public LivePreview CreatePreview() => new LivePreview(_renderer, Shaders, () => Governor.Level);

    // ---------------------------------------------------------------- what the user can do from several places
    public void SetPaused(bool paused) => Settings.Change(SettingsArea.Governor, config => config.UserPaused = paused);

    /// <summary>Changes the resource level; every display is fitted again from scratch.</summary>
    public void SetResourceLevel(ResourceLevelId level)
    {
        Settings.Change(SettingsArea.None, config => config.Resource = level);
        Shaders.ClearLight();
        Governor.Evaluate();
    }

    /// <summary>Next or previous shader (hotkeys, tray).</summary>
    public void Cycle(int direction)
    {
        string? applied = Wallpaper.Cycle(direction);
        if (applied != null)
        {
            _library?.ShowToast(Library.Title(applied) + " is now your wallpaper");
        }
    }

    /// <summary>
    /// Has the cost of a shader measured (GPU ms per 1080p frame, with the settings it runs with right now) and stores
    /// the result for the fitter and the gallery.
    /// </summary>
    public void RequestCost(string path)
    {
        bool light = Shaders.IsLight(path);
        Thumbnails.RequestCost(path, (measured, ms) => OnUi(() =>
        {
            if (Shaders.IsLight(measured) != light)
            {
                RequestCost(measured);   // switched to or from its light settings meanwhile: measure what runs now
                return;
            }
            Shaders.StoreCost(measured, ms);
            Log.Write($"cost of {Path.GetFileName(measured)}{(light ? " (light settings)" : "")}: {ms:0.00} ms per 1080p frame");
            CostMeasured?.Invoke(measured);
        }));
    }

    /// <summary>A short message: a toast in the library window when it is open, otherwise a tray notification.</summary>
    public void Notify(string text, bool isError = false)
    {
        if (_exiting)
        {
            return;
        }
        if (_library != null)
        {
            _library.ShowToast(text, isError);
        }
        else
        {
            _tray.ShowBalloon(text);
        }
    }

    // ---------------------------------------------------------------- windows
    public void ShowLibrary()
    {
        if (_exiting)
        {
            return;
        }
        if (_library == null)
        {
            Library.Rescan();
            _library = new LibraryWindow(this);
            _library.Closed += (_, _) => OnLibraryClosed();
            RefreshMeasuring();
        }
        _library.Show();
        if (_library.WindowState == System.Windows.WindowState.Minimized)
        {
            _library.WindowState = System.Windows.WindowState.Normal;
        }
        _library.Activate();
    }

    public void ShowSettings()
    {
        ShowLibrary();
        _library?.OpenSettings();
    }

    public void ShowBenchmark()
    {
        ShowLibrary();
        _library?.OpenBenchmark();
    }

    public void Exit()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        _library?.Close();
        Settings.Flush();
        _perfLog?.Dispose();
        Updates.Dispose();
        _tray.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Library.Dispose();
        Governor.Dispose();
        _session.Dispose();
        Web.Dispose();
        _desktop.Detach(_renderer);
        _renderer.Dispose();
        _desktop.RestoreWallpaper();
        _hotkeys.Dispose();
        _ui.InvokeShutdown();
    }

    // ---------------------------------------------------------------- wiring
    /// <summary>Runs on the UI thread: now when already there, otherwise queued.</summary>
    private void OnUi(Action action)
    {
        if (_ui.HasShutdownStarted)
        {
            return;
        }
        if (_ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ui.InvokeAsync(action);
        }
    }

    private void ApplyRendererSettings()
    {
        var config = Settings.Config;
        _renderer.DefaultScale = config.RenderScale;
        _renderer.Speed = config.Speed;
        var background = Try.Get("read the background colour", () => System.Drawing.ColorTranslator.FromHtml(config.Background), System.Drawing.Color.Black);
        _renderer.SetBackground(background.R / 255f, background.G / 255f, background.B / 255f);
    }

    private void OnSettingsChanged(SettingsArea area)
    {
        if (area.HasFlag(SettingsArea.Rendering))
        {
            ApplyRendererSettings();
        }
        if (area.HasFlag(SettingsArea.Displays))
        {
            Wallpaper.ApplyTargets();   // ends with an evaluation
        }
        else if (area != SettingsArea.None)
        {
            Governor.Evaluate();
        }
    }

    private void OnCompiled(string path, string? error)
    {
        if (error != null)
        {
            Log.Write($"compile {Path.GetFileName(path)}: {error}");
        }
        Status.Finished(path, error);
    }

    /// <summary>Hot reload: an edited shader is compiled again; a missing file falls back to the default.</summary>
    private void OnLibraryChanged(IReadOnlyCollection<string> paths)
    {
        foreach (string path in paths.Where(ShaderLibrary.IsShader))
        {
            _renderer.Invalidate(path);
        }
        bool lostItem = _desktop.Slots.Any(slot => slot.ItemPath == null || !File.Exists(slot.ItemPath));
        if (lostItem || paths.Any(WebInfo.IsWeb))
        {
            Wallpaper.ApplyTargets();
        }
        _renderer.Wake();
    }

    private void OnWindowsLost()
    {
        Log.Write("wallpaper windows were destroyed from outside (Explorer rebuilt the desktop) - attaching again");
        Wallpaper.Reattach();
    }

    private void OnRecoveryNeeded()
    {
        Log.Write("no frames although nothing is paused - rebuilding the wallpaper windows and the graphics device");
        _renderer.ResetDevice();
        Wallpaper.Reattach();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        Wallpaper.Reattach();
        _tray.RefreshIcon();
    });

    private void OnDisplayPowerChanged(bool off)
    {
        if (_session.DisplayOff == off)
        {
            return;
        }
        _session.DisplayOff = off;
        Log.Write(off ? "display off - rendering stops" : "display on - rendering resumes");
        Governor.Evaluate();
    }

    private void OnHotkey(Hotkey hotkey)
    {
        switch (hotkey)
        {
            case Hotkey.NextWallpaper:
                Cycle(+1);
                break;
            case Hotkey.PreviousWallpaper:
                Cycle(-1);
                break;
            case Hotkey.TogglePause:
                SetPaused(!Settings.Config.UserPaused);
                break;
        }
    }

    /// <summary>GPU timing stays on only while someone looks at it; gallery pictures are only drawn while the window is open.</summary>
    private void RefreshMeasuring()
    {
        Governor.MeasureWanted = _library != null || _perfLog != null;
        Thumbnails.PicturesEnabled = _library != null;
        if (_library == null)
        {
            Thumbnails.CancelPictures();
        }
    }

    /// <summary>Back to tray-only: give back what the library window needed.</summary>
    private void OnLibraryClosed()
    {
        _library = null;
        RefreshMeasuring();
        Settings.Flush();
        _renderer.TrimShaders();
        // The window's pictures, visual tree and brushes are garbage now, most of it in the large object heap, which
        // the runtime would otherwise keep for a long time in a process that hardly allocates any more.
        _ui.InvokeAsync(
            () =>
            {
                if (_library == null)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                }
            },
            DispatcherPriority.ApplicationIdle);
    }
}
