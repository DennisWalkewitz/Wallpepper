using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpepper.Desktop;
using Wallpepper.Interop;

namespace Wallpepper.Ui;

/// <summary>Big number flashed on each display ("Identify").</summary>
internal sealed class IdentifyWindow : Window
{
    private const double ShownSeconds = 1.8;
    private const int ScreenMargin = 48;

    private IdentifyWindow(int number)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Width = 200;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        Content = new Border
        {
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Theme.Color("WindowColor", 0xEE)),
            BorderBrush = Theme.Brush("Accent"),
            BorderThickness = new Thickness(3),
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 120,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -12, 0, 0),
                FontFamily = Theme.Font("UiDisplay"),
            },
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ShownSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    public static void ShowAll(IEnumerable<DisplaySlot> displays)
    {
        foreach (var display in displays)
        {
            var bounds = display.Screen.Bounds;
            var window = new IdentifyWindow(display.Index + 1);
            window.Show();
            // device pixels: WPF's own Left/Top are unreliable across displays with different scaling
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            User32.SetWindowPos(
                hwnd, IntPtr.Zero, bounds.X + ScreenMargin, bounds.Y + ScreenMargin, 0, 0,
                User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
    }
}
