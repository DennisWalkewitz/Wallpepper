using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Wallpepper.Interop;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Commands;

/// <summary>
/// "--bench [--quick] [--json] [--out FILE] [--size WxH]": runs the benchmark on its own device and reports the result
/// ("--out" writes the JSON object; "--json" makes standard output the JSON object alone). "--progress" (used by the
/// settings page) prints "@ phase frames seconds target" lines and the JSON on one line. See docs/BENCHMARK.md.
/// </summary>
internal static class BenchCommand
{
    private const int FullWidth = 3840;
    private const int FullHeight = 2160;
    private const int QuickWidth = 1280;
    private const int QuickHeight = 720;
    private const int FullSeconds = 10;
    private const int QuickSeconds = 5;
    private const int MinSize = 16;
    private const int MaxSize = 16384;
    private const int ExitOk = 0;
    private const int ExitFailed = 1;
    private const int ExitOutFileFailed = 2;

    /// <summary>A running Wallpepper pauses its wallpaper while the benchmark is announced; it looks once a second.</summary>
    private const int WaitForAppMs = 1500;

    public static int Run(CommandLine args)
    {
        AttachToParentConsole();
        BenchResult result = Measure(args);
        result.Os = RuntimeInformation.OSDescription;
        Report(result, args);
        if (args.OutFile != null)
        {
            try
            {
                File.WriteAllText(args.OutFile, result.ToJson() + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("write the benchmark result", e);
                return ExitOutFileFailed;
            }
        }
        return result.Error == null ? ExitOk : ExitFailed;
    }

    private static BenchResult Measure(CommandLine args)
    {
        int width = args.Quick ? QuickWidth : FullWidth;
        int height = args.Quick ? QuickHeight : FullHeight;
        int seconds = args.Quick ? QuickSeconds : FullSeconds;
        bool custom = args.ShaderFile != null;
        if (args.Size != null)
        {
            if (!TryParseSize(args.Size, out width, out height))
            {
                return BenchResult.Failed("--size expects WIDTHxHEIGHT, for example 1920x1080", "custom");
            }
            custom = true;
        }
        string mode = custom ? "custom" : args.Quick ? "quick" : "full";

        try
        {
            string source = Benchmark.LoadSource(args.ShaderFile);
            using var announcement = SingleInstance.AnnounceBenchmark();
            if (SingleInstance.IsRunning)
            {
                Thread.Sleep(WaitForAppMs);
            }
            Action<string, int, double>? progress = null;
            if (args.Progress)
            {
                Console.Out.WriteLine("@ start 0 0 0");
                progress = (phase, frames, elapsed) =>
                {
                    Console.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "@ {0} {1} {2:0.00} {3}", phase, frames, elapsed, seconds));
                    Console.Out.Flush();
                };
            }
            using var device = GraphicsDevice.Create();
            BenchResult result = Benchmark.Run(device, source, width, height, seconds, progress);
            result.Mode = mode;
            return result;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return BenchResult.Failed(e.Message, mode);
        }
    }

    /// <summary>
    /// "--json": standard output carries nothing but the JSON object, the summary goes to standard error.
    /// "--progress": the JSON object on one line.
    /// </summary>
    private static void Report(BenchResult result, CommandLine args)
    {
        try
        {
            if (args.Progress)
            {
                Console.Out.WriteLine(result.ToJson(oneLine: true));
            }
            else
            {
                var summary = args.Json ? Console.Error : Console.Out;
                summary.WriteLine();
                summary.WriteLine(result.Summary());
                if (result.Error == null && (result.Gpu ?? "").Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                {
                    summary.WriteLine();
                    summary.WriteLine($"WARNING: {result.Gpu} is a software renderer. No GPU was used, so this score is meaningless.");
                }
                summary.Flush();
                if (args.Json)
                {
                    Console.Out.WriteLine(result.ToJson());
                }
            }
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // no console to write to (started from a shortcut): "--out" still gets the result
        }
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = text.ToLowerInvariant().Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width >= MinSize && height >= MinSize && width <= MaxSize && height <= MaxSize;
    }

    /// <summary>A GUI exe has no console of its own: use the parent's unless output is already redirected.</summary>
    private static void AttachToParentConsole()
    {
        IntPtr output = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
        bool redirected = output != IntPtr.Zero && output != Kernel32.INVALID_HANDLE_VALUE
            && Kernel32.GetFileType(output) != Kernel32.FILE_TYPE_UNKNOWN;
        if (!redirected)
        {
            Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        }
    }
}
