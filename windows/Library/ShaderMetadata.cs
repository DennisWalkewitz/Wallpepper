using Wallpepper.Web;

namespace Wallpepper.Library;

/// <summary>What the library knows about one item without compiling it.</summary>
/// <param name="Name">the "@name" header or a web wallpaper's "name"; null = use the file name</param>
/// <param name="Tags">normalised tags, including "web" and "custom" where they apply</param>
/// <param name="LightSettings">the shader's "@light" settings; empty when it declares none</param>
/// <param name="Web">the parsed file of a web wallpaper, else null</param>
internal sealed record ShaderMetadata(
    string? Name, IReadOnlySet<string> Tags, IReadOnlyDictionary<string, string> LightSettings, WebInfo? Web)
{
    public const string TerminalOnlyTag = "terminalonly";
    public const string MultiMonitorTag = "multimonitor";
    public const string CustomTag = "custom";
    public const string WebTag = "web";

    public static readonly ShaderMetadata Empty = new ShaderMetadata(
        null, new HashSet<string>(), new Dictionary<string, string>(), null);

    /// <summary>A terminal effect only changes terminal text, which a wallpaper does not have.</summary>
    public bool IsTerminalOnly => Tags.Contains(TerminalOnlyTag);

    /// <summary>Written to travel across several displays.</summary>
    public bool IsMultiMonitor => Tags.Contains(MultiMonitorTag);
}
