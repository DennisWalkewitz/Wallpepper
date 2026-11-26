using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Wallpepper.Ui;

/// <summary>How heavy a shader is for this PC (the dot on a card).</summary>
internal enum CostClass
{
    Unknown = -1,
    Light = 0,
    Medium = 1,
    Heavy = 2,
}

/// <summary>One gallery card: a shader or a web wallpaper. The card template binds to these properties.</summary>
internal sealed class ShaderItem : INotifyPropertyChanged
{
    private const float LightBelowMs = 4f;
    private const float MediumBelowMs = 10f;

    private string _name = "";
    private ImageSource? _thumb;
    private bool _isLive;
    private bool _hasError;
    private bool _isLoading = true;
    private bool _isFavourite;
    private bool _tooHeavy;
    private float _cost = -1;

    public ShaderItem(string path, bool isWeb)
    {
        Path = path;
        IsWeb = isWeb;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    /// <summary>A web wallpaper (*.wallpepperweb) rather than a shader.</summary>
    public bool IsWeb { get; }

    public IReadOnlySet<string> Tags { get; set; } = new HashSet<string>();

    public long LastUsed { get; set; }

    public bool TerminalOnly { get; set; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public ImageSource? Thumb
    {
        get => _thumb;
        set => Set(ref _thumb, value);
    }

    public bool IsLive
    {
        get => _isLive;
        set => Set(ref _isLive, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => Set(ref _hasError, value);
    }

    /// <summary>The picture is still being made (the card shimmers).</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => Set(ref _isLoading, value);
    }

    public bool IsFavourite
    {
        get => _isFavourite;
        set => Set(ref _isFavourite, value);
    }

    /// <summary>GPU milliseconds per 1080p frame, -1 = not measured yet.</summary>
    public float Cost
    {
        get => _cost;
        set
        {
            if (Set(ref _cost, value))
            {
                RaiseCostChanged();
            }
        }
    }

    /// <summary>Cannot be fitted into the resource budget on this PC.</summary>
    public bool TooHeavy
    {
        get => _tooHeavy;
        set
        {
            if (Set(ref _tooHeavy, value))
            {
                RaiseCostChanged();
            }
        }
    }

    public CostClass CostClass
    {
        get
        {
            if (_cost < 0)
            {
                return CostClass.Unknown;
            }
            if (_tooHeavy)
            {
                return CostClass.Heavy;
            }
            return _cost < LightBelowMs ? CostClass.Light : _cost < MediumBelowMs ? CostClass.Medium : CostClass.Heavy;
        }
    }

    /// <summary>For the card template's triggers: -1 unknown, 0 light, 1 medium, 2 heavy.</summary>
    public int CostLevel => (int)CostClass;

    public string CostLabel => _tooHeavy && _cost >= 0 ? "Too heavy for this PC" : CostClass == CostClass.Unknown ? "" : CostClass.ToString();

    public string? CostTip
    {
        get
        {
            if (_cost < 0)
            {
                return null;
            }
            string tip = $"About {_cost:0.0} ms of GPU time per 1080p frame on this PC";
            return _tooHeavy ? tip + ". Even at the lowest resolution and frame rate that is more than the resource level allows." : tip;
        }
    }

    private void RaiseCostChanged()
    {
        Raise(nameof(CostLabel));
        Raise(nameof(CostLevel));
        Raise(nameof(CostTip));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Raise(property);
        return true;
    }

    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
