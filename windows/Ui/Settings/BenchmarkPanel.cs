using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wallpepper.Rendering;
using Wallpepper.Settings;

namespace Wallpepper.Ui.Settings;

/// <summary>
/// The benchmark on the performance page (docs/BENCHMARK.md). Runs "Wallpepper.exe --bench --progress" as a child
/// process: the same code path as the command line, and the wallpaper pauses by itself while that process exists.
/// </summary>
internal sealed class BenchmarkPanel
{
    private const int RefreshMs = 250;
    private const double MinFramesForFullBar = 3;

    private readonly App _app;
    private readonly Action<string, bool> _toast;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;
    private Process? _process;
    private bool _quick;
    private string? _status;
    private string? _error;
    private double _fraction;
    private Stopwatch? _waiting;

    private Button? _runButton;
    private Border? _modes;
    private Border? _fill;
    private Grid? _track;
    private TextBlock? _statusText;
    private StackPanel? _result;

    public BenchmarkPanel(App app, Action<string, bool> toast)
    {
        _app = app;
        _toast = toast;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshMs) };
        _timer.Tick += (_, _) => Render();
    }

    private bool Running => _process != null;

    /// <summary>Builds the panel (again); a running benchmark carries on.</summary>
    public UIElement Build()
    {
        _modes = SettingsRows.Segments(new[] { "Full  ·  4K, 10 s", "Quick  ·  720p, 5 s" }, _quick ? 1 : 0, index => _quick = index == 1);
        _runButton = new Button
        {
            Style = Theme.Style("Primary"),
            Height = 32,
            MinWidth = 132,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _runButton.Click += (_, _) =>
        {
            if (Running)
            {
                Cancel();
            }
            else
            {
                Start();
            }
        };
        _statusText = SettingsRows.Hint("", new Thickness(12, 0, 0, 0));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        var run = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_runButton, Dock.Left);
        run.Children.Add(_runButton);
        run.Children.Add(_statusText);

        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.Brush("Accent"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        _track = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 0) };
        _track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.Brush("Well") });
        _track.Children.Add(_fill);
        _result = new StackPanel();

        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        body.Children.Add(SettingsRows.Hint(
            "Path-traces a fixed scene off screen and measures how many megapixels per second this GPU renders. The wallpaper pauses while it runs.",
            new Thickness(0, 0, 0, 12)));
        body.Children.Add(_modes);
        body.Children.Add(run);
        body.Children.Add(_track);
        body.Children.Add(_result);
        Render();
        return SettingsRows.Group(body);
    }

    /// <summary>The window closes: a running benchmark is stopped.</summary>
    public void Close()
    {
        _timer.Stop();
        var process = _process;
        _process = null;
        if (process != null)
        {
            Kill(process);
        }
    }

    private void Start()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("--bench");
        if (_quick)
        {
            start.ArgumentList.Add("--quick");
        }
        start.ArgumentList.Add("--progress");
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            process = null;
            Log.Warn("start the benchmark", e);
        }
        if (process == null)
        {
            _toast("The benchmark could not be started", true);
            return;
        }
        _process = process;
        _error = null;
        _status = "Starting";
        _fraction = 0;
        _waiting = Stopwatch.StartNew();
        _timer.Start();
        Render();
        Task.Run(() => ReadOutput(process));
    }

    /// <summary>Reads the child's lines (thread pool): "@ ..." progress, and at the end the JSON result.</summary>
    private void ReadOutput(Process process)
    {
        string? json = null;
        try
        {
            while (process.StandardOutput.ReadLine() is string line)
            {
                if (line.StartsWith('@'))
                {
                    _ui.InvokeAsync(() => OnProgress(process, line));
                }
                else if (line.StartsWith('{'))
                {
                    json = line;
                }
            }
            process.WaitForExit();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            Log.Warn("read the benchmark's output", e);
        }
        _ui.InvokeAsync(() => OnFinished(process, json));
    }

    /// <summary>"@ phase frames seconds target" from the child.</summary>
    private void OnProgress(Process process, string line)
    {
        string[] parts = line.Split(' ');
        if (!ReferenceEquals(process, _process) || parts.Length < 5)
        {
            return;
        }
        int.TryParse(parts[2], out int frames);
        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds);
        double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double target);
        if (parts[1] == "warmup")
        {
            _status = "Warming up";
            _waiting = Stopwatch.StartNew();
        }
        else if (parts[1] == "frame")
        {
            _status = $"Frame {frames} done · {seconds.ToString("0.0", CultureInfo.InvariantCulture)} of {target:0} s";
            _fraction = Math.Min(1, Math.Min(seconds / Math.Max(1, target), frames / MinFramesForFullBar));
            _waiting = null;
        }
        Render();
    }

    private void OnFinished(Process process, string? json)
    {
        bool current = ReferenceEquals(process, _process);
        process.Dispose();
        if (!current)
        {
            return;   // cancelled, or the window was closed
        }
        _process = null;
        _timer.Stop();
        BenchResult? result = BenchResult.Parse(json);
        if (result != null && result.Error == null)
        {
            _app.Settings.Change(SettingsArea.Governor, config => config.LastBenchmark = result);   // the wallpaper comes back at once
            _status = "Finished";
            _fraction = 1;
        }
        else
        {
            _error = result?.Error ?? "The benchmark ended without a result.";
            _status = null;
            _fraction = 0;
            _app.Governor.Evaluate();
        }
        Render();
    }

    private void Cancel()
    {
        var process = _process;
        if (process == null)
        {
            return;
        }
        _process = null;
        _timer.Stop();
        Kill(process);
        _status = "Cancelled";
        _fraction = 0;
        Render();
    }

    private static void Kill(Process process) => Try.Run("stop the benchmark", process.Kill);

    private void Render()
    {
        if (_runButton == null || _modes == null || _statusText == null || _track == null || _fill == null || _result == null)
        {
            return;
        }
        _runButton.Content = Running ? "Cancel" : "Run benchmark";
        _modes.IsEnabled = !Running;
        _modes.Opacity = Running ? 0.5 : 1;
        bool waitingLong = Running && _waiting != null && _waiting.Elapsed.TotalSeconds >= 2;
        string wait = waitingLong ? $" · {_waiting!.Elapsed.TotalSeconds:0} s" : "";
        _statusText.Text = Running ? _status + wait + "…" : _status ?? "";
        _track.Visibility = Running ? Visibility.Visible : Visibility.Collapsed;
        _fill.Width = Math.Max(0, _track.ActualWidth * _fraction);

        _result.Children.Clear();
        if (Running)
        {
            return;
        }
        if (_error != null)
        {
            _result.Children.Add(new TextBlock
            {
                Text = _error,
                Foreground = Theme.Brush("Danger"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
        else if (_app.Settings.Config.LastBenchmark is BenchResult last)
        {
            ShowResult(_result, last);
        }
    }

    private void ShowResult(StackPanel panel, BenchResult result)
    {
        string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        head.Children.Add(new TextBlock
        {
            Text = result.Score.ToString(CultureInfo.InvariantCulture),
            FontFamily = Theme.Font("UiDisplay"),
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        head.Children.Add(new TextBlock
        {
            Text = "points",
            Foreground = Theme.Brush("Text2"),
            Margin = new Thickness(8, 0, 0, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        string label = result.Label.Replace(", ", " · ");
        if (label.Length > 0)
        {
            head.Children.Add(new Border
            {
                Background = Theme.Brush("AccentSoft"),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1, 8, 2),
                Margin = new Thickness(12, 0, 0, 8),
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = label, FontSize = 11, Foreground = Theme.Brush("Accent") },
                ToolTip = result.Calibrated
                    ? null
                    : "The scale is anchored to an RTX 5090 (10000 points). Until one has been measured, 10000 points stand for 60 frames per second in 4K.",
            });
        }
        panel.Children.Add(head);

        string detail = $"{Number(result.MPixelsPerSecond, "0.00")} megapixels per second\n"
            + $"{Number(result.AvgMs, "0.0")} ms per frame ({Number(result.MinMs, "0.0")} to {Number(result.MaxMs, "0.0")}) · "
            + $"{result.Frames} frames at {result.Width} × {result.Height}\n{result.Gpu}";
        TextBlock text = SettingsRows.Hint(detail, new Thickness(0, 6, 0, 0));
        text.FontSize = 12;
        text.LineHeight = 18;
        text.Foreground = Theme.Brush("Text2");
        panel.Children.Add(text);

        var copy = new Button
        {
            Content = "Copy result",
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
        };
        copy.Click += (_, _) =>
        {
            bool copied = Try.Run("copy the benchmark result", () => Clipboard.SetText(result.OneLine() + Environment.NewLine + result.ToJson()));
            _toast(copied ? "Benchmark result copied" : "Could not copy the result", !copied);
        };
        panel.Children.Add(copy);
    }
}
