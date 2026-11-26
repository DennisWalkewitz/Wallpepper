using System.Threading;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>What a compile produced: bytecode, or an error, or nothing because nobody wanted the shader any more.</summary>
internal readonly record struct CompileOutput(byte[]? Bytecode, string? Error, bool Skipped);

/// <summary>
/// Compiles shaders on one background thread of low priority, so that neither the wallpaper nor the window ever waits
/// for the HLSL compiler. What a display or the preview needs comes first, newest request first; thumbnails follow in
/// order. A request nobody wants any more when its turn comes is skipped.
/// </summary>
internal sealed class ShaderCompileQueue : IDisposable
{
    private sealed class Job
    {
        public required string Path { get; init; }
        public required string Profile { get; init; }
        public required Action<CompileOutput> Done { get; init; }
        public bool Urgent { get; set; }
        public long Order { get; set; }
    }

    private readonly object _gate = new object();
    private readonly List<Job> _jobs = new List<Job>();
    private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
    private readonly Func<string, string> _loadSource;
    private readonly Func<string, bool> _isWanted;
    private Thread? _thread;
    private long _order;
    private volatile bool _stopped;

    /// <param name="loadSource">final HLSL source for a library path (settings applied); called on the compile thread</param>
    /// <param name="isWanted">whether anything still needs the shader of a path; called on the compile thread</param>
    public ShaderCompileQueue(Func<string, string> loadSource, Func<string, bool> isWanted)
    {
        _loadSource = loadSource;
        _isWanted = isWanted;
    }

    /// <summary>Queues a compile; <paramref name="done"/> is called on the compile thread.</summary>
    public void Enqueue(string path, string profile, bool urgent, Action<CompileOutput> done)
    {
        lock (_gate)
        {
            _jobs.Add(new Job { Path = path, Profile = profile, Done = done, Urgent = urgent, Order = ++_order });
            if (_thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Wallpepper compile", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }
        _available.Release();
    }

    /// <summary>A display or the preview now waits for a shader that was queued for a thumbnail.</summary>
    public void Promote(string path)
    {
        lock (_gate)
        {
            foreach (var job in _jobs)
            {
                if (!job.Urgent && string.Equals(job.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    job.Urgent = true;
                    job.Order = ++_order;
                }
            }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        _available.Release();
    }

    private void Run()
    {
        while (true)
        {
            _available.Wait();
            if (_stopped)
            {
                return;
            }
            Job? job = Take();
            if (job == null)
            {
                continue;
            }
            Thread.CurrentThread.Priority = job.Urgent ? ThreadPriority.BelowNormal : ThreadPriority.Lowest;
            CompileOutput output;
            try
            {
                output = _isWanted(job.Path) ? Compile(job) : new CompileOutput(null, null, true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // a missing or unreadable file, or the compiler itself failing: the shader has an error, the app goes on
                output = new CompileOutput(null, e.Message, false);
            }
            try
            {
                job.Done(output);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Error("compile result", e);
            }
        }
    }

    private Job? Take()
    {
        lock (_gate)
        {
            if (_jobs.Count == 0)
            {
                return null;
            }
            Job best = _jobs[0];
            foreach (var job in _jobs)
            {
                bool better = job.Urgent != best.Urgent ? job.Urgent : job.Urgent ? job.Order > best.Order : job.Order < best.Order;
                if (better)
                {
                    best = job;
                }
            }
            _jobs.Remove(best);
            return best;
        }
    }

    private CompileOutput Compile(Job job)
    {
        string source = _loadSource(job.Path);
        string key = CompiledShaderStore.KeyFor(source, job.Profile);
        byte[]? bytecode = CompiledShaderStore.TryLoad(key);
        if (bytecode != null)
        {
            return new CompileOutput(bytecode, null, false);
        }
        bytecode = ShaderCompiler.Compile(source, job.Path, "main", job.Profile, out string? error);
        if (bytecode == null)
        {
            return new CompileOutput(null, error ?? "compile error", false);
        }
        CompiledShaderStore.Save(key, bytecode);
        return new CompileOutput(bytecode, null, false);
    }
}
