using System.ComponentModel;
using System.Diagnostics;
using Wallpepper.Settings;

namespace Wallpepper.Setup;

/// <summary>Stops other Wallpepper processes: politely first (the exit event), by force when they stay.</summary>
internal static class ProcessControl
{
    private const int ExitWaitMs = 8000;
    private const int KillWaitMs = 3000;

    /// <summary>
    /// Asks the installed copy to exit (it restores the normal wallpaper on its way out) and waits; kills it if it stays.
    /// </summary>
    public static void StopInstalledInstance() => Stop(path => SamePath(path, Installer.InstalledExe));

    /// <summary>
    /// Asks any running Wallpepper (installed or not) to exit and waits for it. Returns its exe path, null when none ran.
    /// </summary>
    public static string? StopRunningInstance() => Stop(_ => true);

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <returns>the exe path of a process that was stopped, null when there was none</returns>
    private static string? Stop(Func<string?, bool> matches)
    {
        var others = new List<Process>();
        string? stoppedPath = null;
        foreach (var process in Process.GetProcessesByName(Installer.AppName))
        {
            string? path = process.Id == Environment.ProcessId ? null : ExePathOf(process);
            if (path != null && matches(path))
            {
                others.Add(process);
                stoppedPath ??= path;
            }
            else
            {
                process.Dispose();
            }
        }
        if (others.Count == 0)
        {
            return null;
        }

        SingleInstance.SignalExit();
        foreach (var process in others)
        {
            try
            {
                if (!process.WaitForExit(ExitWaitMs))
                {
                    process.Kill();
                    process.WaitForExit(KillWaitMs);
                }
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                Log.Warn("stop process " + process.Id, e);
            }
            finally
            {
                process.Dispose();
            }
        }
        return stoppedPath;
    }

    /// <summary>The exe of a process; null when it cannot be read (it ended, or belongs to another user).</summary>
    private static string? ExePathOf(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
