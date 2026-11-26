using System.Globalization;

namespace Wallpepper.Library;

internal enum ShaderParamKind
{
    /// <summary>An integer 0 or 1: a switch.</summary>
    Toggle,

    /// <summary>Any other number: a slider.</summary>
    Number,

    /// <summary>Three numbers NAME_R, NAME_G, NAME_B (0..1 each): one colour.</summary>
    Colour,
}

/// <summary>
/// One setting of a shader as the app presents it, made from a numeric "#define NAME value // description" (or from
/// three of them for a colour). Label, range and unit are inferred here, away from the controls that show them.
/// </summary>
internal sealed class ShaderParam
{
    public required ShaderParamKind Kind { get; init; }

    /// <summary>The define's name; for a colour the three names in the order red, green, blue.</summary>
    public required IReadOnlyList<string> Defines { get; init; }

    /// <summary>The literals from the source, one per define.</summary>
    public required IReadOnlyList<string> Defaults { get; init; }

    /// <summary>"BH position X" for BH_POS_X.</summary>
    public required string Label { get; init; }

    /// <summary>The shader's own description of the setting; null when it gives none.</summary>
    public string? Description { get; init; }

    // ---- numbers only
    public double Minimum { get; init; }

    public double Maximum { get; init; } = 1;

    public bool IsInteger { get; init; }

    public int Decimals { get; init; }

    public double Step { get; init; }

    /// <summary>Shown after the value: "°", " s", " px" or nothing.</summary>
    public string Unit { get; init; } = "";

    public string Name => Defines[0];

    public double Default => ParseNumber(Defaults[0], 0);

    public static double ParseNumber(string? literal, double fallback) =>
        double.TryParse((literal ?? "").TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    /// <summary>The value as it is shown next to a slider.</summary>
    public string Display(double value) => value.ToString("F" + Decimals, CultureInfo.InvariantCulture) + Unit;

    /// <summary>The value as it is written into the source; null when it is the shader's own default.</summary>
    public string? Literal(double value)
    {
        double rounded = IsInteger ? Math.Round(value) : Math.Round(value, Decimals);
        if (Math.Abs(rounded - Default) < 1e-9)
        {
            return null;
        }
        return IsInteger
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.0#####", CultureInfo.InvariantCulture);
    }
}

/// <summary>Settings a shader keeps together (under a comment heading, or between blank lines).</summary>
/// <param name="Title">the heading's text, null for a group without one</param>
internal sealed record ShaderParamSection(string? Title, IReadOnlyList<ShaderParam> Params);
