using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wallpepper.Library;
using Wallpepper.Settings;
using Wallpepper.Setup;
using Wallpepper.Ui.Settings;

namespace Wallpepper.Ui;

/// <summary>
/// The library window's shell: gallery on the left, inspector or settings on the right. The parts do the work
/// (<see cref="GalleryController"/>, <see cref="InspectorPane"/>, <see cref="SettingsPane"/>); this class holds what
/// belongs to the window as a whole: chrome, keyboard, drag and drop, toasts, the confirmation overlay, the actions.
/// </summary>
internal partial class LibraryWindow : Window
{
    private const int ToastMs = 2600;
    private const int ErrorToastMs = 4200;

    private readonly App _app;
    private readonly ThumbnailCache _thumbnails;
    private readonly GalleryController _gallery;
    private readonly InspectorPane _inspector;
    private readonly SettingsPane _settings;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _tickTimer;
    private Action? _confirmed;
    private string? _dismissedUpdate;
    private bool _dialogOpen;
    private bool _dropping;
    private bool _closed;

    internal LibraryWindow(App app)
    {
        _app = app;
        UiHost.EnsureTheme();
        InitializeComponent();
        Icon = Assets.WindowIcon;

        _thumbnails = new ThumbnailCache(app);
        _gallery = new GalleryController(this, app, _thumbnails);
        _inspector = new InspectorPane(this, app, _thumbnails, ShowToast);
        _settings = new SettingsPane(this, app, ShowToast);
        _gallery.SelectionChanged += OnSelectionChanged;
        _settings.OpenChanged += UpdateCovered;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ToastMs) };
        _toastTimer.Tick += (_, _) => HideToast();
        _tickTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => PerfText.Text = _settings.Tick();

        _app.Library.Changed += OnLibraryChanged;
        _app.Status.Changed += OnStatusChanged;
        _app.Governor.StatusChanged += UpdateStatus;
        _app.Wallpaper.Changed += OnWallpaperChanged;
        _app.Wallpaper.DisplaysChanged += OnDisplaysChanged;
        _app.Shaders.QualityChanged += OnQualityChanged;
        _app.CostMeasured += OnCostMeasured;
        _app.Web.Changed += _inspector.OnWebChanged;
        _app.Updates.Changed += RefreshUpdate;

        PreviewKeyDown += OnKey;
        StateChanged += (_, _) => OnStateChanged();
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        Loaded += OnLoaded;
    }

    // ---------------------------------------------------------------- for the app and the self-test
    internal LivePreview Preview => _inspector.Preview;

    internal IReadOnlyList<string> VisiblePaths => _gallery.VisiblePaths;

    internal int PendingThumbnails => _thumbnails.Pending;

    internal void SelectForTest(string path) => _gallery.Select(path);

    public void OpenSettings() => WhenLoaded(() => _settings.Show(true));

    /// <summary>Opens the settings at the benchmark (tray menu).</summary>
    public void OpenBenchmark() => WhenLoaded(_settings.ShowBenchmark);

    public void ShowToast(string text, bool isError = false)
    {
        if (_closed)
        {
            return;
        }
        ToastText.Text = text;
        ToastIcon.Text = isError ? Glyphs.Error : Glyphs.Done;
        ToastIcon.Foreground = Theme.Brush(isError ? "Danger" : "Accent");
        var rise = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)ToastBox.RenderTransform).BeginAnimation(TranslateTransform.YProperty, rise);
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(isError ? ErrorToastMs : ToastMs);
        _toastTimer.Start();
    }

    // ---------------------------------------------------------------- window
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!Chrome.Apply(this))
        {
            Tint.Background = Theme.Brush("Solid");   // Windows 10, or transparency effects off: solid dark
        }
        Chrome.RestorePlacement(this, _app.Settings.Config.Window);
        OnStateChanged();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Chrome.SavePlacement(this) is WindowPlacement placement)
        {
            _app.Settings.Change(SettingsArea.None, config => config.Window = placement);
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _toastTimer.Stop();
        _tickTimer.Stop();
        _app.Library.Changed -= OnLibraryChanged;
        _app.Status.Changed -= OnStatusChanged;
        _app.Governor.StatusChanged -= UpdateStatus;
        _app.Wallpaper.Changed -= OnWallpaperChanged;
        _app.Wallpaper.DisplaysChanged -= OnDisplaysChanged;
        _app.Shaders.QualityChanged -= OnQualityChanged;
        _app.CostMeasured -= OnCostMeasured;
        _app.Web.Changed -= _inspector.OnWebChanged;
        _app.Updates.Changed -= RefreshUpdate;
        _settings.Close();
        _inspector.Close();
        _thumbnails.Close();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _gallery.Rebuild();
        string? live = _app.Displays.FirstOrDefault()?.ItemPath;
        if (live != null && _gallery.VisiblePaths.Contains(live, StringComparer.OrdinalIgnoreCase))
        {
            _gallery.Select(live);
        }
        _settings.Build();
        RefreshUpdate();
        UpdateStatus();
        _tickTimer.Start();
        Gallery.Focus();
    }

    private void WhenLoaded(Action action)
    {
        if (IsLoaded)
        {
            action();
        }
        else
        {
            Loaded += (_, _) => action();
        }
    }

    private void OnStateChanged()
    {
        // a maximised WindowChrome window hangs over the screen edge by the resize border
        bool maximised = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(maximised ? 7 : 0);
        MaxBtn.Content = maximised ? Glyphs.Restore : Glyphs.Maximise;
        UpdateCovered();
        _tickTimer.IsEnabled = IsLoaded && WindowState != WindowState.Minimized;
    }

    /// <summary>The preview is a child window on top of all WPF content: it hides while something has to cover it.</summary>
    private void UpdateCovered() =>
        _inspector.SetCovered(_settings.CoversDetail || _dialogOpen || _dropping || WindowState == WindowState.Minimized);

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- what the app tells the window
    private void OnSelectionChanged(string? path)
    {
        if (_settings.IsOpen && path != null)
        {
            _settings.Show(false);
        }
        _inspector.Show(_gallery.SelectedItem);
    }

    private void OnLibraryChanged(IReadOnlyCollection<string> paths)
    {
        string? selected = _gallery.Selected;
        _gallery.Rebuild();
        foreach (string path in paths)
        {
            if (_gallery.ItemFor(path) is ShaderItem item && File.Exists(path))
            {
                _thumbnails.Ensure(item);   // an edited file has a new picture
            }
        }
        bool selectedEdited = selected != null && paths.Contains(selected, StringComparer.OrdinalIgnoreCase);
        if (selectedEdited && string.Equals(selected, _gallery.Selected, StringComparison.OrdinalIgnoreCase))
        {
            _inspector.Show(_gallery.SelectedItem);
        }
    }

    private void OnStatusChanged(string path)
    {
        _gallery.OnCompiled(path);
        _inspector.OnStatusChanged(path);
        bool finished = !_app.Status.IsCompiling(path);
        if (finished && !ShaderLibrary.IsShader(path) && _app.Status.ErrorOf(path) is string problem)
        {
            ShowToast(problem, isError: true);   // a web page could not be started
        }
    }

    private void OnWallpaperChanged()
    {
        _gallery.RefreshLive();
        _inspector.OnWallpaperChanged();
        _settings.OnWallpaperChanged();
    }

    private void OnDisplaysChanged()
    {
        _inspector.OnWallpaperChanged();
        _settings.OnDisplaysChanged();
    }

    private void OnQualityChanged()
    {
        _gallery.RefreshCosts();
        _inspector.OnQualityChanged();
    }

    private void OnCostMeasured(string path) => _gallery.RefreshCosts();

    private void UpdateStatus()
    {
        if (_closed || !IsLoaded)
        {
            return;
        }
        var governor = _app.Governor;
        bool allPaused = governor.GlobalReason != null || governor.Running == 0;
        StatusText.Text = governor.Status;
        StatusDot.Fill = Theme.Brush(governor.Stalled ? "Danger" : allPaused ? "Warn" : "Ok");
        bool paused = _app.Settings.Config.UserPaused;
        PauseBtn.Content = paused ? Glyphs.Play : Glyphs.Pause;
        PauseBtn.ToolTip = paused ? "Resume wallpaper" : "Pause wallpaper";
        PauseBtn.Foreground = Theme.Brush(paused ? "Accent" : "Text");
    }

    // ---------------------------------------------------------------- title bar and gallery events (named in the XAML)
    private void Sort_Click(object sender, RoutedEventArgs e) => _gallery.ShowSortMenu();

    private void Pause_Click(object sender, RoutedEventArgs e) => _app.SetPaused(!_app.Settings.Config.UserPaused);

    private void Settings_Click(object sender, RoutedEventArgs e) => _settings.Toggle();

    private void SettingsClose_Click(object sender, RoutedEventArgs e) => _settings.Show(false);

    private void Tab_Click(object sender, RoutedEventArgs e) => _settings.ShowSelectedTab();

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ShaderItem item)
        {
            _gallery.ToggleFavourite(item);
        }
        e.Handled = true;
    }

    private void Gallery_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        bool onButton = e.OriginalSource is DependencyObject source && FindParent<Button>(source) != null;
        if (e.ChangedButton == MouseButton.Left && !onButton && GalleryController.ItemAt(e.OriginalSource) != null)
        {
            ApplySelected();
        }
    }

    private void Gallery_RightClick(object sender, MouseButtonEventArgs e)
    {
        ShaderItem? item = GalleryController.ItemAt(e.OriginalSource);
        if (item == null)
        {
            return;
        }
        _gallery.Select(item.Path);
        var menu = new ContextMenu { PlacementTarget = Gallery, Placement = PlacementMode.MousePoint };
        void Add(string text, string glyph, Action click, string shortcut = "")
        {
            var entry = new MenuItem { Header = text, Tag = glyph, InputGestureText = shortcut };
            entry.Click += (_, _) => click();
            menu.Items.Add(entry);
        }

        if (!item.TerminalOnly)
        {
            Add("Set as wallpaper", Glyphs.Wallpaper, ApplySelected, "Enter");
        }
        if (!item.IsWeb)
        {
            Add("Use in Windows Terminal", Glyphs.Terminal, UseInTerminal);
        }
        else if (_app.Web.IsReady(item.Path))
        {
            Add("Interact", Glyphs.Interact, () => _app.Web.Interact(item.Path, true));
            Add("Reload", Glyphs.Reload, () => _app.Web.Reload(item.Path));
        }
        menu.Items.Add(new Separator());
        Add(
            item.IsFavourite ? "Remove from favourites" : "Add to favourites", item.IsFavourite ? Glyphs.Favourite : Glyphs.NotFavourite,
            () => _gallery.ToggleFavourite(item));
        Add("Edit", Glyphs.Edit, EditSelected);
        Add("Show in folder", Glyphs.Folder, () => Shell.Reveal(item.Path));
        menu.Items.Add(new Separator());
        Add("Delete", Glyphs.Delete, DeleteSelected, "Del");
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        DisplayMode mode = ReferenceEquals(sender, ModePer) ? DisplayMode.PerDisplay
            : ReferenceEquals(sender, ModeSpan) ? DisplayMode.Span : DisplayMode.Duplicate;
        if (mode != _app.Wallpaper.Mode)
        {
            _app.Wallpaper.SetMode(mode);
        }
    }

    private static T? FindParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element != null && element is not T)
        {
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return element as T;
    }

    // ---------------------------------------------------------------- keyboard
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_dialogOpen)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                CloseDialog(confirmed: e.Key == Key.Enter);
            }
            e.Handled = e.Key != Key.Tab;
            return;
        }
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool inText = Keyboard.FocusedElement is TextBox;
        if (ctrl && e.Key == Key.F)
        {
            Search.Focus();
            Search.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemComma)
        {
            _settings.Toggle();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_settings.IsOpen)
            {
                _settings.Show(false);
            }
            else if (inText || Search.Text.Length > 0)
            {
                Search.Text = "";
                Gallery.Focus();
            }
            e.Handled = true;
        }
        else if (inText)
        {
            if (ReferenceEquals(Keyboard.FocusedElement, Search) && (e.Key == Key.Down || e.Key == Key.Enter))
            {
                Gallery.Focus();
                e.Handled = true;
            }
        }
        else
        {
            bool controlHasKey = Keyboard.FocusedElement is Slider || (Keyboard.FocusedElement is ButtonBase && e.Key == Key.Enter);
            if (!controlHasKey && !_settings.IsOpen)
            {
                e.Handled = OnGalleryKey(e.Key);
            }
        }
    }

    /// <returns>true when the key did something.</returns>
    private bool OnGalleryKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                _gallery.MoveSelection(-1);
                return true;
            case Key.Right:
                _gallery.MoveSelection(+1);
                return true;
            case Key.Up:
                _gallery.MoveSelection(-_gallery.Columns);
                return true;
            case Key.Down:
                _gallery.MoveSelection(+_gallery.Columns);
                return true;
            case Key.Home:
                _gallery.MoveSelection(int.MinValue / 2);
                return true;
            case Key.End:
                _gallery.MoveSelection(int.MaxValue / 2);
                return true;
            case Key.Enter:
                ApplySelected();
                return true;
            case Key.Delete:
                DeleteSelected();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- actions on the selected item
    private void Apply_Click(object sender, RoutedEventArgs e) => ApplySelected();

    private void Terminal_Click(object sender, RoutedEventArgs e) => UseInTerminal();

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Shaders and web wallpapers (*.hlsl, *.wallpepperweb)|*.hlsl;*" + Web.WebInfo.Extension,
            Multiselect = true,
            Title = "Add to the library",
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void ApplySelected()
    {
        ShaderItem? item = _gallery.SelectedItem;
        if (item == null)
        {
            return;
        }
        if (item.TerminalOnly)
        {
            UseInTerminal();
            return;
        }
        if (item.HasError)
        {
            ShowToast("This shader does not compile yet", isError: true);
            return;
        }
        if (item.IsWeb && _app.Library.Metadata(item.Path).Web?.Error is string problem)
        {
            ShowToast(problem, isError: true);
            return;
        }
        string file = Path.GetFileName(item.Path);
        if (item.TooHeavy && !Config.ContainsName(_app.Settings.Config.HeavyConfirmed, file))
        {
            // asked once per shader
            Confirm(
                $"“{item.Name}” is heavy for this PC",
                "Even at the lowest resolution and frame rate it needs more GPU time than the resource level allows. "
                + "It will run, but slowly, and it will use more power.",
                "Set anyway",
                () =>
                {
                    _app.Settings.Change(SettingsArea.None, config => config.HeavyConfirmed.Add(file));
                    ApplySelected();
                });
            return;
        }

        bool perDisplay = _app.Wallpaper.Mode == DisplayMode.PerDisplay && _app.Displays.Count > 1;
        _app.Wallpaper.Apply(item.Path, perDisplay ? _inspector.PickedDisplays.ToList() : null);
        if (_app.Status.IsCompiling(item.Path))
        {
            ShowToast($"Compiling {item.Name} · it appears as soon as it is ready");
        }
        else
        {
            ShowToast(item.Name + (_app.Wallpaper.Mode == DisplayMode.Span ? " now spans your displays" : " is now your wallpaper"));
        }
    }

    /// <summary>Points Windows Terminal at the selected shader; the user's values are baked into a copy next to the library.</summary>
    private void UseInTerminal()
    {
        ShaderItem? item = _gallery.SelectedItem;
        if (item == null || item.IsWeb)
        {
            return;
        }
        try
        {
            string target = item.Path;
            if (_app.Shaders.OverridesFor(item.Path).Count > 0)
            {
                // Terminal compiles only *.hlsl; tuned copies stay out of the library listing (sub folder)
                string folder = Path.Combine(_app.Library.Folder, "tuned");
                Directory.CreateDirectory(folder);
                target = Path.Combine(folder, Path.GetFileName(item.Path));
                File.WriteAllText(target, _app.Shaders.LoadSource(item.Path));
            }
            bool keptFormatting = TerminalIntegration.UseShader(target);
            ShowToast("Windows Terminal now uses " + item.Name + (keptFormatting ? "" : " (comments in its settings file were not kept; a backup is next to it)"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Warn("set Windows Terminal's shader", e);
            ShowToast("Could not update Terminal: " + e.Message, isError: true);
        }
    }

    private void EditSelected()
    {
        if (_gallery.Selected is string path)
        {
            Shell.Edit(path);
            ShowToast("Opening " + Path.GetFileName(path) + " · saving reloads it live");
        }
    }

    private void DeleteSelected()
    {
        if (_gallery.Selected is not string path)
        {
            return;
        }
        string title = _app.Library.Title(path);
        Confirm(
            $"Delete “{title}”?", $"{Path.GetFileName(path)} is moved to the Recycle Bin, so you can restore it from there.", "Delete",
            () =>
            {
                string? next = _gallery.NeighbourOf(path);
                try
                {
                    _app.Library.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    ShowToast("Delete failed: " + e.Message, isError: true);
                    return;
                }
                _app.Status.Forget(path);
                ThumbnailCache.Forget(path);
                _gallery.Select(next);
                ShowToast(title + " moved to the Recycle Bin");
            });
    }

    private void AddFiles(IEnumerable<string> files)
    {
        IReadOnlyList<string> added;
        try
        {
            added = _app.Library.Add(files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not add the files: " + e.Message, isError: true);
            return;
        }
        if (added.Count == 0)
        {
            ShowToast("Only .hlsl pixel shaders and .wallpepperweb files can be added", isError: true);
            return;
        }
        _gallery.ShowAllAndSelect(added[0]);
        ShowToast(added.Count == 1 ? _app.Library.Title(added[0]) + " added" : added.Count + " wallpapers added");
    }

    // ---------------------------------------------------------------- updates (banner in the library)
    private void RefreshUpdate()
    {
        if (_closed)
        {
            return;
        }
        _settings.OnUpdateChanged();
        var updates = _app.Updates;
        var update = updates.Available;
        bool show = update != null && update.Version != _dismissedUpdate;
        UpdateBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (update == null || !show)
        {
            return;
        }
        UpdateText.Text = updates.Busy ? $"Downloading Wallpepper {update.Version}…" : $"Wallpepper {update.Version} is available";
        UpdateNowBtn.IsEnabled = !updates.Busy;
        UpdateNowBtn.Content = Installer.IsRunningInstalled ? "Update" : "Download";
    }

    private void UpdateNow_Click(object sender, RoutedEventArgs e) => _app.Updates.Install();

    private void UpdateNotes_Click(object sender, RoutedEventArgs e) => Shell.OpenUrl(_app.Updates.Available?.Notes ?? Updater.ReleasesUrl);

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        _dismissedUpdate = _app.Updates.Available?.Version;
        RefreshUpdate();
    }

    // ---------------------------------------------------------------- toast, confirmation overlay, drag and drop
    private void HideToast()
    {
        _toastTimer.Stop();
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        ((TranslateTransform)ToastBox.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, TimeSpan.FromMilliseconds(220)));
    }

    private static void FadeIn(FrameworkElement element, int ms)
    {
        element.Visibility = Visibility.Visible;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(ms)));
    }

    private static void FadeOut(FrameworkElement element, int ms)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms));
        fade.Completed += (_, _) =>
        {
            if (element.Opacity < 0.01)
            {
                element.Visibility = Visibility.Collapsed;
            }
        };
        element.BeginAnimation(OpacityProperty, fade);
    }

    private void Confirm(string title, string text, string okText, Action onConfirmed)
    {
        DialogTitle.Text = title;
        DialogText.Text = text;
        DialogOk.Content = okText;
        _confirmed = onConfirmed;
        _dialogOpen = true;
        UpdateCovered();
        FadeIn(DialogOverlay, 150);
        DialogCancel.Focus();
    }

    private void CloseDialog(bool confirmed)
    {
        if (!_dialogOpen)
        {
            return;
        }
        _dialogOpen = false;
        FadeOut(DialogOverlay, 150);
        UpdateCovered();
        Action? action = _confirmed;
        _confirmed = null;
        Gallery.Focus();
        if (confirmed)
        {
            action?.Invoke();
        }
    }

    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(confirmed: false);

    private void DialogOk_Click(object sender, RoutedEventArgs e) => CloseDialog(confirmed: true);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (e.Effects != DragDropEffects.None && !_dropping)
        {
            _dropping = true;
            UpdateCovered();
            FadeIn(DropOverlay, 120);
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        Point position = e.GetPosition(this);
        if (position.X <= 1 || position.Y <= 1 || position.X >= ActualWidth - 1 || position.Y >= ActualHeight - 1)
        {
            EndDrop();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        EndDrop();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }
        e.Handled = true;
    }

    private void EndDrop()
    {
        if (_dropping)
        {
            _dropping = false;
            FadeOut(DropOverlay, 120);
            UpdateCovered();
        }
    }
}
