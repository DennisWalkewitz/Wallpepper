using System.Runtime.InteropServices;

namespace Wallpepper.Interop;

internal static class Kernel32
{
    public const int STD_OUTPUT_HANDLE = -11;
    public const int ATTACH_PARENT_PROCESS = -1;
    public const uint FILE_TYPE_UNKNOWN = 0;
    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    /// <summary>ACLineStatus 0 = on battery; SystemStatusFlag 1 = battery saver on.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    public static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    public static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(
        IntPtr timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForMultipleObjects(
        uint count, IntPtr[] handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);
}
