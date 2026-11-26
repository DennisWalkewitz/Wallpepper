using System.Globalization;
using Wallpepper.Library;
using Wallpepper.Rendering;

namespace Wallpepper.Commands;

/// <summary>
/// "--check &lt;shaderDir&gt; &lt;outFile&gt;": compiles every .hlsl in a folder exactly like the app does and measures
/// the GPU time of 1920x1080 frames. One line per shader: name | OK or ERROR | gpu_ms | first error text. Runs without a
/// window and next to a running instance.
/// </summary>
internal static class CheckShadersCommand
{
    private const int FrameWidth = 1920;
    private const int FrameHeight = 1080;
    private const int Frames = 20;

    public static int Run(CommandLine args)
    {
        if (args.Values.Count < 2)
        {
            return 2;
        }
        string folder = args.Values[0];
        string outFile = args.Values[1];
        var lines = new List<string>();
        try
        {
            using var device = GraphicsDevice.Create();
            string[] files = Directory.GetFiles(folder, "*.hlsl");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                lines.Add(Check(device, path));
                File.WriteAllLines(outFile + ".partial", lines);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            lines.Add("FATAL | ERROR | - | " + e.Message);
        }
        File.WriteAllLines(outFile, lines);
        File.Delete(outFile + ".partial");
        return lines.Any(line => line.Contains("| ERROR |", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static string Check(GraphicsDevice device, string path)
    {
        string? error;
        float ms = -1;
        try
        {
            string source = File.ReadAllText(path);
            byte[]? code = Compile(device, source, path, out error);
            if (code != null)
            {
                using var shader = device.CreatePixelShader(code);
                ms = CostMeter.Measure(device, shader, FrameWidth, FrameHeight, Frames);
                error = LightSettingsProblem(device, source, path);
            }
        }
        catch (Exception e) when (e is IOException or SharpGen.Runtime.SharpGenException)
        {
            error = e.Message;
        }
        string firstLine = error == null ? "" : error.Split('\n')[0].Trim();
        string cost = error == null && ms >= 0 ? ms.ToString("0.00", CultureInfo.InvariantCulture) : "-";
        return $"{Path.GetFileName(path)} | {(error == null ? "OK" : "ERROR")} | {cost} | {firstLine}";
    }

    /// <summary>"@light" may only name settings the shader really has, and the light version has to compile too.</summary>
    private static string? LightSettingsProblem(GraphicsDevice device, string source, string path)
    {
        var light = ShaderHeader.Parse(source).LightSettings;
        var known = ShaderCompiler.ParseDefines(source).Select(define => define.Name).ToHashSet(StringComparer.Ordinal);
        string? unknown = light.Keys.FirstOrDefault(name => !known.Contains(name));
        if (unknown != null)
        {
            return "@light names a setting the shader does not have: " + unknown;
        }
        if (light.Count > 0 && Compile(device, ShaderCompiler.ApplyOverrides(source, light), path, out string? lightError) == null)
        {
            return "with its @light settings: " + lightError;
        }
        return null;
    }

    private static byte[]? Compile(GraphicsDevice device, string source, string path, out string? error) =>
        ShaderCompiler.Compile(ShaderCompiler.WithoutBenchmarkMode(source), path, "main", device.PixelProfile, out error);
}
