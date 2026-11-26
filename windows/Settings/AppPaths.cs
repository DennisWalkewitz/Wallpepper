namespace Wallpepper.Settings;

/// <summary>Where Wallpepper keeps its data: everything lives under %LOCALAPPDATA%\Wallpepper.</summary>
internal static class AppPaths
{
    /// <summary>Names another data folder (a test run that must not touch the real settings).</summary>
    public const string DataDirVariable = "WALLPEPPER_DATA";

    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDir { get; } = Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } other
        ? other
        : Path.Combine(LocalAppData, "Wallpepper");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string LogFile => Path.Combine(DataDir, "log.txt");

    public static string PreviousLogFile => Path.Combine(DataDir, "log.1.txt");

    /// <summary>Gallery pictures, one PNG per shader and settings combination.</summary>
    public static string ThumbnailDir => Path.Combine(DataDir, "thumbs");

    /// <summary>Compiled shader bytecode, so that a shader is only compiled again when its source changes.</summary>
    public static string ShaderCacheDir => Path.Combine(DataDir, "compiled");

    /// <summary>Browser profile of the web wallpapers.</summary>
    public static string WebViewDir => Path.Combine(DataDir, "WebView2");

    /// <summary>Downloaded updates wait here until they are applied.</summary>
    public static string UpdateStagingDir => Path.Combine(DataDir, "update");

    /// <summary>The library folder used when Windows Terminal has no shaders folder to share.</summary>
    public static string OwnLibraryDir => Path.Combine(DataDir, "shaders");
}
