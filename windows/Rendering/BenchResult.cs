using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallpepper.Rendering;

/// <summary>
/// Result of the GPU benchmark (docs/BENCHMARK.md, procedure version 0): the JSON object that "--bench" writes, that
/// the settings page reads back from the child process, and that is kept in the settings.
/// </summary>
internal sealed class BenchResult
{
    public const int ProcedureVersion = 0;

    /// <summary>Megapixels per second of the reference card (RTX 5090). Provisional until measured on a real one.</summary>
    public const double ReferenceMPixelsPerSecond = 497.664;
    public const int ReferenceScore = 10000;
    public const bool IsCalibrated = false;

    private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

    [JsonPropertyName("benchmark")]
    public string Benchmark { get; set; } = "wallpepper-pathtracer";

    [JsonPropertyName("version")]
    public int Version { get; set; } = ProcedureVersion;

    [JsonPropertyName("calibrated")]
    public bool Calibrated { get; set; } = IsCalibrated;

    /// <summary>"full" (4K, 10 s), "quick" (720p, 5 s) or "custom" (another size or shader).</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "full";

    [JsonPropertyName("gpu")]
    public string? Gpu { get; set; }

    [JsonPropertyName("driver")]
    public string? Driver { get; set; }

    [JsonPropertyName("api")]
    public string Api { get; set; } = "d3d11";

    [JsonPropertyName("os")]
    public string? Os { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("frames")]
    public int Frames { get; set; }

    [JsonPropertyName("seconds")]
    public double Seconds { get; set; }

    [JsonPropertyName("frame_ms_avg")]
    public double AvgMs { get; set; }

    [JsonPropertyName("frame_ms_min")]
    public double MinMs { get; set; }

    [JsonPropertyName("frame_ms_max")]
    public double MaxMs { get; set; }

    [JsonPropertyName("mpixels_per_second")]
    public double MPixelsPerSecond { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>"provisional", "quick", "provisional, quick" ... or empty for a calibrated full run.</summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            var parts = new List<string>();
            if (!Calibrated)
            {
                parts.Add("provisional");
            }
            if (Mode != "full")
            {
                parts.Add(Mode);
            }
            return string.Join(", ", parts);
        }
    }

    public static BenchResult Failed(string error, string mode) => new BenchResult { Error = error, Mode = mode };

    public static BenchResult? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<BenchResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson(bool oneLine = false) => JsonSerializer.Serialize(this, oneLine ? null : Indented);

    /// <summary>The human-readable result block.</summary>
    public string Summary()
    {
        if (Error != null)
        {
            return "Wallpepper GPU benchmark failed: " + Error;
        }
        string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        text.AppendLine("Wallpepper GPU benchmark v" + Version + (Label.Length > 0 ? " (" + Label + ")" : ""));
        text.AppendLine("GPU     " + Gpu + (string.IsNullOrEmpty(Driver) ? "" : ", driver " + Driver) + ", Direct3D 11");
        text.AppendLine("Size    " + Width + " x " + Height);
        text.AppendLine($"Frames  {Frames} in {Number(Seconds, "0.00")} s  ({Number(AvgMs, "0.0")} ms average, "
            + $"{Number(MinMs, "0.0")} min, {Number(MaxMs, "0.0")} max)");
        text.AppendLine("Speed   " + Number(MPixelsPerSecond, "0.00") + " megapixels per second");
        text.Append("Score   " + Score + (Calibrated ? "" : $"  (provisional: {ReferenceScore} = 60 frames per second in 4K)"));
        return text.ToString();
    }

    /// <summary>One line for the clipboard.</summary>
    public string OneLine()
    {
        string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        return "Wallpepper benchmark v" + Version + (Label.Length > 0 ? " (" + Label + ")" : "") + ": " + Score + " points, "
            + Number(MPixelsPerSecond, "0.00") + " MP/s, " + Number(AvgMs, "0.0") + " ms per frame, " + Gpu;
    }
}
