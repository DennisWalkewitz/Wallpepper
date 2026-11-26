using System.Text.RegularExpressions;
using Wallpepper.Rendering;

namespace Wallpepper.Library;

/// <summary>
/// Turns a shader's "#define" lines into the settings the app shows: which defines are settings at all, which three
/// form a colour, what range a slider gets, and how the shader groups them.
/// </summary>
internal static class ShaderParamLayout
{
    private static readonly Regex RangeRx = new Regex(@"(-?\d+(?:\.\d+)?)\s*\.\.\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>"// ---- colours (0..1 each) ----": a comment whose text is framed by dashes, equals signs or stars.</summary>
    private static readonly Regex HeadingRx = new Regex(@"^\s*//\s*[-=*#]{2,}\s*(?<text>.*?)\s*[-=*#/]*\s*$", RegexOptions.Compiled);

    /// <summary>Maths constants are not settings.</summary>
    private static readonly HashSet<string> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        "PI", "TAU", "TWO_PI", "TWOPI", "HALF_PI", "PI2", "PI_2", "M_PI", "E", "EPS", "EPSILON", "PHI", "SQRT2", "INV_PI",
    };

    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POS"] = "position", ["COL"] = "colour", ["COLOR"] = "colour", ["BG"] = "background", ["FG"] = "foreground",
        ["FREQ"] = "frequency", ["AMP"] = "amplitude", ["SPD"] = "speed", ["ROT"] = "rotation", ["NUM"] = "number of",
        ["DIST"] = "distance", ["DIR"] = "direction", ["MAX"] = "maximum", ["MIN"] = "minimum", ["CENTER"] = "centre",
    };

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "IOR", "FOV", "RGB", "HDR", "CRT", "FPS", "GPU", "UV", "3D", "2D",
    };

    private static readonly HashSet<string> ShortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "to", "in", "on", "by", "at", "no", "up", "or", "is",
    };

    /// <summary>A heading that only says "these are the settings" names nothing.</summary>
    private static readonly string[] GenericHeadingWords = { "setting", "parameter", "tweak", "knob", "option", "control" };

    /// <summary>The sections of a source, in the order the shader lists them. Empty when it has no settings.</summary>
    public static IReadOnlyList<ShaderParamSection> Parse(string source)
    {
        var defines = ShaderCompiler.ParseDefines(source)
            .Where(define => !Constants.Contains(define.Name) && define.Name != ShaderCompiler.BenchmarkDefine)
            .ToList();
        var byName = defines.ToDictionary(define => define.Name, StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var sections = new List<ShaderParamSection>();
        var current = new List<ShaderParam>();
        string? title = null;
        int previousEnd = -1;

        foreach (var define in defines)
        {
            // what stands between this define and the one before decides whether a new group starts here
            if (previousEnd >= 0)
            {
                string between = source.Substring(previousEnd, define.Position - previousEnd);
                string? heading = HeadingIn(between, out bool found);
                if (found || HasBlankLine(between))
                {
                    Close(sections, current, title);
                    current = new List<ShaderParam>();
                    title = heading;
                }
            }
            else
            {
                title = HeadingIn(LinesBefore(source, define.Position, 3), out _);
            }
            previousEnd = source.IndexOf('\n', define.Position) is int end and >= 0 ? end + 1 : source.Length;

            if (consumed.Contains(define.Name))
            {
                continue;
            }
            current.Add(TryColour(define, byName, consumed) ?? Scalar(define));
        }
        Close(sections, current, title);
        return sections;
    }

    /// <summary>"BH position X" for BH_POS_X: words spelled out, acronyms and axes left in capitals.</summary>
    public static string Humanise(string defineName)
    {
        var words = new List<string>();
        foreach (string token in defineName.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Words.TryGetValue(token, out string? word))
            {
                words.Add(word);
            }
            else if (Acronyms.Contains(token) || (token.Length <= 2 && !ShortWords.Contains(token) && !char.IsDigit(token[0])))
            {
                words.Add(token.ToUpperInvariant());
            }
            else
            {
                words.Add(token.ToLowerInvariant());
            }
        }
        if (words.Count == 0)
        {
            return defineName;
        }
        string first = words[0];
        words[0] = char.ToUpperInvariant(first[0]) + first.Substring(1);
        return string.Join(' ', words);
    }

    private static void Close(List<ShaderParamSection> sections, List<ShaderParam> current, string? title)
    {
        if (current.Count > 0)
        {
            sections.Add(new ShaderParamSection(title, current));
        }
    }

    /// <summary>A colour: this define ends in _R and its _G and _B siblings exist.</summary>
    private static ShaderParam? TryColour(
        ShaderCompiler.Define red, Dictionary<string, ShaderCompiler.Define> byName, HashSet<string> consumed)
    {
        if (!red.Name.EndsWith("_R", StringComparison.Ordinal))
        {
            return null;
        }
        string prefix = red.Name.Substring(0, red.Name.Length - 2);
        if (!byName.TryGetValue(prefix + "_G", out var green) || !byName.TryGetValue(prefix + "_B", out var blue))
        {
            return null;
        }
        consumed.Add(green.Name);
        consumed.Add(blue.Name);

        // "oculus light colour: red" describes the colour up to the colon
        string? description = red.Comment;
        int colon = description?.LastIndexOf(':') ?? -1;
        if (colon > 0)
        {
            description = description!.Substring(0, colon).Trim();
        }
        return new ShaderParam
        {
            Kind = ShaderParamKind.Colour,
            Defines = new[] { red.Name, green.Name, blue.Name },
            Defaults = new[] { red.Value, green.Value, blue.Value },
            Label = Humanise(prefix),
            Description = Sentence(description),
        };
    }

    private static ShaderParam Scalar(ShaderCompiler.Define define)
    {
        string literal = define.Value;
        if (literal == "0" || literal == "1")
        {
            return new ShaderParam
            {
                Kind = ShaderParamKind.Toggle,
                Defines = new[] { define.Name },
                Defaults = new[] { literal },
                Label = Humanise(define.Name),
                Description = Sentence(define.Comment),
            };
        }

        bool isInteger = !literal.Contains('.') && !literal.Contains('e') && !literal.Contains('E');
        double value = ShaderParam.ParseNumber(literal, 0);
        var (min, max) = RangeFor(define, value, isInteger);
        double span = Math.Max(1e-6, max - min);
        return new ShaderParam
        {
            Kind = ShaderParamKind.Number,
            Defines = new[] { define.Name },
            Defaults = new[] { literal },
            Label = Humanise(define.Name),
            Description = Sentence(define.Comment),
            Minimum = min,
            Maximum = max,
            IsInteger = isInteger,
            Decimals = isInteger ? 0 : span >= 100 ? 1 : span >= 10 ? 2 : span >= 0.5 ? 3 : 4,
            Step = isInteger ? 1 : span / 200,
            Unit = UnitIn(define.Comment),
        };
    }

    /// <summary>
    /// The range the shader's comment names ("1..16"); without one, a guess from the name and the default: positions
    /// run from 0 to 1, a negative default gets a symmetric range, everything else up to three times its default.
    /// </summary>
    private static (double Min, double Max) RangeFor(ShaderCompiler.Define define, double value, bool isInteger)
    {
        Match range = define.Comment != null ? RangeRx.Match(define.Comment) : Match.Empty;
        if (range.Success)
        {
            double low = ShaderParam.ParseNumber(range.Groups[1].Value, 0);
            double high = ShaderParam.ParseNumber(range.Groups[2].Value, 1);
            if (high > low)
            {
                return (Math.Min(low, value), Math.Max(high, value));
            }
        }
        if (define.Name.EndsWith("_X", StringComparison.Ordinal) || define.Name.EndsWith("_Y", StringComparison.Ordinal))
        {
            return (Math.Min(0, value), Math.Max(1, value));
        }
        if (value < 0)
        {
            return (value * 3, -value * 3);
        }
        if (isInteger)
        {
            return (value >= 1 ? 1 : 0, Math.Max(8, value * 3));
        }
        return (0, Math.Max(1, value * 3));
    }

    private static string UnitIn(string? comment)
    {
        if (comment == null)
        {
            return "";
        }
        if (comment.Contains("degree", StringComparison.OrdinalIgnoreCase))
        {
            return "°";
        }
        if (Regex.IsMatch(comment, @"\b(in seconds|seconds)\b", RegexOptions.IgnoreCase))
        {
            return " s";
        }
        return Regex.IsMatch(comment, @"\b(in pixels|pixels|px)\b", RegexOptions.IgnoreCase) ? " px" : "";
    }

    /// <summary>The description as a sentence: first letter in upper case.</summary>
    private static string? Sentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        text = text.Trim();
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    /// <summary>Whether a run of whole lines contains an empty one.</summary>
    private static bool HasBlankLine(string lines)
    {
        string[] parts = lines.Split('\n');
        return parts.Take(parts.Length - 1).Any(line => line.Trim().Length == 0);
    }

    /// <summary>The last heading comment in a piece of source; <paramref name="found"/> also for one that names nothing.</summary>
    private static string? HeadingIn(string text, out bool found)
    {
        found = false;
        string? heading = null;
        foreach (string line in text.Split('\n'))
        {
            Match match = HeadingRx.Match(line.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }
            found = true;
            heading = TitleOf(match.Groups["text"].Value);
        }
        return heading;
    }

    /// <summary>"colours (0..1 each)" -> "Colours"; null for a heading that only announces the settings.</summary>
    private static string? TitleOf(string text)
    {
        int cut = text.IndexOfAny(new[] { ':', '(' });
        if (cut >= 0)
        {
            text = text.Substring(0, cut);
        }
        text = text.Trim(' ', '-', '=', '*', '#', '/');
        if (text.Length == 0 || text.Length > 40 || GenericHeadingWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static string LinesBefore(string source, int position, int count)
    {
        int start = position;
        for (int i = 0; i < count && start > 0; i++)
        {
            start = source.LastIndexOf('\n', Math.Max(0, start - 2)) + 1;
        }
        return source.Substring(start, position - start);
    }
}
