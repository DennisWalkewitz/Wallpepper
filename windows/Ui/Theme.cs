using System.Windows;
using System.Windows.Media;

namespace Wallpepper.Ui;

/// <summary>Typed access to the resources of Theme.xaml, for the parts of the UI that are built in code.</summary>
internal static class Theme
{
    public static Style Style(string key) => (Style)Find(key);

    public static Brush Brush(string key) => (Brush)Find(key);

    public static Color Color(string key) => (Color)Find(key);

    public static FontFamily Font(string key) => (FontFamily)Find(key);

    public static ImageSource Image(string key) => (ImageSource)Find(key);

    private static object Find(string key)
    {
        UiHost.EnsureTheme();
        return Application.Current.FindResource(key);
    }

    /// <summary>A colour of the theme with another opacity.</summary>
    public static Color Color(string key, byte alpha)
    {
        Color color = Color(key);
        return System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
