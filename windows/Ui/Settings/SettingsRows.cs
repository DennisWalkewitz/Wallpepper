using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wallpepper.Ui.Settings;

/// <summary>The building blocks of the settings pages and the inspector: captions, groups, rows with a control.</summary>
internal static class SettingsRows
{
    public static TextBlock Caption(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = Theme.Style("Caption") };

    public static TextBlock Hint(string text, Thickness margin) => new TextBlock { Text = text, Style = Theme.Style("RowHint"), Margin = margin };

    /// <summary>Right-aligned small text in the monospace font (values, readouts).</summary>
    public static TextBlock Value(string text = "") => new TextBlock
    {
        Text = text,
        FontFamily = Theme.Font("Mono"),
        FontSize = 12,
        Foreground = Theme.Brush("Text2"),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rounded card with its rows separated by hairlines; null rows are left out.</summary>
    public static Border Group(params UIElement?[] rows)
    {
        var panel = new StackPanel();
        foreach (UIElement row in rows.OfType<UIElement>())
        {
            if (panel.Children.Count > 0)
            {
                panel.Children.Add(new Border { Style = Theme.Style("Hairline") });
            }
            panel.Children.Add(row);
        }
        return new Border { Style = Theme.Style("Group"), Child = panel };
    }

    public static Grid Row(string label, string? hint, UIElement? right)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = label, Style = Theme.Style("RowLabel") });
        if (hint != null)
        {
            text.Children.Add(new TextBlock { Text = hint, Style = Theme.Style("RowHint") });
        }
        var row = new Grid { Margin = new Thickness(14, 10, 14, 10), MinHeight = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        if (right != null)
        {
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
        }
        return row;
    }

    public static Grid SwitchRow(string label, string? hint, bool value, Action<bool> set)
    {
        var toggle = new CheckBox { Style = Theme.Style("Switch"), IsChecked = value };
        toggle.Click += (_, _) => set(toggle.IsChecked == true);
        return Row(label, hint, toggle);
    }

    /// <summary>A row of mutually exclusive buttons.</summary>
    public static Border Segments(IReadOnlyList<string> names, int selected, Action<int> set)
    {
        var grid = new UniformGrid { Rows = 1 };
        string group = "segments-" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < names.Count; i++)
        {
            int index = i;
            var button = new RadioButton { Style = Theme.Style("Seg"), Content = names[i], GroupName = group, IsChecked = i == selected };
            button.Click += (_, _) => set(index);
            grid.Children.Add(button);
        }
        return new Border { Style = Theme.Style("SegHost"), Child = grid };
    }

    public static StackPanel SegmentRow(string label, IReadOnlyList<string> names, int selected, Action<int> set)
    {
        var row = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        row.Children.Add(new TextBlock { Text = label, Style = Theme.Style("RowLabel"), Margin = new Thickness(0, 0, 0, 8) });
        row.Children.Add(Segments(names, selected, set));
        return row;
    }

    /// <summary>A label, the formatted value and a slider below. Double-click puts the neutral value back.</summary>
    public static StackPanel SliderRow(
        string label, double min, double max, double value, double neutral, Func<double, string> format, Action<double> set)
    {
        TextBlock shown = Value(format(value));
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Margin = new Thickness(0, 6, 0, 0),
            SmallChange = (max - min) / 100,
            LargeChange = (max - min) / 10,
        };
        slider.ValueChanged += (_, _) =>
        {
            shown.Text = format(slider.Value);
            set(slider.Value);
        };
        slider.MouseDoubleClick += (_, _) => slider.Value = neutral;
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = label, Style = Theme.Style("RowLabel") });
        top.Children.Add(shown);
        var row = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
        row.Children.Add(top);
        row.Children.Add(slider);
        return row;
    }

    /// <summary>Index of the value closest to <paramref name="wanted"/>.</summary>
    public static int Nearest(IReadOnlyList<int> values, double wanted)
    {
        int best = 0;
        for (int i = 1; i < values.Count; i++)
        {
            if (Math.Abs(values[i] - wanted) < Math.Abs(values[best] - wanted))
            {
                best = i;
            }
        }
        return best;
    }
}
