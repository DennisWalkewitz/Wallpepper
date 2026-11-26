using System.ComponentModel;
using System.Diagnostics;
using Wallpepper.Settings;

namespace Wallpepper;

/// <summary>Hands things to Windows: addresses to the browser, folders to File Explorer, files to an editor.</summary>
internal static class Shell
{
    /// <summary>Opens a web address in the default browser. Anything that is not http(s) is ignored.</summary>
    public static void OpenUrl(string? url)
    {
        bool isWeb = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        if (isWeb)
        {
            Start("open " + uri!.Host, new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    public static void OpenFolder(string folder) => Start("open folder", new ProcessStartInfo(folder) { UseShellExecute = true });

    /// <summary>Opens the file's folder in File Explorer with the file selected.</summary>
    public static void Reveal(string file) =>
        Start("show in folder", new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });

    /// <summary>
    /// Opens a text file for editing: in Visual Studio Code when it is installed (shader authors mostly have it), else
    /// with whatever Windows has registered for editing the file type, else in Notepad.
    /// </summary>
    public static void Edit(string file)
    {
        string? code = FindOnPath("code.cmd");
        if (code != null && Start("edit in Visual Studio Code", new ProcessStartInfo(code, $"\"{file}\"") { UseShellExecute = false, CreateNoWindow = true }))
        {
            return;
        }
        if (Start("edit", new ProcessStartInfo(file) { UseShellExecute = true, Verb = "edit" }))
        {
            return;
        }
        Start("edit in Notepad", new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool Start(string what, ProcessStartInfo start)
    {
        try
        {
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Warn(what, e);
            return false;
        }
    }
}
