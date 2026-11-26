using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Ui;

/// <summary>
/// The gallery's pictures. A picture is rendered once per shader file and set of user values and kept as a PNG; from
/// then on it is only decoded, on a background thread. Rendering is the renderer's slow work (in slices between
/// wallpaper frames), so neither the wallpaper nor the window waits for pictures.
/// </summary>
internal sealed class ThumbnailCache
{
    private const int Width = 480;
    private const int Height = 270;
    private const float ShaderTime = 4.5f;
    private const string Version = "v3";

    private readonly App _app;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _closed;

    public ThumbnailCache(App app)
    {
        _app = app;
    }

    /// <summary>The picture of an item changed.</summary>
    public event Action<ShaderItem>? Updated;

    /// <summary>Pictures that are still being loaded or rendered (self-test).</summary>
    public int Pending => _pending.Count;

    /// <summary>Gives an item its picture: from the disk when it is there, otherwise it is rendered.</summary>
    public void Ensure(ShaderItem item) => Load(item, renderAgain: false);

    /// <summary>The user changed a shader's values: its picture is rendered again.</summary>
    public void Refresh(ShaderItem item) => Load(item, renderAgain: true);

    /// <summary>Removes the cached pictures of a deleted item.</summary>
    public static void Forget(string path)
    {
        string pattern = $"{Path.GetFileNameWithoutExtension(path)}-{Version}-*.png";
        Task.Run(() => Try.Run("remove cached pictures", () =>
        {
            if (Directory.Exists(AppPaths.ThumbnailDir))
            {
                foreach (string file in Directory.GetFiles(AppPaths.ThumbnailDir, pattern))
                {
                    File.Delete(file);
                }
            }
        }));
    }

    public void Close() => _closed = true;

    private void Load(ShaderItem item, bool renderAgain)
    {
        if (item.IsWeb)
        {
            // no rendering involved: a generated card picture
            Show(item, WebThumbnail.Render(_app.Library.Metadata(item.Path).Web, item.Name));
            return;
        }
        if (!_pending.Add(item.Path))
        {
            return;
        }
        if (item.Thumb == null)
        {
            item.IsLoading = true;
        }
        string path = item.Path;
        string settingsKey = _app.Shaders.OverridesKey(path);
        Task.Run(() =>
        {
            string file = FileFor(path, settingsKey);
            ImageSource? cached = renderAgain ? null : Decode(file);
            _ui.InvokeAsync(() =>
            {
                if (_closed)
                {
                    return;
                }
                if (cached != null)
                {
                    _pending.Remove(path);
                    Show(item, cached);
                }
                else
                {
                    _app.Thumbnails.RequestPicture(path, Width, Height, ShaderTime, picture => OnRendered(item, picture, file));
                }
            });
        });
    }

    /// <summary>Render thread: turns the pixels into a picture, hands it to the UI and saves it.</summary>
    private void OnRendered(ShaderItem item, ThumbnailPicture picture, string file)
    {
        BitmapSource? bitmap = null;
        if (picture.Pixels != null)
        {
            bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgr32, null, picture.Pixels, picture.Width * 4);
            bitmap.Freeze();
        }
        _ui.InvokeAsync(() =>
        {
            _pending.Remove(item.Path);
            if (_closed)
            {
                return;
            }
            if (bitmap != null)
            {
                Show(item, bitmap);
            }
            else
            {
                item.IsLoading = false;   // it does not compile: the card says so
            }
        });
        if (bitmap != null)
        {
            Task.Run(() => Save(bitmap, file, item.Path));
        }
    }

    private void Show(ShaderItem item, ImageSource picture)
    {
        item.Thumb = picture;
        item.IsLoading = false;
        Updated?.Invoke(item);
    }

    /// <summary>The picture's file: shader name, a format version, the file's time stamp and a hash of the user's values.</summary>
    private static string FileFor(string path, string settingsKey)
    {
        long stamp = Try.Get("read the time of " + Path.GetFileName(path), () => File.GetLastWriteTimeUtc(path).Ticks, 0L);
        uint hash = 2166136261;   // FNV-1a: stable across runs (string.GetHashCode is not)
        foreach (char c in settingsKey)
        {
            hash = (hash ^ c) * 16777619;
        }
        return Path.Combine(AppPaths.ThumbnailDir, $"{Path.GetFileNameWithoutExtension(path)}-{Version}-{stamp:X}-{hash:X8}.png");
    }

    private static ImageSource? Decode(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException)
        {
            Log.Warn("read the cached picture " + Path.GetFileName(file), e);
            return null;
        }
    }

    /// <summary>Writes the PNG and removes the pictures of this shader's earlier versions.</summary>
    private static void Save(BitmapSource bitmap, string file, string shaderPath)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ThumbnailDir);
            string pattern = $"{Path.GetFileNameWithoutExtension(shaderPath)}-{Version}-*.png";
            foreach (string old in Directory.GetFiles(AppPaths.ThumbnailDir, pattern))
            {
                File.Delete(old);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("save the picture of " + Path.GetFileName(shaderPath), e);
        }
    }
}
