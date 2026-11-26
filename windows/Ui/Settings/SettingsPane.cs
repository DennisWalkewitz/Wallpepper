using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Wallpepper.Ui.Settings;

/// <summary>The settings slide-over of the library window: opening and closing, the tabs, and the four pages.</summary>
internal sealed class SettingsPane
{
    private const double SlideDistance = 56;

    private readonly LibraryWindow _view;
    private readonly PerformancePage _performance;
    private readonly DisplaysPage _displays;
    private readonly FiltersPage _filters;
    private readonly GeneralPage _general;

    public SettingsPane(LibraryWindow view, App app, Action<string, bool> toast)
    {
        _view = view;
        _performance = new PerformancePage(app, view.PagePerf, toast);
        _displays = new DisplaysPage(app, view.PageDisplays);
        _filters = new FiltersPage(app, view.PageFilters);
        _general = new GeneralPage(app, view.PageGeneral, view, toast);
    }

    /// <summary>The pane opened or closed (the preview is hidden behind it).</summary>
    public event Action? OpenChanged;

    public bool IsOpen { get; private set; }

    public void Build()
    {
        _performance.Build();
        _displays.Build();
        _filters.Build();
        _general.Build();
    }

    /// <summary>The displays were re-attached: everything per display is built again.</summary>
    public void OnDisplaysChanged()
    {
        _performance.Build();
        _displays.Build();
        _filters.Build();
    }

    /// <summary>What is live, or the mode, changed.</summary>
    public void OnWallpaperChanged() => _displays.Build();

    public void OnUpdateChanged() => _general.RefreshUpdate();

    /// <summary>Once a second. Returns the readout for the status line.</summary>
    public string Tick() => _performance.Tick(IsOpen && _view.PagePerf.Visibility == Visibility.Visible);

    public void Close() => _performance.Benchmark.Close();

    public void Toggle() => Show(!IsOpen);

    public void Show(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }
        IsOpen = open;
        _view.SettingsBtn.Foreground = Theme.Brush(open ? "Accent" : "Text");
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = (TranslateTransform)_view.SettingsPane.RenderTransform;
        if (open)
        {
            OpenChanged?.Invoke();
            _view.SettingsPane.Visibility = Visibility.Visible;
            _view.SettingsPane.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideDistance, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) =>
            {
                if (IsOpen)
                {
                    _view.DetailPane.Visibility = Visibility.Hidden;
                }
            };
            _view.DetailPane.BeginAnimation(UIElement.OpacityProperty, fade);
            Tick();
        }
        else
        {
            _view.DetailPane.Visibility = Visibility.Visible;
            _view.DetailPane.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) =>
            {
                if (!IsOpen)
                {
                    _view.SettingsPane.Visibility = Visibility.Collapsed;
                    OpenChanged?.Invoke();   // only now the preview may show again
                }
            };
            _view.SettingsPane.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideDistance, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }
    }

    /// <summary>A tab was clicked: its page shows.</summary>
    public void ShowSelectedTab()
    {
        _view.PagePerf.Visibility = Visible(_view.TabPerf.IsChecked);
        _view.PageDisplays.Visibility = Visible(_view.TabDisplays.IsChecked);
        _view.PageFilters.Visibility = Visible(_view.TabFilters.IsChecked);
        _view.PageGeneral.Visibility = Visible(_view.TabGeneral.IsChecked);
        _view.SettingsScroll.ScrollToTop();
    }

    /// <summary>Opens the performance page and scrolls to the benchmark (tray menu).</summary>
    public void ShowBenchmark()
    {
        Show(true);
        _view.TabPerf.IsChecked = true;
        ShowSelectedTab();
        _view.Dispatcher.InvokeAsync(_view.SettingsScroll.ScrollToEnd, DispatcherPriority.Loaded);
    }

    /// <summary>The detail pane is covered while the settings are open or still fading out.</summary>
    public bool CoversDetail => IsOpen || _view.SettingsPane.Visibility == Visibility.Visible;

    private static Visibility Visible(bool? isChecked) => isChecked == true ? Visibility.Visible : Visibility.Collapsed;
}
