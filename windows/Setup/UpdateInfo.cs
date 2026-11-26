using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallpepper.Setup;

/// <summary>
/// What a look at the latest release found: the one JSON line the "--update-check" and "--update-fetch" child
/// processes print and the tray process reads.
/// </summary>
internal sealed class UpdateInfo
{
    /// <summary>The newer version ("0.4.0"), null when there is none.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>The release page.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>The downloaded exe whose checksum matched (after a fetch).</summary>
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Why the check or the download failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public static UpdateInfo Failed(string error) => new UpdateInfo { Error = error };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static UpdateInfo Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UpdateInfo>(json) ?? Failed("The update check gave no answer.");
        }
        catch (JsonException)
        {
            return Failed("The update check gave no answer.");
        }
    }
}
