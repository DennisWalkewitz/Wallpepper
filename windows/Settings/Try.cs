namespace Wallpepper.Settings;

/// <summary>Best-effort steps (clean-up, cosmetic extras): a failure is logged with its context and the caller goes on.</summary>
internal static class Try
{
    public static bool Run(string context, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warn(context, e);
            return false;
        }
    }

    public static T Get<T>(string context, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warn(context, e);
            return fallback;
        }
    }
}
