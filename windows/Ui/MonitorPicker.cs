using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Wallpepper.Ui;

/// <summary>Scaled picture of the real display arrangement; click to pick one or several displays.</summary>
internal sealed class MonitorPicker : Canvas
{
    private const double TileGap = 4;
    private const double MinTileSize = 8;

    private static readonly Brush Fill = Frozen("#14FFFFFF");
    private static readonly Brush Edge = Frozen("#40FFFFFF");
    private static readonly Brush TextOn = Frozen("#F4F4F6");
    private static readonly Brush TextOff = Frozen("#B9B9C2");
    private static readonly Brush CaptionOff = Frozen("#84848F");

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PickerMonitor> _monitors = Array.Empty<PickerMonitor>();

    public MonitorPicker()
    {
        SizeChanged += (_, _) => Rebuild();
        ClipToBounds = false;
    }

    public event Action? SelectionChanged;

    /// <summary>Ids of the picked displays.</summary>
    public IReadOnlyCollection<string> Selected => _selected;

    /// <summary>false: every display is shown as selected and clicks do nothing (duplicate / span).</summary>
    public bool Interactive { get; set; } = true;

    public bool SingleSelect { get; set; }

    public void SetMonitors(IReadOnlyList<PickerMonitor> monitors)
    {
        _monitors = monitors;
        _selected.RemoveWhere(id => !monitors.Any(monitor => string.Equals(monitor.Id, id, StringComparison.OrdinalIgnoreCase)));
        if (_selected.Count == 0 && monitors.Count > 0)
        {
            _selected.Add((monitors.FirstOrDefault(monitor => monitor.Primary) ?? monitors[0]).Id);
        }
        Rebuild();
    }

    private static Brush Frozen(string colour)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(colour)!;
        brush.Freeze();
        return brush;
    }

    private void Rebuild()
    {
        Children.Clear();
        if (_monitors.Count == 0 || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }
        double left = _monitors.Min(monitor => monitor.Bounds.Left);
        double top = _monitors.Min(monitor => monitor.Bounds.Top);
        double width = Math.Max(1, _monitors.Max(monitor => monitor.Bounds.Right) - left);
        double height = Math.Max(1, _monitors.Max(monitor => monitor.Bounds.Bottom) - top);
        double scale = Math.Min(ActualWidth / width, ActualHeight / height);
        double offsetX = (ActualWidth - width * scale) / 2;
        double offsetY = (ActualHeight - height * scale) / 2;
        foreach (var monitor in _monitors)
        {
            Border tile = Tile(monitor, scale);
            SetLeft(tile, offsetX + (monitor.Bounds.Left - left) * scale + TileGap / 2);
            SetTop(tile, offsetY + (monitor.Bounds.Top - top) * scale + TileGap / 2);
            Children.Add(tile);
        }
    }

    private Border Tile(PickerMonitor monitor, double scale)
    {
        bool on = !Interactive || _selected.Contains(monitor.Id);
        double width = Math.Max(MinTileSize, monitor.Bounds.Width * scale - TileGap);
        double height = Math.Max(MinTileSize, monitor.Bounds.Height * scale - TileGap);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = (monitor.Index + 1).ToString(),
            FontSize = Math.Clamp(height * 0.34, 12, 26),
            FontWeight = FontWeights.SemiBold,
            Foreground = on ? TextOn : TextOff,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(monitor.Caption) && height >= 50 && width >= 70)
        {
            text.Children.Add(new TextBlock
            {
                Text = monitor.Caption,
                FontSize = 10.5,
                Foreground = on ? TextOff : CaptionOff,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = width - 10,
            });
        }
        var tile = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(7),
            Background = on ? Theme.Brush("AccentSoft") : Fill,
            BorderBrush = on ? Theme.Brush("Accent") : Edge,
            BorderThickness = new Thickness(on ? 1.5 : 1),
            Child = text,
            Cursor = Interactive ? Cursors.Hand : Cursors.Arrow,
            ToolTip = $"Display {monitor.Index + 1}{(monitor.Primary ? " (main)" : "")} · {monitor.Bounds.Width:0} × {monitor.Bounds.Height:0}",
            SnapsToDevicePixels = true,
        };
        tile.MouseLeftButtonDown += (_, e) => OnTileClicked(monitor.Id, e);
        return tile;
    }

    private void OnTileClicked(string id, MouseButtonEventArgs e)
    {
        if (!Interactive)
        {
            return;
        }
        e.Handled = true;
        if (SingleSelect)
        {
            _selected.Clear();
            _selected.Add(id);
        }
        else if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }
        else if (_selected.Count == 0)
        {
            _selected.Add(id);   // at least one display stays picked
        }
        // rebuild after the click has finished (the tile raising this event is replaced)
        Dispatcher.InvokeAsync(
            () =>
            {
                Rebuild();
                SelectionChanged?.Invoke();
            },
            DispatcherPriority.Input);
    }
}
