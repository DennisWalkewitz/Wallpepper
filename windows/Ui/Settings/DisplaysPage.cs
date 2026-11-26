using System.Windows;
using System.Windows.Controls;
using Wallpepper.Settings;

namespace Wallpepper.Ui.Settings;

/// <summary>Settings > Displays: the arrangement, the mode, and frame rate / resolution / pause rule per display.</summary>
internal sealed class DisplaysPage
{
    private static readonly int[] FpsChoices = { 0, 15, 24, 30, 60 };
    private static readonly int[] ScaleChoices = { 0, 50, 75, 100 };
    private static readonly DisplayMode[] Modes = { DisplayMode.Duplicate, DisplayMode.PerDisplay, DisplayMode.Span };

    private readonly App _app;
    private readonly StackPanel _host;
    private readonly StackPanel _detail = new StackPanel();
    private MonitorPicker? _picker;

    public DisplaysPage(App app, StackPanel host)
    {
        _app = app;
        _host = host;
    }

    public void Build()
    {
        _host.Children.Clear();
        _picker = new MonitorPicker { Height = 120, SingleSelect = true, Margin = new Thickness(0, 4, 0, 0) };
        _picker.SelectionChanged += BuildDetail;
        var identify = new Button
        {
            Content = "Identify",
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = 30,
            Margin = new Thickness(0, 12, 0, 0),
            ToolTip = "Flash each display's number on screen",
        };
        identify.Click += (_, _) => IdentifyWindow.ShowAll(_app.Displays);
        var arrangement = new StackPanel { Margin = new Thickness(14, 14, 14, 14) };
        arrangement.Children.Add(_picker);
        arrangement.Children.Add(identify);
        _host.Children.Add(SettingsRows.Caption("Arrangement"));
        _host.Children.Add(SettingsRows.Group(arrangement));

        if (_app.Displays.Count > 1)
        {
            DisplayMode mode = _app.Wallpaper.Mode;
            _host.Children.Add(SettingsRows.Caption("Mode"));
            _host.Children.Add(SettingsRows.Segments(
                new[] { "Duplicate", "Per display", "Span" }, Math.Max(0, Array.IndexOf(Modes, mode)), index => _app.Wallpaper.SetMode(Modes[index])));
            string text = mode switch
            {
                DisplayMode.Span => "One image runs across all displays, continuous over the bezels. Shaders tagged Multi-monitor are made for this.",
                DisplayMode.PerDisplay => "Each display has its own shader. Pick displays in the inspector, then set a shader.",
                _ => "Every display shows the same shader.",
            };
            _host.Children.Add(SettingsRows.Hint(text, new Thickness(2, 8, 0, 20)));
        }
        _host.Children.Add(_detail);
        Refresh();
    }

    /// <summary>What the displays show changed.</summary>
    public void Refresh()
    {
        if (_picker == null)
        {
            return;
        }
        _picker.SetMonitors(PickerMonitor.From(_app.Displays, _app.Library.Title));
        BuildDetail();
    }

    private void BuildDetail()
    {
        _detail.Children.Clear();
        var display = _app.Displays.FirstOrDefault(candidate => _picker != null && _picker.Selected.Contains(candidate.Id));
        if (display == null)
        {
            return;
        }
        string id = display.Id;
        _app.Settings.Config.Monitors.TryGetValue(id, out MonitorSettings? saved);
        MonitorSettings monitor = saved ?? new MonitorSettings();
        void Change(SettingsArea area, Action<MonitorSettings> change) =>
            _app.Settings.Change(area, config => change(config.GetOrAddMonitor(id)));

        var bounds = display.Screen.Bounds;
        var wallpaper = new TextBlock
        {
            Text = display.ItemPath == null ? "none" : _app.Library.Title(display.ItemPath),
            Foreground = Theme.Brush("Text2"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
        };
        int scaleIndex = monitor.RenderScale <= 0 ? 0 : 1 + SettingsRows.Nearest(ScaleChoices.Skip(1).ToList(), monitor.RenderScale * 100);
        _detail.Children.Add(SettingsRows.Caption(
            $"Display {display.Index + 1}  ·  {bounds.Width} × {bounds.Height}  ·  {display.Target.Settings.RefreshHz} Hz"));
        _detail.Children.Add(SettingsRows.Group(
            SettingsRows.Row("Wallpaper", null, wallpaper),
            SettingsRows.SegmentRow(
                "Frame rate limit", new[] { "Default", "15", "24", "30", "60" }, Math.Max(0, Array.IndexOf(FpsChoices, monitor.Fps)),
                index => Change(SettingsArea.Governor, settings => settings.Fps = FpsChoices[index])),
            SettingsRows.SegmentRow(
                "Render resolution", new[] { "Default", "50%", "75%", "100%" }, scaleIndex,
                index => Change(SettingsArea.Governor, settings => settings.RenderScale = ScaleChoices[index] / 100f)),
            SettingsRows.SwitchRow(
                "Keep running when covered", "Ignore the pause rule for full-screen windows on this display.", monitor.KeepRunningWhenCovered,
                on => Change(SettingsArea.Governor, settings => settings.KeepRunningWhenCovered = on))));
        _detail.Children.Add(SettingsRows.Hint("Filters per display are on the Filters tab.", new Thickness(2, -12, 0, 0)));
    }
}
