using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Wallpepper.Ui;

/// <summary>Small fixed-size acrylic window with the app's look (setup, confirmations).</summary>
internal abstract class AppDialog : Window
{
    private const byte TintAlpha = 0xB8;

    private readonly Border _tint = new Border { Background = new SolidColorBrush(Theme.Color("WindowColor", TintAlpha)) };

    protected AppDialog(string title, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        Foreground = Theme.Brush("Text");
        FontFamily = Theme.Font("Ui");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Icon = Assets.WindowIcon;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 36,
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        var close = new Button
        {
            Style = Theme.Style("CaptionBtn"),
            Content = Glyphs.Close,
            Tag = "#E81123",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        Root.Children.Add(_tint);
        Root.Children.Add(Body);
        Root.Children.Add(close);
        Content = Root;
        PreviewKeyDown += OnKeyDown;
    }

    protected Grid Root { get; } = new Grid();

    protected StackPanel Body { get; } = new StackPanel { Margin = new Thickness(28, 26, 28, 24) };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!Chrome.Apply(this))
        {
            _tint.Background = Theme.Brush("Solid");   // Windows 10, or transparency effects off: solid dark
        }
    }

    protected static StackPanel Header(string title, string? subtitle)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Theme.Font("UiDisplay"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        if (subtitle != null)
        {
            text.Children.Add(new TextBlock { Text = subtitle, Foreground = Theme.Brush("Text2"), Margin = new Thickness(1, 3, 0, 0) });
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image
        {
            Source = Theme.Image("Logo"),
            Width = 56,
            Height = 56,
            Margin = new Thickness(-6, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
        });
        row.Children.Add(text);
        return row;
    }

    protected static TextBlock Paragraph(string text) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Theme.Brush("Text2"),
        LineHeight = 19,
        Margin = new Thickness(0, 16, 0, 0),
    };

    /// <summary>A row with a label, a hint below it and a switch on the right, in a rounded group.</summary>
    protected static Border SwitchGroup(string label, string hint, CheckBox toggle)
    {
        var text = new StackPanel { Margin = new Thickness(0, 0, 56, 0) };
        text.Children.Add(new TextBlock { Text = label, Style = Theme.Style("RowLabel") });
        text.Children.Add(new TextBlock { Text = hint, Style = Theme.Style("RowHint") });
        toggle.Style = Theme.Style("Switch");
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        row.Children.Add(text);
        row.Children.Add(toggle);
        return new Border { Style = Theme.Style("Group"), Child = row, Margin = new Thickness(0, 20, 0, 0) };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
