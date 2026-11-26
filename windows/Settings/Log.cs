namespace Wallpepper.Settings;

/// <summary>The one log file (log.txt in the data folder). Safe to call from any thread; never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new object();

    /// <summary>Development aid: a file "debuglog.on" next to the exe mirrors the log to "debug.log" there.</summary>
    public static readonly string? Mirror = File.Exists(Path.Combine(AppContext.BaseDirectory, "debuglog.on"))
        ? Path.Combine(AppContext.BaseDirectory, "debug.log")
        : null;

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                Rotate();
                File.AppendAllText(AppPaths.LogFile, line);
                if (Mirror != null)
                {
                    File.AppendAllText(Mirror, line);
                }
            }
            catch (IOException)
            {
                // nowhere left to report a failing log
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>An expected failure: what was attempted and why it did not work.</summary>
    public static void Warn(string attempted, Exception error) => Write($"{attempted}: {error.GetType().Name}: {error.Message}");

    /// <summary>An unexpected failure, with its stack trace.</summary>
    public static void Error(string context, Exception error) => Write($"{context}: {error}");

    /// <summary>Keeps one previous file instead of growing without limit.</summary>
    private static void Rotate()
    {
        var file = new FileInfo(AppPaths.LogFile);
        if (file.Exists && file.Length > MaxBytes)
        {
            File.Move(AppPaths.LogFile, AppPaths.PreviousLogFile, true);
        }
    }
}
