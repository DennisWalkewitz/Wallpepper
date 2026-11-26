namespace Wallpepper.Settings;

/// <summary>What a settings change touches, so that the app re-applies only that.</summary>
[Flags]
internal enum SettingsArea
{
    /// <summary>Only saved (window placement, favourites, costs ...).</summary>
    None = 0,

    /// <summary>Renderer defaults: speed, background, manual frame rate and resolution.</summary>
    Rendering = 1,

    /// <summary>Pause rules, resource level, dim on idle, per-display frame rate.</summary>
    Governor = 2,

    /// <summary>What each display shows and how: assignment, mode, filters, per-display resolution.</summary>
    Displays = 4,
}
