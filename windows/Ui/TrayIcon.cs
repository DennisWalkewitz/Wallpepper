using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wallpepper.Desktop;
using Wallpepper.Interop;
using Wallpepper.Settings;
using Wallpepper.Setup;
using WinForms = System.Windows.Forms;

namespace Wallpepper.Ui;

/// <summary>
/// Notification-area icon (WinForms NotifyIcon, which also puts itself back after an Explorer restart) with a themed
/// WPF menu. The tooltip names the wallpaper and says why it is paused.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int BalloonMs = 6000;
    private const int MaxTooltipLength = 63;   // the shell's limit
    private const int MaxMenuTitleLength = 34;
    private const int BaseIconSize = 16;

    private readonly App _app;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.NativeWindow _anchor = new WinForms.NativeWindow();   // activated so the menu closes when you click elsewhere
    private ContextMenu? _menu;
    private int _iconSize;

    public TrayIcon(App app)
    {
        _app = app;
        _anchor.CreateHandle(new WinForms.CreateParams { Caption = "Wallpepper.tray" });
        _icon = new WinForms.NotifyIcon { Text = "Wallpepper" };
        RefreshIcon();
        _icon.Visible = true;
        _icon.MouseUp += OnMouseUp;
        _icon.BalloonTipClicked += (_, _) => _app.ShowLibrary();
        _app.Governor.StatusChanged += RefreshText;
        _app.Wallpaper.Changed += RefreshText;
        RefreshText();
    }

    /// <summary>
    /// Picks the icon frame that matches the taskbar's scaling (16 px at 100%, 24 px at 150% ...), so it is never resampled.
    /// </summary>
    public void RefreshIcon()
    {
        IntPtr taskbar = User32.FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar != IntPtr.Zero ? Math.Max(96, User32.GetDpiForWindow(taskbar)) : 96;
        int size = (int)Math.Round(BaseIconSize * dpi / 96.0);
        if (size == _iconSize)
        {
            return;
        }
        Try.Run("load the tray icon", () =>
        {
            using Stream stream = Assets.OpenIcon();
            var previous = _icon.Icon;
            _icon.Icon = new System.Drawing.Icon(stream, size, size);
            previous?.Dispose();
            _iconSize = size;
        });
    }

    public void ShowBalloon(string text) =>
        Try.Run("show a notification", () => _icon.ShowBalloonTip(BalloonMs, "Wallpepper", text, WinForms.ToolTipIcon.None));

    public void Dispose()
    {
        _app.Governor.StatusChanged -= RefreshText;
        _app.Wallpaper.Changed -= RefreshText;
        if (_menu != null)
        {
            _menu.IsOpen = false;
        }
        _icon.Visible = false;   // removes the icon at once (no ghost left in the notification area)
        var icon = _icon.Icon;
        _icon.Dispose();
        icon?.Dispose();
        _anchor.DestroyHandle();
    }

    /// <summary>Left: library. Right (or Ctrl + left, for one-button pointing devices): menu.</summary>
    private void OnMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        bool ctrl = (WinForms.Control.ModifierKeys & WinForms.Keys.Control) != 0;
        if (e.Button == WinForms.MouseButtons.Left && !ctrl)
        {
            _app.ShowLibrary();
        }
        else if (e.Button == WinForms.MouseButtons.Right || e.Button == WinForms.MouseButtons.Left)
        {
            ShowMenu();
        }
    }

    private void RefreshText()
    {
        var governor = _app.Governor;
        string? state = null;
        if (governor.GlobalReason is PauseReason reason)
        {
            state = $"paused ({PauseReasonText.Of(reason)})";
        }
        else if (governor.Stalled)
        {
            state = "not rendering (recovering)";
        }
        else if (governor.Total > 0 && governor.Running == 0)
        {
            PauseReason first = _app.Displays.Select(display => display.PauseReason).FirstOrDefault(r => r != null) ?? PauseReason.Covered;
            state = $"paused ({PauseReasonText.Of(first)})";
        }
        string title = _app.Wallpaper.CurrentTitle();
        string text = "Wallpepper" + (title.Length > 0 ? " · " + title : "") + (state != null ? " · " + state : "");
        if (text.Length > MaxTooltipLength)
        {
            text = text.Substring(0, MaxTooltipLength - 1) + "…";
        }
        if (_icon.Text != text)
        {
            Try.Run("set the tray tooltip", () => _icon.Text = text);
        }
    }

    private void ShowMenu()
    {
        if (_menu != null)
        {
            _menu.IsOpen = false;
        }
        UiHost.EnsureTheme();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        _menu = menu;

        string title = _app.Wallpaper.CurrentTitle();
        if (title.Length > 0)
        {
            string shown = title.Length > MaxMenuTitleLength ? title.Substring(0, MaxMenuTitleLength - 1) + "…" : title;
            menu.Items.Add(Item(shown, Glyphs.Wallpaper, null));
            menu.Items.Add(new Separator());
        }
        bool paused = _app.Settings.Config.UserPaused;
        menu.Items.Add(Item("Open library", Glyphs.Library, _app.ShowLibrary));
        menu.Items.Add(Item("Next shader", Glyphs.Next, () => _app.Cycle(+1), "Ctrl+Win+Alt+→"));
        menu.Items.Add(Item("Previous shader", Glyphs.Previous, () => _app.Cycle(-1), "Ctrl+Win+Alt+←"));
        menu.Items.Add(Item(paused ? "Resume" : "Pause", paused ? Glyphs.Play : Glyphs.Pause, () => _app.SetPaused(!paused), "Ctrl+Win+Alt+P"));
        AddWebItems(menu);
        AddModeItems(menu);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Benchmark", Glyphs.Benchmark, _app.ShowBenchmark));
        var updates = _app.Updates;
        if (updates.Busy)
        {
            menu.Items.Add(Item("Checking for updates…", Glyphs.CheckForUpdates, null));
        }
        else if (updates.Available != null)
        {
            string verb = Installer.IsRunningInstalled ? "Update to " : "Get version ";
            menu.Items.Add(Item(verb + updates.Available.Version, Glyphs.Update, updates.Install));
        }
        else
        {
            menu.Items.Add(Item("Check for updates", Glyphs.CheckForUpdates, () => updates.Check(manual: true)));
        }
        menu.Items.Add(Item("Settings", Glyphs.Settings, _app.ShowSettings));
        bool autostart = Installer.AutostartOrDefault();
        menu.Items.Add(CheckItem("Start with Windows", autostart, () => Try.Run("change autostart", () => Installer.Autostart = !autostart)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", Glyphs.Quit, _app.Exit));

        User32.SetForegroundWindow(_anchor.Handle);
        menu.IsOpen = true;
    }

    private void AddWebItems(ContextMenu menu)
    {
        string? page = _app.Web.LiveItem;
        if (page == null)
        {
            return;
        }
        if (_app.Web.IsReady(page))
        {
            bool lifted = _app.Web.Interacting;
            menu.Items.Add(Item(lifted ? "Done interacting" : "Interact with the page", Glyphs.Interact, () => _app.Web.Interact(page, !lifted)));
        }
        if (_app.Library.Metadata(page).Web?.Audio == true)
        {
            bool muted = _app.Web.IsMuted(page);
            menu.Items.Add(Item(muted ? "Unmute the page" : "Mute the page", muted ? Glyphs.Unmute : Glyphs.Mute, () => _app.Web.SetMuted(page, !muted)));
        }
    }

    private void AddModeItems(ContextMenu menu)
    {
        if (_app.Displays.Count <= 1)
        {
            return;
        }
        DisplayMode mode = _app.Wallpaper.Mode;
        menu.Items.Add(new Separator());
        menu.Items.Add(CheckItem("Same on every display", mode == DisplayMode.Duplicate, () => _app.Wallpaper.SetMode(DisplayMode.Duplicate)));
        menu.Items.Add(CheckItem("One per display", mode == DisplayMode.PerDisplay, () => _app.Wallpaper.SetMode(DisplayMode.PerDisplay)));
        menu.Items.Add(CheckItem("Span across displays", mode == DisplayMode.Span, () => _app.Wallpaper.SetMode(DisplayMode.Span)));
    }

    /// <summary>A menu entry with an icon; without an action it is a disabled label.</summary>
    private static MenuItem Item(string text, string glyph, Action? click, string? shortcut = null)
    {
        var item = new MenuItem { Header = text, Tag = glyph, InputGestureText = shortcut ?? "", IsEnabled = click != null };
        if (click != null)
        {
            item.Click += (_, _) => click();
        }
        return item;
    }

    /// <summary>A menu entry with a check mark where the icon would be.</summary>
    private static MenuItem CheckItem(string text, bool isChecked, Action click)
    {
        var item = new MenuItem { Header = text, IsChecked = isChecked };
        item.Click += (_, _) => click();
        return item;
    }
}
