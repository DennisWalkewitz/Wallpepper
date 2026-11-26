using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Wallpepper.Ui;

/// <summary>Small flyout colour picker: hue / saturation / brightness sliders and a hex field.</summary>
internal static class ColorPicker
{
    private const double SwatchWidth = 44;
    private const double SwatchHeight = 24;

    /// <summary>The button that shows a colour and opens the picker.</summary>
    public static Button Swatch(Color color) => new Button
    {
        Width = SwatchWidth,
        Height = SwatchHeight,
        Padding = new Thickness(0),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "Choose a colour",
        Content = new Border
        {
            Width = SwatchWidth,
            Height = SwatchHeight,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        },
    };

    public static void SetSwatch(Button swatch, Color color) => ((Border)swatch.Content).Background = new SolidColorBrush(color);

    /// <summary>Opens the picker below <paramref name="target"/>; <paramref name="changed"/> is called for every change.</summary>
    public static void Show(FrameworkElement target, Color initial, Action<Color> changed)
    {
        ToHsv(initial, out double hue, out double saturation, out double value);
        bool updating = false;
        var preview = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(initial),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var hex = new TextBox
        {
            Text = Hex(initial),
            Width = 96,
            Height = 30,
            FontFamily = Theme.Font("Mono"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var panel = new StackPanel { Width = 220 };
        panel.Children.Add(preview);
        Slider hueSlider = AddSlider(panel, "Hue", 360, hue);
        Slider saturationSlider = AddSlider(panel, "Saturation", 1, saturation);
        Slider valueSlider = AddSlider(panel, "Brightness", 1, value);
        panel.Children.Add(hex);

        void Push(Color color, bool fromHex)
        {
            preview.Background = new SolidColorBrush(color);
            if (!fromHex)
            {
                updating = true;
                hex.Text = Hex(color);
                updating = false;
            }
            changed(color);
        }

        void OnSlide(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!updating)
            {
                Push(FromHsv(hueSlider.Value, saturationSlider.Value, valueSlider.Value), fromHex: false);
            }
        }

        hueSlider.ValueChanged += OnSlide;
        saturationSlider.ValueChanged += OnSlide;
        valueSlider.ValueChanged += OnSlide;
        hex.TextChanged += (_, _) =>
        {
            if (updating || !TryParse(hex.Text, out Color typed))
            {
                return;
            }
            ToHsv(typed, out double typedHue, out double typedSaturation, out double typedValue);
            updating = true;
            hueSlider.Value = typedHue;
            saturationSlider.Value = typedSaturation;
            valueSlider.Value = typedValue;
            updating = false;
            Push(typed, fromHex: true);
        };

        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -150,
            VerticalOffset = 2,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Margin = new Thickness(14),
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(12),
                Background = Theme.Brush("Flyout"),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = panel,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black },
            },
        };
        popup.IsOpen = true;
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Black;
        text = (text ?? "").Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return false;
        }
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static void ToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta + 6) % 6);
        }
        else if (max == g)
        {
            hue = 60 * ((b - r) / delta + 2);
        }
        else
        {
            hue = 60 * ((r - g) / delta + 4);
        }
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - chroma;
        (double r, double g, double b) = ((int)(hue / 60)) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static Slider AddSlider(Panel panel, string label, double max, double value)
    {
        var slider = new Slider { Minimum = 0, Maximum = max, Value = value };
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Theme.Brush("Text2"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        panel.Children.Add(row);
        return slider;
    }
}
