using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Wallpepper.Interop;

namespace Wallpepper.Ui;

/// <summary>The small "Done" pill shown at the top of a web wallpaper while it is lifted in front for interaction.</summary>
internal sealed class InteractPill : Window
{
    private const int TopMargin = 8;

    private readonly System.Drawing.Rectangle _screen;

    /// <param name="owner">the page's window (the pill stays in front of it)</param>
    /// <param name="screen">the display to show the pill on, in device pixels</param>
    public InteractPill(IntPtr owner, System.Drawing.Rectangle screen, Action done)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        Foreground = Theme.Brush("Text");
        FontFamily = Theme.Font("Ui");
        FontSize = 13;
        new WindowInteropHelper(this).Owner = owner;

        var button = new Button
        {
            Style = Theme.Style("Primary"),
            Content = "Done",
            Height = 30,
            MinWidth = 76,
            Margin = new Thickness(14, 0, 0, 0),
        };
        button.Click += (_, _) => done();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "Interacting with the wallpaper", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = "Esc",
            Foreground = Theme.Brush("Text3"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });
        row.Children.Add(button);
        Content = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(18, 8, 8, 8),
            CornerRadius = new CornerRadius(23),
            Background = Theme.Brush("Flyout"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            Child = row,
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                done();
            }
        };
        ContentRendered += (_, _) => MoveToTopCentre();
    }

    /// <summary>
    /// Top centre of the display, in device pixels (WPF's own Left/Top are unreliable across displays with different scaling).
    /// </summary>
    private void MoveToTopCentre()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !User32.GetWindowRect(hwnd, out var rect))
        {
            return;
        }
        User32.SetWindowPos(
            hwnd, IntPtr.Zero, _screen.X + (_screen.Width - rect.Width) / 2, _screen.Y + TopMargin, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }
}
