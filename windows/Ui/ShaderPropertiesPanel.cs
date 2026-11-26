using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpepper.Library;
using Wallpepper.Settings;
using Wallpepper.Ui.Settings;
using Shapes = System.Windows.Shapes;

namespace Wallpepper.Ui;

/// <summary>
/// The "Properties" of the selected shader: one control per setting (<see cref="ShaderParam"/>), in the shader's own
/// groups, each with its description. The file is read and parsed off the UI thread and the controls are created a
/// few at a time, so that browsing the gallery never waits for a long list of sliders.
/// </summary>
internal sealed class ShaderPropertiesPanel
{
    /// <summary>Recompiling on every mouse move would stutter: the latest value wins about ten times a second.</summary>
    private const int ApplyDelayMs = 90;
    private const int RowsPerStep = 3;

    private readonly LibraryWindow _view;
    private readonly App _app;
    private readonly DispatcherTimer _applyTimer;
    private Action? _pendingChange;
    private string? _path;
    private int _buildId;

    public ShaderPropertiesPanel(LibraryWindow view, App app)
    {
        _view = view;
        _app = app;
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ApplyDelayMs) };
        _applyTimer.Tick += (_, _) => Flush();
        view.ResetBtn.Click += (_, _) => Reset();
    }

    /// <summary>The user changed a value of the shader (path): its picture and cost are out of date.</summary>
    public event Action<string>? ValuesChanged;

    /// <summary>Shows the settings of a shader; null clears the panel.</summary>
    public void Show(string? path)
    {
        Flush();
        _path = path;
        int build = ++_buildId;
        _view.PropsPanel.Children.Clear();
        if (path == null)
        {
            return;
        }
        _view.ResetBtn.IsEnabled = _app.Shaders.OverridesFor(path).Count > 0;
        Task.Run(() =>
        {
            IReadOnlyList<ShaderParamSection> sections = Try.Get<IReadOnlyList<ShaderParamSection>>(
                "read the settings of " + Path.GetFileName(path),
                () => ShaderParamLayout.Parse(File.ReadAllText(path)),
                Array.Empty<ShaderParamSection>());
            _view.Dispatcher.InvokeAsync(() => BeginBuild(build, path, sections), DispatcherPriority.Background);
        });
    }

    /// <summary>Applies a change that is still waiting for the throttle (the window closes, another shader is selected).</summary>
    public void Flush()
    {
        _applyTimer.Stop();
        Action? change = _pendingChange;
        _pendingChange = null;
        change?.Invoke();
    }

    private void Reset()
    {
        if (_path == null)
        {
            return;
        }
        _pendingChange = null;
        _app.Shaders.ResetOverrides(_path);
        ValuesChanged?.Invoke(_path);
        Show(_path);
    }

    private void BeginBuild(int build, string path, IReadOnlyList<ShaderParamSection> sections)
    {
        if (build != _buildId)
        {
            return;   // another shader was selected meanwhile
        }
        bool any = sections.Count > 0;
        _view.PropsGroup.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _view.NoProps.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _view.ResetBtn.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ContinueBuild(build, Rows(path, sections).GetEnumerator());
    }

    /// <summary>
    /// Adds a few rows, then lets the window lay them out and handle input before the next few. A control costs most
    /// when it is laid out for the first time, so the number of new rows per step is what keeps a step short.
    /// </summary>
    private void ContinueBuild(int build, IEnumerator<UIElement> rows)
    {
        if (build != _buildId)
        {
            return;
        }
        for (int added = 0; added < RowsPerStep; added++)
        {
            if (!rows.MoveNext())
            {
                return;
            }
            _view.PropsPanel.Children.Add(rows.Current);
        }
        _view.Dispatcher.InvokeAsync(() => ContinueBuild(build, rows), DispatcherPriority.Background);
    }

    private IEnumerable<UIElement> Rows(string path, IReadOnlyList<ShaderParamSection> sections)
    {
        bool first = true;
        foreach (ShaderParamSection section in sections)
        {
            if (section.Title != null || !first)
            {
                yield return SectionHeader(section.Title, first);
            }
            bool firstInSection = true;
            foreach (ShaderParam param in section.Params)
            {
                if (!firstInSection)
                {
                    yield return new Border { Style = Theme.Style("Hairline") };
                }
                firstInSection = false;
                yield return param.Kind switch
                {
                    ShaderParamKind.Colour => ColourRow(path, param),
                    ShaderParamKind.Toggle => ToggleRow(path, param),
                    _ => NumberRow(path, param),
                };
            }
            first = false;
        }
    }

    /// <summary>The shader's own heading for a group; a group without one is set off by a wider gap.</summary>
    private static UIElement SectionHeader(string? title, bool first)
    {
        if (title == null)
        {
            return new Border { Height = 8, Background = Theme.Brush("Well") };
        }
        return new Border
        {
            Background = Theme.Brush("Well"),
            Padding = new Thickness(14, first ? 8 : 10, 14, 7),
            Child = new TextBlock { Text = title.ToUpperInvariant(), Style = Theme.Style("Caption"), Margin = new Thickness(0) },
        };
    }

    private string Current(string path, ShaderParam param, int define = 0) =>
        _app.Shaders.OverridesFor(path).TryGetValue(param.Defines[define], out string? value) ? value : param.Defaults[define];

    private bool IsChanged(string path, ShaderParam param) => param.Defines.Any(define => _app.Shaders.OverridesFor(path).ContainsKey(define));

    /// <summary>Label, a dot that marks a value changed from the shader's default, and the control on the right.</summary>
    private static Grid Head(ShaderParam param, bool changed, out Shapes.Ellipse dot, UIElement? right)
    {
        dot = new Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Theme.Brush("Accent"),
            Margin = new Thickness(7, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = changed ? Visibility.Visible : Visibility.Hidden,
            ToolTip = "Changed from the shader's default",
        };
        var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        label.Children.Add(new TextBlock { Text = param.Label, Style = Theme.Style("RowLabel") });
        label.Children.Add(dot);
        var head = new Grid();
        head.Children.Add(label);
        if (right != null)
        {
            head.Children.Add(right);
        }
        return head;
    }

    /// <summary>A row: head, the control below it if it needs the width, and the shader's description as helper text.</summary>
    private static StackPanel Row(ShaderParam param, Grid head, UIElement? below)
    {
        var row = new StackPanel { Margin = new Thickness(14, 10, 14, 9), ToolTip = param.Description };
        row.Children.Add(head);
        if (below != null)
        {
            row.Children.Add(below);
        }
        if (param.Description != null)
        {
            row.Children.Add(SettingsRows.Hint(param.Description, new Thickness(0, 3, 0, 0)));
        }
        return row;
    }

    private UIElement ToggleRow(string path, ShaderParam param)
    {
        var toggle = new CheckBox
        {
            Style = Theme.Style("Switch"),
            IsChecked = Current(path, param).TrimEnd('f', 'F') != "0",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid head = Head(param, IsChanged(path, param), out Shapes.Ellipse dot, toggle);
        toggle.Click += (_, _) =>
        {
            string value = toggle.IsChecked == true ? "1" : "0";
            bool isDefault = value == param.Defaults[0];
            dot.Visibility = isDefault ? Visibility.Hidden : Visibility.Visible;
            Apply(path, new Dictionary<string, string?> { [param.Name] = isDefault ? null : value });
        };
        return Row(param, head, null);
    }

    private UIElement NumberRow(string path, ShaderParam param)
    {
        double value = ShaderParam.ParseNumber(Current(path, param), param.Default);
        TextBlock shown = SettingsRows.Value(param.Display(value));
        Grid head = Head(param, IsChanged(path, param), out Shapes.Ellipse dot, shown);
        var slider = new Slider
        {
            Minimum = Math.Min(param.Minimum, value),
            Maximum = Math.Max(param.Maximum, value),
            Value = value,
            Margin = new Thickness(0, 6, 0, 0),
            SmallChange = param.Step,
            LargeChange = param.Step * 10,
            IsSnapToTickEnabled = param.IsInteger,
            TickFrequency = param.IsInteger ? 1 : 0,
        };
        slider.ValueChanged += (_, _) =>
        {
            string? literal = param.Literal(slider.Value);
            shown.Text = param.Display(param.IsInteger ? Math.Round(slider.Value) : slider.Value);
            dot.Visibility = literal == null ? Visibility.Hidden : Visibility.Visible;
            ApplySoon(path, new Dictionary<string, string?> { [param.Name] = literal });
        };
        slider.MouseDoubleClick += (_, _) => slider.Value = param.Default;   // double-click: back to the shader's default
        return Row(param, head, slider);
    }

    private UIElement ColourRow(string path, ShaderParam param)
    {
        Color Shown()
        {
            byte Channel(int define) => (byte)Math.Round(Math.Clamp(ShaderParam.ParseNumber(Current(path, param, define), 0), 0, 1) * 255);
            return Color.FromRgb(Channel(0), Channel(1), Channel(2));
        }

        Button swatch = ColorPicker.Swatch(Shown());
        Grid head = Head(param, IsChanged(path, param), out Shapes.Ellipse dot, swatch);
        swatch.Click += (_, _) => ColorPicker.Show(swatch, Shown(), colour =>
        {
            ColorPicker.SetSwatch(swatch, colour);
            dot.Visibility = Visibility.Visible;
            string Literal(byte channel) => (channel / 255.0).ToString("0.0##", CultureInfo.InvariantCulture);
            ApplySoon(path, new Dictionary<string, string?>
            {
                [param.Defines[0]] = Literal(colour.R),
                [param.Defines[1]] = Literal(colour.G),
                [param.Defines[2]] = Literal(colour.B),
            });
        });
        return Row(param, head, null);
    }

    private void ApplySoon(string path, Dictionary<string, string?> values)
    {
        _pendingChange = () => Apply(path, values);
        if (!_applyTimer.IsEnabled)
        {
            _applyTimer.Start();
        }
    }

    private void Apply(string path, Dictionary<string, string?> values)
    {
        _app.Shaders.SetOverrides(path, values);
        if (path == _path)
        {
            _view.ResetBtn.IsEnabled = _app.Shaders.OverridesFor(path).Count > 0;
        }
        ValuesChanged?.Invoke(path);
    }
}
