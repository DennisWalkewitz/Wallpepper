using Microsoft.Win32;
using Wallpepper.Interop;

namespace Wallpepper.Desktop;

/// <summary>Power source of the PC.</summary>
internal readonly record struct PowerState(bool OnBattery, bool BatterySaver);

/// <summary>
/// What the session is doing that matters to a wallpaper: locked, remote, display off, on battery. UI thread only.
/// </summary>
internal sealed class SessionState : IDisposable
{
    private bool _locked;
    private bool _simulated;

    public SessionState()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>The session was locked or unlocked, connected or disconnected (raised on the thread of the system event).</summary>
    public event Action? Changed;

    public bool Remote { get; private set; }

    /// <summary>The displays are powered off (set from the system's power notifications).</summary>
    public bool DisplayOff { get; set; }

    /// <summary>
    /// Whether the session is locked. An unlock event can get lost; when the user's desktop takes input again the
    /// session is not locked (and the other way round: no input desktop means the lock screen or a secure prompt is up).
    /// </summary>
    public bool IsLocked()
    {
        bool input = User32.InputDesktopIsDefault();
        if (_locked && !_simulated && input)
        {
            _locked = false;
        }
        return _locked || !input;
    }

    public static PowerState Power()
    {
        Kernel32.GetSystemPowerStatus(out var status);
        return new PowerState(status.ACLineStatus == 0, status.SystemStatusFlag == 1);
    }

    /// <summary>Self-test: the same state a real lock or unlock produces.</summary>
    public void SimulateLock(bool locked)
    {
        _locked = locked;
        _simulated = locked;
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
                _locked = true;
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
                _locked = false;
                Remote = false;
                break;
            case SessionSwitchReason.RemoteConnect:
                Remote = true;
                break;
            case SessionSwitchReason.RemoteDisconnect:
                Remote = false;
                break;
        }
        Changed?.Invoke();
    }
}
