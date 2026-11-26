using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wallpepper.Web;

namespace Wallpepper.Ui;

/// <summary>Card picture for a web wallpaper: name, host and a row of equaliser bars derived from the address.</summary>
internal static class WebThumbnail
{
    private const int Width = 480;
    private const int Height = 270;
    private const int Bars = 28;
    private const double LeftMargin = 34;

    public static ImageSource Render(WebInfo? info, string name)
    {
        string host = info != null && info.Error == null ? info.Host : "";
        uint seed = Hash(info?.Url ?? name);
        Color accent = Theme.Color("AccentColor");
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            var backdrop = new LinearGradientBrush(Color.FromRgb(0x14, 0x16, 0x26), Color.FromRgb(0x36, 0x1A, 0x48), 35);
            context.DrawRectangle(backdrop, null, new Rect(0, 0, Width, Height));
            var glow = new RadialGradientBrush(Color.FromArgb(0x70, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
            context.DrawEllipse(glow, null, new Point(Width * 0.8, Height * 0.15), 230, 230);

            var bar = new LinearGradientBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B), Color.FromArgb(0x40, 0xFF, 0x4F, 0x8B), 90);
            for (int i = 0; i < Bars; i++)
            {
                seed = seed * 1664525 + 1013904223;
                double height = 14 + (seed >> 24) / 255.0 * 84 * (0.45 + 0.55 * Math.Sin(i / (Bars - 1.0) * Math.PI));
                context.DrawRoundedRectangle(bar, null, new Rect(LeftMargin + i * 14.9, Height - 30 - height, 9, height), 3, 3);
            }
            context.DrawText(Text(Glyphs.Browser, Theme.Font("Icons"), 26, Theme.Brush("Accent"), FontWeights.Normal), new Point(LeftMargin, 30));
            context.DrawText(Text(name, Theme.Font("UiDisplay"), 30, Brushes.White, FontWeights.SemiBold), new Point(LeftMargin, 68));
            context.DrawText(Text(host, Theme.Font("Ui"), 15, Theme.Brush("Text2"), FontWeights.Normal), new Point(LeftMargin + 1, 110));
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static FormattedText Text(string text, FontFamily font, double size, Brush brush, FontWeight weight)
    {
        var typeface = new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0)
        {
            MaxTextWidth = Width - 2 * LeftMargin,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }

    /// <summary>FNV-1a: the same picture for the same address in every run (string.GetHashCode is not stable).</summary>
    private static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }
}
