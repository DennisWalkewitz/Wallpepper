using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Wallpepper.Settings;

namespace Wallpepper.Ui.Settings;

/// <summary>Settings > Filters: brightness, contrast, blur and so on, for all displays or one; and dim on idle.</summary>
internal sealed class FiltersPage
{
    private static readonly int[] IdleSecondChoices = { 30, 60, 120, 300 };

    private readonly App _app;
    private readonly StackPanel _host;

    /// <summary>null = all displays, else the display the sliders apply to.</summary>
    private string? _displayId;

    public FiltersPage(App app, StackPanel host)
    {
        _app = app;
        _host = host;
    }

    private Config Config => _app.Settings.Config;

    public void Build()
    {
        _host.Children.Clear();
        var displays = _app.Displays;
        if (_displayId != null && displays.All(display => display.Id != _displayId))
        {
            _displayId = null;
        }
        if (displays.Count > 1)
        {
            var names = new[] { "All displays" }.Concat(displays.Select(display => "Display " + (display.Index + 1))).ToList();
            int selected = _displayId == null ? 0 : 1 + displays.ToList().FindIndex(display => display.Id == _displayId);
            Border target = SettingsRows.Segments(names, selected, index =>
            {
                _displayId = index == 0 ? null : displays[index - 1].Id;
                Build();
            });
            target.Margin = new Thickness(0, 0, 0, 20);
            _host.Children.Add(SettingsRows.Caption("Apply to"));
            _host.Children.Add(target);
        }

        string? id = _displayId;
        MonitorSettings? monitor = null;
        if (id != null)
        {
            Config.Monitors.TryGetValue(id, out monitor);
            monitor ??= new MonitorSettings();
            _host.Children.Add(SettingsRows.Group(SettingsRows.SwitchRow(
                "Own filters for this display", "Off: this display follows “All displays”.", monitor.CustomFilters,
                on =>
                {
                    _app.Settings.Change(SettingsArea.Displays, config => config.GetOrAddMonitor(id).CustomFilters = on);
                    Build();
                })));
        }
        AddPicture(id, monitor?.Filters ?? Config.Filters, enabled: monitor == null || monitor.CustomFilters);
        if (id == null)
        {
            AddIdleDim();
        }
    }

    private void AddPicture(string? displayId, FilterSettings shown, bool enabled)
    {
        var reset = new Button
        {
            Style = Theme.Style("Ghost"),
            Content = "Reset",
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, -4, 4),
            IsEnabled = enabled && !shown.IsNeutral(),
        };
        void Change(Action<FilterSettings> change)
        {
            _app.Settings.Change(SettingsArea.Displays, config =>
            {
                FilterSettings filters = displayId == null ? config.Filters : config.GetOrAddMonitor(displayId).Filters;
                change(filters);
                reset.IsEnabled = !filters.IsNeutral();
            });
        }
        reset.Click += (_, _) =>
        {
            Change(filters => filters.Reset());
            Build();
        };
        var head = new Grid();
        head.Children.Add(SettingsRows.Caption("Picture"));
        head.Children.Add(reset);
        _host.Children.Add(head);

        string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        string Temperature(double value)
        {
            if (Math.Abs(value) < 0.005)
            {
                return "neutral";
            }
            string amount = Math.Abs(value * 100).ToString("0", CultureInfo.InvariantCulture);
            return value < 0 ? "cool -" + amount : "warm +" + amount;
        }

        Border picture = SettingsRows.Group(
            SettingsRows.SliderRow("Brightness", 0.2, 1.5, shown.Brightness, 1, Percent, value => Change(filters => filters.Brightness = (float)value)),
            SettingsRows.SliderRow("Contrast", 0.5, 1.5, shown.Contrast, 1, Percent, value => Change(filters => filters.Contrast = (float)value)),
            SettingsRows.SliderRow("Saturation", 0, 2, shown.Saturation, 1, Percent, value => Change(filters => filters.Saturation = (float)value)),
            SettingsRows.SliderRow(
                "Colour temperature", -1, 1, shown.Temperature, 0, Temperature,
                value => Change(filters => filters.Temperature = Math.Abs(value) < 0.02 ? 0 : (float)value)),
            SettingsRows.SliderRow("Blur", 0, 1, shown.Blur, 0, Percent, value => Change(filters => filters.Blur = value < 0.01 ? 0 : (float)value)),
            SettingsRows.SliderRow("Vignette", 0, 1, shown.Vignette, 0, Percent, value => Change(filters => filters.Vignette = value < 0.01 ? 0 : (float)value)));
        picture.IsEnabled = enabled;
        picture.Opacity = enabled ? 1 : 0.45;
        _host.Children.Add(picture);
        _host.Children.Add(SettingsRows.Hint(
            "A little blur or dimming keeps desktop icons readable. With everything neutral the extra pass is skipped. "
            + "Double-click a slider to reset it. Filters apply to shaders, not to web wallpapers.",
            new Thickness(2, -12, 0, 20)));
    }

    private void AddIdleDim()
    {
        void Change(Action<Config> change) => _app.Settings.Change(SettingsArea.Governor, change);
        string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        _host.Children.Add(SettingsRows.Caption("Idle"));
        _host.Children.Add(SettingsRows.Group(
            SettingsRows.SwitchRow(
                "Dim when idle", "Fades the wallpaper down when you have not touched the PC for a while.", Config.DimOnIdle,
                on => Change(config => config.DimOnIdle = on)),
            SettingsRows.SegmentRow(
                "After", new[] { "30 s", "1 min", "2 min", "5 min" }, SettingsRows.Nearest(IdleSecondChoices, Config.IdleSeconds),
                index => Change(config => config.IdleSeconds = IdleSecondChoices[index])),
            SettingsRows.SliderRow("Dim by", 0.1, 0.9, Config.IdleDim, 0.6, Percent, value => Change(config => config.IdleDim = (float)value))));
    }
}
