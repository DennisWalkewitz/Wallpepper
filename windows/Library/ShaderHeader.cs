namespace Wallpepper.Library;

/// <summary>
/// The comment header of a shader: "// @name: Pretty Name", "// @tags: water, nature" and
/// "// @light: SAMPLES=1, BOUNCES=2" within its first lines.
/// </summary>
internal sealed record ShaderHeader(string? Name, IReadOnlySet<string> Tags, IReadOnlyDictionary<string, string> LightSettings)
{
    private const int HeaderLines = 30;

    private static readonly char[] ListSeparators = { ',', ';' };

    public static ShaderHeader Parse(string source)
    {
        string? name = null;
        HashSet<string>? tags = null;
        var light = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(source);
        for (int i = 0; i < HeaderLines; i++)
        {
            string? line = reader.ReadLine()?.Trim();
            if (line == null)
            {
                break;
            }
            if (!line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            if (name == null && TryField(line, "@name", out string nameText))
            {
                name = nameText;
            }
            else if (tags == null && TryField(line, "@tags", out string tagText))
            {
                tags = new HashSet<string>(SplitList(tagText).Select(NormaliseTag), StringComparer.OrdinalIgnoreCase);
            }
            else if (TryField(line, "@light", out string lightText))
            {
                foreach (string pair in SplitList(lightText))
                {
                    string[] parts = pair.Split('=');
                    if (parts.Length == 2 && parts[0].Trim().Length > 0)
                    {
                        light[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
        }
        return new ShaderHeader(string.IsNullOrWhiteSpace(name) ? null : name, tags ?? new HashSet<string>(), light);
    }

    /// <summary>One spelling per tag: lower case without separators, and a few synonyms folded together.</summary>
    public static string NormaliseTag(string tag)
    {
        string plain = tag.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        return plain switch
        {
            "terminal" => "retro",
            "terminaleffect" or "terminaleffects" => "terminalonly",
            "multimon" or "span" or "multiscreen" => "multimonitor",
            "ocean" or "sea" or "rain" => "water",
            "calm" => "calming",
            _ => plain,
        };
    }

    private static bool TryField(string line, string field, out string value)
    {
        int at = line.IndexOf(field, StringComparison.OrdinalIgnoreCase);
        value = at < 0 ? "" : line.Substring(at + field.Length).TrimStart(' ', ':', '=').Trim();
        return at >= 0;
    }

    private static string[] SplitList(string text) => text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries);
}
