namespace Wallpepper.Settings;

/// <summary>How wallpapers are spread over several displays.</summary>
internal enum DisplayMode
{
    /// <summary>The same wallpaper on every display.</summary>
    Duplicate,

    /// <summary>Each display has its own wallpaper.</summary>
    PerDisplay,

    /// <summary>One image across all displays.</summary>
    Span,
}

/// <summary>Order of the gallery.</summary>
internal enum LibrarySort
{
    Name,
    Recent,
    Cost,
}
