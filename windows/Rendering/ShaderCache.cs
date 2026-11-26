using Vortice.Direct3D11;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>What the cache has for a path right now.</summary>
/// <param name="Shader">usable shader; while <paramref name="Pending"/> it may be the previous version of an edited one</param>
/// <param name="Error">why the current source does not compile, else null</param>
/// <param name="Pending">the current source is still being compiled</param>
internal readonly record struct ShaderLookup(ID3D11PixelShader? Shader, string? Error, bool Pending);

/// <summary>
/// Library path -> compiled pixel shader. Belongs to the render thread; the compiling itself happens on the
/// <see cref="ShaderCompileQueue"/>'s thread and comes back as a posted job. Bytecode is kept, so a rebuilt graphics
/// device gets its shaders back without compiling.
/// </summary>
internal sealed class ShaderCache : IDisposable
{
    private sealed class Entry
    {
        public ID3D11PixelShader? Shader { get; set; }
        public byte[]? Bytecode { get; set; }
        public string? Error { get; set; }

        /// <summary>Bumped by every invalidation; the shader is current when <see cref="CompiledVersion"/> matches.</summary>
        public int Version { get; set; }
        public int CompiledVersion { get; set; } = -1;
        public bool Requested { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShaderCompileQueue _queue;
    private readonly Action<Action> _postToRenderThread;
    private GraphicsDevice? _device;

    public ShaderCache(ShaderCompileQueue queue, Action<Action> postToRenderThread)
    {
        _queue = queue;
        _postToRenderThread = postToRenderThread;
    }

    /// <summary>Changes whenever a lookup could give a different answer: targets resolve their shader again.</summary>
    public int Generation { get; private set; }

    /// <summary>A compile was queued for a path (render thread).</summary>
    public event Action<string>? CompileStarted;

    /// <summary>A compile finished: path and error, null when it worked (render thread).</summary>
    public event Action<string, string?>? Compiled;

    /// <summary>The shader for a path; queues a compile when the cache has no current one.</summary>
    /// <param name="urgent">a display or the preview waits for it (thumbnails are not urgent)</param>
    public ShaderLookup Resolve(string path, bool urgent)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = new Entry();
            _entries[path] = entry;
        }
        bool current = entry.CompiledVersion == entry.Version;
        if (current)
        {
            return new ShaderLookup(entry.Shader, entry.Error, false);
        }
        if (!entry.Requested)
        {
            Request(path, entry, urgent);
        }
        else if (urgent)
        {
            _queue.Promote(path);
        }
        return new ShaderLookup(entry.Shader, null, true);
    }

    /// <summary>The shader a path has right now, without asking for anything.</summary>
    public ID3D11PixelShader? Peek(string? path) =>
        path != null && _entries.TryGetValue(path, out var entry) ? entry.Shader : null;

    /// <summary>The source of a path (or of everything) changed. The old shader stays in use until the new one is ready.</summary>
    public void Invalidate(string? path)
    {
        if (path == null)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Version++;
            }
        }
        else if (_entries.TryGetValue(path, out var entry))
        {
            entry.Version++;
        }
        Generation++;
    }

    /// <summary>Drops every shader nothing shows or waits for.</summary>
    public void Trim(Func<string, bool> inUse)
    {
        foreach (string path in _entries.Keys.Where(path => !inUse(path)).ToList())
        {
            _entries[path].Shader?.Dispose();
            _entries.Remove(path);
        }
        Generation++;
    }

    /// <summary>A thumbnail is done with a path: its shader goes unless a display or the preview uses it.</summary>
    public void Release(string path, Func<string, bool> inUse)
    {
        if (!inUse(path) && _entries.Remove(path, out var entry))
        {
            entry.Shader?.Dispose();
            Generation++;
        }
    }

    /// <summary>A (new) device: shaders are created again from their bytecode.</summary>
    public void AttachDevice(GraphicsDevice device)
    {
        _device = device;
        foreach (var (path, entry) in _entries)
        {
            if (entry.Bytecode != null)
            {
                CreateShader(path, entry);
            }
        }
        Generation++;
    }

    /// <summary>The device is going away: its shader objects go with it, the bytecode stays.</summary>
    public void DetachDevice()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Shader?.Dispose();
            entry.Shader = null;
        }
        _device = null;
        Generation++;
    }

    public void Dispose() => DetachDevice();

    private void Request(string path, Entry entry, bool urgent)
    {
        if (_device == null)
        {
            return;
        }
        entry.Requested = true;
        int version = entry.Version;
        CompileStarted?.Invoke(path);
        _queue.Enqueue(path, _device.PixelProfile, urgent, output => _postToRenderThread(() => Complete(path, version, output)));
    }

    private void Complete(string path, int version, CompileOutput output)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            return;   // trimmed while it compiled
        }
        entry.Requested = false;
        Generation++;
        if (output.Skipped || version != entry.Version)
        {
            return;   // not wanted any more, or edited again meanwhile: whoever still wants it asks again
        }
        entry.Shader?.Dispose();
        entry.Shader = null;
        entry.Bytecode = output.Bytecode;
        entry.Error = output.Error;
        entry.CompiledVersion = version;
        if (entry.Bytecode != null && _device != null)
        {
            CreateShader(path, entry);
        }
        Compiled?.Invoke(path, entry.Error);
    }

    private void CreateShader(string path, Entry entry)
    {
        try
        {
            entry.Shader = _device!.CreatePixelShader(entry.Bytecode!);
            entry.Error = null;
        }
        catch (SharpGen.Runtime.SharpGenException e)
        {
            Log.Warn("create shader " + Path.GetFileName(path), e);
            entry.Shader = null;
            entry.Error = "The graphics driver rejected the compiled shader: " + e.Message;
        }
    }
}
