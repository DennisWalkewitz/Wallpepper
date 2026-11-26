using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpepper.Settings;
using Wallpepper.Ui.Settings;
using Wallpepper.Web;

namespace Wallpepper.Ui;

/// <summary>
/// The right-hand side of the library window for the selected item: live preview, name, the displays block, errors,
/// the fit note, and the shader properties or web controls.
/// </summary>
internal sealed class InspectorPane
{
    private const int MaxErrorLength = 1200;
    private const int PictureRefreshMs = 900;
    private const int PreviewPollMs = 30;

    private readonly LibraryWindow _view;
    private readonly App _app;
    private readonly ThumbnailCache _thumbnails;
    private readonly PreviewHost _previewHost = new PreviewHost();
    private readonly ShaderPropertiesPanel _properties;
    private readonly WebPanel _web;
    private readonly DispatcherTimer _pictureTimer;
    private readonly DispatcherTimer _previewPoll;
    private ShaderItem? _item;
    private bool _covered;

    public InspectorPane(LibraryWindow view, App app, ThumbnailCache thumbnails, Action<string, bool> toast)
    {
        _view = view;
        _app = app;
        _thumbnails = thumbnails;
        Preview = app.CreatePreview();
        _properties = new ShaderPropertiesPanel(view, app);
        _properties.ValuesChanged += _ => RestartPictureTimer();
        _web = new WebPanel(app, view.WebBlock, toast);
        _pictureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PictureRefreshMs) };
        _pictureTimer.Tick += (_, _) => RefreshPicture();
        _previewPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(PreviewPollMs) };
        _previewPoll.Tick += (_, _) => UpdatePreviewVisibility();

        _previewHost.Ready += hwnd =>
        {
            Preview.Attach(hwnd);
            ShowPreview();
        };
        view.PreviewSlot.Child = _previewHost;
        view.Picker.SelectionChanged += UpdateApplyButton;
        thumbnails.Updated += OnPictureUpdated;
    }

    public LivePreview Preview { get; }

    /// <summary>The displays picked for "per display".</summary>
    public IReadOnlyCollection<string> PickedDisplays => _view.Picker.Selected;

    private string? Path => _item?.Path;

    private bool IsWeb => _item != null && _item.IsWeb;

    /// <summary>Shows another item (null: nothing selected).</summary>
    public void Show(ShaderItem? item)
    {
        _properties.Flush();
        _item = item;
        ShowPreview();
        RefreshHeader();
        RefreshError();
        _view.PropsBlock.Visibility = item != null && !item.IsWeb ? Visibility.Visible : Visibility.Collapsed;
        _properties.Show(item != null && !item.IsWeb ? item.Path : null);
        _web.Show(Path);
        RefreshFitNote();
        RefreshDisplays();
    }

    /// <summary>What is live, or the mode, changed.</summary>
    public void OnWallpaperChanged()
    {
        RefreshHeader();
        RefreshDisplays();
        _web.Show(Path);
    }

    /// <summary>The selected shader runs with other settings now (light settings, full quality).</summary>
    public void OnQualityChanged() => RefreshFitNote();

    /// <summary>A shader started or finished compiling, or a web page started or failed.</summary>
    public void OnStatusChanged(string path)
    {
        if (!string.Equals(path, Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        RefreshError();
        if (IsWeb)
        {
            _web.Show(path);
        }
    }

    public void OnWebChanged() => _web.Show(Path);

    /// <summary>Something lies over the detail pane (settings, a dialog, a drop) or the window is minimised.</summary>
    public void SetCovered(bool covered)
    {
        _covered = covered;
        UpdatePreviewVisibility();
    }

    public void Close()
    {
        _pictureTimer.Stop();
        _previewPoll.Stop();
        _properties.Flush();
        _thumbnails.Updated -= OnPictureUpdated;
        Preview.Dispose();
    }

    private void ShowPreview()
    {
        var dpi = VisualTreeHelper.GetDpi(_view);
        double pixels = _view.PreviewSlot.ActualWidth * dpi.DpiScaleX * _view.PreviewSlot.ActualHeight * dpi.DpiScaleY;
        Preview.Show(_item != null && !_item.IsWeb ? _item.Path : null, pixels);
        UpdatePreviewVisibility();
    }

    private void RefreshHeader()
    {
        bool has = _item != null;
        _view.ApplyBtn.IsEnabled = has;
        _view.TermBtn.IsEnabled = has;
        _view.EditBtn.IsEnabled = has;
        _view.DeleteBtn.IsEnabled = has;
        if (_item == null)
        {
            _view.NameText.Text = "Nothing selected";
            _view.FileText.Text = "Pick a wallpaper from the library";
            _view.PreviewStill.Background = null;
            return;
        }
        _view.NameText.Text = _item.Name;
        _view.FileText.Text = System.IO.Path.GetFileName(_item.Path) + LiveText(_item.Path);
        ShowStill(_item);
    }

    private string LiveText(string path)
    {
        var displays = _app.Displays;
        var live = displays.Where(display => string.Equals(display.ItemPath, path, StringComparison.OrdinalIgnoreCase))
            .Select(display => (display.Index + 1).ToString())
            .ToList();
        if (live.Count == 0)
        {
            return "";
        }
        if (displays.Count == 1)
        {
            return "  ·  Live on your desktop";
        }
        return live.Count == displays.Count ? "  ·  Live on all displays" : "  ·  Live on display " + string.Join(" & ", live);
    }

    private void ShowStill(ShaderItem item) =>
        _view.PreviewStill.Background = item.Thumb == null ? null : new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill };

    private void OnPictureUpdated(ShaderItem item)
    {
        if (ReferenceEquals(item, _item))
        {
            ShowStill(item);
        }
    }

    private void RefreshError()
    {
        string? error = _app.Status.ErrorOf(Path);
        _view.ErrorTitle.Text = IsWeb ? "This page could not be shown" : "Compile error";
        _view.ErrorText.Text = error == null ? "" : Shorten(error);
        _view.ErrorCard.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
        _view.PreviewError.Visibility = error != null && !IsWeb ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreviewVisibility();
    }

    /// <summary>The compiler names the full path on every line; the file name says as much.</summary>
    private string Shorten(string error)
    {
        string text = error.Length > MaxErrorLength ? error.Substring(0, MaxErrorLength) + "…" : error;
        return Path == null ? text : text.Replace(Path, System.IO.Path.GetFileName(Path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The preview is a child window (always on top of WPF content), so it is hidden whenever something must cover it,
    /// and while its shader is still compiling: then the card's picture shows with a "Compiling" note.
    /// Web wallpapers have no live preview at all.
    /// </summary>
    private void UpdatePreviewVisibility()
    {
        bool hasShader = _item != null && !_item.IsWeb && !_app.Status.HasError(_item.Path);
        bool compiling = hasShader && !Preview.Shows(_item!.Path);
        bool show = hasShader && !compiling && !_covered;
        _previewHost.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        _view.PreviewBusy.Visibility = compiling && _app.Status.IsCompiling(_item!.Path) ? Visibility.Visible : Visibility.Collapsed;
        Preview.SetVisible(hasShader && !_covered);
        // the first frame of the newly selected shader is on its way: look again shortly
        _previewPoll.IsEnabled = compiling && !_covered;
    }

    /// <summary>Says when the fitter runs this shader with its light settings (or would), and lets the user overrule it.</summary>
    private void RefreshFitNote()
    {
        _view.FitNote.Children.Clear();
        string? path = IsWeb ? null : Path;
        bool light = path != null && _app.Shaders.IsLight(path);
        bool full = path != null && _app.Shaders.LightSettings(path).Count > 0 && _app.Shaders.WantsFullQuality(path);
        _view.FitNote.Visibility = light || full ? Visibility.Visible : Visibility.Collapsed;
        if (path == null || (!light && !full))
        {
            return;
        }
        TextBlock text = SettingsRows.Hint(
            light ? "Light settings applied to fit this PC" : "Full quality by your choice (heavy for this PC)", new Thickness(0));
        text.VerticalAlignment = VerticalAlignment.Center;
        if (light)
        {
            text.ToolTip = "At its normal settings this shader needs more GPU time than the resource level allows: "
                + string.Join(", ", _app.Shaders.LightSettings(path).Select(pair => pair.Key + " = " + pair.Value));
        }
        var button = new Button
        {
            Style = Theme.Style("Ghost"),
            Content = light ? "Use full quality anyway" : "Fit to this PC",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        button.Click += (_, _) => _app.Shaders.SetFullQuality(path, light);
        var row = new Grid { Margin = new Thickness(14, 8, 8, 8) };
        row.Children.Add(text);
        row.Children.Add(button);
        _view.FitNote.Children.Add(new Border { Style = Theme.Style("Group"), Margin = new Thickness(0), Child = row });
    }

    /// <summary>The displays block: the mode buttons, the picker and the wording of the main button.</summary>
    private void RefreshDisplays()
    {
        var displays = _app.Displays;
        bool terminalOnly = _item != null && _item.TerminalOnly;
        DisplayMode mode = _app.Wallpaper.Mode;
        _view.DisplaysBlock.Visibility = displays.Count > 1 && _item != null && !terminalOnly ? Visibility.Visible : Visibility.Collapsed;
        _view.ModeDuplicate.IsChecked = mode == DisplayMode.Duplicate;
        _view.ModePer.IsChecked = mode == DisplayMode.PerDisplay;
        _view.ModeSpan.IsChecked = mode == DisplayMode.Span;
        _view.Picker.Interactive = mode == DisplayMode.PerDisplay;
        _view.Picker.SetMonitors(PickerMonitor.From(displays, _app.Library.Title));
        _view.PickerHint.Text = mode switch
        {
            DisplayMode.PerDisplay => "Click the displays this wallpaper should go to",
            DisplayMode.Span => "One image stretched across all displays",
            _ => "The same wallpaper on every display",
        };
        UpdateApplyButton();
    }

    private void UpdateApplyButton()
    {
        bool terminalOnly = _item != null && _item.TerminalOnly;
        _view.TermBtn.Visibility = terminalOnly || IsWeb ? Visibility.Collapsed : Visibility.Visible;
        var displays = _app.Displays;
        DisplayMode mode = _app.Wallpaper.Mode;
        if (terminalOnly)
        {
            _view.ApplyBtn.Content = "Use in Windows Terminal";
        }
        else if (displays.Count <= 1 || mode == DisplayMode.Duplicate)
        {
            _view.ApplyBtn.Content = "Set as wallpaper";
        }
        else if (mode == DisplayMode.Span)
        {
            _view.ApplyBtn.Content = "Span across all displays";
        }
        else
        {
            var picked = displays.Where(display => PickedDisplays.Contains(display.Id)).Select(display => (display.Index + 1).ToString()).ToList();
            _view.ApplyBtn.Content = picked.Count == displays.Count ? "Set on all displays" : "Set on display " + string.Join(" & ", picked);
        }
    }

    private void RestartPictureTimer()
    {
        _pictureTimer.Stop();
        _pictureTimer.Start();
    }

    /// <summary>A moment after the last change of a value: the card's picture and the shader's cost are made again.</summary>
    private void RefreshPicture()
    {
        _pictureTimer.Stop();
        if (_item != null && !_item.IsWeb)
        {
            _thumbnails.Refresh(_item);
            _app.RequestCost(_item.Path);
        }
    }
}
