namespace Wallpepper;

/// <summary>
/// What is known about each library item's ability to show: compiling right now, or the error of its last compile
/// (for a web wallpaper: why its page did not start). UI thread only.
/// </summary>
internal sealed class CompileStatus
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _compiling = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The state of an item changed (path).</summary>
    public event Action<string>? Changed;

    public bool IsCompiling(string? path) => path != null && _compiling.Contains(path);

    public bool HasError(string? path) => path != null && _errors.ContainsKey(path);

    public string? ErrorOf(string? path) => path != null && _errors.TryGetValue(path, out string? error) ? error : null;

    public void Started(string path)
    {
        if (_compiling.Add(path))
        {
            Changed?.Invoke(path);
        }
    }

    /// <summary>A compile (or a page start) ended; <paramref name="error"/> null = it works.</summary>
    public void Finished(string path, string? error)
    {
        _compiling.Remove(path);
        if (error == null)
        {
            _errors.Remove(path);
        }
        else
        {
            _errors[path] = error;
        }
        Changed?.Invoke(path);
    }

    public void Forget(string path)
    {
        _compiling.Remove(path);
        _errors.Remove(path);
    }
}
