using Wallpepper.Setup;

namespace Wallpepper.Commands;

/// <summary>
/// The updater's child-process commands. "--update-check" and "--update-fetch" print one JSON line with the result;
/// "--apply-update &lt;exe&gt; &lt;sha256&gt;" is run from the downloaded exe and swaps the installed one.
/// </summary>
internal static class UpdateCommands
{
    public static int Check() => Print(Updater.Check());

    public static int Fetch() => Print(Updater.Fetch());

    public static int Apply(CommandLine args) =>
        args.Values.Count < 2 ? Updater.ExitChecksumMismatch : Updater.Apply(args.Values[0], args.Values[1]);

    private static int Print(UpdateInfo info)
    {
        Console.Out.WriteLine(info.ToJson());
        Console.Out.Flush();
        return info.Error == null ? 0 : 1;
    }
}
