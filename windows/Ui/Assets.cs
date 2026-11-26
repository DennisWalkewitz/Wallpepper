using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wallpepper.Settings;

namespace Wallpepper.Ui;

/// <summary>Pictures that travel inside the exe.</summary>
internal static class Assets
{
    public const string IconResource = "app.ico";

    private static ImageSource? _windowIcon;

    public static Stream OpenIcon() =>
        typeof(Assets).Assembly.GetManifestResourceStream(IconResource)
        ?? throw new InvalidOperationException("This build does not contain " + IconResource + ".");

    /// <summary>The largest frame of the app icon (title bars, Alt+Tab); null when it cannot be read.</summary>
    public static ImageSource? WindowIcon
    {
        get
        {
            if (_windowIcon != null)
            {
                return _windowIcon;
            }
            try
            {
                using Stream stream = OpenIcon();
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                best.Freeze();
                _windowIcon = best;
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or IOException)
            {
                Log.Warn("read the window icon", e);
            }
            return _windowIcon;
        }
    }
}
