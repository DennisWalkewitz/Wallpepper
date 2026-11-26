using Wallpepper.Desktop.Performance;
using Wallpepper.Library;
using Wallpepper.Rendering;

namespace Wallpepper;

/// <summary>
/// The library window's live preview: one more render target, with a small GPU budget of its own
/// (<see cref="PreviewPlanner"/>). UI thread only.
/// </summary>
internal sealed class LivePreview : IDisposable
{
    private readonly Renderer _renderer;
    private readonly ShaderSettings _shaders;
    private readonly Func<ResourceLevel?> _level;
    private RenderTarget? _target;

    public LivePreview(Renderer renderer, ShaderSettings shaders, Func<ResourceLevel?> level)
    {
        _renderer = renderer;
        _shaders = shaders;
        _level = level;
    }

    /// <summary>Frames presented so far (self-test).</summary>
    public int Frames => _target?.Stats.Frames ?? 0;

    /// <summary>The last frame showed this shader (self-test).</summary>
    public bool Shows(string path) => string.Equals(_target?.Stats.DrawnPath, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>The preview draws into this window from now on.</summary>
    public void Attach(IntPtr hwnd)
    {
        Dispose();
        _target = new RenderTarget(hwnd, isPreview: true, new TargetSettings { Paused = true });
        _renderer.AddTarget(_target);
    }

    /// <summary>Shows a shader (null: nothing), fitted to the preview's budget.</summary>
    /// <param name="pixels">size of the preview in device pixels</param>
    public void Show(string? shaderPath, double pixels)
    {
        if (_target == null)
        {
            return;
        }
        var plan = new Fit(1f, 30, true);
        if (shaderPath != null)
        {
            plan = PreviewPlanner.Plan(_shaders.KnownCost(shaderPath, _shaders.IsLight(shaderPath)), pixels, _level());
        }
        _target.Update(settings => settings with { ShaderPath = shaderPath, Scale = plan.Scale, FpsCap = plan.Fps });
        _renderer.Wake();
    }

    /// <summary>A hidden preview draws nothing.</summary>
    public void SetVisible(bool visible)
    {
        if (_target != null && _target.Settings.Paused == visible)
        {
            _target.Update(settings => settings with { Paused = !visible });
            _renderer.Wake();
        }
    }

    public void Dispose()
    {
        if (_target != null)
        {
            _renderer.RemoveTarget(_target);
            _target = null;
        }
    }
}
