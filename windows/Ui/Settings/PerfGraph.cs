using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace Wallpepper.Ui.Settings;

/// <summary>One display's row on the performance page: a readout and the GPU time per frame over the last minute.</summary>
internal sealed class PerfGraph
{
    private const int Samples = 60;
    private const double ChartHeight = 30;
    private const double ChartPadding = 3;

    private readonly Queue<float> _history = new Queue<float>();
    private readonly TextBlock _readout = SettingsRows.Value();
    private readonly Shapes.Polyline _line = new Shapes.Polyline
    {
        Stroke = Theme.Brush("Accent"),
        StrokeThickness = 1.5,
        StrokeLineJoin = PenLineJoin.Round,
        SnapsToDevicePixels = false,
    };

    private readonly Border _chart;

    public PerfGraph(string title)
    {
        _readout.FontSize = 11.5;
        _chart = new Border
        {
            Height = ChartHeight,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.Brush("Well"),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = new Canvas { Children = { _line } },
            ToolTip = "GPU time per frame over the last minute. The percentage is the share of GPU time Wallpepper uses on this display; "
                + "an idle GPU clocks itself down, so light loads look larger than they are.",
        };
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = title, Style = Theme.Style("RowLabel") });
        top.Children.Add(_readout);
        var row = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        row.Children.Add(top);
        row.Children.Add(_chart);
        Row = row;
    }

    public UIElement Row { get; }

    /// <summary>Adds the latest GPU time; the history is kept also while the page is not visible.</summary>
    public void Add(float gpuMs)
    {
        _history.Enqueue(gpuMs);
        while (_history.Count > Samples)
        {
            _history.Dequeue();
        }
    }

    /// <summary>Shows the readout and redraws the line (only needed while the page is visible).</summary>
    public void Show(string readout)
    {
        _readout.Text = readout;
        double width = _chart.ActualWidth;
        float max = Math.Max(1f, _history.Max());
        var points = new PointCollection(_history.Count);
        int index = 0;
        foreach (float value in _history)
        {
            double x = width - (_history.Count - 1 - index) * (width / (Samples - 1));
            double y = ChartHeight - ChartPadding - value / max * (ChartHeight - 2 * ChartPadding);
            points.Add(new Point(x, y));
            index++;
        }
        _line.Points = points;
    }
}
