using System.Text.Json;

namespace Wallpepper.Web;

/// <summary>
/// A web wallpaper: a small JSON file (*.wallpepperweb) in the library, for example
/// { "name": "FlowBeat Radio", "url": "https://flowbeat.walksoft.online", "tags": ["web"], "audio": true }.
/// </summary>
internal sealed class WebInfo
{
    public const string Extension = ".wallpepperweb";

    private WebInfo()
    {
    }

    public string? Name { get; private init; }

    public string Url { get; private init; } = "";

    public IReadOnlyList<string> Tags { get; private init; } = Array.Empty<string>();

    /// <summary>The page plays sound: offer a mute switch, and let only one display play it.</summary>
    public bool Audio { get; private init; }

    /// <summary>Why the file cannot be used, null when it is fine.</summary>
    public string? Error { get; private init; }

    /// <summary>Host name of the address, empty when there is none.</summary>
    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "";

    public static bool IsWeb(string? path) => path != null && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static WebInfo Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new WebInfo { Error = "The web wallpaper file cannot be read: " + e.Message };
        }
    }

    public static WebInfo Parse(string json)
    {
        string? name = null;
        string url = "";
        bool audio = false;
        var tags = new List<string>();
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(json, options);
            var root = document.RootElement;
            if (root.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String)
            {
                name = nameValue.GetString();
            }
            if (root.TryGetProperty("url", out var urlValue) && urlValue.ValueKind == JsonValueKind.String)
            {
                url = urlValue.GetString() ?? "";
            }
            if (root.TryGetProperty("audio", out var audioValue))
            {
                audio = audioValue.ValueKind == JsonValueKind.True;
            }
            if (root.TryGetProperty("tags", out var tagValues) && tagValues.ValueKind == JsonValueKind.Array)
            {
                tags.AddRange(tagValues.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String).Select(tag => tag.GetString()!));
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return new WebInfo { Error = "Not a valid web wallpaper file: " + e.Message };
        }

        // the file can come from anywhere: only encrypted web addresses are accepted (plain http for this PC itself)
        string? error = IsAllowedAddress(url) ? null : "The file needs a \"url\" that starts with https://";
        return new WebInfo { Name = name, Url = url, Audio = audio, Tags = tags, Error = error };
    }

    /// <summary>https anywhere; http only for a page served by this PC.</summary>
    public static bool IsAllowedAddress(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
