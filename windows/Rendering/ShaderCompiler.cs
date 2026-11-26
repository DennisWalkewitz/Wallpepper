using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Wallpepper.Interop;

namespace Wallpepper.Rendering;

/// <summary>Compiles Windows Terminal style HLSL with d3dcompiler_47 and rewrites the "#define" settings of a source.</summary>
internal static class ShaderCompiler
{
    /// <summary>Name of the switch that turns a shader tagged "benchmark" into the fixed reference workload.</summary>
    public const string BenchmarkDefine = "BENCHMARK";

    private const string NumberPattern = @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?[fF]?";

    // "#define NAME 1.25f   // comment"
    private static readonly Regex DefineRx = new Regex(
        @"^(?<pre>[ \t]*#define[ \t]+(?<name>[A-Za-z_]\w*)[ \t]+)(?<val>" + NumberPattern + @")(?<post>[ \t]*(?://[ \t]*(?<cmt>[^\r\n]*))?\r?)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex NumberRx = new Regex("^" + NumberPattern + "$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> BenchmarkOff = new Dictionary<string, string> { [BenchmarkDefine] = "0" };

    /// <summary>Returns bytecode or null; error text in <paramref name="error"/>. Safe to call from any thread.</summary>
    public static byte[]? Compile(string source, string sourceName, string entryPoint, string profile, out string? error)
    {
        error = null;
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        int hr = D3DCompiler.D3DCompile(
            bytes, (UIntPtr)bytes.Length, sourceName, IntPtr.Zero, D3DCompiler.StandardFileInclude,
            entryPoint, profile, D3DCompiler.D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, out var code, out var messages);
        try
        {
            if (messages != null && hr < 0)
            {
                error = Marshal.PtrToStringAnsi(messages.GetBufferPointer(), (int)messages.GetBufferSize())?.TrimEnd('\0', '\n', '\r');
            }
            if (hr < 0 || code == null)
            {
                error ??= $"D3DCompile failed (0x{hr:X8})";
                return null;
            }
            var result = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), result, 0, result.Length);
            return result;
        }
        finally
        {
            if (code != null)
            {
                Marshal.ReleaseComObject(code);
            }
            if (messages != null)
            {
                Marshal.ReleaseComObject(messages);
            }
        }
    }

    /// <summary>One "#define NAME number" line of a source.</summary>
    public readonly record struct Define(string Name, string Value, string? Comment, int Position);

    /// <summary>Every numeric "#define" of a source, in order; a name that is defined twice counts once.</summary>
    public static List<Define> ParseDefines(string source)
    {
        var defines = new List<Define>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in DefineRx.Matches(source))
        {
            string name = match.Groups["name"].Value;
            if (seen.Add(name))
            {
                string? comment = match.Groups["cmt"].Success ? match.Groups["cmt"].Value.Trim() : null;
                defines.Add(new Define(name, match.Groups["val"].Value, comment, match.Index));
            }
        }
        return defines;
    }

    /// <summary>A value that may be written into a "#define": a plain number, nothing else.</summary>
    public static bool IsNumber(string value) => NumberRx.IsMatch(value);

    /// <summary>
    /// Forces "#define BENCHMARK" to 0. Everything the app compiles for display, thumbnails or cost checks goes through
    /// this, so only the benchmark itself can ever run the heavy reference workload.
    /// </summary>
    public static string WithoutBenchmarkMode(string source) =>
        source.Contains(BenchmarkDefine, StringComparison.Ordinal) ? ApplyOverrides(source, BenchmarkOff) : source;

    /// <summary>Rewrites the literal of each overridden #define (first occurrence): no macro redefinition warnings.</summary>
    public static string ApplyOverrides(string source, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides == null || overrides.Count == 0)
        {
            return source;
        }
        var done = new HashSet<string>(StringComparer.Ordinal);
        return DefineRx.Replace(source, match =>
        {
            string name = match.Groups["name"].Value;
            if (done.Contains(name) || !overrides.TryGetValue(name, out string? value) || !IsNumber(value))
            {
                return match.Value;
            }
            done.Add(name);
            // a whole number for a setting written as a float ("0" for 0.5) keeps its type: "0.0"
            string was = match.Groups["val"].Value;
            bool wasFloat = was.Contains('.') || was.Contains('e') || was.Contains('E');
            if (wasFloat && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                value += ".0";
            }
            return match.Groups["pre"].Value + value + match.Groups["post"].Value;
        });
    }
}
