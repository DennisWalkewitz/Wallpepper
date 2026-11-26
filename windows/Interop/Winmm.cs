using System.Runtime.InteropServices;

namespace Wallpepper.Interop;

internal static class Winmm
{
    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint milliseconds);
}
