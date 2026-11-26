using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using Wallpepper.Desktop;
using Wallpepper.Interop;
using Wallpepper.Settings;
using Wallpepper.Ui;

namespace Wallpepper.Setup;

/// <summary>
/// Self-installer: per-user, no admin. Copies the exe to %LOCALAPPDATA%\Programs\Wallpepper, adds a Start menu
/// shortcut and an entry in Settings > Apps (uninstall), optional autostart. Running a newer exe from anywhere else
/// offers to update the installed copy in place.
/// </summary>
internal static class Installer
{
    public const string AppName = "Wallpepper";
    public const string Publisher = "Dennis Walkewitz";

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ShortcutDescription = "Animated shader wallpapers";
    private const int CopyAttempts = 20;
    private const int CopyRetryMs = 250;

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallDir, AppName + ".exe");

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"0.3.0"</summary>
    public static string VersionText => CurrentVersion.ToString(3);

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>This process is the installed copy (not one started from a download or build folder).</summary>
    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Version of the installed exe ("0.2.0"), null when it cannot be read.</summary>
    public static string? InstalledVersion
    {
        get
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(InstalledExe);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart).ToString(3);
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                return null;
            }
        }
    }

    private static string StartMenuLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    // ------------------------------------------------------------- autostart
    /// <summary>Whether Wallpepper starts with Windows (the user's Run key).</summary>
    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!value)
            {
                key.DeleteValue(AppName, false);
                return;
            }
            // a copy that is not installed starts itself, without offering the setup at every sign-in
            string command = IsRunningInstalled || IsInstalled
                ? $"\"{InstalledExe}\" --tray"
                : $"\"{Environment.ProcessPath}\" --tray --portable";
            key.SetValue(AppName, command);
        }
    }

    /// <summary>Reads the autostart setting; false when the registry cannot be read.</summary>
    public static bool AutostartOrDefault() => Try.Get("read autostart", () => Autostart, false);

    // ------------------------------------------------------------- install / update in place
    /// <summary>
    /// Offers to install (or installs quietly) when this exe was started from outside its install folder.
    /// </summary>
    /// <returns>true if the caller should exit: the installed copy was launched, or the user cancelled.</returns>
    public static bool RunSetupIfNeeded(CommandLine args)
    {
        if (IsRunningInstalled || args.Portable || (IsInstalled && args.Tray))
        {
            return false;
        }

        // "--install [--autostart]": install or update without asking (scripts, package managers); otherwise the setup window
        bool autostart;
        if (args.Install)
        {
            autostart = args.Autostart || (IsInstalled && AutostartOrDefault());
        }
        else
        {
            var dialog = new SetupWindow(IsInstalled ? InstalledVersion ?? "?" : null);
            dialog.ShowDialog();
            if (dialog.Choice == SetupChoice.Cancel)
            {
                return true;
            }
            if (dialog.Choice == SetupChoice.JustRun)
            {
                return false;
            }
            autostart = dialog.StartWithWindows;
        }

        try
        {
            Install(autostart);
            Process.Start(new ProcessStartInfo(InstalledExe, args.Install ? "--tray" : "") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("install", e);
            if (!args.Install)
            {
                MessageDialog.Show("Install failed", e.Message, "Close");
            }
        }
        return true;
    }

    /// <summary>The entry in Settings > Apps > Installed apps (per user). Rewritten after every install and update.</summary>
    public static void WriteUninstallEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", InstalledVersion ?? VersionText);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("URLInfoAbout", Updater.RepoUrl);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    // ------------------------------------------------------------- uninstall
    /// <summary>
    /// Stops the running instance, removes the program, shortcut, autostart and registry entry. The library and
    /// settings stay unless the user asks to remove them too (a quiet uninstall always keeps them).
    /// </summary>
    public static void Uninstall(bool quiet)
    {
        bool keepData = true;
        if (!quiet)
        {
            var dialog = new UninstallWindow();
            dialog.ShowDialog();
            if (!dialog.Confirmed)
            {
                return;
            }
            keepData = dialog.KeepData;
        }

        ProcessControl.StopInstalledInstance();
        DesktopHost.RepaintWallpaper();   // in case the instance had to be killed
        Try.Run("remove autostart", () => Autostart = false);
        Try.Run("remove the uninstall entry", () => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false));
        Try.Run("remove the Start menu shortcut", () => File.Delete(StartMenuLink));
        DeleteFolder(AppPaths.ThumbnailDir);
        DeleteFolder(AppPaths.ShaderCacheDir);
        DeleteFolder(AppPaths.UpdateStagingDir);
        if (!keepData)
        {
            // the library is only removed when it is Wallpepper's own folder (it can be Windows Terminal's or any other)
            Try.Run("reset Windows Terminal's shader", () => TerminalIntegration.ClearShaderUnder(AppPaths.OwnLibraryDir));
            DeleteFolder(AppPaths.WebViewDir);
            DeleteFolder(AppPaths.OwnLibraryDir);
            foreach (string file in new[] { AppPaths.ConfigFile, AppPaths.ConfigFile + ".tmp", AppPaths.ConfigFile + ".bad", AppPaths.LogFile, AppPaths.PreviousLogFile })
            {
                Try.Run("remove " + Path.GetFileName(file), () => File.Delete(file));
            }
            Try.Run("remove the data folder", () => Directory.Delete(AppPaths.DataDir, false));
        }
        RemoveProgramFolderAfterExit();
    }

    private static void Install(bool autostart)
    {
        ProcessControl.StopInstalledInstance();
        Directory.CreateDirectory(InstallDir);
        CopyWithRetry(Environment.ProcessPath!, InstalledExe);
        WriteUninstallEntry();
        Autostart = autostart;
        // the shortcut is a convenience: without it the app is installed all the same
        Try.Run("create the Start menu shortcut", () => ShellLink.Create(StartMenuLink, InstalledExe, ShortcutDescription));
        Log.Write("installed version " + VersionText + " to " + InstallDir);
    }

    private static void CopyWithRetry(string source, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                return;
            }
            catch (IOException) when (attempt < CopyAttempts)
            {
                Thread.Sleep(CopyRetryMs);   // the old process may still be letting go of the file
            }
        }
    }

    /// <summary>
    /// A running exe cannot delete itself: a hidden cmd removes the program folder (and the runtime's unpack folder)
    /// once this process has ended, trying for a few seconds until the exe is no longer locked.
    /// </summary>
    private static void RemoveProgramFolderAfterExit()
    {
        string unpacked = Path.Combine(Path.GetTempPath(), ".net", AppName);
        string script =
            $"for /l %i in (1,1,10) do (ping -n 2 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\" 2>nul"
            + $" & rmdir /s /q \"{unpacked}\" 2>nul & if not exist \"{InstallDir}\" exit /b 0)";
        var start = new ProcessStartInfo("cmd.exe", "/d /c " + script)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(start)?.Dispose();
    }

    private static void DeleteFolder(string folder)
    {
        Try.Run("remove " + folder, () =>
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        });
    }
}
