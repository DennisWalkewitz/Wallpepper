using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Wallpepper.Settings;
using Wallpepper.Web;

namespace Wallpepper.Library;

/// <summary>
/// The library folder: its items (shaders *.hlsl and web wallpapers *.wallpepperweb), what their headers say, the
/// favourites, adding and deleting. The list and the metadata are kept in memory and refreshed when the folder
/// changes, so asking for them costs nothing. UI thread only.
/// </summary>
internal sealed class ShaderLibrary : IDisposable
{
    private const string ShaderPattern = "*.hlsl";
    private const string DefaultShaderPrefix = "blackhole";
    private const int ChangeDelayMs = 300;

    private readonly SettingsStore _settings;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _changeTimer;
    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShaderMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private List<string> _items = new List<string>();

    public ShaderLibrary(SettingsStore settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
        _changeTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(ChangeDelayMs) };
        _changeTimer.Tick += (_, _) => FlushChanges();
        Folder = ResolveFolder(settings.Config.LibraryPath);
        Watch();
        Rescan();
    }

    /// <summary>
    /// Files were added, edited or removed: the paths that changed (empty when only the list may have changed).
    /// Raised a moment after the last file event, so an editor's save arrives as one change.
    /// </summary>
    public event Action<IReadOnlyCollection<string>>? Changed;

    public string Folder { get; private set; }

    /// <summary>Full paths of everything in the library, by file name.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>The library folder Windows Terminal would share, when Terminal is installed.</summary>
    public static string? TerminalShadersFolder
    {
        get
        {
            string? state = TerminalIntegration.LocalStateFolder;
            return state == null ? null : Path.Combine(state, "shaders");
        }
    }

    public static bool IsShader(string path) => path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Rain on glass" for rain-on-glass.hlsl.</summary>
    public static string Pretty(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ');
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    public static bool IsBundled(string path) => BundledLibrary.Contains(Path.GetFileName(path));

    /// <summary>Header information of an item; read once and again after the file changed.</summary>
    public ShaderMetadata Metadata(string path)
    {
        if (!_metadata.TryGetValue(path, out var metadata))
        {
            metadata = ReadMetadata(path);
            _metadata[path] = metadata;
        }
        return metadata;
    }

    /// <summary>Card title: the "@name" header (or a web wallpaper's "name") when present, else the prettified file name.</summary>
    public string Title(string? path) => path == null ? "" : Metadata(path).Name ?? Pretty(path);

    public bool Contains(string? path) => path != null && _items.Contains(path, StringComparer.OrdinalIgnoreCase);

    public string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>The shader shown when nothing (or something that no longer exists) is assigned.</summary>
    public string? DefaultShader()
    {
        var shaders = _items.Where(IsShader).ToList();
        return shaders.FirstOrDefault(path => Path.GetFileName(path).StartsWith(DefaultShaderPrefix, StringComparison.OrdinalIgnoreCase))
            ?? shaders.FirstOrDefault();
    }

    public bool IsFavourite(string path) => Config.ContainsName(_settings.Config.Favourites, Path.GetFileName(path));

    public void SetFavourite(string path, bool favourite)
    {
        string file = Path.GetFileName(path);
        _settings.Change(SettingsArea.None, config =>
        {
            Config.RemoveName(config.Favourites, file);
            if (favourite)
            {
                config.Favourites.Add(file);
            }
        });
    }

    /// <summary>Copies files into the library (a name that is taken gets a number). Returns the library paths.</summary>
    public IReadOnlyList<string> Add(IEnumerable<string> files)
    {
        var added = new List<string>();
        foreach (string file in files)
        {
            if (!(IsShader(file) || WebInfo.IsWeb(file)) || !File.Exists(file))
            {
                continue;
            }
            string target = PathOf(Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                for (int number = 2; File.Exists(target); number++)
                {
                    target = PathOf($"{Path.GetFileNameWithoutExtension(file)}-{number}{Path.GetExtension(file)}");
                }
                File.Copy(file, target);
            }
            added.Add(target);
        }
        if (added.Count > 0)
        {
            Rescan();
            Changed?.Invoke(added);
        }
        return added;
    }

    /// <summary>Moves an item to the Recycle Bin and forgets its settings.</summary>
    public void Delete(string path)
    {
        // Microsoft.VisualBasic is the one managed API that deletes to the Recycle Bin
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        _settings.Change(SettingsArea.None, config => config.Forget(Path.GetFileName(path)));
        Rescan();
        Changed?.Invoke(new[] { path });
    }

    /// <summary>Uses another folder as the library (it is created when missing) and brings the bundled items into it.</summary>
    public void SetFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        _settings.Change(SettingsArea.None, config =>
        {
            config.LibraryPath = folder;
            config.SeededByBuild = null;
        });
        Folder = folder;
        SyncBundled();
        Watch();
        Rescan();
        Changed?.Invoke(Array.Empty<string>());
    }

    /// <summary>Copies new and updated bundled items into the folder (once per build). Returns the number written.</summary>
    public int SyncBundled()
    {
        int written = 0;
        _settings.Change(SettingsArea.None, config => written = BundledLibrary.Sync(Folder, config));
        if (written > 0)
        {
            Log.Write($"library: {written} bundled item(s) added or updated in {Folder}");
            Rescan();
        }
        return written;
    }

    /// <summary>Puts back bundled items that are missing. Returns the number written.</summary>
    public int RestoreBundled()
    {
        int written = 0;
        _settings.Change(SettingsArea.None, config => written = BundledLibrary.RestoreMissing(Folder, config));
        Rescan();
        Changed?.Invoke(Array.Empty<string>());
        return written;
    }

    /// <summary>Reads the folder again and forgets all cached headers.</summary>
    public void Rescan()
    {
        _metadata.Clear();
        try
        {
            _items = Directory.GetFiles(Folder, ShaderPattern)
                .Concat(Directory.GetFiles(Folder, "*" + WebInfo.Extension))
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("read the library folder " + Folder, e);
            _items = new List<string>();
        }
    }

    public void Dispose()
    {
        _changeTimer.Stop();
        _watcher?.Dispose();
    }

    /// <summary>
    /// The configured folder when it can be used; otherwise Windows Terminal's shaders folder when there is one (both
    /// then see the same files), otherwise Wallpepper's own.
    /// </summary>
    private string ResolveFolder(string configured)
    {
        string folder = configured;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            string? terminal = TerminalShadersFolder;
            folder = terminal != null && Directory.Exists(terminal) ? terminal : AppPaths.OwnLibraryDir;
            if (!string.IsNullOrEmpty(configured))
            {
                Log.Write($"library folder {configured} is not available, using {folder}");
            }
        }
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("create the library folder " + folder, e);
            folder = AppPaths.OwnLibraryDir;
            Directory.CreateDirectory(folder);
        }
        if (folder != configured)
        {
            _settings.Change(SettingsArea.None, config => config.LibraryPath = folder);
        }
        return folder;
    }

    /// <summary>Hot reload: edit a .hlsl in an editor and the wallpaper and the preview update on save.</summary>
    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Filters.Add(ShaderPattern);
            watcher.Filters.Add("*" + WebInfo.Extension);
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // an unreachable folder (removed drive, network path): the library still works, without live reload
            Log.Warn("watch the library folder " + Folder, e);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_changedPaths)
        {
            _changedPaths.Add(e.FullPath);
        }
        _ui.InvokeAsync(RestartChangeTimer);
    }

    /// <summary>The watcher lost events (too many at once): everything is read again.</summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Log.Warn("library folder watcher", e.GetException());
        _ui.InvokeAsync(() =>
        {
            Rescan();
            Changed?.Invoke(_items.ToList());
        });
    }

    private void RestartChangeTimer()
    {
        _changeTimer.Stop();
        _changeTimer.Start();
    }

    private void FlushChanges()
    {
        _changeTimer.Stop();
        string[] paths;
        lock (_changedPaths)
        {
            paths = _changedPaths.ToArray();
            _changedPaths.Clear();
        }
        Rescan();
        Changed?.Invoke(paths);
    }

    private static ShaderMetadata ReadMetadata(string path)
    {
        try
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsBundled(path))
            {
                tags.Add(ShaderMetadata.CustomTag);
            }
            if (WebInfo.IsWeb(path))
            {
                var web = WebInfo.Load(path);
                tags.UnionWith(web.Tags.Select(ShaderHeader.NormaliseTag));
                tags.Add(ShaderMetadata.WebTag);
                return new ShaderMetadata(web.Name, tags, ShaderMetadata.Empty.LightSettings, web);
            }
            var header = ShaderHeader.Parse(File.ReadAllText(path));
            tags.UnionWith(header.Tags);
            return new ShaderMetadata(header.Name, tags, header.LightSettings, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("read " + Path.GetFileName(path), e);
            return ShaderMetadata.Empty;
        }
    }
}
