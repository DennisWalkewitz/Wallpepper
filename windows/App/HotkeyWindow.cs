using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Wallpepper.Interop;
using Wallpepper.Settings;

namespace Wallpepper;

internal enum Hotkey
{
    NextWallpaper = 1,
    PreviousWallpaper = 2,
    TogglePause = 3,
}

/// <summary>
/// Hidden window that receives what only a window can: the global hotkeys (Ctrl+Win+Alt + Right, Left, P), Explorer's
/// "TaskbarCreated" (it restarted and rebuilt the desktop), and the display being switched off or on.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const uint Modifiers = User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_WIN | User32.MOD_NOREPEAT;
    private const int DisplayOffState = 0;

    private readonly uint _taskbarCreated = User32.RegisterWindowMessage("TaskbarCreated");
    private readonly IntPtr _powerNotification;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "Wallpepper.msg" });
        _powerNotification = User32.RegisterPowerSettingNotification(Handle, User32.GUID_CONSOLE_DISPLAY_STATE, 0);
        if (_powerNotification == IntPtr.Zero)
        {
            Log.Warn("watch the display power state", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        Register(Hotkey.NextWallpaper, Keys.Right);
        Register(Hotkey.PreviousWallpaper, Keys.Left);
        Register(Hotkey.TogglePause, Keys.P);
    }

    public event Action<Hotkey>? HotkeyPressed;

    /// <summary>Explorer restarted: the wallpaper windows it destroyed have to be created again.</summary>
    public event Action? ExplorerRestarted;

    /// <summary>The display was switched off (true) or on (false).</summary>
    public event Action<bool>? DisplayPowerChanged;

    public void Dispose()
    {
        if (_powerNotification != IntPtr.Zero)
        {
            User32.UnregisterPowerSettingNotification(_powerNotification);
        }
        DestroyHandle();   // the hotkeys go with the window
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == _taskbarCreated)
        {
            ExplorerRestarted?.Invoke();
        }
        else if (m.Msg == User32.WM_POWERBROADCAST && (int)m.WParam == User32.PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<User32.POWERBROADCAST_SETTING>(m.LParam);
            if (setting.PowerSetting == User32.GUID_CONSOLE_DISPLAY_STATE)
            {
                DisplayPowerChanged?.Invoke(setting.Data == DisplayOffState);
            }
        }
        else if (m.Msg == User32.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((Hotkey)m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }

    private void Register(Hotkey hotkey, Keys key)
    {
        if (!User32.RegisterHotKey(Handle, (int)hotkey, Modifiers, (uint)key))
        {
            Log.Warn($"register the hotkey Ctrl+Win+Alt+{key} (another program has it)", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
}
