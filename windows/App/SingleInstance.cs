using System.Threading;
using Wallpepper.Settings;

namespace Wallpepper;

/// <summary>
/// One Wallpepper per session, and the named objects other processes use to talk to it: a second launch asks it to
/// show its window, an update or uninstall asks it to exit, the benchmark asks it to stand still.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Wallpepper.Instance";
    private const string ShowEventName = @"Local\Wallpepper.Show";
    private const string ExitEventName = @"Local\Wallpepper.Exit";
    private const string StartedEventName = @"Local\Wallpepper.Started";
    private const string BenchmarkMutexName = @"Local\Wallpepper.Bench";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    private readonly EventWaitHandle _exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
    private readonly ManualResetEvent _stop = new ManualResetEvent(false);

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Another process asked for the library window (raised on a background thread).</summary>
    public event Action? ShowRequested;

    /// <summary>Another process asked this instance to exit (raised on a background thread).</summary>
    public event Action? ExitRequested;

    /// <summary>A Wallpepper instance is running in this session.</summary>
    public static bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(MutexName, out Mutex? other))
            {
                return false;
            }
            other.Dispose();
            return true;
        }
    }

    /// <summary>"Wallpepper.exe --bench" is running and needs the GPU to itself.</summary>
    public static bool BenchmarkIsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(BenchmarkMutexName, out Mutex? benchmark))
            {
                return false;
            }
            benchmark.Dispose();
            return true;
        }
    }

    /// <summary>Becomes the instance of this session. Returns null when another one already is.</summary>
    /// <param name="waitMs">how long to wait for an instance that is on its way out</param>
    public static SingleInstance? TryAcquire(int waitMs = 0)
    {
        var mutex = new Mutex(true, MutexName, out bool owned);
        if (!owned && waitMs > 0)
        {
            try
            {
                owned = mutex.WaitOne(waitMs);
            }
            catch (AbandonedMutexException)
            {
                owned = true;   // the previous instance was killed: the mutex is ours
            }
        }
        if (!owned)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex);
    }

    /// <summary>Asks the running instance to show its library window. Returns false when none is running.</summary>
    public static bool SignalShow() => Signal(ShowEventName);

    /// <summary>Asks the running instance to exit (it restores the normal wallpaper on its way out).</summary>
    public static bool SignalExit() => Signal(ExitEventName);

    /// <summary>Called by an instance once it is up: tells a waiting updater that the new exe works.</summary>
    public static void SignalStarted() => Signal(StartedEventName);

    /// <summary>Updater: the event a freshly started instance sets. Create it before starting the instance.</summary>
    public static EventWaitHandle CreateStartedEvent() => new EventWaitHandle(false, EventResetMode.ManualReset, StartedEventName);

    /// <summary>Benchmark: while the returned object lives, a running instance draws nothing.</summary>
    public static IDisposable AnnounceBenchmark() => new Mutex(false, BenchmarkMutexName);

    /// <summary>Starts listening for the requests of other processes.</summary>
    public void Listen()
    {
        var thread = new Thread(ListenLoop) { IsBackground = true, Name = "Wallpepper signals" };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        Try.Run("release the instance mutex", _mutex.ReleaseMutex);
        _mutex.Dispose();
    }

    private static bool Signal(string eventName)
    {
        if (!EventWaitHandle.TryOpenExisting(eventName, out EventWaitHandle? handle))
        {
            return false;
        }
        using (handle)
        {
            handle.Set();
        }
        return true;
    }

    private void ListenLoop()
    {
        var handles = new WaitHandle[] { _show, _exit, _stop };
        while (true)
        {
            int signalled = WaitHandle.WaitAny(handles);
            if (signalled == 0)
            {
                ShowRequested?.Invoke();
            }
            else
            {
                if (signalled == 1)
                {
                    ExitRequested?.Invoke();
                }
                return;
            }
        }
    }
}
