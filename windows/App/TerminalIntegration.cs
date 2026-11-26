using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Wallpepper;

/// <summary>
/// Points Windows Terminal at a shader ("experimental.pixelShaderPath" in the defaults of all profiles). Terminal's
/// settings.json belongs to the user: every change is preceded by a dated backup, written through a temp file, and
/// made as a targeted text edit whenever possible so that comments and formatting survive.
/// </summary>
internal static class TerminalIntegration
{
    public const string ShaderSetting = "experimental.pixelShaderPath";

    private const string SettingsFile = "settings.json";

    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The settings folders of the Terminal installs there can be: Store, Preview, unpackaged.</summary>
    private static readonly string[] SettingsFolders =
    {
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState"),
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState"),
        Path.Combine(LocalAppData, "Microsoft", "Windows Terminal"),
    };

    private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The settings folder of the installed Windows Terminal (the Store build first), null when there is none.</summary>
    public static string? LocalStateFolder => SettingsFolders.FirstOrDefault(Directory.Exists);

    /// <summary>
    /// Makes every installed Terminal use the shader at <paramref name="shaderPath"/>.
    /// </summary>
    /// <returns>true when comments and formatting of every settings file were kept; false when one had to be rewritten.</returns>
    /// <exception cref="FileNotFoundException">no Terminal settings were found</exception>
    public static bool UseShader(string shaderPath)
    {
        var files = SettingsFolders.Select(folder => Path.Combine(folder, SettingsFile)).Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            throw new FileNotFoundException("Windows Terminal's settings.json was not found.");
        }
        bool keptFormatting = true;
        foreach (string file in files)
        {
            string before = File.ReadAllText(file);
            string after = SetShader(before, shaderPath, out bool edited);
            keptFormatting &= edited;
            Replace(file, before, after);
        }
        return keptFormatting;
    }

    /// <summary>Removes the shader setting from Terminal when it points into a folder that is about to be deleted.</summary>
    public static void ClearShaderUnder(string folder)
    {
        string prefix = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        foreach (string file in SettingsFolders.Select(candidate => Path.Combine(candidate, SettingsFile)).Where(File.Exists))
        {
            string before = File.ReadAllText(file);
            var root = JsonNode.Parse(before, null, Lenient);
            if (Defaults(root) is JsonObject defaults && defaults[ShaderSetting]?.GetValue<string>() is string current
                && current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                defaults.Remove(ShaderSetting);
                Replace(file, before, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    /// <summary>
    /// The settings text with the shader set in "profiles.defaults". <paramref name="edited"/> is true when only the
    /// one value was replaced in the text; false when the document had to be rebuilt (comments are lost then).
    /// </summary>
    public static string SetShader(string settingsJson, string shaderPath, out bool edited)
    {
        JsonNode root = JsonNode.Parse(settingsJson, null, Lenient) ?? new JsonObject();
        string value = JsonSerializer.Serialize(shaderPath);

        // the setting already stands in the defaults and nowhere else: replace just its value
        var occurrences = Regex.Matches(settingsJson, "\"" + Regex.Escape(ShaderSetting) + "\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"");
        if (occurrences.Count == 1 && Defaults(root)?[ShaderSetting] != null)
        {
            edited = true;
            var match = occurrences[0];
            return settingsJson.Substring(0, match.Index) + "\"" + ShaderSetting + "\": " + value + settingsJson.Substring(match.Index + match.Length);
        }

        edited = false;
        JsonObject profiles;
        if (root["profiles"] is JsonArray list)
        {
            // the old form, a bare list of profiles, which Terminal still reads: wrapped so that it can have defaults
            root["profiles"] = null;
            profiles = new JsonObject { ["defaults"] = new JsonObject(), ["list"] = list };
            root["profiles"] = profiles;
        }
        else if (root["profiles"] is JsonObject existing)
        {
            profiles = existing;
        }
        else
        {
            profiles = new JsonObject();
            root["profiles"] = profiles;
        }
        if (profiles["defaults"] is not JsonObject defaults)
        {
            defaults = new JsonObject();
            profiles["defaults"] = defaults;
        }
        defaults[ShaderSetting] = shaderPath;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject? Defaults(JsonNode? root) => (root?["profiles"] as JsonObject)?["defaults"] as JsonObject;

    /// <summary>Backup with a time stamp, then temp file and rename.</summary>
    private static void Replace(string file, string before, string after)
    {
        if (after == before)
        {
            return;
        }
        File.WriteAllText($"{file}.wallpepper-{DateTime.Now:yyyyMMdd-HHmmss}.bak", before);
        string temp = file + ".wallpepper.tmp";
        File.WriteAllText(temp, after);
        File.Move(temp, file, true);
    }
}
