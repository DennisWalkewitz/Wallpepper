using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wallpepper.Settings;
using Wallpepper.Setup;

namespace Wallpepper;

/// <summary>
/// Looks for a newer release shortly after start and at most once a day from then on, and installs it when asked.
/// The network part runs in a child process (see <see cref="Updater"/>). UI thread only.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const int FirstCheckSeconds = 45;
    private const int CheckTimeoutSeconds = 40;
    private const int FetchTimeoutSeconds = 20 * 60;

    private readonly SettingsStore _settings;
    private readonly Action<string, bool> _notify;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;
    private bool _cleanedUp;

    /// <param name="notify">shows a short message to the user (text, is an error)</param>
    public UpdateService(SettingsStore settings, Action<string, bool> notify)
    {
        _settings = settings;
        _notify = notify;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(FirstCheckSeconds) };
        _timer.Tick += (_, _) => OnTimer();
        _timer.Start();
    }

    /// <summary>A newer release was found, or a check or download started or ended.</summary>
    public event Action? Changed;

    /// <summary>A newer release that was found, else null.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>A check or a download is running.</summary>
    public bool Busy { get; private set; }

    /// <summary>Asks GitHub for the latest release. A background check never shows a failure; a manual one reports either way.</summary>
    public void Check(bool manual)
    {
        if (Busy)
        {
            return;
        }
        SetBusy(true);
        RunChild("--update-check", CheckTimeoutSeconds, info =>
        {
            SetBusy(false);
            if (info.Error == null)
            {
                _settings.Change(SettingsArea.None, config => config.LastUpdateCheck = DateTime.UtcNow.Ticks);
            }
            OnChecked(info, manual);
        });
    }

    /// <summary>
    /// Downloads and verifies the new exe, then hands over to it: it asks this instance to exit, replaces the
    /// installed exe and starts the new version. A copy that is not installed only opens the release page.
    /// </summary>
    public void Install()
    {
        var update = Available;
        if (update == null || Busy)
        {
            return;
        }
        if (!Installer.IsRunningInstalled)
        {
            Shell.OpenUrl(update.Notes ?? Updater.ReleasesUrl);
            return;
        }
        SetBusy(true);
        RunChild("--update-fetch", FetchTimeoutSeconds, info =>
        {
            SetBusy(false);
            if (info.File == null || info.Sha256 == null)
            {
                _notify(info.Error ?? "The update could not be downloaded", true);
                return;
            }
            StartSwap(info.File, info.Sha256);
        });
    }

    public void Dispose() => _timer.Stop();

    private void OnTimer()
    {
        _timer.Interval = TimeSpan.FromHours(1);
        if (!_cleanedUp)
        {
            _cleanedUp = true;
            Updater.CleanUp();
        }
        long now = DateTime.UtcNow.Ticks;
        long last = _settings.Config.LastUpdateCheck;
        if (_settings.Config.CheckUpdates && (now - last >= TimeSpan.TicksPerDay || last > now))
        {
            Check(manual: false);
        }
    }

    private void OnChecked(UpdateInfo info, bool manual)
    {
        bool isNews = info.Version != null && info.Version != Available?.Version;
        if (info.Version != null)
        {
            Available = info;
        }
        else if (info.Error == null)
        {
            Available = null;
        }
        Changed?.Invoke();

        if (info.Version != null)
        {
            if (!manual && _settings.Config.AutoInstallUpdates && Installer.IsRunningInstalled)
            {
                Install();
            }
            else if (manual || isNews)
            {
                _notify("Wallpepper " + info.Version + " is available", false);
            }
        }
        else if (manual)
        {
            bool failed = info.Error != null;
            _notify(failed ? "Could not check for updates" : "Wallpepper " + Installer.VersionText + " is up to date", failed);
        }
    }

    /// <summary>The downloaded exe replaces the installed one itself; it is checked once more right before it is run.</summary>
    private void StartSwap(string file, string sha256)
    {
        try
        {
            if (!string.Equals(Updater.Sha256(file), sha256, StringComparison.OrdinalIgnoreCase))
            {
                _notify("The downloaded update was changed after it was verified and is not installed", true);
                return;
            }
            var start = new ProcessStartInfo(file) { UseShellExecute = false };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(file);
            start.ArgumentList.Add(sha256);
            Process.Start(start)?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn("start the update", e);
            _notify("The update could not be started", true);
        }
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }

    /// <summary>Runs this exe with an updater command and reads the one JSON line it prints.</summary>
    private void RunChild(string command, int timeoutSeconds, Action<UpdateInfo> done)
    {
        Task.Run(() =>
        {
            UpdateInfo info = ReadChild(command, timeoutSeconds);
            if (info.Error != null)
            {
                Log.Write("update: " + info.Error);
            }
            _ui.InvokeAsync(() => done(info));
        });
    }

    private static UpdateInfo ReadChild(string command, int timeoutSeconds)
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!, command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The update check could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            if (process.WaitForExit(timeoutSeconds * 1000))
            {
                return UpdateInfo.Parse(output.GetAwaiter().GetResult());
            }
            Try.Run("stop the update check", process.Kill);
            return UpdateInfo.Failed("timed out");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return UpdateInfo.Failed(e.Message);
        }
    }
}
