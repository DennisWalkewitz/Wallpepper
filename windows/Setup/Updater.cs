using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Wallpepper.Settings;

namespace Wallpepper.Setup;

/// <summary>
/// Updates from GitHub releases. Releases are tagged v&lt;version&gt; and carry Wallpepper.exe plus SHA256SUMS.txt.
/// All network work runs in a short-lived child process ("--update-check" / "--update-fetch"), so the tray process
/// never loads the HTTP stack. The swap itself is "--apply-update", run from the downloaded exe.
///
/// The checksum protects against a damaged download. It does not prove who published the release (the sums file comes
/// from the same place as the exe), which is why installing updates without asking is off unless the user turns it on.
/// </summary>
internal static class Updater
{
    /// <summary>GitHub repository (owner/name) that publishes the releases.</summary>
    public const string Repo = "DennisWalkewitz/Wallpepper";
    public const string RepoUrl = "https://github.com/" + Repo;
    public const string ReleasesUrl = RepoUrl + "/releases";

    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitChecksumMismatch = 2;
    public const int ExitNotInstalled = 3;
    public const int ExitRolledBack = 4;

    private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    private const string DownloadPathPrefix = "/" + Repo + "/releases/download/";
    private const string ExeAsset = "Wallpepper.exe";
    private const string SumsAsset = "SHA256SUMS.txt";
    private const long MaxSumsBytes = 1 << 20;
    private const long MaxExeBytes = 300L << 20;
    private const int MaxRedirects = 6;
    private const int StartWaitSeconds = 45;
    private const int RetryAttempts = 20;
    private const int RetryMs = 250;

    private static readonly string[] ApiHosts = { "api.github.com" };
    private static readonly string[] DownloadHosts = { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };

    private static string PreviousExe => Path.Combine(Installer.InstallDir, "Wallpepper.old.exe");

    private static string IncomingExe => Path.Combine(Installer.InstallDir, "Wallpepper.new.exe");

    // ------------------------------------------------------------- child process: check / fetch
    /// <summary>Asks GitHub for the latest release. Never throws: a failure is in the result.</summary>
    public static UpdateInfo Check() => Run(download: false);

    /// <summary>Like <see cref="Check"/>, and downloads and verifies the new exe.</summary>
    public static UpdateInfo Fetch() => Run(download: true);

    /// <summary>0.3 and 0.3.0.0 are 0.3.0.</summary>
    public static Version Normalise(Version version) => new Version(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>The newer version a release tag names ("v0.4.0"), null when it is not newer or not a version.</summary>
    public static Version? NewerVersion(string tag, Version current)
    {
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
        {
            return null;
        }
        return Normalise(latest) > Normalise(current) ? Normalise(latest) : null;
    }

    /// <summary>The checksum SHA256SUMS.txt lists for a file ("&lt;64 hex digits&gt;  name"), null when it has none.</summary>
    public static string? ChecksumFor(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n'))
        {
            string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName && parts[0].Length == 64)
            {
                return parts[0];
            }
        }
        return null;
    }

    /// <summary>
    /// Whether an address may be loaded: https, one of the given hosts, and (for downloads) this repository's release path.
    /// </summary>
    public static bool IsAllowed(Uri uri, string[] hosts, string? pathPrefix)
    {
        bool hostAllowed = uri.Scheme == Uri.UriSchemeHttps && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        bool onGitHub = string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
        bool pathAllowed = pathPrefix == null || !onGitHub || uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase);
        return hostAllowed && pathAllowed;
    }

    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ------------------------------------------------------------- swap
    /// <summary>
    /// "--apply-update &lt;exe&gt; &lt;sha256&gt;": replaces the installed exe with a new one. Asks the running instance to
    /// exit, keeps the previous exe as Wallpepper.old.exe, starts the new one and waits for it to report in; if it does
    /// not, the previous exe is put back and started. Never touches a portable copy, never elevates. The checksum on the
    /// command line guards against a file that changed since it was downloaded and verified.
    /// </summary>
    public static int Apply(string newExe, string sha256)
    {
        string target = Installer.InstalledExe;
        try
        {
            newExe = Path.GetFullPath(newExe);
            if (!File.Exists(newExe) || !SameHash(Sha256(newExe), sha256))
            {
                Log.Write("update: refused, the file does not match the given SHA-256");
                return ExitChecksumMismatch;
            }
            if (!File.Exists(target) || string.Equals(newExe, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("update: refused, Wallpepper is not installed");
                return ExitNotInstalled;
            }

            Log.Write("update: installing " + Path.GetFileName(newExe));
            Swap(newExe, sha256, target);
            if (StartAndConfirm(target))
            {
                Installer.WriteUninstallEntry();
                Try.Run("remove the previous exe", () => Retry(() => File.Delete(PreviousExe)));
                Log.Write("update: done");
                return ExitOk;
            }

            Log.Write("update: the new version did not start, putting the previous one back");
            ProcessControl.StopInstalledInstance();
            Retry(() => File.Move(PreviousExe, target, true));
            Start(target, "--tray");
            return ExitRolledBack;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("update", e);
            return ExitFailed;
        }
    }

    /// <summary>Removes downloads and a left-over previous exe (the updater normally does that itself).</summary>
    public static void CleanUp()
    {
        Try.Run("remove update downloads", () =>
        {
            if (Directory.Exists(AppPaths.UpdateStagingDir))
            {
                Directory.Delete(AppPaths.UpdateStagingDir, true);
            }
        });
        if (Installer.IsRunningInstalled)
        {
            Try.Run("remove the previous exe", () => File.Delete(PreviousExe));
            Try.Run("remove an unfinished update", () => File.Delete(IncomingExe));
        }
    }

    /// <summary>
    /// The installed exe is never written in place: the new one is copied next to it, checked again, and only then
    /// renamed over it (a rename cannot leave half a file behind). The old exe stays as Wallpepper.old.exe.
    /// </summary>
    private static void Swap(string newExe, string sha256, string target)
    {
        ProcessControl.StopInstalledInstance();
        Retry(() => File.Copy(newExe, IncomingExe, true));
        if (!SameHash(Sha256(IncomingExe), sha256))
        {
            File.Delete(IncomingExe);
            throw new IOException("The copy of the new exe is damaged.");
        }
        Retry(() => File.Delete(PreviousExe));
        Retry(() => File.Move(target, PreviousExe));
        try
        {
            File.Move(IncomingExe, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Retry(() => File.Move(PreviousExe, target, true));
            Start(target, "--tray");
            throw;
        }
    }

    /// <summary>Starts the new exe and waits for it to say that it runs.</summary>
    private static bool StartAndConfirm(string target)
    {
        using var started = SingleInstance.CreateStartedEvent();
        using var fresh = Process.Start(new ProcessStartInfo(target, "--tray --updated") { UseShellExecute = false })
            ?? throw new IOException("The new version could not be started.");
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed.TotalSeconds < StartWaitSeconds && !fresh.HasExited)
        {
            if (started.WaitOne(RetryMs))
            {
                return true;
            }
        }
        if (started.WaitOne(0))
        {
            return true;
        }
        Try.Run("stop the new version", () =>
        {
            if (!fresh.HasExited)
            {
                fresh.Kill();
                fresh.WaitForExit(5000);
            }
        });
        return false;
    }

    private static void Start(string exe, string arguments) =>
        Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })?.Dispose();

    private static bool SameHash(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A file that was just released by an exiting process can stay locked for a moment.</summary>
    private static void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < RetryAttempts)
            {
                Thread.Sleep(RetryMs);
            }
        }
    }

    // ------------------------------------------------------------- network
    private static UpdateInfo Run(bool download)
    {
        try
        {
            return Query(download);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            return UpdateInfo.Failed(e.Message);
        }
    }

    private static UpdateInfo Query(bool download)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Wallpepper/" + Installer.VersionText);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var info = new UpdateInfo();
        string? exeUrl = null;
        string? sumsUrl = null;
        using (var response = Get(http, LatestApi, ApiHosts, null))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return info;   // no release published (yet)
            }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(response.Content.ReadAsStream());
            var release = document.RootElement;
            if (IsTrue(release, "draft") || IsTrue(release, "prerelease"))
            {
                return info;
            }
            Version? newer = NewerVersion(Text(release, "tag_name") ?? "", Installer.CurrentVersion);
            if (newer == null)
            {
                return info;
            }
            info.Version = newer.ToString(3);
            string? page = Text(release, "html_url");
            info.Notes = page != null && page.StartsWith(RepoUrl + "/", StringComparison.OrdinalIgnoreCase) ? page : ReleasesUrl;
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = Text(asset, "name");
                    string? url = Text(asset, "browser_download_url");
                    if (name == ExeAsset)
                    {
                        exeUrl = url;
                    }
                    else if (name == SumsAsset)
                    {
                        sumsUrl = url;
                    }
                }
            }
        }
        if (!download)
        {
            return info;
        }
        if (exeUrl == null || sumsUrl == null)
        {
            info.Error = $"The release has no {ExeAsset} with {SumsAsset}.";
            return info;
        }

        Directory.CreateDirectory(AppPaths.UpdateStagingDir);
        foreach (string stale in Directory.GetFiles(AppPaths.UpdateStagingDir))
        {
            Try.Run("remove an old download", () => File.Delete(stale));
        }
        string file = Path.Combine(AppPaths.UpdateStagingDir, "Wallpepper-" + info.Version + ".exe");

        string sumsFile = file + ".sums";
        Download(http, sumsUrl, sumsFile, MaxSumsBytes);
        string? expected = ChecksumFor(File.ReadAllText(sumsFile), ExeAsset);
        File.Delete(sumsFile);
        if (expected == null)
        {
            info.Error = $"{SumsAsset} has no checksum for {ExeAsset}.";
            return info;
        }

        Download(http, exeUrl, file, MaxExeBytes);

        // nothing is done with the file before its checksum has been compared
        string actual = Sha256(file);
        if (!SameHash(actual, expected))
        {
            Try.Run("remove the damaged download", () => File.Delete(file));
            info.Error = "The download does not match its SHA-256 checksum and was discarded.";
            return info;
        }
        info.File = file;
        info.Sha256 = actual;
        return info;
    }

    /// <summary>Saves a release asset. The size limit is enforced while copying, whatever the server announces.</summary>
    private static void Download(HttpClient http, string url, string file, long maxBytes)
    {
        using var response = Get(http, url, DownloadHosts, DownloadPathPrefix);
        response.EnsureSuccessStatusCode();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var input = response.Content.ReadAsStream(cancel.Token);
        using var output = File.Create(file);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(Path.GetFileName(new Uri(url).AbsolutePath) + " is too large.");
            }
            cancel.Token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
        }
    }

    /// <summary>HTTPS GET that follows redirects by hand, so that every hop can be checked against the allowed hosts.</summary>
    private static HttpResponseMessage Get(HttpClient http, string url, string[] hosts, string? firstHopPathPrefix)
    {
        string? pathPrefix = firstHopPathPrefix;
        for (int hop = 0; hop < MaxRedirects; hop++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsAllowed(uri, hosts, pathPrefix))
            {
                throw new InvalidOperationException("Refused to load from " + (uri?.Host ?? "an invalid address") + ".");
            }
            var response = http.Send(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead);
            int code = (int)response.StatusCode;
            bool redirect = code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
            if (!redirect || response.Headers.Location == null)
            {
                return response;
            }
            url = new Uri(uri, response.Headers.Location).AbsoluteUri;
            response.Dispose();
        }
        throw new InvalidOperationException("Too many redirects.");
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
